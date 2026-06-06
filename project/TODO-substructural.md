# Substructural Type System — Future Work

Gaps and improvements specific to the substructural type system (linear, affine, relevant).

---

### 1. Field discipline check uses wrong rules

The field containment check (`MIST_DROP_FIELD_NOT_OWNED`) currently only asks "is the container owned?" — if yes, affine/linear fields are permitted; if no, they're rejected. But it doesn't enforce the substructural compatibility between container and field disciplines.

The correct rules (derived from each container's drop guarantees):

| Container | Permitted fields |
|-----------|-----------------|
| **linear** (exactly once) | linear, affine, relevant, unrestricted — anything is safe |
| **affine** (0 or 1 times) | affine, unrestricted only — relevant/linear need ≥1 drop |
| **relevant** (1+ times) | relevant, unrestricted only — affine/linear need ≤1 drop |

Currently an affine container can hold a relevant field (wrong — if the affine is never dropped, the relevant field's "must be used" invariant is violated). Similarly a relevant container can hold an affine field (wrong — if the relevant is dropped multiple times, the affine field's "at most once" invariant is violated).

### 2. Generic fields bypass substructural field constraints

The field containment check validates concrete field types but doesn't constrain generic type parameters used as fields:

```mist
// Correctly rejected:
public linear class Bad { string Value; }  // MIST_DROP_FIELD_NOT_OWNED

// Incorrectly accepted:
public linear class Sneaky<T> { T Value; }  // No error — T could be string
```

A `Sneaky<string>` would violate the same invariant. The fix: generic type parameters used as fields in substructural types need constraints (e.g., `where T : linear`), or the instantiation site needs validation.
