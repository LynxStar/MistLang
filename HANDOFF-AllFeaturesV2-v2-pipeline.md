# Handoff: make `AllFeaturesV2_CompilesRunsAndMatchesBaseline` pass

Branch `compiler-v2`, based on `28a65b43`. Written mid-task so this can be picked up on
another machine.

## Finish line

Remove the `Skip` from `AllFeaturesV2_CompilesRunsAndMatchesBaseline` in
`Test/Mist.Tests/Integration/SamplesCompileAndMatchBaselineTest.cs:71-75` and have it pass.
That test compiles `samples/AllFeaturesV2` through the CLI with `--pipeline experimental`
and byte-compares stdout against `samples/__baselines__/AllFeaturesV2.out` (57 lines).

Reproduce the failure with:

```
dotnet run --project Source/Mist.CLI --no-build -- run samples/AllFeaturesV2 --pipeline experimental
```

## Working tree state

Two files modified, **uncommitted and never built**. Everything else is clean at
`28a65b43`.

| File | Change |
|---|---|
| `Source/Mist/ProgramModel/Construction/Resolution/LookupResult.cs` | Added `NamespaceResult(string NamespaceName)` record between `MethodGroupResult` and `AmbiguousResult`. |
| `Source/Mist/ProgramModel/Construction/Resolution/ScopeResolver.cs` | In `Lookup`, after `ResolveByNamespace` returns null, return `NamespaceResult` when `IsKnownNamespace(name)`. Added `public LookupResult LookupQualified(string namespaceName, string memberName)` and `private bool IsKnownNamespace(string name)`. |

These are steps 1 and 2 of the plan below. Steps 3-11 are not started. Nothing has been
built or tested since the edits, so treat "compiles" as unverified.

Reverting is safe if you would rather restart: `git checkout -- Source/Mist/ProgramModel/Construction/Resolution/`

## Correction to the original problem statement

The task brief said classic solves the multiple-`Main` problem with `EntryPointAnalyzer`,
and that the experimental pipeline lacks it because it runs zero analyzers. **That is
wrong, and it matters** -- porting an analyzer would not fix anything.

`Analysis/Builtin/EntryPointAnalyzer.cs` is diagnostics-only, and its
`FindClassesWithStaticVoidMain` explicitly skips referenced projects, so for AllFeaturesV2
it finds exactly one `Main` and never fires.

What actually prevents Roslyn CS0017 in classic is `CSharpTranspilerBackend.cs:199-325`,
`DetermineMainTypeName(model)`, whose result is passed as `mainTypeName` into
`CSharpCompilationDriver.Compile` (`CSharpTranspilerBackend.cs:159`). That flows to
`CSharpCompilationOptions(..., mainTypeName: mainTypeName)` at
`CSharpCompilationDriver.cs:273`.

`CSharpEmitterV2.Emit` (`Emitter/CSharpEmitterV2.cs:72-78`) simply omits that argument. The
parameter already exists on the driver -- nothing new is needed there.

So blocker 2 is an emit-time fix, and the experimental pipeline's stubbed
`RunAnalyzers` (`ExperimentalPipeline.cs:56-66`) is irrelevant to it. Leave the stub alone.

## Blocker 1: namespace-qualified member access

`Simpliest.Program.RunDemo()` emits `/* unsupported: UnresolvedExpression */.Program.RunDemo();`

### Why

`Simpliest.Program.RunDemo()` parses as a `PostfixNode`: target
`IdentifierExpressionPrimaryNode("Simpliest")`, then `MemberAccessSuffix(".Program")`, then
`MethodCallSuffix(".RunDemo", args)`.

`IdentifierBinder.BindIdentifier` calls `ScopeResolver.Lookup("Simpliest", ...)`.
`LookupResult` had no namespace case, and `ResolveByNamespace` only ever builds
`ns + "." + name` candidates against `TypesByFQN` -- its Tier 4 FQN fallback is gated on
`name.Contains('.')`, so a bare `Simpliest` never reaches it. Result: `NotFoundResult` ->
`UnresolvedExpression`. `Console` works only because `using Standard;` makes
`Standard.Console` a Tier 3 hit.

`PostfixBinder.BindMemberAccess` then unconditionally wraps whatever it got in a
`MemberAccessExpression`, so `.Program` and `.RunDemo` stack on top of the sentinel.

`ProgramModel.Namespaces` **does** contain `Simpliest`, `Primitives`, `ArrayLiterals`,
`AllFeaturesV2` and `Standard` -- `NamespaceBuilder.RegisterNamespaces` calls
`GetOrAddNamespace(fileNamespace)` for every unit including referenced projects. The binder
just never consulted it. `BuilderContext.ProgramModel` is the concrete
`Model.ProgramModel`, so `Namespaces` is reachable even though it is not on `IProgramModel`.

### Second half of blocker 1: the emitter would emit a bare name

Once `Simpliest.Program` resolves to a `TypeReferenceExpression`, `BodyEmitter.cs:282`
renders it via `TypeNameEmitter.Emit`, whose `MapMemberType` returns bare
`memberType.Name` -> `Program`. Inside the emitted `namespace AllFeaturesV2;` that binds to
`AllFeaturesV2.Program`, i.e. infinite recursion instead of the intended call. The FQN is
available as `MistType.FullyQualifiedName`.

## Remaining plan

### Step 3 -- new `Source/Mist/ProgramModel/Model/Expressions/NamespaceReferenceExpression.cs`

`internal sealed class NamespaceReferenceExpression(SyntaxNode node, string namespaceName) : Expression(node)`
with `public string NamespaceName { get; }` and `public override IMistType Type => ErrorType.Instance`.

A namespace is not a value, so the error sentinel is the honest type -- document that in the
XML comment. Nothing consumes `ErrorType` as a diagnostic trigger today (the v2 analyzer
stage is a no-op), so this is safe. Do **not** use `UnknownType`, which the emitter renders
as `object`.

### Step 4 -- `IdentifierBinder.cs`

Add an arm before `AmbiguousResult`:

```csharp
NamespaceResult ns => new NamespaceReferenceExpression(node, ns.NamespaceName),
```

### Step 5 -- `PostfixBinder.cs`

In `BindPostfixSuffix`, both the `MemberAccessSuffixPostfixSuffixNode` and
`MethodCallSuffixPostfixSuffixNode` cases must special-case a namespace receiver before
falling through to `BindMemberAccess`. Something like a new
`BindQualifiedName(suffix, nsRef, name, context)` that calls
`context.Resolver.LookupQualified(nsRef.NamespaceName, name)` and maps:

- `TypeResult t` -> `new TypeReferenceExpression(suffix, t.Type)`
- `NamespaceResult n` -> `new NamespaceReferenceExpression(suffix, n.NamespaceName)`
- otherwise -> `new UnresolvedExpression(suffix)`

For the method-call case, resolve the namespace step first and then let the existing
invocation path run on the resulting receiver.

`ResolveMemberType` (`PostfixBinder.cs:88-121`) already handles static access once the
receiver is a `TypeReferenceExpression`, so `.RunDemo` needs no change -- it finds `RunDemo`
in `MemberType.MethodsList` and returns `Standard.Void`.

`BindMemberAccess` currently takes no `BuilderContext`; the call sites in
`BindPostfixSuffix` already have one, so thread it through.

### Step 6 -- `Source/Mist/Backends/CSharpTranspiler/Emitter/TypeNameEmitter.cs`

Emit the fully qualified name for source-declared types outside `Standard`. Guard
conditions, all required:

- `NamespaceName` is non-empty
- `NamespaceName` is not `Standard` and does not start with `Standard.`
- `Origin == TypeOrigin.Source` (leave ambient/assembly-projected types alone)
- `Name` contains no `<` (generic definitions like `Array<,>` must keep the existing
  `EmitConstructed` path)

Verified: all stdlib `.mist` under `Source/Standard/` declares exactly `namespace Standard;`
and nothing else, so the `Standard` guard is exact, and `Console` keeps emitting bare so it
still binds to `System.Console` through the emitted `using System;`.

Known and accepted side effect: `Primitives.StringUtils.IsGreeting(...)` starts emitting as
`Primitives.StringUtils.IsGreeting(...)` inside `namespace Primitives;`. That is valid C#
(the enclosing-namespace lookup misses `Primitives.Primitives` then finds global
`Primitives`) and program behaviour is unchanged. Nothing asserts on emitted v2 C# text --
checked: no test references `CSharpEmitterV2`, and
`backlog/emitter-v2-sample-fingerprints.json` is derived from the **parser**, not the
emitter, so it does not need regenerating.

Do not touch `EmitConstructed`; its `Standard.Array<,>` special case and bare generic base
names are out of scope.

### Step 7 -- `Source/Mist/ProgramModel/Model/Types/MistType.cs`

Add `public string? DeclaringProjectPath { get; internal set; }` -- the declaring
compilation unit's `CompilationUnit.OriginProjectPath`. `null` means the root project (and
also stdlib, which is harmless because no stdlib type declares `Main`).

This is the datum the model is missing: types currently carry only
`Origin` (`Source` vs `Ambient`) and there is no way to tell a root-project type from a
referenced-project one. Matching `NamespaceName` against `options.NamespaceRoot` was
considered and rejected -- it silently picks the wrong `Main` when a root-project file
declares an unrelated namespace.

### Step 8 -- `Source/Mist/ProgramModel/Construction/Types/TypeBuilder.cs`

Take the origin project path on `Build(Scope, CompilationUnitNode, string?)`, hold it in a
private field for the duration of the file, and stamp it in `FinalizeRegistration` (the one
place every type kind funnels through, nested types included).

### Step 9 -- `Source/Mist/ProgramModel/Construction/Scaffolding/CompilationUnitBuilder.cs`

Pass `unit.OriginProjectPath` into `typeBuilder.Build(...)`.

### Step 10 -- new `Source/Mist/Backends/CSharpTranspiler/Emitter/EntryPointSelector.cs`

Given the program model, return the FQN of the root project's entry-point class, or null.

- Consider only `MemberType` with `DeclaringProjectPath is null` and `Origin != Ambient`
- Candidate method: `Modifiers.IsStatic`, `Name == "Main"`, `ParametersList.Count is 0 or 1`,
  return type `Standard.Void` or `Standard.Int32`
- Exactly one candidate -> return its declaring type's `FullyQualifiedName`
- Zero or more than one -> return null and let Roslyn report it

Deliberately **not** handled, and worth a comment saying so:

- Top-level statements. `TypeBuilder.Build` ignores `StatementItemTopLevelItemNode`
  entirely, so v2 has no TLS support at all -- there is nothing to select.
- `CompilerOptions.EntryPoint` (the `entry_point` key in `mist.toml`). No v2 sample sets it,
  and it is ambiguous whether the configured value is a simple name or an FQN.

### Step 11 -- `Source/Mist/Backends/CSharpTranspiler/Emitter/CSharpEmitterV2.cs`

Pass `mainTypeName:` into the `CSharpCompilationDriver.Compile` call at lines 72-78.

`ProjectFileEmitter` needs no change: the emitted `.csproj` is cosmetic in the v2 path (the
real build is the in-process Roslyn call), so `<StartupObject>` is optional parallel work.

### Step 12 -- remove the `Skip`

Delete the `Skip = "..."` argument on the `[Fact]` at
`Test/Mist.Tests/Integration/SamplesCompileAndMatchBaselineTest.cs:71`, leaving `[Fact]`.
Also refresh the XML doc above it if it still describes the two gaps as open.

## Verification

1. `dotnet build Mist.slnx` -- must be **0 warnings**. The zero-warnings policy is enforced.
   Do a full rebuild before believing it: a CS8604 in `LiftedType.cs` once hid behind an
   incremental build for an entire commit.
2. `dotnet format Mist.slnx --verify-no-changes --include Source/Mist/ProgramModel/` for the
   binder edits, and the same over the emitter folder. `.editorconfig` sets
   `insert_final_newline = false`, so new files must **not** end with a trailing newline;
   `end_of_line` is `unset`, so line endings follow the platform. Easiest either way is to let
   `dotnet format Mist.slnx --include <folder>` normalise new files before verifying.
3. `dotnet test Mist.slnx` -- baseline before this work was **5294 passed, 0 failed,
   1 skipped**, and the 1 skipped is the test being enabled. Target is 5295 passed, 0 skipped.
4. Sanity-check the emitted orchestrator by hand: `samples/AllFeaturesV2/out/AllFeaturesV2/Program.cs`
   should contain three qualified calls and no `/* unsupported: ... */` comment.

`BodyBindingIntegrationTests` was reviewed and asserts nothing about namespace-qualified
access, so it should be unaffected -- but it does assert "no sentinels" on a Simpliest-shaped
program, so it is the fastest signal if binding regresses.

## Scope notes

When the test goes green, `samples/AllFeaturesV2` becomes the authoritative record of v2
progress and `backlog/emitter-v2-sample-progression.yaml` stays a planning artifact only --
its `status:` field is advisory per `.kiro/steering/testing-philosophy.md` Tier 6. The
steering also permits exactly two Tier 6 tests, one per pipeline, so do not add a third
orchestrator.

Deliberately independent of this goal, do not get drawn in: the Storm labelled-repetition
defect across 14 rules and other items in `backlog/storm.md`, `TypeParameter.Domain` never
being populated, and interned-type name collisions.

Also worth remembering: "works standalone on v2" and "works composed on v2" are different
claims. Simpliest, Primitives and ArrayLiterals each already run correctly standalone;
composition is what additionally exercises cross-project reference loading,
namespace-qualified access and entry-point selection. Project reference resolution itself is
pipeline-agnostic (Stage 1, shared; the pipelines only diverge at Stages 4-6), and all four
referenced projects already emit correctly into `samples/AllFeaturesV2/out/`. Only the
orchestrator's own `Program.cs` is malformed.

## Reference map

| What | Where |
|---|---|
| The skipped test | `Test/Mist.Tests/Integration/SamplesCompileAndMatchBaselineTest.cs:71-100` |
| Sample + baseline | `samples/AllFeaturesV2/{mist.toml,Program.mist}`, `samples/__baselines__/AllFeaturesV2.out` |
| Identifier binding | `Source/Mist/ProgramModel/Construction/Binding/Expressions/IdentifierBinder.cs` (30 lines) |
| Member-access folding | `.../Expressions/PostfixBinder.cs:28-66`, `ResolveMemberType` at 88-121 |
| Primary dispatch | `.../Expressions/ExpressionBinder.cs:37-59` |
| Name resolution | `.../Resolution/ScopeResolver.cs`: `Lookup` 388, `ResolveByNamespace` 471-546, `LookupInNamespace` 549-560 |
| Expression node kinds | `Source/Mist/ProgramModel/Model/Expressions/` (15 files) |
| Namespace registry | `Model/ProgramModel.cs:20-24` + `GetOrAddNamespace` 58; registration in `Construction/Scaffolding/NamespaceBuilder.cs` |
| Type registration | `Construction/Types/TypeBuilder.cs`, `FinalizeRegistration` |
| Expression emit | `Backends/CSharpTranspiler/Emitter/BodyEmitter.cs:196-300` (`default` arm at 298 produces the `/* unsupported */` text) |
| Type-name emit | `Backends/CSharpTranspiler/Emitter/TypeNameEmitter.cs`, `MapMemberType` |
| V2 emit + compile | `Backends/CSharpTranspiler/Emitter/CSharpEmitterV2.cs:72-78` |
| Classic's CS0017 fix | `Backends/CSharpTranspiler/CSharpTranspilerBackend.cs:199-325` |
| Roslyn `mainTypeName` | `Backends/CSharpTranspiler/CSharpCompilationDriver.cs:236, 273` |
| Pipeline wiring | `Driver/CompilerDriver.cs:48-49, 95-100`; `Driver/ExperimentalPipeline.cs` |
| Stdlib `.mist` sources | `Source/Standard/` -- **not** a root `Standard/`; repo-root searches otherwise match only the build copies under `bin/` |
