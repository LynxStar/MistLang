# Future Work

Tracked issues and improvements that don't belong to an active spec but should be addressed.

---

## Substructural Type System Gaps

### 1. Substructural field discipline check uses wrong rules

The field discipline check (`MIST_TC_0110`) is too restrictive. The correct rules depend on what drop behavior the container *guarantees* for its fields:

- **Linear container** (dropped exactly once): can hold anything — linear, affine, relevant, or unrestricted fields are all safe because exactly-once satisfies all constraints.
- **Affine container** (dropped 0 or 1 times): can only hold **affine** or **unrestricted** fields. The 0-drop case means relevant/linear fields might never get dropped, violating their "must be used" semantics.
- **Relevant container** (dropped 1+ times): can only hold **relevant** or **unrestricted** fields. The multi-drop case means affine/linear fields could get dropped multiple times, violating their "at most once" semantics.

This differs from owned types (where all fields must also be owned). The current check likely demands an exact discipline match or uses the owned-type rule uniformly.

**Concrete example:** `Task<T>` is relevant. A `linear` class can safely store it (linear guarantees exactly-once drop). But `MIST_TC_0110` fires.

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
