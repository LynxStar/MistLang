# Future Work

Tracked issues and improvements that don't belong to an active spec but should be addressed.

---

## 🔴 CRITICAL: Array (and Span) indexer upper bound is deferred to runtime for unknown-length types

**Severity: critical** — Mist advertises static bounds safety via the `ArrayBounds` refinement (`>= 0 and < Length`), but that guarantee silently degrades to a runtime check whenever the length is not a compile-time constant. A program can compile with zero errors and throw `System.IndexOutOfRangeException` at runtime.

**Verified empirically** — the following compiles clean (0 errors, 0 warnings) and crashes at runtime:

```mist
int[] numbers = new int[3];
numbers[7] = 10;   // compiles; throws System.IndexOutOfRangeException at runtime
```

**Root cause:** `ValidateArrayIndexRefinement` in `Source/Mist/Analysis/Builtin/TypeCheckerAnalyzer.cs` (~line 13059). When an array's `ArrayType` has neither `KnownLength` nor `SymbolicLengthId` (the "unknown length" case), only the lower bound `>= 0` is enforced statically and the upper bound is "deferred to runtime — matching C# semantics." Crucially, declaring a local with an explicit array annotation (`int[] numbers = new int[3]`) **erases** the known length `3` to unknown, so even a freshly-created concrete-length array loses its static upper-bound protection the moment it is bound to an annotated local. A literal index `>= 0` is accepted (the literal-fallback branch returns when `!KnownLength.HasValue`), and non-constant indices on unknown-length arrays are accepted unconditionally (Case 4 returns without a diagnostic).

**Why this matters now:** The `stack-only-types` spec deliberately mirrors `Array` for `Span<T, Length>` (Option 1), so spans inherit this exact hole. Closing the array gap closes it for spans too, since spans reuse the same bounds machinery.

**Action (to scope into its own spec):**
1. Decide the intended guarantee: should unknown-length indexing be a hard compile error (no runtime fallback), or should it require an explicit opt-in / pattern-match to index?
2. Stop erasing `KnownLength` when a known-length creation is bound to an annotated local of the same element type (narrow the local's tracked length, like nullable flow narrowing) so `int[] x = new int[3]` retains length 3.
3. Re-evaluate the literal-fallback branch (`longVal >= 0 && !KnownLength.HasValue → return`) and Case 4's unconditional accept for unknown-length arrays.
4. Apply the same tightening to `Span<T, Length>` / `ReadOnlySpan<T, Length>` once they exist.
5. Audit the blast radius: existing samples/tests/snippets that rely on runtime-deferred indexing into unknown-length arrays will need updating.

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

## `mist build samples/Testing --pipeline experimental` throws before any rule runs

**Severity: medium** -- the only unbanked sample that cannot be measured on v2 at all. The ambient
projection of `Microsoft.TestPlatform.CoreUtilities` (the `netcoreapp3.1` build under
`microsoft.testplatform.objectmodel/17.12.0`) throws `TypeLoadException` for
`System.Diagnostics.CodeAnalysis.NotNullIfNotNullAttribute` inside `ClrNullability.FirstFlag`, and the
CLI dies with exit code 134. Classic builds the same project cleanly.

**Verified** at `a5b8ec2c` in a clean worktree, so it predates Stage 5's order 5, which found it while
sweeping the three unbanked samples. Reading attributes off a reference assembly built for an older
framework needs the metadata-only load path, or the attribute lookup needs to tolerate a type the
loaded framework does not define.
