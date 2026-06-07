# Future Work

Tracked issues and improvements that don't belong to an active spec but should be addressed.

---

## Ownership Redesign

### Core Insight

Ownership is a property of the *value/binding*, not purely the type. Any type can be owned at the instance level via `owned var`. The `owned` keyword on types survives but with a different meaning: a cooperation contract, not a field restriction.

### Owned Types — New Meaning

`owned` on a type declaration means: "I, the type author, proactively cooperate with ownership semantics. All mutations inside this type maintain ownership invariants — internal field assignments come from move operations."

This does NOT restrict what fields the type can have. It signals: "this type is safe to use as an owned binding with full method access because I handle the complexity internally."

### Method Call Rules on Owned Bindings

When you hold a variable in an `owned var` binding:

**Non-owned types (tier 3):**
- Direct field/property assignment — allowed, caller proves provenance (RHS must be owned)
- Readonly methods — always callable
- Mutating methods with no parameters — callable (self-mutation is fine)
- Mutating methods WITH parameters — **NOT callable** (can't prove the method won't store the parameter into a field, breaking the graph)

**Owned types (full cooperation):**
- All of the above, PLUS mutating methods with parameters are callable
- The type author guarantees correctness: any method parameter that gets stored into a field is declared `owned` on the method signature
- The compiler verifies inside the type body that field assignments come from owned sources

**In both cases:**
- `owned.Field = expr` requires `expr` to be owned (move into the graph)

### IClonable — Shared-to-Owned Bridge

When assigning to an owned destination and the source isn't owned, the compiler checks if the source type implements `IClonable<T>`:

```mist
public interface IClonable<T>
{
    public owned T Clone();
}
```

- If implemented: `.Clone()` is called (implicitly or explicitly) to produce a fresh owned value
- If not implemented: compile error — can't prove the value is unique
- Auto-derivable: for types where all fields are themselves clonable (primitives, other clonables)

**Struct auto-derivation:** Structs that contain only other structs (recursively — no classes/interfaces/traits) automatically derive `IClonable<T>`. Pass-by-value already produces a fully owned object graph for pure-struct types. The compiler can optimize away the `.Clone()` call entirely since the copy is implicit in the value semantics.

Structs that embed classes/interfaces/traits are NOT auto-derivable — the heap-allocated reference could be aliased, so a bitwise copy doesn't produce a unique graph.

**String**: would implement `IClonable<String>` trivially (the "clone" is a no-op since strings are immutable and interned).

### What Changes From Today

**Remove:**
- Owned type field restriction (all fields must be owned types) — owned types can have any fields
- `owned` field modifier on individual fields — redundant with binding-level rules
- `MIST_OWN_FIELD_NOT_OWNED` for the field-modifier case
- Structural "is this type eligible for ownership" analysis
- Struct ownership inference (not needed — any struct can be owned at binding level)

**Keep:**
- `owned` on type declarations — but with new meaning (cooperation contract, not field restriction)
- `owned var x = ...` — instance-level ownership binding
- `owned T param` — caller must move ownership in
- `owned T` return — handing out a fresh owned value
- Move semantics (`owned var y = x` makes x dead)
- Lending system (borrowed, frozen, readonly ref from owned bindings)

**Add:**
- Field assignment provenance enforcement on owned bindings (MISSING TODAY)
- Method call tier restrictions for non-owned types on owned bindings
- `IClonable<T>` interface and auto-derivation

### Implementation Gap (exists today)

Field assignment provenance is not enforced at all. Even with today's `owned` field modifier (Req 10.3), assigning from a shared binding to an owned field compiles without error. This must be fixed as part of the redesign.

### Open Design Question: Statics and Method Body Safety

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

**Two concerns are tangled:**
1. Method return annotation — "does this return an owned value?" (already have `owned T` return)
2. Method body safety — "is this method safe to call on an owned instance?" (doesn't leak external aliases into the graph)

**Options being considered:**
- `owned` on the type = compiler verifies ALL methods are body-safe (no field assignments from statics/globals). This is what the "cooperation contract" means — the type author opts into enforcement.
- Method-level annotation for body safety (more precise but more annotation burden)
- Conservative fallback for non-owned types: tier 3 rules (no parameterized mutating methods callable)
- Ban mutable static state of reference types in owned type bodies

**Unresolved:** How to ergonomically express both "returns owned" and "body-safe for owned context" on the same method. What static field reads are safe (readonly statics? deeply immutable statics?). Whether method-level granularity is needed or type-level is sufficient.

---

## Remove Task<T> storage restriction (MIST_TC_0110)

`MIST_TC_0110` prevents `Task<T>` from being stored in fields or properties. This was a protective measure before drop semantics existed — without it, a task stored in a field could escape its structured scope and be silently abandoned.

With drop semantics in place, this restriction is obsolete. Substructural typing already ensures that any container holding a Task has the same stack-reachability guarantee that a bare Task local has: the container can only outlive the function if it's returned (same as the Task restriction). There is always something on the stack pointing back to the live task — either a local variable or a container with equivalent constraints.

**Action:** Remove the `MIST_TC_0110` diagnostic and its associated checks. Allow `Task<T>` in fields/properties when the containing type has appropriate substructural discipline. Update tests accordingly.
