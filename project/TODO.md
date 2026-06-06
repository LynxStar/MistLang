# Future Work

Tracked issues and improvements that don't belong to an active spec but should be addressed.

---

## Substructural Type System Gaps

### 1. Substructural field discipline check uses wrong rules

The field containment check (`MIST_DROP_FIELD_NOT_OWNED`) currently only asks "is the container owned?" — if yes, affine/linear fields are permitted; if no, they're rejected. But it doesn't enforce the substructural compatibility between container and field disciplines.

The correct rules (derived from each container's drop guarantees):

| Container | Permitted fields |
|-----------|-----------------|
| **linear** (exactly once) | linear, affine, relevant, unrestricted — anything is safe |
| **affine** (0 or 1 times) | affine, unrestricted only — relevant/linear need ≥1 drop |
| **relevant** (1+ times) | relevant, unrestricted only — affine/linear need ≤1 drop |
| **owned** (exactly once, like linear) | owned types only (existing rule) |

Currently an affine container can hold a relevant field (wrong — if the affine is never dropped, the relevant field's "must be used" invariant is violated). Similarly a relevant container can hold an affine field (wrong — if the relevant is dropped multiple times, the affine field's "at most once" invariant is violated).

### 2. Generic fields bypass substructural and ownership field constraints

The field containment check validates concrete field types but doesn't constrain generic type parameters used as fields:

```mist
// Correctly rejected:
public linear class Bad { string Value; }  // MIST_DROP_FIELD_NOT_OWNED

// Incorrectly accepted:
public linear class Sneaky<T> { T Value; }  // No error — T could be string
```

A `Sneaky<string>` would violate the same invariant. The fix: generic type parameters used as fields in substructural/owned types need constraints (e.g., `where T : linear`), or the instantiation site needs validation.

This likely also applies to `owned` types — a generic field could bypass the "all fields must be owned" rule if T is unconstrained.

### 3. Relax owned field restriction: allow deeply readonly fields

Currently owned types require ALL reference-type fields to be owned types. This is overly restrictive — the invariant we're actually protecting is: "if you own something and you don't touch it, its state cannot change."

Mutable unrestricted fields violate this (external aliases can mutate the field's state without the owner's involvement). But *deeply immutable* fields don't — no mutation path exists through any alias.

**Proposed rule change:** Owned type fields must be either:
- Owned types (existing rule — sole ownership of mutable data)
- Deeply readonly types (immutable through entire reachable graph — safe to alias)

**Prerequisite:** Deep readonly enforcement. Currently `readonly` is shallow — it prevents reassigning the field but doesn't guarantee the referent's internal state is immutable. We need `readonly` on a type to mean the entire reachable object graph is immutable.

**Solves:** The string-in-owned-type ergonomics problem. `string` is already immutable through and through — with deep readonly it would naturally qualify as an owned-type field without requiring move semantics or borrow tracking on strings.

**Invariant preserved:** Owned objects never mutate without their owner's action. Deeply readonly fields can't mutate at all (no mutation path through any alias), so external sharing is harmless.

### 3. Remove Task<T> storage restriction (MIST_TC_0110)

`MIST_TC_0110` prevents `Task<T>` from being stored in fields or properties. This was a protective measure before drop semantics existed — without it, a task stored in a field could escape its structured scope and be silently abandoned.

With drop semantics in place, this restriction is obsolete. Substructural typing already ensures that any container holding a Task has the same stack-reachability guarantee that a bare Task local has: the container can only outlive the function if it's returned (same as the Task restriction). There is always something on the stack pointing back to the live task — either a local variable or a container with equivalent constraints.

**Action:** Remove the `MIST_TC_0110` diagnostic and its associated checks. Allow `Task<T>` in fields/properties when the containing type has appropriate substructural discipline. Update tests accordingly.

### 4. Forbid owned-type fields in non-owned containers; sunset `owned` field modifier

Non-owned containers (unrestricted classes without `owned`/`affine`/`linear`) should not be allowed to hold affine/linear/owned-type fields. Multiple aliases to the container means multiple paths to the field, which fundamentally breaks exclusive ownership guarantees. The current `MIST_DROP_FIELD_NOT_OWNED` check already partially enforces this (rejecting affine/linear fields in non-owned containers), but it should be comprehensive.

**Consequence:** The `owned` field modifier (Requirement 10 in deep-ownership-types spec) becomes obsolete and should be removed entirely. It existed to allow non-owned containers to exclusively own specific field instances, but this is architecturally unsound — you can't guarantee exclusive access to a field when the container itself is aliasable.

**Action:**
- Enforce: non-owned containers cannot hold fields whose type is affine, linear, or owned
- Remove: the `owned` keyword on field declarations (parser, type checker, emitter, tests)
- Remove: Requirement 10 acceptance criteria and related diagnostics (`MIST_OWN_FIELD_NOT_OWNED` for the modifier case)
- Update: DeepOwnershipTypes sample to remove `owned` field modifier usage
