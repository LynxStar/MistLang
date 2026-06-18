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
