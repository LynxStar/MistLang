# Future Work

Tracked issues and improvements that don't belong to an active spec but should be addressed.

---

## Ownership — Open Design Questions

### Statics and Method Body Safety

There's a gap between "owned type cooperation" and static/global state:

**The problem:** An owned type's constructor or method could store a reference from a static field into an instance field. From the call site, you can't tell this happened. Multiple `owned var` instances of the same type would share state through the static, breaking the "unique graph" guarantee.

```mist
public owned class Sneaky
{
    Widget cache;
    public Sneaky() { this.cache = StaticCache.Instance; }  // alias from global
}

owned var a = new Sneaky();  // a.cache == StaticCache.Instance
owned var b = new Sneaky();  // b.cache == same object — not unique!
```

The cooperation contract verifier currently rejects static reads as field assignment sources (emits `MIST_OWN_CONTRACT_VIOLATION`). But the broader question remains:

**Unresolved:** How to ergonomically express both "returns owned" and "body-safe for owned context" on the same method. What static field reads are safe (readonly statics? deeply immutable statics?). Whether method-level granularity is needed or type-level is sufficient.

### Non-Owned Type Constructors in Owned Bindings

`new NonOwnedType()` is accepted as a valid owned binding initializer (fresh allocation = unique root). But the constructor body of a non-owned type has no cooperation contract — it might store shared/static references into fields during construction.

```mist
public class Foo
{
    public Widget Item;
    public Foo() { this.Item = StaticCache.Get(); }  // alias leaks in
}

owned var foo = new Foo();  // root is unique, but foo.Item is aliased
```

For owned types, the cooperation contract catches this. For non-owned types, there's no verification — the type doesn't opt in.

**Current stance:** Accept this as a known weakness. `new NonOwnedType()` gives you a unique root. The mutable-methods-hidden rule prevents further pollution after construction. But the initial state may contain shared references from the constructor. Full graph uniqueness requires the owned type cooperation contract.

**Future options:**
- Constructor body analysis at the call site (expensive, may not be feasible across compilation units)
- Require non-owned types to have only "trivial" constructors (field assignment from parameters only) to be eligible for owned bindings
- Accept the weaker guarantee and document it

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
