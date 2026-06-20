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
