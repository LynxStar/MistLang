# Future Work

Tracked issues and improvements that don't belong to an active spec but should be addressed.

---

## Type System Gaps

### Task<T> not recognized as relevant when used as a field in linear types

Storing a `Task<T>` in a field of a `linear` class triggers `MIST_TC_0110` (field type does not satisfy the type's substructural discipline). The type checker doesn't recognize `Task<T>` as relevant despite it having `Drop` (via `await`) and being scope-exit-dropped by the emitter.

This means you can't write:

```mist
public linear class TaskBox
{
    Task<string> Inner;  // ERROR: MIST_TC_0110

    public TaskBox(Task<string> inner) { this.Inner = inner; }
    public Task<string> Drop() { return Inner; }
}
```

**Expected behavior:** `Task<T>` should be recognized as relevant (it has a Drop mechanism via `await`), allowing it to be stored in linear/relevant type fields.

**Workaround:** Avoid storing `Task<T>` directly in linear type fields. Use alternative patterns (e.g., return `ResourceHandle` from Drop instead of `Task<T>`).

**Affected code:** Type checker's substructural field validation (likely `TypeCheckerAnalyzer` or ownership analysis).
