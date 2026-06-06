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

### 3. Task<T> structured concurrency storage restriction (MIST_TC_0110)

`Task<T>` cannot be stored in fields or properties at all — this is the structured concurrency rule preventing tasks from escaping their scope. This is separate from the substructural field check and may need revisiting depending on whether certain patterns (like a linear type holding a Task that it awaits in its Drop) should be permitted.
