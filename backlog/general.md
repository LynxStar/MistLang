# Future Work

Tracked issues and improvements that don't belong to an active spec but should be addressed.

---

## 🟡 Classic only: Array (and Span) indexer upper bound is deferred to runtime for unknown-length types

**Severity: critical** — Mist advertises static bounds safety via the `ArrayBounds` refinement (`>= 0 and < Length`), but that guarantee silently degrades to a runtime check whenever the length is not a compile-time constant. A program can compile with zero errors and throw `System.IndexOutOfRangeException` at runtime.

**Verified empirically** — the following compiles clean (0 errors, 0 warnings) and crashes at runtime:

```mist
int[] numbers = new int[3];
numbers[7] = 10;   // compiles; throws System.IndexOutOfRangeException at runtime
```

**Root cause:** `ValidateArrayIndexRefinement` in `Source/Mist/Analysis/Builtin/TypeCheckerAnalyzer.cs` (~line 13059). When an array's `ArrayType` has neither `KnownLength` nor `SymbolicLengthId` (the "unknown length" case), only the lower bound `>= 0` is enforced statically and the upper bound is "deferred to runtime — matching C# semantics." Crucially, declaring a local with an explicit array annotation (`int[] numbers = new int[3]`) **erases** the known length `3` to unknown, so even a freshly-created concrete-length array loses its static upper-bound protection the moment it is bound to an annotated local. A literal index `>= 0` is accepted (the literal-fallback branch returns when `!KnownLength.HasValue`), and non-constant indices on unknown-length arrays are accepted unconditionally (Case 4 returns without a diagnostic).

**Why this matters now:** The `stack-only-types` spec deliberately mirrors `Array` for `Span<T, Length>` (Option 1), so spans inherit this exact hole. Closing the array gap closes it for spans too, since spans reuse the same bounds machinery.

**Closed on v2 (2026-09-11):** analysis order 12 refuses an unproven index, read or written, into any array or span of unknown length, whether a local or a field of `this` holds it; the example above is `MIST_PA_INDEX_OUT_OF_BOUNDS` there. A field-held array carries the same symbolic bound `i < items.Length` a local does, forgotten on any assignment to the field and, unless the field is `readonly`, at any call, object creation or `await`. Two samples indexed a field-held array without a bound and were changed to prove it (`Iterators`'s enumerator and `Attributes`'s indexer, getter and setter; the spec proof code follows them). Everything above this paragraph now describes classic alone.

**Action (to scope into its own spec):**
1. Decide the intended guarantee: should unknown-length indexing be a hard compile error (no runtime fallback), or should it require an explicit opt-in / pattern-match to index?
2. Stop erasing `KnownLength` when a known-length creation is bound to an annotated local of the same element type (narrow the local's tracked length, like nullable flow narrowing) so `int[] x = new int[3]` retains length 3.
3. Re-evaluate the literal-fallback branch (`longVal >= 0 && !KnownLength.HasValue → return`) and Case 4's unconditional accept for unknown-length arrays.
4. Apply the same tightening to `Span<T, Length>` / `ReadOnlySpan<T, Length>` once they exist.
5. Audit the blast radius: existing samples/tests/snippets that rely on runtime-deferred indexing into unknown-length arrays will need updating.

---

## 🟡 Classic cannot compose DelegateFuncConversion beside a Mist type that implements a CLR interface

**Severity: medium** -- `samples/AllFeatures` is the classic regression guard, and it now lacks one sample that classic compiles fine standalone: `DelegateFuncConversion`.

**Verified empirically** (2026-09-11) -- a root project referencing `DelegateFuncConversion` plus a second project whose only content is `public class Manual : IDisposable { public void Dispose() { } }` fails on classic with:

```
Program.mist(46,43): error MIST_TC_0001: Cannot implicitly convert type 'Func<[Int32], Boolean>' to 'Predicate<Int32>'.
Program.mist(52,44): error MIST_TC_0001: Cannot implicitly convert type 'Predicate<Int32>' to 'Func<[Int32], Boolean>'.
```

The same root without the second project compiles and runs, as it does when the second project only uses CLR types (`using System;`, `new IO.MemoryStream()`, `new System.IO.MemoryStream()`) or declares a `[global_usings]` table. In the sample tree the two carriers are `DisposableDropInterop` (`ManualDisposable : IDisposable`) and `Attributes` (`ParamTagAttribute : Attribute`); each alone beside `DelegateFuncConversion` reproduces it, and the other 70 were cleared in groups of 18, 6 and 1. The experimental pipeline composes all of them.

**Consequence:** `DelegateFuncConversion` is banked on `AllFeaturesV2` but not on `AllFeatures`, and classic cannot compile `AllFeaturesV2` at all, so that baseline's last 11 lines are assembled from classic's standalone run (see the sample's entry in `backlog/emitter-v2-capability-progression.yaml`).

**Action:** none in classic, which the v2 rewrite replaces. Cite this entry if `AllFeatures` is ever expected to be the complete sample list.

---

## 🟡 Classic's `mist test` fails on any test project without a `Main`

**Severity: medium** -- `mist test samples/Testing` on classic ends in `CSC : error CS5001: Program does not contain a static 'Main' method`, and has since 12341680 (2026-06-28) added `<GenerateProgramFile>false</GenerateProgramFile>` to every library project. That is right when the library composes an executable (two entry points are CS0017) and wrong when nothing carries a `Main`: `Microsoft.NET.Test.Sdk` still makes the output an executable, and there is then no entry point at all. Nothing tested the command, so nothing noticed.

**Where v2 stands:** `ProjectFileEmitter` emits the suppression only when an assembly in the program carries an entry point (emitter order 120), and `Testing_RunsUnderTheTestCommandOnTheExperimentalPipeline` holds `mist test` on v2 to the runner's tally. Classic's `CSharpEmitter.EmitCsproj` still writes it unconditionally and knows nothing about entry points at that point.

**Action:** none in classic, which the v2 rewrite replaces; run `mist test` with `--pipeline experimental`.

---

## 🟡 Remove remaining Array/Span special-case paths in TypeCheckerAnalyzer

**Severity: medium** — The compiler still has hardcoded fast-paths for array/span indexer element-type resolution and `.Length` that bypass the stdlib declarations. These should be removed so the stdlib `.mist` files are the single source of truth.

**What remains hardcoded:**

1. **Indexer element type** — `arr[i]` resolves via `if (targetType is ArrayType) return arrayType.ElementType` before searching the member scope. Same for `span[i]` with `SpanType`. The stdlib declares `intrinsic public TValue this[ArrayBounds index]` but that indexer is never consulted at use sites.

2. **`.Length` property** — `arr.Length` resolves via a hardcoded `if (memberName == "Length") return Int32` check for `ArrayType`/`SpanType` before the member-scope lookup path runs.

**Why this wasn't done in the refinement-constraints spec:** The design doc explicitly scoped these out ("what remains unchanged") to keep the blast radius manageable. Each removal requires the same bridge pattern already proven for constructors and foreach: `FindRegisteredClassType` → look up member → substitute type parameters (`TValue → elementType`, `Length → unknown`).

**The interesting architectural question:** The indexer parameter type in `Array.mist` is `ArrayBounds` (`type ArrayBounds = ZeroOrPositiveInt and < Length`). If indexer resolution went through member lookup, the bounds-checking analyzer could validate against the *declared* refinement predicate on the parameter rather than implementing bounds checking as a parallel hardcoded system. This is the path toward the compiler genuinely using refinement types for bounds safety — but it's a significant change to the analysis pipeline.

**Action:**
1. Remove the `if (targetType is ArrayType)` indexer fast-path in `ResolvePostfixSuffix` → `IndexSuffix`. Route through the `FindRegisteredClassType` bridge + type argument substitution (same pattern as constructor/foreach bridges).
2. Remove the `if (memberName == "Length")` fast-path in `LookupMemberType` for `ArrayType`/`SpanType`. The `.Length` property is already registered on the stdlib ClassType/StructType.
3. Verify bounds checking still works — it currently uses `ArrayType.KnownLength` directly, which is orthogonal to the member-lookup path.
4. Consider (separate spec) whether `ValidateArrayIndexRefinement` should consume the `ArrayBounds` refinement predicate from the indexer parameter declaration rather than reimplementing bounds logic.

---

## 🟢 Newtypes cannot be inherited from

**Severity: low (follow-up feature)** — Newtypes produce `NewtypeType` rather than `ClassType`, so using a newtype in an inheritance position (`class Derived : MyNewtype { }`) does not currently work. The base type resolution logic expects a `ClassType` and rejects newtypes.

**Expected behavior:** `class Derived : MyNewtype { }` should unwrap the newtype to its source class type during base type resolution, allowing inheritance through newtypes the same way type aliases are unwrapped.

**Context:** The `unified-type-resolution` spec (Task 4) added unwrapping for `TypeAliasType` in `Pass1_ResolveBaseTypes`, but newtypes were intentionally left out because `NewtypeType` semantics are richer (member cloning, distinct identity). Supporting inheritance from newtypes requires deciding whether the derived class inherits from the *newtype's source class* directly or whether newtypes should carry class-like inheritance semantics of their own.

**Action:**
1. In `Pass1_ResolveBaseTypes` / `Pass1_ResolveStructBaseTypes`, add unwrapping for `NewtypeType` → `.SourceType` when the source type is a `ClassType` or `StructType`.
2. Decide whether the derived class's base should be the newtype itself or the unwrapped source class (affects `is`/`as` casts and virtual dispatch).
3. Add tests for single-level and chained newtype inheritance.
4. Ensure `NewtypeType` cycle detection handles this case.

**Not part of:** the `unified-type-resolution` bugfix spec — this is a new feature.

---

## 🟡 Add `ResolvedType` non-nullable accessor to Symbol for post-construction consumers

**Severity: medium (architectural hygiene)** — `Symbol.DeclaredType` is `IType?` because the symbol exists before its type is resolved (Phase 1 creates the shell, Phase 4 fills the type). This nullable property is used by both construction-phase code (which legitimately reads null to check "is this resolved yet?") and post-construction code (Pass 2+, which should never see null).

**Problem:** Post-construction consumers (Pass 2, type checker, emitter) do null checks on `DeclaredType` and take silent fallback paths when it's null. This hides bugs where Phase 4 missed a symbol — the user gets wrong inference instead of a crash with a stack trace.

**Proposed fix:** Add a phase-gated accessor that separates the two use cases:

```csharp
// Construction API — used by Phases 1–4b. Nullable. No crash.
public IType? DeclaredType { get; internal set; }

// Post-construction API — used by Pass 2+. Non-nullable. Throws if null.
/// <summary>
/// Returns the resolved type for this symbol. This accessor is intended for
/// post-construction consumers (Pass 2 and later). If this throws, it means
/// Phase 4 failed to resolve this symbol's type — that is a compiler bug in
/// the resolution pipeline, not something to work around with a null check.
/// </summary>
public IType ResolvedType => DeclaredType
    ?? throw new InvalidOperationException(
        $"Symbol '{Name}' ({Kind}) has no resolved type. " +
        $"Phase 4 (Pass1_5_ResolveExplicitMemberTypes) should have set DeclaredType before Pass 2 runs.");
```

**Migration:**
1. Add `ResolvedType` to `Symbol`.
2. In Pass 2 and all downstream consumers, replace `symbol.DeclaredType` reads with `symbol.ResolvedType`. Remove null checks — they become dead code since `ResolvedType` is non-nullable.
3. Phase 4 and earlier phases continue using `DeclaredType` (nullable) since they're doing the writing and need to read partially-constructed state.
4. Any remaining `DeclaredType is not null` checks in Phase 4 are legitimate "skip if already resolved" guards — but ideally Phase 4's iteration should be fixed to visit each symbol exactly once, eliminating those too.

**Why not just make `DeclaredType` non-nullable?** Because construction-phase code legitimately reads it before it's set. The two-accessor pattern lets construction code and consumption code have different contracts on the same underlying storage.

**Why not `Debug.Assert`?** It only fires in debug builds. A null `DeclaredType` in production causes silent wrong compilation. The throw fires always, giving a clear error message pointing at the phase that should have resolved it.


---

## 🔴 Expression lambda return type not validated against target delegate

**Severity: critical** — When an expression lambda's body produces a type that is not assignable to the expected delegate return type, the compiler silently accepts it. This defeats type safety for fallible operations (like enum reverse casts returning `T | void`) used inside lambdas.

**Reproduces in Pendragon:**

```mist
qt.Property(q => q.Resolution).HasConversion(v => v, v => (ResolutionTier)v);
```

`(ResolutionTier)v` returns `ResolutionTier | void` (the cast is fallible). But `HasConversion` expects a `Func<int, ResolutionTier>` — the lambda must return `ResolutionTier`, not `ResolutionTier?`. This should be a compile error but passes silently.

**Two separate sub-problems:**

1. **Expression lambda body type check is missing.** Block lambdas validate return statements against the expected type via `CheckBlock(body, expectedReturnType)`. Expression lambdas just call `CheckExpression(body)` without comparing the result to what the delegate expects. A partial fix was added (checks union-body into non-union target) but it's scoped narrow to avoid false positives from async auto-wrapping and currying.

2. **Generic method type arg inference doesn't propagate into lambda target types.** In `HasConversion<TModel, TProvider>(...)`, the generic type args are inferred from the property builder context. But `ResolveArgumentListWithParameterTypes` only propagates target types into lambdas when it already has the substituted parameter types. For generic methods where type args are inferred from the receiver or sibling arguments, the lambda's expected return type is never known — so even if the body type check existed, it wouldn't have the information to fire.

**Why it "works" at runtime:** The emitted C# record class is a reference type, so `ResolutionTier?` in C# is just a nullable annotation — same CLR type. The C# compiler accepts the assignment. At runtime, if no variant matches, `null` propagates and eventually throws NullReferenceException.

**Correct behavior:** Mist should emit a compile error forcing the user to handle the void case:

```mist
qt.Property(q => q.Resolution).HasConversion(v => v, v => (ResolutionTier)v ?? ResolutionTier.R1080p);
```

**Action:**
1. Fix expression lambda body type validation — compare `model.GetTypeOf(body)` against `GetFuncReturnType(resolvedType)` when the lambda has a resolved Func target type. Handle async wrapping (body `T` → expected `Task<T>`) and avoid false positives.
2. Fix generic method inference propagation — after inferring type args for a generic method, substitute them into parameter types BEFORE resolving lambda bodies. This is the same pattern `ResolveArgumentListWithParameterTypes` already does when parameter types are known; it just needs to also handle the case where parameter types require substitution from inferred type args.
3. Add a test case: expression lambda with `(EnumType)v` inside a generic method that expects `Func<underlying, EnumType>` — must produce a diagnostic.

**On the experimental pipeline (measured 2026-09-12):** sub-problem 1 does not arise — Stage 5's assignability rule judges an expression lambda's result against its target — and the Pendragon shape is refused now that a cast to an enum is typed as the optional it is (enums spec Requirement 15): `func<int, Tier> f = v => (Tier)v;` reports `MIST_PA_NO_IMPLICIT_CONVERSION`, 'Tier | void' to 'Tier'. Sub-problem 2's v2 counterpart -- stated type arguments not reaching a lambda argument's target, which refused `Apply<int, string>(1, v => v.ToString())` where classic prints `1` -- is closed by 1583cdfa, and with both halves in place this entry's own test case is refused on v2: `Tier t = Apply<int, Tier>(7, v => (Tier)v);` against `TOut Apply<TIn, TOut>(TIn value, func<TIn, TOut> f)` reports `MIST_PA_NO_IMPLICIT_CONVERSION` at the lambda body. The entry stays open for classic.

---

## 🔴 CRITICAL: AST and CompilationUnit arrays can go out of sync in the driver

**Severity: critical** — The compiler driver maintains two parallel arrays — `compilation.Units` (metadata for each source file) and `asts` (parsed syntax trees) — that are correlated by positional index. When a unit fails to lex or parse, its AST is excluded from `asts` but its entry remains in `compilation.Units`. This causes every subsequent index to be off by one, silently pairing the wrong metadata with the wrong AST.

**Root cause:** In `CompilerDriver.Parse()`, the `asts` builder only adds entries for units that successfully parse (`unitParseResult.Root is not null && !parseBag.HasErrors`). Meanwhile, `compilation.Units` is never filtered — it retains all units from Stage 1 regardless of lex/parse outcome. The `PipelineInput` record bundles these two arrays without an invariant assertion that they have equal length.

**Current impact:** Low in practice because:
1. Stdlib files are well-tested and never fail.
2. User files that fail to *read from disk* are never added to `compilation.Units`.
3. If any unit fails to lex/parse, the resulting error diagnostics are reported and the classic pipeline's `SemanticModelBuilder` has its own internal correlation logic.

However, `ProgramModelBuilder.Build()` in the experimental pipeline uses raw index-based pairing (`compilation.Units[i]` ↔ `asts[i]`), which will silently produce wrong results if the arrays ever diverge. The existing null guard (`i < compilation.Units.Length ? compilation.Units[i] : null`) protects against the wrong direction (asts longer than units) and masks the real bug (units longer than asts → wrong pairing).

**Proposed fix (choose one):**

1. **Paired tuples** — Change `ParseResult` to return `ImmutableArray<(CompilationUnit Unit, SyntaxNode Ast)>` instead of two separate arrays. Consumers iterate tuples, making misalignment impossible by construction.

2. **Filter Units in sync** — When a unit's AST is excluded from `asts`, also exclude the corresponding unit from `compilation.Units`. Both arrays shrink together.

3. **Assert the invariant** — Add `Debug.Assert(asts.Length == compilation.Units.Length)` at the top of `ProgramModelBuilder.Build()` and in `PipelineInput` construction, so any divergence crashes immediately with a clear message instead of silently corrupting.

Option 1 is the strongest guarantee (misalignment is structurally impossible). Option 3 is the cheapest short-term fix.

**Affected code:**
- `Source/Mist/Driver/CompilerDriver.cs` — `Parse()` method
- `Source/Mist/Driver/Contracts/PipelineInput.cs` — record definition
- `Source/Mist/ProgramModel/Construction/ProgramModelBuilder.cs` — `Build()` loop
- `Source/Mist/Semantics/SemanticModelBuilder.cs` — same parallel-array pattern (classic pipeline)

---

## 🟡 Parameter-level attributes not supported

**Severity: medium (missing feature)** — Mist supports class-level and method-level attributes (e.g. `[ApiController]`, `[HttpPost]`), but does not support attributes on parameters. This means attributes like `[FromBody]`, `[FromQuery]`, `[FromRoute]`, `[FromHeader]`, and `[FromServices]` cannot be expressed in Mist.

**Current workaround:** For ASP.NET controllers, applying `[ApiController]` at the class level enables automatic model binding from the request body for complex types, so `[FromBody]` is not required. However, this does not cover:

- `[FromQuery]` — binding from query string parameters
- `[FromRoute]` — binding from route template segments
- `[FromHeader]` — binding from HTTP headers
- `[FromServices]` — binding from the DI container
- Validation attributes on parameters (e.g. `[Required]`, `[Range]`, `[StringLength]`)
- `[CallerMemberName]`, `[CallerFilePath]`, `[CallerLineNumber]` — compiler-injected caller info
- `[EnumeratorCancellation]` — for async iterators
- `[NotNullWhen]`, `[MaybeNullWhen]` — nullable flow analysis annotations

**Scope:** This requires changes to:
1. **Syntax** — the grammar needs to allow `[Attribute]` before a parameter name in function/method/constructor/lambda parameter lists.
2. **Semantic model** — `ParameterSymbol` needs an `Attributes` collection (currently only `ClassSymbol`, `MethodSymbol`, etc. carry attributes).
3. **C# emitter** — attribute metadata must be emitted on the parameter in the generated C# output.

**Action:**
1. Design the syntax (likely `fn handle([FromQuery] name: string, [FromBody] request: CreateRequest)` mirroring the class/method attribute syntax).
2. Extend the parser to recognize attribute lists in parameter position.
3. Add `Attributes` storage to `ParameterSymbol`.
4. Update the C# backend to emit parameter attributes.
5. Add tests covering ASP.NET binding attributes, caller info attributes, and nullable annotations.

---

## 🟡 STORM0006 false positive: MemberDeclaration alternative 13 (ModifiedAssociatedTypeMember) flagged unreachable

**Severity: medium (build warning, no runtime impact)** — The Storm source generator's reachability pass (`ReachabilityPass.cs`) flags alternative 13 in `MemberDeclaration` as unreachable. This is a false positive caused by bounded k=3 lookahead being insufficient to distinguish `MemberTypeAliasMember` (alt 10) from `ModifiedAssociatedTypeMember` (alt 13).

**Why it's a false positive:** Both alternatives start with `Modifier+ TYPE IDENTIFIER`, making their FIRST-3 sequences identical. The disambiguating token (`=` for alias, `:` or `;` for slot) appears at position 4+ — or even further out when `TypeParams?` is present (e.g., `public type Foo<T> : Bar;`). Since the PEG parser uses unlimited backtracking, it correctly tries alt 10 first, fails (no `=`), backtracks, and eventually reaches alt 13. The warning is wrong — the alternative IS reachable at runtime.

**Why bumping `MaxLookahead` doesn't fully fix it:** The optional `TypeParams?` between `IDENTIFIER` and the disambiguating `=`/`:` is variable-length (`<T>`, `<T, U>`, `<T, U, V>`, etc.). No finite lookahead depth can always see past it. Bumping to 4 would only silence the warning for the no-type-params case.

**Verified correct at runtime:** The `AssociatedTypeDisambiguationPropertyTests` and `EmitModifiersOnAssociatedTypeSlotDiagnostic` semantic handler confirm that `ModifiedAssociatedTypeMemberMemberDeclarationNode` is produced by the parser and handled correctly by the semantic layer (emitting a diagnostic when used in wrong context).

**Options:**
1. Add a `@suppress(STORM0006)` annotation to the grammar rule (requires adding suppression support to the source generator).
2. Restructure the grammar to factor out the common `Modifier+ TYPE IDENTIFIER TypeParams?` prefix into a shared rule, then disambiguate on the next token.
3. Accept the warning with a comment in the grammar explaining why it's safe.

**Context:** Introduced by the grammar-type-declaration-unification spec (task 1.8), which moved `ModifiedAssociatedTypeSlotDeclaration` from `AbstractMemberDeclaration` (now deleted) into the unified `MemberDeclaration` rule.

---

## 🟡 MIST_TC_0015 / MIST_TC_0016 reach the user with unformatted `{0}` placeholders, and both fire at once

**Severity: medium** — two static/instance access diagnostics in the classic pipeline are reported without substituting their message arguments, so the user sees a literal `'{0}'` where the member name belongs. The same program also triggers both at once, and they contradict each other.

**Verified empirically** — this program, on the classic pipeline:

```mist
public class Outer
{
    public static int Shared = 7;

    public class Inner
    {
        public static int Read()
        {
            return Shared;
        }
    }
}
// ... Outer.Inner.Read() at the call site
```

reports:

```
Program.mist(22,40): error MIST_TC_0016: Instance member '{0}' cannot be accessed via a type name
Program.mist(22,46): error MIST_TC_0015: Static member '{0}' cannot be accessed via an instance
```

Two separate defects in one output:

1. **Unformatted placeholders.** Neither message had its arguments applied. `DiagnosticBagExtensions.ReportAtNode` only calls `string.Format` when `args.Length > 0`, so a call site that omits the arguments emits the template verbatim. Whatever reports these two passes none.
2. **Contradictory pair.** `Read` is a static method reached through a type name, which is correct usage. One of the two diagnostics is wrong about what it is looking at, and arguably both are — the real problem in that program is reaching a nested type's member at all.

**Why this matters:** a diagnostic that prints `{0}` tells the user nothing about their own code, and a contradictory pair leaves them no way to work out which one to act on. Both are in the shipping pipeline rather than the experimental one.

**Action:**
1. Find the report sites for `MIST_TC_0015` and `MIST_TC_0016` and pass the member name.
2. Work out why both fire for one access, and which is correct for a static member reached through a nested type name.
3. Consider whether `ReportAtNode` should fail loudly when a template contains `{0}` and no arguments are supplied, so the next instance of this cannot reach a user silently.

## MIST_VAR_0012 extends a range the variadics spec enumerates

`.kiro/specs/variadics/requirements.md` lists MIST_VAR_0001 through MIST_VAR_0011 under
"Diagnostics", and the code now also defines **MIST_VAR_0012**: a homogeneous `params T[]`
that is not the last parameter.

**Why it exists:** the arguments a params array collects are whatever runs off the end of the
fixed ones, which no earlier slot can gather. The declaration is not well-formed Mist, and
stays ill-formed under a backend that is not C# -- so it is the compiler's to reject, not the
target's. Before it, both pipelines let the target refuse instead: classic surfaced CS0231
against a generated file, and v2 surfaced only the call-site arity error, which points nowhere
near the cause. An LLVM or WASM backend would have had to rediscover the rule for itself.

**Why it is here rather than in the spec:** Requirement 13 governs Value_Level_Params and
says the call site supplies "trailing arguments (those supplied after the fixed leading
parameters)", which implies the position but states no diagnostic for breaking it. The code
now says something the spec does not.

**Action:** decide whether the requirement should name MIST_VAR_0012 and add it to the
spec's diagnostic table, or whether the rule belongs outside the MIST_VAR range entirely --
it is the one `params` rule that has nothing to do with packs.

## `owned` on a binding initialised from a string-valued local does not parse

Reproducible, and reduced as far as it goes:

```mist
var borrowed = "x";
owned var s = borrowed;   // MIST_PARSE_INCOMPLETE, reported at the enclosing class
```

The same shape over a class-typed local parses:

```mist
Config other = new Config();
owned var s = other;      // fine
```

So does `owned var s = new Config();` and `owned Config s = other;`. Only the string-valued
source fails, which is strange for a PARSER -- it knows no types -- so the interesting question
is what else differs between the two, not the ownership specifier itself.

The error surfaces at the enclosing class rather than the statement, which is the
`MIST_PARSE_INCOMPLETE` behaviour CLAUDE.md warns makes the signature look at fault.

Found while writing tests for order 157; nothing in samples/ hits it, because the sample uses the
TYPED form (`owned Config implicitClone = shared;`) everywhere. Not investigated further -- it is a
parser question and the clone work it turned up in is unrelated.

## AnonymousTypes: a property pattern over an `unknown` scrutinee

CLOSED. samples/AnonymousTypes is banked on AllFeaturesV2; the reasoning is recorded on orders 93,
180 and 181 of `backlog/emitter-v2-capability-progression.yaml`.

The statement channel this entry called for was never built, and was not needed. The tests go into the
CONDITION instead -- a null test that binds the scrutinee, plus one `((dynamic)p).Member is <sub>` per
member joined on with `&&`, which the existing `hoisted` list already carries. That leaves the
bindings readable in the rest of the condition as well as in the branch, which classic's hoisted
statements cannot offer, and every subpattern shape survives because deferring the read defers the
whole subpattern to the runtime binder.

---

## 🟢 Every book snippet regenerates on the experimental pipeline

**As of 2026-09-11**, the day `tools/SnippetEmitter` moved from classic to `--pipeline experimental`; 58 of
368 failed that morning. By evening 30 named the sibling snippets they build on with a `// with:` line,
which the tool compiles alongside them (four keep a declaration inline where their own chapter's
pages overlap); 5 imported the CLR namespaces they used or declared the attribute they applied; 2 were
content bugs classic's tool never caught because it skipped Roslyn; 10 placed a bare or `public
static` function at file top, which the grammar has never accepted -- Mist has no local functions --
and are now classes with a `Main`; 2 variadic declaration snippets keep a hand-written C# tab marked
as an illustration, by decision, since a monomorph is emitted only per arity used; and 10 were v2
defects since fixed: nested local shadowing, union-into-union widening, span slicing, a CLR delegate
as a lambda's target, the element type of a projected `List<int>`, a method generic over a
slot-bearing abstract with `T.Output`, narrowing after `??=`, a union alias over a type parameter,
and a static member reached through a type alias, `Task.Yield()`, in both binder and emitter.
Nothing under `book/snippets/` is outside the tool. Later the same day local functions landed
(`.kiro/specs/local-functions`, emitter order 187), and the ten fragments went back to the shape they
were written in -- a bare or `public static` function at file top -- and regenerate as such.

---

## 🟢 Representation choice: `Array<void, n>` is a number, and no layer of the compiler can say so

**Severity: design note** -- no defect. Recorded from a 2026-09-11 design discussion so the analysis is not
re-derived.

**The observation.** An array's runtime content is `bits(T) * n`, plus `n` itself when the length is not in
the type. Two corners of one rule:

- `Array<bool, 32>` -- payload 32 bits, length static. That is what an `int` IS.
- `Array<void, unknown>` -- payload 0 bits, length dynamic. The only datum is the count, so it is `ℕ`
  (`List(1)` is the free monoid on one generator). Mist already names the target type: `ZeroOrPositiveInt`,
  declared in Array.mist as the array's own `Length` parameter.

**Where it bit.** Order 186's `MistTasks.WhenAll(Task[])` materializes `new Standard.Void[tasks.Length]` --
a unary encoding of a number, N bytes plus an array header for a value that fits in four, whose elements are
indistinguishable tally marks. The count it appends is information the CALLER already held, having passed the
array. Rust ships this erasure (`[(); N]` is zero-sized, `Vec<()>` is a bare counter); C# cannot, the CLR
forbidding zero-size instance types, so an empty struct is 1 byte. The erasure therefore cannot happen in the
C# emitter at all -- it has to be a Mist-side representation decision that emits an `int` and never mentions
an array to the backend.

**The classification.** Two questions that look alike and are not:

1. `Array<void, n>` is `ℕ` is a THEOREM of definitions already in the language, so lowering it is a compiler
   implementation detail -- no spec, no language change, free to differ per backend (Rust: literally nothing;
   C#: an int).
2. `type int = Array<bool, 32>` is DEFINITIONAL, and so a language semantic: a program could then rely on
   `int` having a `Length` of 32 and being indexable, forever.

The test that separates them: CAN A MIST PROGRAM TELL? If no program can distinguish the representations it
belongs to the compiler; if a program can say something new, it belongs to the language. Applied backwards,
`Task<void>` to `Task`, optionals to nullable, struct flattening and `void` returns to C# `void` are all
compiler; `void` being a real unit type with a writable value is language, which is why it has a spec.

**Why an alias is the wrong mechanism, even for (1).** Representational equality is not type identity: same
bits, different interface. The mechanism that fits is coercion over a shared representation -- Haskell's
`Coercible`/`coerce`, Rust's `repr(transparent)` and safe transmute. Iteration shows why the two must stay
distinct types: an array yields its ELEMENTS (19 meaningless `void`s) while a number yields its INDEX SET
(`0..18`, meaningful -- the type Mist already names `ArrayBounds`). Unifying the representation deliberately
does not unify the interface.

**What is missing to express (2).** Not tighter refinements. A refinement CONSTRAINS a value (`int >= 0`);
`struct Int<Length> : Bit[Length]` needs to COMPUTE a type from a value -- `Length - 1`, a conditional type,
a slice type. Mist has dependent parameters (values in type position) but no functions from values to types.
The floor of the model is two primitives, `void` and `bool` -- one inhabitant and one bit -- and everything
above is constructible, which would shrink the bootstrap set from thirteen types to two.

**Action, if taken up:**

1. Name the representation layer that already exists implicitly, scattered through the emitter as the
   ad-hoc decisions listed above.
2. Move the void-array lowering there, so the void `WhenAll` returns the count rather than an array.
3. Independent of all of the above, and cheap: the wrapper is unnecessary when the result is discarded
   (`await Task.WhenAll(pending);` in statement position needs no array), and three of its four members
   need not be `async` -- the generic `WhenAll` is a pass-through and both `WhenAny`s are
   `TaskExtensions.Unwrap`.

---

## 🔴 v2 drops the parentheses around a coalesce nested inside an operator

**Severity: critical** -- a silent wrong result. The program compiles and prints a plausible value that differs from classic's.

**Verified empirically** (2026-09-13) on `--pipeline experimental`:

```mist
using Standard;

namespace Co;

public class Program
{
    public static void Main(string[] args)
    {
        int? maybe = 5;
        int? absent = void;
        int total = 1 + (maybe ?? 0) + 2;
        int other = 10 + (absent ?? 1) * 3;
        Console.WriteLine($"total={total} other={other}");
    }
}
```

v2 prints `total=6 other=3`; classic prints `total=8 other=13`. The emitted C# is `int total = 1 + maybe ?? 0 + 2;` and `int other = 10 + absent ?? 1 * 3;`.

**Likely cause:** `Source/Mist/Backends/CSharpTranspiler/Emitter/BodyEmitter.cs` emits `CoalesceExpression`, and the precedence `GetPrecedence` gives it does not reflect that C#'s `??` binds looser than every arithmetic, comparison and logical operator.

**Action:**
1. Fix the precedence, and check the other low-precedence forms for the same gap: `is`, `as`, switch expressions, lambdas, assignment.
2. Pin it with a Tier 3 test on the emitted text. The failure compiles and prints a plausible value, so consider whether a Tier 4 run in the style of `Test/Mist.Tests/Integration/RegexEmissionTests.cs` is justified too. Mutation-check it.
3. Record evidence in the matching capability entry of `backlog/emitter-v2-capability-progression.yaml`, inside its `evidence: >` block.

---

## 🟡 v2 emits an optional reference field as non-nullable

**Severity: medium** -- the emitted project sets `<Nullable>enable</Nullable>`, so MSBuild reports CS8618, and `samples/CLAUDE.md` requires samples to build with zero warnings.

**Verified empirically** (2026-09-13) on `--pipeline experimental`:

```mist
using Standard;

namespace Nf;

public class Holder
{
    private string? note;

    public Holder()
    {
    }

    public string Show()
    {
        return note ?? "none";
    }
}

public class Program
{
    public static void Main(string[] args)
    {
        Console.WriteLine(new Holder().Show());
    }
}
```

It prints `none`, but `out/Nf/Program.cs` declares `private string note;` and assigns `this.note = null;` in the constructor, and `dotnet build out/Nf.csproj` reports CS8618 four times.

**Action:**
1. Read `FieldEmitter`, `TypeNameEmitter` and how `OptionalUnion` lowers `T | void` for a reference `T`. Decide whether the field -- and by the same reasoning locals, parameters and properties -- should carry `?`, checking what those positions already do before changing one in isolation.
2. Pin it with a Tier 3 test on the emitted text and mutation-check it.
3. Record evidence in the optional-union capability entry of `backlog/emitter-v2-capability-progression.yaml`.

---

## 🟡 v2 refuses an F-bounded constraint on any class

**Severity: medium** -- a valid program is refused.

**Verified empirically** (2026-09-13) on `--pipeline experimental`:

```mist
using Standard;

namespace P2f;

public abstract IRank<T>
{
    int CompareTo(T other);
}

public class Score : IRank<Score>
{
    public int Points;

    public Score(int points)
    {
        this.Points = points;
    }

    public int CompareTo(Score other)
    {
        return Points - other.Points;
    }
}

public class Sorted<T> where T : IRank<T>
{
    private T low;

    public Sorted(T a)
    {
        this.low = a;
    }
}

public class Program
{
    public static void Main(string[] args)
    {
        var sorted = new Sorted<Score>(new Score(9));
        Console.WriteLine("ok");
    }
}
```

Observed: `error MIST_PA_CONSTRAINT_NOT_SATISFIED: Type argument 'Score' does not satisfy the 'IRank<T>' constraint on type parameter 'T'.` Expected: admitted, printing `ok`. Check classic's verdict before deciding; its runtime behaviour is the contract, not its code.

**Root cause:** `Judge` in `Source/Mist/ProgramAnalysis/Rules/ConstraintAnalyzer.cs` calls `ConversionClassifier.Classify(argument, typed.ConstraintType, ...)` without substituting the closing's type arguments into the constraint type.

**Action:**
1. Substitute the declaration's type parameters by the closing's arguments (`TypeSubstitution`) before classifying. `TypeArgumentClosing` and `ConstructorConstraintClosures` record which declaration's parameters a closing closes.
2. Prove it at Tier 3 beside the existing constraint analysis tests (grep `Test/` for `ConstraintNotSatisfied`): an F-bounded admit, and a refusal where the argument implements the abstract over a different type. Mutation-check both.
3. Record evidence in `backlog/analysis-v2-capability-progression.yaml`, order 6 (declaration-shape checks).

---

## 🟡 A constraint error anywhere becomes the reason every clonable newtype cast is refused

**Severity: medium** -- misleading extra errors. The program is already refused, but each clonable cast reports a reason that has nothing to do with it.

**Observed** (2026-09-13, not yet reduced): a program held an unrelated constraint violation, `new Ranked<Score>(...)` where `Ranked<V>` is a newtype over `Sorted<V> where V : IRank<V>` (it hits the F-bounded entry above), and every cast between `Cell<int>` and its clonable newtype `IntCell` was also refused:

```
error MIST_PA_CLONE_CONVERSION_DOES_NOT_HOLD: Cannot cast 'Cell<int>' to 'IntCell'. The cast runs 'IntCell.Clone' with a 'Cell<int>' as this, and it does not hold at 'Clone': Type argument 'Score' does not satisfy the 'IRank<V>' constraint on type parameter 'V'.
```

**Root cause:** `Source/Mist/ProgramAnalysis/ProgramAnalysisRunner.cs` judges each conversion that runs a `Clone` body by running every analyzer over an `AnalysisScope` holding just that body, and `CloneConversionRefusals.Judge` takes the first error as the refusal reason. `ConstraintAnalyzer` ignores the scope and walks `context.Model.ConstructorConstraintClosures.Closings` for the whole model, so any program-wide constraint finding lands in every per-body run.

**Reproduction to confirm first** (unverified; once the F-bounded entry is fixed, use an ordinary violation):

```mist
using Standard;

namespace Leak;

public abstract IShow
{
    string Show();
}

public class Plain
{
    public Plain()
    {
    }
}

public class Holder<T> where T : IShow
{
    public Holder()
    {
    }
}

public class Tiger : IClonable<Tiger>
{
    private int stripes;

    public Tiger(int stripes)
    {
        this.stripes = stripes;
    }

    public owned Tiger Clone()
    {
        return new Tiger(stripes);
    }
}

public type Jaguar = new Tiger;

public class Program
{
    public static void Main(string[] args)
    {
        var bad = new Holder<Plain>();
        var jaguar = (Jaguar)new Tiger(3);
    }
}
```

Expected: one `MIST_PA_CONSTRAINT_NOT_SATISFIED` on the `Holder<Plain>` line and nothing at the cast.

**Action:**
1. Make the program-wide analyzers honour the analysis scope, or filter the per-conversion findings to diagnostics located inside the conversion body's syntax. Audit every analyzer in `ProgramAnalysisRunner.Analyzers` for the same shape.
2. Prove it in `Test/Mist.Tests/ProgramAnalysis/ClonableNewtypeConversionDecisionTests.cs` with an admit test whose program also holds an unrelated error, and mutation-check it.
3. Record evidence in the `newtype-declarations` entry (order 86) of `backlog/emitter-v2-capability-progression.yaml`, inside its `evidence: >` block.

---

## 🟢 Two tests change process-wide state while other test classes run beside them

**Severity: low** -- both are contained today, and both are the shape of defect that produced the mass failures in `backlog/flaky-tests.yaml`: a test writing state every other class in the testhost reads, under xunit's per-run shuffle of which classes run together.

1. `Test/Mist.Tests/Backend/TypeInspectorTests.cs` calls `Console.SetError` (lines 197 and 233) and restores it in a `finally`. `Console.Error` is process-wide, so anything another class writes to stderr during that window is captured by this test's writer instead, and a test asserting on stderr would lose it.
2. `Test/Mist.Tests/Integration/Vector3OperatorIntegrationTest.cs` (lines 186-187) writes the mutable static `TypeCheckerAnalyzer.useOperatorOverloadResolution` (`Source/Mist/Analysis/Builtin/TypeCheckerAnalyzer.cs:43`). It writes `true`, which the flag already holds, so it is inert; writing any other value would switch every classic compile in the process for the duration.

**Action:**
1. Give `TypeInspector` a `TextWriter` to report to rather than redirecting the process's stderr, or move the two tests into a collection with parallelization disabled.
2. Delete the flag write, since it restores the default, or the flag itself if nothing sets it to `false`.

Recorded during the flaky-tests investigation (2026-09-14), not fixed there, because neither has produced a failure.

---

## 🟡 Two spellings of a function type still do not parse: `new func<T, R>[n]` and `(func<T, R>)?`

**Severity: low** -- both are narrow, and each has a spelling that works today, so nothing is unreachable.

Found while giving the `func<...>` type reference its own array rank and optional suffix (order 104 of
`backlog/emitter-v2-capability-progression.yaml`, 2026-09-20). Measured on both pipelines, each
`MIST_PARSE_INCOMPLETE` at the enclosing class:

```mist
var made = new func<int, int>[2];      // MIST_PARSE_INCOMPLETE
(func<int, int>)? maybe = step;        // MIST_PARSE_INCOMPLETE
```

Two separate causes in `Source/Mist/Grammar/Mist.storm`:

1. `ElementTypeRef` -- the element type of an array CREATION -- has a primitive, an unknown and a named
   alternative and no `func` one, so `new func<int, int>[2]` cannot be written. The array literal
   `func<int, int>[] fs = [a, b];` is the way to make one, which is why nothing has needed it.
2. `ParenthesizedTypeRef` carries `arrayRank:ArrayRankSuffix?` but no `optional:QUESTION?`, so the
   parenthesized spelling admits `[]` and not `?`. The bare `func<int, int>?` now parses and runs on both
   pipelines, so the parenthesized one is redundant rather than needed.

**Action:**
1. For (1), add a `FUNC typeArgs:TypeArgumentList` alternative to `ElementTypeRef` and resolve it on the
   array-creation path. It is the element type only, so it takes no suffix of its own -- the `[n]` after it
   is the creation's.
2. For (2), weigh admitting `optional:QUESTION?` on `ParenthesizedTypeRef` against what it costs: `(x)?`
   in statement position is also the opening of a ternary whose condition is parenthesized, and the
   uninitialized-declaration alternative would see a type where an expression was written. Measure the
   corpus before changing it, as order 104's entry did.
