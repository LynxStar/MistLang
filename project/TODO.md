# Future Work

Tracked issues and improvements that don't belong to an active spec but should be addressed.

---

## Ownership System Gaps

### 1. Field assignment provenance not enforced

Assigning to a field of an owned-type instance does not check that the source value has owned provenance. The spec (Req 4.5/4.6) defines provenance checks for owned *binding* initialization, and Req 10.3 defines them for `owned`-modifier fields, but neither is enforced at assignment sites.

**Broken example:**
```mist
public owned class Widget { public int Count; }
public owned class Container { public Widget Item; }

var foo = new Widget(5);           // shared binding
owned var container = new Container(foo);  // no error — should reject
container.Item = foo;              // no error — should reject
```

Both `new Container(foo)` and `container.Item = foo` should fail because `foo` is not an owned binding. Storing a shared reference breaks the "self-contained graph" guarantee.

This also applies inside constructors:
```mist
public Container(Widget item) { this.Item = item; }  // item is not owned — should fail
```

The correct constructor signature would be `public Container(owned Widget item)`.

**Scope:** This is both a requirements gap (no explicit requirement for field assignment provenance on owned-type fields without the `owned` modifier) AND an implementation gap (Req 10.3 for `owned`-modifier fields exists but isn't enforced).

### 2. Relax owned field restriction: allow deeply readonly fields

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
