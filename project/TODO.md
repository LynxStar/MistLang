# Future Work

Tracked issues and improvements that don't belong to an active spec but should be addressed.

---

## Ownership Redesign: Instance-Level Ownership

### Core Insight

Ownership is a property of the *value/binding*, not the type. Any type can be owned at the instance level — the type doesn't need to declare anything. The rules flow from the binding state.

### Current State (what exists today)

- `owned` on type declarations — requires all fields to be owned types (structural check)
- `owned` on field declarations — requires field type to be owned, enforces provenance at assignment
- `owned var` bindings — type must be declared `owned` or pass structural check
- Struct ownership inference — structs with all owned-type fields are inferred as owned

### Target State (what it should become)

- **Remove** `owned` from type declarations entirely — no type-level ownership concept
- **Remove** `owned` from field declarations — redundant with binding-level rules
- **Remove** structural "is this type eligible for ownership" check — any type can be owned
- **Remove** struct ownership inference — not needed when ownership is purely instance-level

- **Keep** `owned var x = ...` — this instance is owned, I'm the sole holder of the graph
- **Keep** `owned T param` — caller must move ownership to me
- **Keep** `owned T` return — I'm handing out a fresh owned value

### Rules (binding-level enforcement)

1. `owned var x = new T()` — establishes ownership. `new` produces a fresh value with no aliases.
2. All field assignments on an owned instance must come from an owned source (owned binding, constructor call, owned-returning function). Assigning a shared value into an owned instance breaks the "sole owner" guarantee.
3. Method calls on an owned instance that store parameters into the instance's fields — same rule, parameters must have owned provenance.
4. `var y = x` where x is owned — move semantics. x is dead, y holds the value without ownership tracking (downleveled to shared).
5. Deeply readonly/immutable values (string, primitives) are always valid as owned sources — aliasing them doesn't matter since they can't mutate.

### What This Solves

- **No bifurcation** — any type can participate in both owned and shared contexts
- **No structural analysis** — no need to recursively prove field types are owned
- **String just works** — deeply readonly, always a valid source for owned assignment
- **Simpler mental model** — "owned" describes what you're doing with a value, not what the type is

### Migration Path

1. Remove `owned` keyword from type declarations (parser, semantic model, type checker)
2. Remove `owned` keyword from field declarations
3. Remove `MIST_OWN_FIELD_NOT_OWNED` diagnostic and structural checks
4. Remove `MIST_OWN_TYPE_REQUIRES_OWNED` diagnostic
5. **Add** field assignment provenance enforcement: when assigning to a field of an owned-bound instance, source must be owned or deeply readonly
6. Update stdlib bootstrap types — remove `owned` from String, Int32, etc. declarations
7. Update samples and tests

### Implementation Gap (exists today regardless of redesign)

Field assignment provenance is not enforced at all. Even with today's `owned` field modifier (Req 10.3), assigning from a shared binding to an owned field compiles without error. This must be fixed whether we do the redesign or not.

---

## Remove Task<T> storage restriction (MIST_TC_0110)

`MIST_TC_0110` prevents `Task<T>` from being stored in fields or properties. This was a protective measure before drop semantics existed — without it, a task stored in a field could escape its structured scope and be silently abandoned.

With drop semantics in place, this restriction is obsolete. Substructural typing already ensures that any container holding a Task has the same stack-reachability guarantee that a bare Task local has: the container can only outlive the function if it's returned (same as the Task restriction). There is always something on the stack pointing back to the live task — either a local variable or a container with equivalent constraints.

**Action:** Remove the `MIST_TC_0110` diagnostic and its associated checks. Allow `Task<T>` in fields/properties when the containing type has appropriate substructural discipline. Update tests accordingly.
