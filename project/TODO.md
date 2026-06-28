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
