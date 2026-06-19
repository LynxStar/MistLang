# Future Work

Tracked issues and improvements that don't belong to an active spec but should be addressed.

---

## Remove Task<T> storage restriction (MIST_TC_0110)

`MIST_TC_0110` prevents `Task<T>` from being stored in fields or properties. This was a protective measure before drop semantics existed — without it, a task stored in a field could escape its structured scope and be silently abandoned.

With drop semantics in place, this restriction is obsolete. Substructural typing already ensures that any container holding a Task has the same stack-reachability guarantee that a bare Task local has: the container can only outlive the function if it's returned (same as the Task restriction). There is always something on the stack pointing back to the live task — either a local variable or a container with equivalent constraints.

**Action:** Remove the `MIST_TC_0110` diagnostic and its associated checks. Allow `Task<T>` in fields/properties when the containing type has appropriate substructural discipline. Update tests accordingly.


---

## Missing `defaultable` constraint on generic type parameters

`default(T)` is syntactically allowed inside generic method/type bodies without any constraint on `T`. The compiler only checks defaultability for concrete types during semantic model construction — generic type definitions are skipped entirely (`ComputeDefaultability` bails out for types with unbound type parameters).

This means a generic like:

```mist
class Box<T>
{
    T value;
    public void Reset() { this.value = default(T); }
}
```

compiles without error at the definition site. If someone instantiates `Box<ISignal>` (where `ISignal` is an interface with no default), the `__CreateDefault` call will fail at the concrete instantiation — but the error message won't point to the `default(T)` usage, and the failure mode may be confusing.

**Options:**
1. Add a `where T : defaultable` constraint (or make it implicit when `default(T)` appears in the body). Reject the generic definition if `default(T)` is used without the constraint.
2. Emit a clear error at instantiation time: "Type argument 'ISignal' is not defaultable, but 'Box<T>' requires a defaultable T because it uses default(T) internally."
3. Accept the current monomorphization-time error as sufficient (C++ template model).

**Recommendation:** Option 1 is the most principled — it surfaces the error at definition time and makes the requirement visible in the type signature. Option 2 is the pragmatic middle ground if adding constraints is too noisy.

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
