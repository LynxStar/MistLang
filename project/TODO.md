# Future Work

Tracked issues and improvements that don't belong to an active spec but should be addressed.

---

## Substructural Type System Gaps

### 1. Substructural field discipline check is too restrictive

The field discipline check (`MIST_TC_0110`) rejects fields whose type doesn't match the containing type's discipline. But a container's substructural discipline governs how the *container itself* is used — not what it can hold. Any type is safe to store inside any substructural container:

- Unrestricted types (string, int, etc.) are always safe as fields — they have no usage obligations, so they don't interfere with the container's discipline.
- The container's Drop method handles cleanup of its contents regardless of field types.

The current check is overly conservative. The only real constraint is on the *container's usage* (enforced at use sites), not on what goes inside it.

**Concrete example:** `Task<T>` is relevant. A `linear` class should be able to store it since the linear container's Drop will handle the Task's cleanup — but `MIST_TC_0110` fires.

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
