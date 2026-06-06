# Future Work

Tracked issues and improvements that don't belong to an active spec but should be addressed.

---

## Substructural Type System Gaps

### 1. Variance interactions between substructural types

The field discipline check (`MIST_TC_0110`) rejects fields whose type doesn't exactly match the containing type's discipline. But the substructural lattice has a natural variance: it should be safe to store types that are *more* restrictive than the container requires.

For example, a `linear` type requires fields that can't be silently discarded (no weakening) and can't be silently duplicated (no contraction). Both `relevant` and `affine` satisfy subsets of this, and `linear` itself satisfies all of it. The safe combinations:

- `linear` container: can hold `linear` fields (both restrictions met)
- `relevant` container: can hold `relevant` or `linear` fields (contraction restricted)
- `affine` container: can hold `affine` or `linear` fields (weakening restricted)

The current check likely demands an exact discipline match rather than checking the lattice ordering.

**Concrete example:** `Task<T>` is `relevant` (must be used/awaited, but can be aliased). A `linear` class should be able to store a `relevant` field since linear is strictly more restrictive — but `MIST_TC_0110` fires.

### 2. Generic fields bypass substructural and ownership field constraints

The field discipline check validates concrete field types but doesn't constrain generic type parameters used as fields. This means:

```mist
// Correctly rejected:
public linear class Bad { string Value; }  // MIST_TC_0110: string is unrestricted

// Incorrectly accepted:
public linear class Sneaky<T> { T Value; }  // No error — T could be string at instantiation
```

A `Sneaky<string>` would violate the same invariant that the concrete check catches. The fix: generic type parameters used as fields in substructural types need constraints (e.g., `where T : linear` or `where T : relevant`), or the instantiation site needs validation.

This likely also applies to `owned` types — a generic field could bypass the "all fields must be owned types" restriction if T is unconstrained.
