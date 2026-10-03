# Future Work

Tracked issues and improvements that don't belong to an active spec but should be addressed.

---

## 🟡 Classic only: Array (and Span) indexer upper bound is deferred to runtime for unknown-length types

**Closed (2026-10-01):** classic is deleted (`backlog/classic-retirement.md`), and with it `ValidateArrayIndexRefinement`. The surviving rule is the one the paragraph above describes; the action list below was classic's and is void.

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

**Closed (2026-10-01):** classic is deleted (`backlog/classic-retirement.md`). `AllFeaturesV2` is the one orchestrator and composes `DelegateFuncConversion`; `samples/AllFeatures` is deleted.

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

**Closed (2026-10-01):** classic is deleted (`backlog/classic-retirement.md`). `mist test` runs the standard pipeline with no flag, and `Testing_RunsUnderTheTestCommand` holds it to the runner's tally; `--pipeline experimental` is no longer a pipeline.

**Severity: medium** -- `mist test samples/Testing` on classic ends in `CSC : error CS5001: Program does not contain a static 'Main' method`, and has since 12341680 (2026-06-28) added `<GenerateProgramFile>false</GenerateProgramFile>` to every library project. That is right when the library composes an executable (two entry points are CS0017) and wrong when nothing carries a `Main`: `Microsoft.NET.Test.Sdk` still makes the output an executable, and there is then no entry point at all. Nothing tested the command, so nothing noticed.

**Where v2 stands:** `ProjectFileEmitter` emits the suppression only when an assembly in the program carries an entry point (emitter order 120), and `Testing_RunsUnderTheTestCommandOnTheExperimentalPipeline` holds `mist test` on v2 to the runner's tally. Classic's `CSharpEmitter.EmitCsproj` still writes it unconditionally and knows nothing about entry points at that point.

**Action:** none in classic, which the v2 rewrite replaces; run `mist test` with `--pipeline experimental`.

---

## 🟡 Remove remaining Array/Span special-case paths in TypeCheckerAnalyzer

**Closed (2026-10-01):** `TypeCheckerAnalyzer` is deleted with classic (`backlog/classic-retirement.md`), and the fast-paths went with it.

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

**Closed (2026-10-01):** `Symbol` and the phased classic model are deleted with classic (`backlog/classic-retirement.md`).

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

**Resolved 2026-09-27.** The production below has since changed, and the pass no longer compares
truncated FIRST-3 sets: it reports an alternative only when an earlier one is proved to win on
every prefix it can start with. The counterexample, proof limits and tests are in
`backlog/storm.md`, "Reachability proves shadowing".

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

**Closed (2026-10-01):** both descriptors and the analyzer that reported them are deleted with classic (`backlog/classic-retirement.md`).

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

**2026-09-20**: three had lapsed, each regenerating a stale C# tab -- and one of the three, the
expression-bodied property page, was showing C# that does not compile, having assigned a get-only
property in a constructor. `MIST_PA_NO_SET_ACCESSOR` was right there, C# refusing the same assignment as
CS0200, so the page moved and `Width` gained an expression-bodied `init`. The other two were
`MIST_PA_INDEX_UNPROVEN` and the pages were idiomatic, so the prover moved instead: a length is never
negative, which is what makes the negation of an equality usable, and a `Range` held in a local now sizes
the slice it takes. Both are recorded on order 12 of the analysis register. The tool then reported 370 of
370, 0 updated: those two tabs had happened to still match what the compiler emits, since only analysis
had refused them, so the one tab actually showing the wrong thing was the property page's.

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

**Closed on v2 (2026-09-25):** the program below prints `ok`, and a `Grade : IRank<Score>` closing `Sorted<Grade>` is refused as not satisfying `'IRank<Grade>'`. Every constraint is now judged as its closing fills it -- a sibling parameter, a receiver's, an extension block's, a constructor signature's -- see the 2026-09-25 paragraphs of order 6 (`declaration-shape-checks`) in `backlog/analysis-v2-capability-progression.yaml`. Classic refuses this program too (`MIST_CONSTRAINT_0009` against `'IRank<T>'`), so the admit follows the generic-constraints spec and C#, not classic. Everything below this paragraph is the entry as filed.

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

## 🟢 A constraint error anywhere becomes the reason every clonable newtype cast is refused

**Severity: medium** -- misleading extra errors. The program is already refused, but each clonable cast reports a reason that has nothing to do with it.

**Observed** (2026-09-13, not yet reduced): a program held an unrelated constraint violation, `new Ranked<Score>(...)` where `Ranked<V>` is a newtype over `Sorted<V> where V : IRank<V>` (it hits the F-bounded entry above), and every cast between `Cell<int>` and its clonable newtype `IntCell` was also refused:

```
error MIST_PA_CLONE_CONVERSION_DOES_NOT_HOLD: Cannot cast 'Cell<int>' to 'IntCell'. The cast runs 'IntCell.Clone' with a 'Cell<int>' as this, and it does not hold at 'Clone': Type argument 'Score' does not satisfy the 'IRank<V>' constraint on type parameter 'V'.
```

**Root cause:** `Source/Mist/ProgramAnalysis/ProgramAnalysisRunner.cs` judges each conversion that runs a `Clone` body by running every analyzer over an `AnalysisScope` holding just that body, and `CloneConversionRefusals.Judge` takes the first error as the refusal reason. `ConstraintAnalyzer` ignores the scope and walks `context.Model.ConstructorConstraintClosures.Closings` for the whole model, so any program-wide constraint finding lands in every per-body run.

**Fixed (2026-09-27, workstream 13):** revalidated the defect on `666c596c1`. Analysis now exposes a read-only view of the constraint and optional-application registries for each pass. Recording sites retain their binding scope, so a source method, its renamed clone, and conversions rebinding the same syntax are distinguishable. Conversion and clone membership follows syntax actually carried by the judged model, including retained source syntax, rather than the destination declaration's span. Reports share this boundary; refusal selection additionally checks the finding's file and location, and member attribution checks the file before choosing the innermost named span. Binding failures still take precedence; warnings and inaccessible-member analysis findings still do not refuse a conversion. All 24 direct-input `CloneAnalysisIsolationTests`, all 3,049 analysis tests, and the full 10,619-test solution suite passed with zero build warnings. Mutations restoring registry leakage, cross-file attribution, and skipping genuine conversion constraints each failed the intended regression. The runtime sample tests require an unsandboxed execution profile on this host; the full green ledger used that profile. The change addresses diagnostic contamination, not every remaining newtype conversion capability.

**Original source reproduction** (unverified; the regression tests construct an ordinary violation directly):

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

---

## 🟡 v2 cannot emit a generic method of a generic extension block, nor an extension method's `where`

**Closed on v2 (2026-09-26):** the program below prints `2`. A call or method group that writes a generic block's method's own type arguments writes the block's ahead of them, and every carrier declares its method's constraints -- a `where` clause, a `new()`, a `new(int)` interface, a dependent parameter's witness, a `default` witness parameter. `samples/GenericExtensionMethods` banks it V2-ONLY; see the 2026-09-26 evidence of `extension-block-declaration` in `backlog/emitter-v2-capability-progression.yaml`. Everything below this paragraph is the entry as filed.

**Severity: medium** -- a valid program fails in Roslyn against the generated file.

**Verified empirically** (2026-09-25) on `--pipeline experimental`, with no constraint involved:

```mist
using Standard;

namespace Ebu;

public class Animal
{
}

public class Dog : Animal
{
}

public class Box<T>
{
}

<T> extension(Box<T> self)
{
    public int Put<U>(U item)
    {
        return 1;
    }
}

public class Program
{
    public static void Main(string[] args)
    {
        var box = new Box<Animal>();
        var a = box.Put<Dog>(new Dog());
        var b = box.Put(new Dog());
        Console.WriteLine(a + b);
    }
}
```

Observed: `MIST_CS_ERROR: 'Box<Animal>' does not contain a definition for 'Put'`, once per call. The carrier is emitted `public static int Put<T, U>(this Ebu.Box<T> self, U item)` and both calls as `box.Put<Ebu.Dog>(...)` -- one type argument for a method of two, which C# cannot bind. Expected: `2`.

The same carrier drops every `where`: in a NON-generic block, `public int Count<U>(U shape) where U : IShape { return shape.Sides(); }` is emitted `Count<U>(this Ew.Holder self, U shape)` and fails as `'U' does not contain a definition for 'Sides'`.

Analysis already judges both constraints at the Mist source, the block's parameter included (order 6 of `backlog/analysis-v2-capability-progression.yaml`, 2026-09-25), so only emission is missing.

**Action:**
1. Read `ExtensionBlockEmitter` and the call emission in `BodyEmitter`. At a call to a method of a generic block, either write the block's arguments ahead of the method's or write none and let C# infer them; decide which by what C# can infer from the receiver when a method argument is written explicitly.
2. Give each carrier method the `where` clauses of the block's parameters and its own (`ConstraintEmitter`).
3. Bank it by extending an existing extension sample only with its owner's leave (`samples/CLAUDE.md`), or add a new sample to `AllFeaturesV2`, and record it in `extension-block-declaration` of `backlog/emitter-v2-capability-progression.yaml`.

---

## 🟡 A nested generic cannot be named under its enclosing instantiation, and a derived type builds it without one

**Closed on v2 (2026-09-26):** the program below prints `7`, and `new Inner<Score>()` inside `Deriv : Outer<Score>` is `Outer<Score>.Inner<Score>`, admitted against `where U : IRank<T>`. A generic last segment is looked up by its arity name, and a nested generic the scope found through a constructed base is carried under that base -- see the 2026-09-26 paragraphs of `nested-type-declarations` in `backlog/emitter-v2-capability-progression.yaml`. Everything below this paragraph is the entry as filed.

**Severity: medium** -- valid programs are refused, one at the binder and one at the constraint rule.

**Verified empirically** (2026-09-25) on `--pipeline experimental`:

```mist
public class Outer<T>
{
    public class Inner<U>
    {
        public int Size()
        {
            return 7;
        }
    }

    public Inner<int> Make()
    {
        return new Inner<int>();
    }
}

// in Main
Outer<string>.Inner<int> i = new Outer<string>().Make();
Console.WriteLine(i.Size());
```

Observed: `MIST_PM_UNRESOLVED_TYPE: The type 'Outer<string>.Inner' could not be found.` Expected: `7`. `nested-type-declarations` in `backlog/emitter-v2-capability-progression.yaml` records that "a generic last segment constructs over its own arguments with the instantiation enclosing", but `ScopeResolver.ResolveNestedGeneric` looks each segment up by its bare name, and a generic nested type is registered under its arity name, so the lookup fails first. Nothing tests a generic last segment beyond the parse (`ConstructedGenericMemberAccessParseTests`).

Second, through inheritance: with `Inner<U> where U : IRank<T>`, `new Inner<Score>()` inside `public class Deriv : Outer<Score>` is `MIST_PA_CONSTRAINT_NOT_SATISFIED` against `'IRank<T>'`, where C# reads it as `Outer<Score>.Inner<Score>` and admits it. The binder constructs `Inner<Score>` with no enclosing instantiation, so the model itself does not know `T` is `Score` there; a member of `Inner` returning `T` would be typed `T` the same way.

`ConstraintClosureRegistry.RecordType` already takes the enclosing instantiation, and `ResolveNestedGeneric` passes it, but no program reaches that line today, so a mutation dropping it survives (order 6, 2026-09-25).

**Action:**
1. Look a generic last segment up by its arity name in `ResolveNestedGeneric`, and pin `Outer<string>.Inner<int>` at Tier 3 on the model, then on the emitted C#.
2. Decide how a simple name reached through a constructed base carries the base's instantiation -- the scope lookup would have to return the base construction it came through, as `TypeMemberLookup`'s `BaseConstructions` already does for members.
3. Record in `nested-type-declarations` of the emitter register.

## A Mist `func` overload beside a CLR delegate overload of the same shape is ambiguous in the emitted C#

Observed 2026-09-26 while reviewing pure function types (v2):

```mist
static string N(func<int, bool> f) { return "func"; }
static string N(System.Predicate<int> f) { return "delegate"; }

// in Main
N(n => n > 1);
```

Mist resolves the call (no diagnostic), but the lambda is emitted bare, so Roslyn reports the call ambiguous between `N(Func<int, bool>)` and `N(Predicate<int>)` (MIST_CS_ERROR). The same holds for a method group argument. Nothing about purity is involved; a `pure func` overload beside the delegate one now ranks the pure overload first at the Mist level (LambdaTrials.ComparedType keeps the purity of either side) and meets the same C# ambiguity.

**Resolved in v2 (2026-09-28):** `OverloadPinning` now targets a lambda or settled method group at the selected call's closed callable parameter when the callee has competing overloads. `BodyEmitter` applies this to ordinary, conditional and carrier calls, object construction, flattened tuple arguments, class constructor `this`/`base` chains, and lowered struct initializer chains; structural-to-nominal crossings still use delegate construction. CLR-projected and intrinsic calls retain contextual conversion because their Mist callable parameter can differ from the CLR signature (`FindAll(Func<T, bool>)` versus `Predicate<T>`). When a derived class hides the selected base method, `HiddenBaseCall` closes and qualifies its declaring base type for explicit and implicit calls. The focused ledger has 30 passing Tier 1, 2 and 4 cases. Roslyn compilation confirms the selected delegate overloads, constructor initializer, and base method symbols; removing the target from the expression lambda reproduces Roslyn CS0121. A direct experimental build of `samples/DelegateFuncConversion` succeeds.

## An extension method is shadowed by a CLR instance member Standard leaves undeclared

Found 2026-09-27 by the purity adversarial rounds (v2). The emitter writes extension calls in dot
notation, so where the receiver's CLR type has an instance member of the same name and a matching
signature that Standard's intrinsic declaration omits (`Array.SetValue`), Roslyn binds the instance
member and the Mist extension never runs. The analysis judges the extension Mist bound, so a pure
scope can run an unjudged CLR member.

**Action:** emit the call through the extension's carrier class. That needs a link from the method to
its block, which the model records only on closed generic copies (`ClosedBlock`), not on the methods
of a non-generic block.

## A regex full match admits a trailing newline

**Closed on v2 (2026-09-28, workstream 19):** the program below prints `False False`. The runtime
lowering and the compile-time refinement check both wrap as `\A(?:...)\z` now, so `IsFullMatch`, a regex
pattern, a refined-type test and a literal refinement proof share one exact boundary. A pattern that
consumes the newline (`` `abc\n` ``) still matches `"abc\n"`, and `IsMatch` stays partial. The regex
spec's Full_Match wording now names these anchors. Classic still prints `True True` (measured
2026-09-28); v2 follows the spec's "entire input" here, not classic. The two ways the textual wrapper
could still leak are closed as well: a pattern that is not a valid regex on its own (`a)|(?:b`, which
wrapped accepted a prefix) is refused as `MIST_PA_REGEX_INVALID` wherever it is written, and a valid
pattern ending in an `(?x)` `#` comment no longer swallows the wrapper's `)` and throws. Tests and
mutations are in order 178 (`regex-full-match-lowering`) of `backlog/emitter-v2-capability-progression.yaml`
and order 14 (`regex-pattern-validity`) of `backlog/analysis-v2-capability-progression.yaml`. Everything
below this paragraph is the entry as filed.

Found 2026-09-27 by the culture review of purity (v2); not about culture.

```mist
var s = "abc\n";
Console.WriteLine($"{`abc`.IsFullMatch(s)} {s is `abc`}");
```

Observed: `True True`. Expected: `False False` -- a full match is meant to match the whole string. Cause:
`RegexLowering.FullMatchPattern` anchors as `"^(?:" + p + ")$"`, and .NET's `$` also matches before a
final newline; `LiteralPredicates.Match` anchors the same way when a refinement is checked at compile time.

**Action:** anchor with `\A(?:...)\z` in both, if Mist's full match is exact, and pin the trailing-newline
case at Tier 3 on the emitted pattern and at Tier 1 on `LiteralPredicates`.

## An absent optional's `ToString()` is "" but its interpolation is "Void"

Found 2026-09-27 by the culture review of purity (v2); not about culture.

```mist
var none = Find(false);           // Box?, absent
var b = none?.V.ToString();
Console.WriteLine($"[{b}] [{none?.V}]");
```

Observed: `b` is the empty string (`b is string` holds), while the same absent value interpolated prints
`Void`. Classic refuses the program (`MIST_TC_0073`, `ToString` is not on every constituent of
`Double | Void`). Cause: an explicit universal `ToString` on a value whose text is decided at runtime is
emitted as `MistInvariant.Text(...)`, which answers "" for null; before culture-invariant emission the
emitted `none?.V.ToString()` answered null inside a string typed non-optional.

**Action:** decide the text an absent optional's `ToString` gives -- "Void", as interpolation does -- or
refuse `ToString` on an optional as classic does; then make `MistInvariant.Text` and the interpolation
agree.

## A newtype over a tuple does not compile

Found 2026-09-27 by the culture review of purity (v2); not about culture.

```mist
type Pair = new (double, int);
Pair p = (Pair)(1.5, -2);
```

Observed: `MIST_CS_ERROR: The type or namespace name 'Pair' could not be found`. Expected: the clone is
emitted, or the frontend refuses a newtype over a tuple. Cause, read from the missing type rather than
traced: nothing emits a clone of a tuple source, while the frontend admits the declaration and its casts.

**Action:** decide whether a newtype over a tuple is admitted (newtypes spec); refuse it in the frontend
or emit it, and pin the choice at Tier 3.

## A newtype over a struct interpolates as its type name

Found 2026-09-27 by the culture review of purity (v2); not about culture.

```mist
public struct Money { public decimal Amount; ... public string ToString() { return $"{Amount} EUR"; } }
type Price = new Money;

Price p = (Price)new Money(2.5m);
Console.WriteLine($"{p}");
Console.WriteLine(p.ToString());
Console.WriteLine($"{new Money(2.5m)}");
```

Observed: `Hunt.Price`, then `2.5 EUR`, then `Hunt.Money`. The newtype is not the cause: the source struct
interpolates as its type name too. Cause: a declared parameterless `ToString` is emitted as
`public readonly string ToString()`, which hides `ValueType.ToString` rather than overriding it, so an
explicit call reaches it and interpolation, concatenation and `Console` do not. Expected: undecided --
Mist has no overriding, so whether formatting a value reaches the `ToString` its type declares has not
been stated.

**Action:** compare classic's output for the three lines, decide whether a declared `ToString` is what
formatting uses, and if so emit it (and a clone's forwarded one) as `override`; pin at Tier 3.

## An unsuffixed integer literal beyond `int` reaches Roslyn, and every hex literal is `int`

**Closed on v2 (2026-09-27):** `IntegerLiterals.Read` reads a decimal, hex or binary literal exactly,
separators dropped, as the first of `int`, `uint`, `long` and `ulong` its suffix allows that holds it, and
holds the constant as that type's own primitive; past `ulong` it is `MIST_PM_NUMERIC_LITERAL_OUT_OF_RANGE`
naming `ulong`. Measured on the merged tree: `var z = 5000000000;` prints 5000000000; `int h = 0x1_0000_0000;`
is `MIST_PA_NO_IMPLICIT_CONVERSION` from `long`; `x`, `u` and `h` print `18446744073709551615
18446744073709551615 9223372036854775808` and the interpolation `18446744073709551615 2147483647
9223372036854775808`, as classic does; `long y = 0x8000_0000_0000_0000;` and `Neg n =
0xFFFF_FFFF_FFFF_FFFF;` are refused from `ulong`; `new Wide<0xFFFF_FFFF_FFFF_FFFF>()` is out of `long`'s
range (see the `ulong` dependent-parameter entry). Pinned at Tier 1 in `IntegerLiteralReadingTests`, with
a 500-case property over every spelling, and in `LiteralCultureInvarianceTests`.

Found 2026-09-27 by the culture review of purity (v2); not about culture.

```mist
var z = 5000000000;
int h = 0x1_0000_0000;
```

Observed: no Mist diagnostic. The emitted code is `int z = 5000000000;` and `int h = 4294967296;`, and the
build fails with `MIST_CS_ERROR` ("Cannot implicitly convert type long to int"). Expected: C#'s rule, under
which an unsuffixed literal is the first of `int`, `uint`, `long` and `ulong` that holds it, or a Mist
refusal. Cause: `LiteralBinder.IntegerValue` and `HexValue` parse every unsuffixed literal as a `long` and
type it `Int32`, so `MIST_PM_NUMERIC_LITERAL_OUT_OF_RANGE` fires only past `long`, and its message then says
"outside the range of 'int'" for a range that was `long`'s.

Hex is silent as well. Found 2026-09-27 by the literal review of purity's culture work; not caused by it:
the merge base 13b67c0e reads `0xFFFFFFFFFFFFFFFF` as -1 too, and bound the forms written with `_` or a
suffix as 0.

```mist
var x = 0xFFFF_FFFF_FFFF_FFFF;
var u = 0xFFFF_FFFF_FFFF_FFFFUL;
long y = 0x8000_0000_0000_0000;
ulong h = 0x8000_0000_0000_0000UL;
Console.WriteLine($"{0xFFFF_FFFF_FFFF_FFFF} {0x7FFF_FFFF} {0x8000_0000_0000_0000}");
```

Observed: `x` and `u` print -1, and `y` compiles and prints -9223372036854775808. `h` is refused with
`MIST_PA_NO_IMPLICIT_CONVERSION` ("'int' to 'ulong'"), and the interpolation prints
`-1 2147483647 -9223372036854775808`. The same reading reaches a dependent type argument
(`new Wide<0xFFFF_FFFF_FFFF_FFFF>().Get()` gives -1) and a refinement (`type Neg = long < 0;` admits
`Neg n = 0xFFFF_FFFF_FFFF_FFFF;`). Expected, as classic prints: 18446744073709551615 for `x` and `u`,
`18446744073709551615 2147483647 9223372036854775808` for the interpolation, and `y` refused, since C#
types that literal `ulong`. Cause: `LiteralBinder.BindHexLiteral` types every hex literal `Int32`, whatever
its value or suffix, and `HexValue` parses the digits with `long.TryParse` and `AllowHexSpecifier`, which
reads sixteen digits with the top bit set as a negative two's-complement `long`. `ScopeResolver`'s
type-argument lift calls the same `HexValue`.

**Action:** type an unsuffixed decimal or hex literal as C# does (or refuse one past `int`, if Mist's
literals are meant to be `int` unless suffixed), make the message name the range checked, and pin each
form at Tier 1 in `LiteralCultureInvarianceTests` beside the rows that already name the overclaim
(`AnIntegerLiteralItsTypeCannotHold_HasNoValue`). For hex (and binary), parse the digits as a `ulong`,
take the first of `int`, `uint`, `long` and `ulong` that holds the value, narrowed by the `u`/`l` suffix,
and refuse past `ulong` (past `long` in a type argument, while `LiftedType` holds a `long`). Pin
`0xFFFF_FFFF_FFFF_FFFF`, `0x8000_0000` and a `UL`-suffixed hex at Tier 1, and the type argument at Tier 2.

## A `u` literal past `uint` is truncated without a diagnostic

**Closed on v2 (2026-09-27):** a `u` literal past `uint` reads as a `ulong`, and `BodyEmitter` writes every
integral constant from its exact value, narrowing none. Measured on the merged tree: `5000000000` and
`4294967296`, as classic prints, and `A = 5000000000u` in a `uint` enum is `MIST_PA_NO_IMPLICIT_CONVERSION`
from `ulong`. Pinned at Tier 1 in `IntegerLiteralReadingTests` and `IntegerLiteralSpellingTests`.

Found 2026-09-27 by the literal review of purity's culture work (v2); not caused by it: the merge base
13b67c0e prints the same.

```mist
var u = 5000000000u;
Console.WriteLine(u);
Console.WriteLine($"{4294967296u}");
```

Observed: `705032704`, then `0`, with no diagnostic. The emitted code is `uint u = 705032704U;`, and
`enum Un : uint { A = 5000000000u }` gives 705032704 the same way. Expected: `5000000000` and
`4294967296`, as classic prints, since C# types a `u` literal past `uint` as `ulong` (for the enum value,
classic and C# refuse the conversion from `ulong` to `uint`). Cause: `LiteralBinder.IntegerValue` types a
`u` literal `UInt32` but checks its range only against `long`, and `BodyEmitter.EmitIntegerLiteral`
writes it through `(uint)value`, which wraps.

**Action:** type a `u` literal as C# does -- `uint` when it fits, otherwise `ulong` -- or refuse one past
`uint` with `MIST_PM_NUMERIC_LITERAL_OUT_OF_RANGE` naming `uint`. Never narrow with a cast in
`EmitIntegerLiteral`. Pin at Tier 1 beside `LiteralCultureInvarianceTests` and at Tier 3 on the emitted
literal.

## A `char` literal's escape reads as its backslash

Found 2026-09-27 by the literal review of purity's culture work (v2); not caused by it: the merge base
13b67c0e behaves the same, and `backlog/program-model-body-tasks.yaml` notes the caveat on its char
literal row.

```mist
char nl = '\n';
char tab = '\t';
char bs = '\\';
char q = '\'';
char z = '\0';
int n = new Ch<'\n'>().Get();      // public class Ch<C:char> { public char Get() { return C; } }
```

Observed, by position. In an expression the emitted code is `char nl = '\';`, and the build fails with
`MIST_CS_ERROR`. In a switch, arms `'\n'` and `'\\'` both bind to the backslash, so the second is
`MIST_PA_SWITCH_ARM_UNREACHABLE`. An enum value `Line = '\n'` emits `new Sep('\')` and fails in Roslyn. A
refinement bound is misread: `type AboveTab = char > '\t';` refuses `AboveTab a = ' ';` with "does not
satisfy refinement '>= 93'". A dependent type argument is silent: `new Ch<'\n'>().Get()` answers 92, and
its carrier is `Const_Char_0x005c`. Expected, as classic prints: `10 9 92 39 0` for the five locals,
`newline` for the switch, and 10 for the enum value and the type argument. Classic refuses the refinement
too (`MIST_TC_0097`, quoting the bound as `'> ?'`), so that position has no classic contract, but `' '`
(32) is above `'\t'` (9). Cause: `LiteralBinder.BindCharLiteral` and `ScopeResolver.LiftCharLiteral` both
take the character after the opening quote (`lexeme[1]`), and `BodyEmitter.EmitLiteral` writes a `char`
back between quotes unescaped.

**Action:** decode the grammar's escape sequences in one helper that `BindCharLiteral` and
`LiftCharLiteral` share, and emit a `char` escaped (`\uXXXX`, as `DependentCarrierNames` already spells
one). Pin the value at Tier 2 in each position and the emitted text at Tier 3.

**Closed on v2 (2026-10-03):** `CharacterLiterals.Decode` gives expression and dependent-argument
binding the same character for every grammar escape, rejecting malformed direct lexemes instead of
substituting NUL. Pattern constants and enum values consume the expression binder's decoded value;
refinement comparisons bind `\t` as 9. `BodyEmitter.EmitLiteral` keeps safe printable ASCII readable
and reuses `DependentCarrierNames.Literal` for quotes, backslash, controls and non-ASCII characters.
The carrier name remains based on the character value: newline is `Const_Char_0x000a`, distinct from
backslash's `Const_Char_0x005c`. `CharacterLiteralValueTests` covers all seven escapes at Tier 1;
`CharacterLiteralBindingTests` covers expressions, lifted arguments, patterns, refinement bounds and
enum values at Tier 2; `LiteralEmissionTests` and `DependentCarrierNameTests` cover C# spelling and
identity at Tier 1. Focused ledger: 91/91. Unfiltered ledger after rebasing onto main
(`TestResults/ledger/20261003-012304`): 8,940/8,940, zero build warnings;
`dotnet format Mist.slnx --verify-no-changes` passed. On this macOS host the full-system sample
stalled during ASP.NET configuration reload with the default environment; the unfiltered ledger
passed with `DOTNET_HOSTBUILDER__RELOADCONFIGONCHANGE=false`. No character grammar, string literal or
regex behavior was changed.

## A binary literal in an expression is not bound

**Closed on v2 (2026-09-27):** `ExpressionBinder` binds hex and binary literals through the decimal path,
`LiteralBinder.BindIntegerLiteral`, which types all three by the same reading. Measured on the merged tree:
`5` and `240`, as classic prints. Pinned at Tier 2 in `Semantics.NumericLiteralBindingTests`, beside the
hex arm, and the reading at Tier 1 in `IntegerLiteralReadingTests`.

Found 2026-09-27 by the literal review of purity's culture work (v2); not caused by it: the merge base
13b67c0e behaves the same, and `backlog/program-model-body-tasks.yaml` records the missing arm.

```mist
var b = 0b101;
Console.WriteLine(b);
int c = 0b1111_0000;
Console.WriteLine(c);
```

Observed: `MIST_PM_UNRESOLVABLE_OVERLOAD` on `WriteLine`, since the literal binds as an unresolved
expression. On its own (`int b = 0b101;`) it is emitted as
`/* unsupported: BinaryLiteralExpressionPrimaryNode */` and reported as `MIST_BE_UNSUPPORTED_EXPRESSION`.
In a type argument it is read (`Narrow<0b1_01>` lifts 5). Expected: `5` and `240`, as classic prints.
Cause: `ExpressionBinder.BindPrimary` has no arm for a binary literal; `LiteralBinder.BinaryValue` exists,
and only `ScopeResolver`'s type-argument lift calls it.

**Action:** add the arm, binding through `LiteralBinder.BinaryValue` and typed by the hex rule (see the
unsuffixed-literal entry), refusing on overflow. Pin at Tier 2 beside `NumericLiteralBindingTests`.

## A sized array type's length written with `_` or a suffix reads as 0

**Closed on v2 (2026-09-27):** `ScopeResolver.ResolveArrayType` and `TypeParameterPopulator.FixedLength` read
the size through `IntegerLiterals.Length`, so a suffix is read and a length no `int` holds is
`MIST_PM_NUMERIC_LITERAL_OUT_OF_RANGE` naming `int`. Measured on the merged tree: `int[1_0] tens = new
int[10];` compiles, and through `var t = Three();` both `t[2]` and `Three().Length` print 3. Indexing the
call directly, `Three()[2]`, is still `MIST_PA_INDEX_UNPROVEN`, for `int[3]` exactly as for `int[3u]`: the
prover reads no length from a call's result, and classic, which cannot compile a sized return type at all
(`MIST_TC_0008`), sets no contract for it. Pinned at Tier 1 in `IntegerLiteralReadingTests` and at Tier 2
in `SizedArrayLengthTests`, against the resolver.

Found 2026-09-27 by the literal review of purity's culture work (v2); not caused by it: the merge base
13b67c0e behaves the same.

```mist
int[1_0] tens = new int[10];

public static int[3u] Three()
{
    return [1, 2, 3];
}
```

Observed: `MIST_PA_NO_IMPLICIT_CONVERSION`, "Cannot implicitly convert type 'int[10]' to 'int[0]'", for
the first. For the second, the same against `int[3]`, and "Index 2 is outside the bounds of 't', which
needs nothing" (`MIST_PA_INDEX_OUT_OF_BOUNDS`) where the result is indexed. `int[10]` works on both
pipelines. Expected: `int[1_0]` is `int[10]`, since the grammar's rank takes an `INTEGER_LITERAL`, which
admits `_` and suffixes, and a suffix is either read or refused, never 0. Classic sets no contract here:
it fails on both forms ("The input string '1_0' was not in a correct format", `MIST_SEMANTIC_ERROR`; and
`MIST_SEMANTIC_FAILURE` for '3u'). Cause: `ScopeResolver.ResolveArrayType` parses the size with
`int.TryParse` and uses 0 when that fails; `TypeParameterPopulator.FixedLength` does the same and answers
null.

**Action:** read the size through `LiteralBinder.IntegerValue`, which drops `_`, and refuse a suffix or a
value past `int` with `MIST_PM_NUMERIC_LITERAL_OUT_OF_RANGE` or a size diagnostic, in both places. Pin at
Tier 2.

## An explicit `ToString()` is refused on a Mist class that declares only `ToString(string, IFormatProvider)`

Found 2026-09-27 by the literal review of purity's culture work (v2); not caused by it: the merge base
13b67c0e behaves the same.

```mist
public class Temp : IFormattable
{
    public string ToString(string format, IFormatProvider provider)
    {
        return "formatted";
    }
}

Console.WriteLine("[explicit ToString] " + new Temp().ToString());
```

Observed: `MIST_PM_NO_APPLICABLE_OVERLOAD`, "There is no overload of method 'ToString' accepting 0
argument(s). Declared arities: 2." Expected: `[explicit ToString] Hunt.Temp`, as classic prints: C# binds
`object.ToString()` when none of the type's own `ToString` overloads applies. Cause: the class's own
two-argument `ToString` group hides the universal parameterless `ToString`, so overload resolution never
sees it.

**Action:** when none of a type's own `ToString` members applies, fall back to the universal `ToString`,
as C# falls back to `object.ToString()`. The culture lowering already routes that call, a Mist type
implementing `IFormattable` through `MistInvariant.Text`. Pin the binding at Tier 3 and the printed name
at Tier 4.

## A `ulong` dependent parameter takes values only up to `long.MaxValue`

Found 2026-09-27 by the literal review of purity's culture work (v2). The limit predates it: the merge
base 13b67c0e lifts a literal past `long` as 0 with no diagnostic, and this branch refuses it instead.

```mist
public class U<N:ulong>
{
    public ulong Get()
    {
        return N;
    }
}

Console.WriteLine(new U<18446744073709551615UL>().Get());
ulong x = 18446744073709551615UL;
```

Observed: `MIST_PM_NUMERIC_LITERAL_OUT_OF_RANGE`, "The literal '18446744073709551615UL' is outside the
range of 'long'", on the type argument, while the local compiles; `U<9223372036854775807>` works. The
merge base printed 0. Expected: 18446744073709551615, since `book/src/type-system/dependent-types.md`
admits `ulong` as a dependent parameter's type. Classic sets no contract: it cannot compile the program
(`MIST_CS_ERROR`, "Type expected"). Cause: `LiftedType` holds its value as a `long`, and `ScopeResolver`'s
lift refuses what no `long` holds; `NumericLiteralBindingTests.ADependentValueTypeArgumentNoLongHolds_IsRefused`
pins the refusal.

**Action:** carry a `ulong` dependent value (a `LiftedType` value and a carrier name wide enough for it),
or state in the dependent-types spec that a `ulong` parameter is limited to `long`'s range and make the
refusal say so.

## `-(-x)` is emitted as the pre-decrement `--x`

Found 2026-09-27 by the literal review of purity's culture work (v2); not caused by it: the merge base
13b67c0e prints the same. A separate task was raised to fix it.

```mist
int x = 5;
int y = -(-x);
Console.WriteLine($"{x} {y}");
```

Observed: `4 4`, with no diagnostic. The emitted code is `int y = --x;`, a C# pre-decrement, which also
changes `x`. `-(-5)` becomes `--5` and fails as `MIST_CS_ERROR`. Expected: `5 5`, as classic prints.
Cause: `BodyEmitter`'s unary arm writes the operand of a negation without its parentheses, and nothing
separates the two adjacent minus signs.

**Action:** keep a space or the parentheses between two adjacent `-` operators, and check `+(+x)`, which
has the same shape; pin `-(-x)` and `-(-5)` at Tier 3 on the emitted text and at Tier 4 on the printed
value.

---

## 🟢 The CLI and the Driver no longer read generated output back as source, except lex output older than its marker

**Severity: low** -- corrected in the CLI on 2026-09-27, and for referenced projects in the Driver the same day (item 1 of "What remains"). Before the second correction, lexing any sample that `AllFeatures` or `AllFeaturesV2` references broke both orchestrators in that checkout until its `out/` was deleted. Output an earlier invocation wrote under flags the current one cannot know (items 2 and 3) is closed for lex output written from 2026-09-28, which carries a marker; what remains is lex output written before that.

**What was wrong.** `Source/Mist.CLI/Program.cs` held three copies of `Directory.EnumerateFiles(projectFolder, "*.mist", AllDirectories)` (build, run, test), each run before the output and bin directories were cleaned. Lex mode writes a reconstructed `.mist` beside each token dump in `<output>/lexer/`, so the next invocation discovered those files, cleaned them away, and then tried to read them. Lex output was also flattened to the file name, so `Alpha/Foo.mist` and `Beta/Foo.mist` wrote the same two artifacts. Measured at 13b67c0e on a four-file project (`Program.mist`, `Alpha/Foo.mist`, `Beta/Foo.mist`, `util/Helper.mist`):
- The first lex left one `Foo.*` pair.
- The second relexed `out/lexer/Foo.mist` and `out/lexer/Program.mist`, then exited 1 on `Failed to read 'out\lexer\Helper.mist'`. That file sorts after `out/`, so the clean had deleted it before discovery's list reached it.
- `build --pipeline experimental` then failed with `MIST_FILE_ERROR` for the three deleted files.

Without lex artifacts, the same project builds and runs (`alpha`, `beta`, `helper`).

**Correction.** `Source/Mist.CLI/Layout/ProjectLayout.cs` is the one discovery policy, and build, run, test and lex all use it. A `.mist` file is a source unless it lies inside the effective output or bin directory. That is decided on full paths resolved against the working directory, as the clean resolves them, so a relative `--output` is not reinterpreted against the project. It is also decided on whole segments, so `out-other` stays a source folder.
- Lex artifacts keep the source's project-relative path (`<output>/lexer/Alpha/Foo.tokens`).
- An output or bin directory that is, or contains, the project is refused before anything is cleaned. Before this, such a directory was cleaned like any other, deleting the project.
- Requirements 2.4-2.5, 4.6 and 8.5 of `project-based-build-model` record the policy, each with an amendment note.

**Verification.**
- These pass:
  - `ProjectLayoutTests` (Tier 1, 22 cases, no I/O).
  - `MistFileDiscoveryTests` (Tier 2, one temp tree). It replaces three tests that exercised `Directory.EnumerateFiles` itself.
  - `RepeatedCliInvocationTests` (Tier 5: lex, lex, experimental build over one temp project, about 5 s).
  - All 89 `Mist.CLI.Tests`.
  - Through the rebuilt CLI: `AllFeatures`, `AllFeaturesV2`, `Testing` under `mist test`, and `NestedBuildBanTest`.
- With `Program.cs` reverted to 13b67c0e, the Tier 5 test fails on the second lex (exit 1, `Failed to read ...\out\lexer\Helper.mist`).
- Five mutations of `ProjectLayout` each fail the tests aimed at them:
  - no exclusion: 9 cases, including the Tier 2 tree;
  - flattened lex names: 2;
  - prefix match without a segment boundary: 3;
  - relative overrides resolved against the project: 10;
  - unnormalized string prefix: 6.
- The overlap refusal was run by hand, on a disposable nested copy only. The spellings were `--output <project>`, `<project>/`, `<project>/Alpha/..` and `.`, and `--bin <parent>` and `<project>/../`, plus the same refusal through `run`, `test` and lex. Every one exited 1, and the tree was unchanged, including a sentinel file in the parent.

**What remains.**
1. **Closed (2026-09-27).** `ProjectReferenceResolver` (`Source/Mist/Driver/ProjectReferenceResolver.cs:194`) still takes every `.mist` file under a referenced project, and the referencing build does not clean that project's `out/`.
   - Measured on disposable copies: `mist build HelloMist --mode lex`, then an experimental build of a project referencing HelloMist, fails with `MIST_PM_DUPLICATE_TYPE` on `HelloMist/out/lexer/Color.mist`, `Point.mist` and `Program.mist`.
   - No checkout on this machine held such files on 2026-09-27.
   - The fix belongs to the Driver's owner: exclude the referenced project's default `out/` and `bin/`. For the Driver to share `ProjectLayout`, it would have to move from `Mist.CLI` into `Mist`.
   - **Correction.** The resolver still enumerates every `.mist` file under a referenced project, then keeps what `SourcePathSelection.ForReferencedProject` (`Source/Mist/Driver/SourcePathSelection.cs`) selects: the files outside the `out/` and `bin/` directly beneath that project's own folder, in ordinal order. Containment is decided on normalized full paths and whole segments. The referencing invocation's `--output` and `--bin` play no part.
     - Selection runs inside the traversal, over whatever the injected enumerator returns, so the stub-loader seam covers it. Manifest caching, per-edge name checks, cycle reporting, partial results and dependency-first order are unchanged.
     - The containment decision moved into `Mist` instead of `ProjectLayout` moving. `ProjectLayout.SelectSources` and `DirectoryHoldingProject` delegate to it; their public API and the CLI's invocation-specific rule are unchanged.
     - `project-references` Requirement 5.6 records the policy, with an amendment note.
   - **Verification.**
     - Revalidated first at `666c596c`, over disposable copies of `HelloMist` and a `Consumer` that references it. The consumer ran. `build HelloMist --mode lex` wrote `out/lexer/{Color,Optional,Point,Program}.mist`, and `build Consumer --pipeline experimental` then exited 1 with `MIST_PM_DUPLICATE_TYPE` on `Color.mist`, `Point.mist` and `Program.mist`.
     - With the fix, over the same lexed tree, that build exits 0 with no diagnostics, and `run` exits 0 on both pipelines. Their 31 lines are identical and equal the HelloMist slice of `AllFeaturesV2.out` from line 68. All 13 files under the dependency had the same SHA-256 and timestamp before and after.
     - `SourcePathSelectionTests` (Tier 1, 11 cases, strings only) and `ReferencedProjectSourceTests` (Tier 2, 2 cases) pass. The Tier 2 cases are a direct-plus-transitive graph through the resolver's stub loaders, and one disposable tree through the real enumerator with a stub manifest loader, which also asserts that no file changed. The 37 existing resolver, graph, seam and property cases pass beside them (50/50), as do all 89 `Mist.CLI.Tests`.
     - Seven mutations each fail the tests aimed at them:
       - no exclusion: 11 cases;
       - excluding every segment named `out` or `bin`: 6, one of them the existing `CorrectRepeatedEdges` graph test, whose fixture paths lie under the test run's own `bin/`;
       - prefix match without a segment boundary: 2;
       - unnormalized string prefix: 5;
       - the resolver skipping selection: 2;
       - the resolver judging against the consumer's folder: 2;
       - no ordinal order: 4.
2. Files left in a directory that an earlier invocation used as output, under different flags, are still discovered. Requirement 2's amendment note says so.
3. The same limit holds for references, and `project-references` Requirement 5.6's amendment note says so. A reference records no output location, so `.mist` files that an invocation of the referenced project wrote under a custom `--output` or `--bin` inside its own folder are compiled into every build that references it. Closing that would need an output marker or a manifest option, which the correction deliberately does not add. Files in an `out/` or `bin/` deeper than the project's root are sources by design.
4. **Items 2 and 3 closed for lex output (2026-09-28).** Lex mode is the only writer of `.mist` files into an output directory. It now writes `.mist-generated` into `<output>/lexer/` before any reconstructed source (`project-based-build-model` 8.6). Both discovery paths skip every directory holding one, wherever it lies and whatever the current flags (2.6, and `project-references` 5.7).
   - `SourcePathSelection.EnumerateCandidates` returns the `.mist` files and markers under a folder, and `Select` excludes each marker's directory beside the output and bin. The resolver's injected enumerator and `ProjectLayout.SelectSources` keep their signatures. The marker is CLI-internal: hand-placing one to exclude a folder is not a supported feature.
   - What remains: lex output written before 2026-09-28 has no marker, and is still read unless it lies in the current invocation's output or bin (or, for a reference, its default `out/` or `bin/`); delete such a folder once. A marker deleted by hand stops protecting its folder.
   - **Verification.** One Tier 1 case in `SourcePathSelectionTests` and two in `ProjectLayoutTests`, one of them joining the writer's `LexMarkerPath` to the reader's `SelectSources`. A marked folder added to the Tier 2 trees of `MistFileDiscoveryTests` and `ReferencedProjectSourceTests`. One more run in the Tier 5 `RepeatedCliInvocationTests`, a lex into `gen/` inside the project between the default lexes and the build, so that each of the last two runs must skip the other's output by its marker alone.
   - Five mutations each fail the tests aimed at them:
     - lex writing no marker: the Tier 5 test, which is the only test that can see the write;
     - enumeration ignoring markers: both Tier 2 trees and the Tier 5 test;
     - selection ignoring markers: 5 tests from Tier 1 to Tier 5;
     - the marker at the output root instead of `lexer/`: the Tier 1 path test and the Tier 5 test;
     - a marker excluding its parent's directory: the Tier 1 case and the CLI's Tier 2 tree.


---

## 🟢 Project-reference names bypass validation on repeated edges, and invalid manifests load repeatedly

**Closed (2026-09-27):** `ProjectReferenceResolver` caches each canonical path's manifest load, including null failures, within one resolution. It validates every edge to a successfully loaded target before completed/active traversal checks. A mismatched edge skips dependencies without preventing a later valid edge from traversing them. DFS cycle chains, dependency-first ordering, partial results and sibling-error collection remain intact. The public entry point, parser, records, descriptors and source discovery policy are unchanged; the resolver has an internal manifest/source-loader seam for direct graph tests. Its XML contract now describes partial results and named `[references]` syntax.

**Defect revalidated:** on the starting `main` commit `2ef14e1d`, after extracting only injectable loaders while retaining the original DFS, the Tier 2 ledger run `20260927-144647` passed 7/17 and failed 10/17. The correct-first diamond omitted the alias mismatch; wrong-first reloaded the target; three edges to a failed load produced three manifest diagnostics. Distinct aliases after completion and mismatched edges to active targets also bypassed name validation. These graph cases use hand-built configurations and stub loaders, with no parser, filesystem, compiler or processes.

**Actual verification:**

```sh
tools/test-ledger/run.sh Test/Mist.Tests --filter 'FullyQualifiedName~ProjectReferenceGraphTests&Tier=2'
tools/test-ledger/run.sh Test/Mist.Tests --filter '(FullyQualifiedName~ProjectReferenceGraphTests&Tier=2)|(FullyQualifiedName~ProjectReferenceManifestSeamTests&Tier=4)|FullyQualifiedName~ProjectReferenceResolverTests|FullyQualifiedName~ProjectReferencePropertyTests'
dotnet format whitespace Mist.slnx --no-restore --include Source/Mist/Driver/ProjectReferenceResolver.cs Test/Mist.Tests/Driver/ProjectReferenceGraphTests.cs Test/Mist.Tests/Driver/ProjectReferenceManifestSeamTests.cs
dotnet format Mist.slnx --verify-no-changes --report TestResults/project-reference-format-report
git diff --check
```

The final ledger `20260927-145107` passed 37/37, with 0 build warnings and 0 errors: 17 new Tier 2 graph cases, 3 new Tier 4 parser/filesystem seam cases, and 17 existing resolver/property cases. New tests each have exactly one class-level Tier trait and private task-specific fixtures. Coverage includes either diamond order, skipped traversal, later valid edges, distinct aliases, ordinal name matching, successful/failed cache scope, exact root/self/longer cycle chains, normalized named-manifest paths, real malformed TOML and missing-name diagnostics, and existing missing-target/path errors. Formatting exited 0 with an empty report after correcting local-function names and enabling restore; the earlier no-restore solution check lacked assets for other test projects. `git diff --check` passed.

**Limits and integration:** no full-solution build or full ledger checkpoint was run; verification builds the Mist test project and its dependencies. Other lanes advanced `main` during this work, so the branch remains based on the clean starting commit above. The purity lane's committed and working changed-file lists were inspected read-only; no source/test ownership overlap or implementation dependency was found. This appended backlog evidence is the only shared-file integration point. Root back-edges retain cycle diagnostics without reloading the root; the entry point receives the root folder and references rather than its configuration.

**Integration verification (2026-09-27):** merged current `main` at `bb4f345e`; only the appended backlog entries conflicted, and both were preserved. The same filtered ledger command above passed 37/37 in `20260927-150424`, with 0 build warnings and 0 errors. `dotnet format Mist.slnx --verify-no-changes --report TestResults/project-reference-main-format-report` exited 0 with an empty report; `git diff --check` passed.

---

## 🟢 A cached SDK reference path answered every target framework

**Closed (2026-09-27).** SDK reference paths are now cached per target framework. A cached path answers only the exact framework it was discovered for.

**What was wrong.** Revalidated at `main` `666c596c`. `SdkDiscovery` cached one in-process path and one line in `~/.mist/sdk-ref-path.cache`, and neither recorded a framework. `FindReferenceAssemblyPath` returned either one whenever its directory existed, and the requested framework reached `RunFullDiscovery` only after both missed. So once `net10.0` resolved, a `net8.0` request received `net10.0`'s reference assemblies: in the same process from memory, and in any later process from the file. If `net8.0`'s pack was absent, the `MIST_SDK_NO_FRAMEWORK` error was suppressed as well. `RegenerateDiskCache` wrote `net10.0`'s path as that unqualified line.

**Correction.**
- `Source/Mist/Driver/SdkReferencePathCache.cs` is an instance with injected discovery, directory-existence, read and write operations. `SdkDiscovery` holds one and delegates to it, and its public signatures and diagnostics are unchanged.
- Identity is the complete requested TFM, compared ordinally. `net10.0-windows` is not `net10.0`, and `NET10.0` is a separate entry, because framework spelling policy is outside this change.
- The in-memory cache and the file each look up the requested framework. A hit is used only if its directory still exists; a stale entry is rediscovered for that framework alone. A failed discovery is not cached, so its diagnostic appears on every request.
- The file holds one `tfm<TAB>path` line per framework. A publication reads the file, updates its own entry and rewrites the rest unchanged, all under one lock. A framework or path that cannot be written as a single line stays in memory only.
- `SdkReferencePathCacheFile` writes a staged file and moves it over the cache, so a reader never sees a truncated path. A truncated path could name an existing ancestor directory, such as `...\dotnet\packs`.
- Persistence is best effort. A read or write that throws is a miss or a skipped write, never a failed discovery.
- `RegenerateDiskCache` forces discovery of `net10.0` and publishes it as the `net10.0` entry only.
- The internal `ResetInProcessCache` had no callers, and nothing replaces it.

**Compatibility choice.** An existing single-line cache holds a bare path. It is read as a miss for every framework, not recognised from its `ref/<tfm>` layout. The first discovery after upgrading rewrites the file with a framework on each line.

On this machine, `~/.mist/sdk-ref-path.cache` held `C:\Program Files\dotnet\packs\Microsoft.NETCore.App.Ref\10.0.9\ref\net10.0` (dated 2026-07-11). The first run on this branch, through the production `SdkDiscovery` wiring, rewrote it at 16:47:49 as `net10.0<TAB>` followed by the same path.

An older binary reads the new file as one nonexistent path. It rediscovers and writes its own format back, and the new code then treats that as a miss. Each switch costs at most one discovery per process and framework, and neither reading can return another framework's path.

**Verification.**
- `SdkReferencePathCacheTests` (Tier 1, 27 cases, fakes only, no filesystem or processes) cover:
  - either request order;
  - reuse from memory and from the store;
  - memory and store each refusing another framework's entry;
  - a same-major framework;
  - an unavailable framework still failing with `MIST_SDK_NO_FRAMEWORK` beside a cached one;
  - failures not being cached;
  - stale memory and stale store entries;
  - the legacy line;
  - eight malformed stores;
  - a throwing reader and a throwing writer;
  - regeneration;
  - the single-line guard;
  - two discoveries held at a barrier, and 300 parallel requests over three frameworks.
- `SdkReferencePathCacheFileTests` (Tier 2, 5 cases, one private temp directory, never `~/.mist`) cover:
  - a missing file;
  - directory creation and whole-file replacement, with no staging file left behind;
  - two frameworks, with a Windows path containing spaces, surviving a reopen;
  - a cache path that is a directory, and one under a regular file, where discovery still succeeds.
- `SdkVersionSelectionPropertyTests` are unchanged, and pass.
- Nine mutants of `SdkReferencePathCache` each fail a different set of the 32 new cases:

  | Mutant | Cases failed |
  |---|---|
  | Memory ignores the TFM | 10 |
  | File ignores the TFM | 11 |
  | Both ignore it (the original defect) | 12 |
  | Keyed by major version | 7 |
  | Publication drops other entries | 5 |
  | No single-line guard | 1 |
  | Store exceptions propagate | 4 |
  | A stale remembered path is trusted | 1 |
  | A bare line is read from its layout | 2 |

  Each mutant was built and run through the ledger, from `20260927-164419` to `20260927-164637`. The restored file was byte-identical to the original.

```sh
tools/test-ledger/run.sh Test/Mist.Tests --filter 'FullyQualifiedName~SdkReferencePathCache|FullyQualifiedName~SdkVersionSelectionPropertyTests'
tools/test-ledger/run.sh Test/Mist.Tests --filter 'FullyQualifiedName~Mist.Tests.Driver|FullyQualifiedName~Mist.Tests.Backend.Ambient|FullyQualifiedName~Mist.Tests.Backend.Clr|FullyQualifiedName~IntrinsicSupplyTests|FullyQualifiedName~SequenceLengthDecisionTests|FullyQualifiedName~ExperimentalRepresentationIntegrationTests'
dotnet build Mist.slnx --no-incremental
dotnet format Mist.slnx --verify-no-changes --report TestResults/sdk-cache-format-report
```

- The first command passed 35/35 (`20260927-164305`).
- The second passed 424/424 (`20260927-164739`), with 0 build warnings.
- The full rebuild had 0 warnings and 0 errors.
- Formatting reported one finding, and it is not in this change: IDE0047 at `Test/Mist.Tests/Backend/ClrSpellingTests.cs:30`, which is unmodified here and was committed in `66ac194b`.
- The full ledger `20260927-165112` passed 10,626 of 10,627 tests: `Mist.CLI.Tests` 89/89, `Storm.SourceGenerator.Tests` 787/787 and `Mist.Tests` 9,750/9,751.
  - The one failure was `CultureInvariantEmissionTests.AProgramRunUnderTurkishAndGermanCultures_PrintsTheInvariantText`, which this change does not touch. It failed the same way in both runs, here and in `20260927-165445`.
  - Its program printed `en-US` where `tr-TR` was expected, and every other line matched. The test selected the child's culture through `LANG`/`LC_ALL`, which .NET on Windows ignores, and this machine's OS culture is `en-US`.
  - That is fixed on `main` by `.kiro/specs/target-platform` (`ee991fc0`, merged in `50543abc`): a program compiled for Windows now applies those variables itself.

**Limits.**
- No test drives the static `SdkDiscovery` wiring, since that would spawn `dotnet` and write `~/.mist`. The rewrite of the real cache above is the only evidence for that wiring.
- Two processes that publish at the same moment can each rewrite the file without the other's new entry. The lost entry is a later miss, never a wrong answer.

**Integration verification (2026-09-28).**
- Merged current `main` at `f1839449`. Nothing conflicted, and `SdkDiscovery.cs` had not changed on `main` since `666c596c`.
- The full ledger `20260928-011405` passed 11,041 of 11,041, including all 32 new cases and `CultureInvariantEmissionTests`. An earlier merge, at `50543abc`, had passed 11,013 of 11,013 in `20260928-010931`.
- `dotnet build Mist.slnx --no-incremental` had 0 warnings and 0 errors. The formatter reported only the IDE0047 already on `main` at `ClrSpellingTests.cs:30`.

---

## 🟢 v2 never runs a base struct's constructor for a derived struct constructor that writes no chain

**Closed (2026-10-03):** An unchained derived struct constructor now calls the accessible base initializer selected by `ImplicitChains.Of` before its own body; delegation through three levels and explicit `this`/`base` chains runs each ancestor initializer once. `DeclarationShapeAnalyzer` reports `MIST_STRUCT_BASE_CTOR_REQUIRED` at the derived constructor when no zero-argument chain is accessible. The original `Meter()` / `Dial(3)` probe now prints `7 3`; replacing `Meter()` with only `Meter(int reading)` reports that Mist diagnostic instead of compiling to `0 3`.

`StructImplicitBaseEmissionTests` (Tier 2), `StructImplicitChainsTests` (Tier 1), and `StructBaseConstructorRefusalTests` (Tier 2) cover the emitted order, three-level and explicit chains, selected `params` overload, generic substitution, constructorless bases, accessibility, static constructors and class isolation. Removing the implicit call and reversing the refusal condition made the three targeted mutation cases fail; restoring them passed the focused ledger, 31/31. After merging main at `61facc87`, the full ledger `20261003-010746` passed 8,892/8,892 with zero build warnings, and `dotnet format Mist.slnx --verify-no-changes` passed. This closes the implicit-chain and missing-chain issue recorded below. Explicit `: base()` accessibility and value-pack `params` specialization remain separate, unmeasured gaps in the analysis register.

**Severity: critical** -- a silent wrong result. The program compiles and prints a value that differs from classic's.

**Verified empirically** (2026-10-01) on both pipelines:

```mist
using Standard;

namespace P6;

public struct Meter
{
    public int reading;

    public Meter()
    {
        this.reading = 7;
    }
}

public struct Dial : Meter
{
    public int face;

    public Dial(int face)
    {
        this.face = face;
    }
}

public class Program
{
    public static void Main()
    {
        var d = new Dial(3);
        Console.WriteLine($"{d.reading} {d.face}");
    }
}
```

Classic prints `7 3`, and v2 prints `0 3`. A derived class keeps C#'s implicit `base()`, but a flattened struct has nothing to stand in for it.

**Cause:** `StructInitEmitter.EmitChainCall` emits a call only for a written `: base(...)` or `: this(...)`. When `ChainKind` is `None` it emits nothing, so no ancestor initializer runs.

**The refusal half of the same hole.** With `Meter(int reading)` as the base's only constructor instead, v2 still compiles the program and prints `0 3`. Classic reports `MIST_STRUCT_BASE_CTOR_REQUIRED`: "Struct 'Meter' requires constructor arguments. Add ': base(...)' to the constructor of 'Dial'." That is struct-inheritance Requirement 7.4, which no v2 rule enforces yet. It is recorded under `declaration-shape-checks` in `backlog/analysis-v2-capability-progression.yaml`.

**Action:**
1. When a derived struct constructor writes no chain, call the initializer of the base constructor that an implicit chain reaches. That is the parameterless or single-params constructor accessible from the derived struct. `ImplicitChains.Of` in `Source/Mist/ProgramAnalysis/` makes the same decision for analysis, so decide whether emission reads it or the model records it, rather than writing a second resolver.
2. Pin it with a Tier 2 or Tier 3 test on the emitted text over a hand-built struct pair, and mutation-check it.
3. Add the struct-inheritance 7.4 refusal as its own descriptor beside `MIST_PA_BASE_CTOR_UNSATISFIED`, reading the same decision.

---

## 🔴 A call that needs a context compiles with none in scope and throws at run time

**Closed (2026-10-02):** `ContextAnalyzer` (order 16 of the analysis register) reports `MIST_CTX_0001` for a call, `new`, constructor chain or method reference owed a context that is not held on every path from the entry point, and `MIST_CTX_0002` (duplicate clause key), `0004` (anonymous-typed key), `0005` (context-bound name reassigned) and `0006` (an implementation's clauses differ from its slot's). The program below is now `MIST_CTX_0001: Method 'GetString' requires context of type 'string', but no matching context declaration is in scope`. Still open, measured, and recorded as tasks on the register entry: a refined clause type is not proven (`with 0;` satisfies `with PositiveInt n`), `string?` and `string` share a runtime key, a lambda that outlives its context's scope fails at run time, and the emitter drops the clause on extension methods and abstract default bodies.

**Severity: critical** -- the structured-context discipline is not enforced by any surviving rule. Found
2026-10-01 while retiring classic, whose `ContextCheckerAnalyzer` (`MIST_CTX_0001` to `MIST_CTX_0006`)
was the only implementation and whose tests were its only proof.

**Verified empirically** (2026-10-01):

```mist
using Standard;

namespace Probe;

public class Helper
{
    public static string GetString() with string s
    {
        return s;
    }
}

public class Program
{
    public static void Main()
    {
        Console.WriteLine(Helper.GetString());
    }
}
```

It builds with no diagnostic and ends in `KeyNotFoundException: The given key 'System.String' was not
present in the dictionary` at `MistContext.Peek[T]()`. `.kiro/specs/structured-context` requires the
call to be refused. `samples/StructuredContext` passes because every call in it has a context.

**Not measured:** the abstract `with`-clause contract and reassignment of a context-bound name, which
classic also refused. No file under `Source/Mist/ProgramAnalysis/Rules/` reads a context clause.

**Action:** a Stage 5 rule, entered in `backlog/analysis-v2-capability-progression.yaml`, proven at
Tier 2 since it walks a body.

---

## 🟡 A pack-typed abstract slot cannot be filled at a concrete arity

**Closed (2026-10-02):** a pack-typed slot is read through `ValuePack.Expand` at the arity the conformer's base list binds, so the program below is admitted, runs through the abstract and directly, and prints `5`; one-element, zero-arity and mixed bindings follow the same rule, and a wrong arity or type is `MIST_PA_SLOT_SIGNATURE_MISMATCH` against the expanded signature. Two consequences: the tuple-parameter form (`int Apply((int, int) pair)`), the only one admitted before, no longer fills the slot, and a class redeclaring an inherited member whose pack its base list closed is `MIST_PA_MEMBER_REDECLARATION`.

**Severity: medium** -- variadics Requirement 22.2 says a variadic abstract is implementable at a
concrete arity. Found 2026-10-01 while retiring classic, which admitted it.

**Verified empirically** (2026-10-01):

```mist
public abstract IApplier<params TArgs, TResult>
{
    TResult Apply(params TArgs args);
}

public class Adder : IApplier<int, int, int>
{
    public int Apply(int a, int b)
    {
        return a + b;
    }
}
```

`error MIST_PA_SLOT_SIGNATURE_MISMATCH: 'Adder' does not fill slot 'int Apply((int, int))' of
'IApplier': its 'Apply' has a different signature.` The slot keeps the pack as one parameter where the
implementation spreads it. The generic form, `class Boxed<params TArgs> : IApplier<TArgs..., int>` with
`int Apply(params TArgs args)`, is admitted, and a missing `Apply` is refused in both forms
(`MIST_PA_SLOT_NOT_FILLED`). `samples/Variadics` implements only a slot with no pack in it.

**Action:** expand a pack-typed slot at the instantiation's arity before conformance compares
signatures; pin the two admitted forms and the refusal beside the conformance rule.

---

## 🟡 Two refusals reach Roslyn instead of the frontend

**Closed (2026-10-02):** an undefined `[global_usings]` namespace is the warning `MIST_GLOBAL_USING_UNDEFINED_NAMESPACE`, once per project and name, and is withdrawn from the scope and the emitted usings so the build continues; an explicit `using` of one is the error `MIST_UNDEFINED_NAMESPACE` at the directive (`UsingValidation`). A call on a value whose type is not a function or delegate is `MIST_PM_NOT_CALLABLE` at the call, naming the namespace when an extension method of that name exists unimported; a union-typed callee is refused before narrowing, and an optional `func?` called un-narrowed is left as it was. The last part closed 2026-10-03 (`7705ba3a`): a `using` of a namespace that holds only type aliases reached Roslyn, because the emitter wrote a using for a namespace it emits nothing into; the emitter now writes a using only for a namespace the C# compilation declares (`UsingSelection`; recorded under Usings in `backlog/classic-retirement.md`).

**Severity: medium** -- the build fails, but with `MIST_CS_ERROR` over generated C#. Found 2026-10-01
while retiring classic, which reported both itself.

**Verified empirically** (2026-10-01):

1. A `[global_usings]` namespace that exists nowhere, `namespaces = ["Standard", "No.Such.Namespace"]`:
   `out/Probe/Program.cs: error MIST_CS_ERROR: The type or namespace name 'No' could not be found`.
   global-usings Requirement 4.3 asks for the warning `MIST_GLOBAL_USING_UNDEFINED_NAMESPACE` and a
   build that continues; nothing reports it and the name is emitted as a `using`.
2. A CLR extension called without its namespace, `numbers.Count()` on a `List<int>` with no
   `using System.Linq`: `error MIST_CS_ERROR: Non-invocable member 'List<int>.Count' cannot be used
   like a method.` The call binds to the `Count` property. extension-method-binding 3.2 and 7.1 make
   the extension unavailable, which holds, but the refusal should be the binder's. With the `using`
   the program prints `1`.

**Action:** report the first at the point the assembly scope imports the name, and refuse invoking a
non-callable member in the binder; one Tier 1-3 row each.
