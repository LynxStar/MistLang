# Findings: Interop Blockers for EF Core DbContext Implementation

## Status: ONE ISSUE REMAINING

## Task
3.1 — Implement EF Core DbContext and entity configurations

## Summary

All original issues (1–5) plus follow-on issues (6–9, 11) are resolved. Issue 10 (chained fluent API type parameter propagation through CLR base class hierarchies) remains as the only blocker for relationship configuration.

---

## Resolved Issues

| Issue | Description | Fix |
|-------|-------------|-----|
| 1 | `protected` access modifier | Mist private maps to C# protected for implicit overrides |
| 2 | `override` access level | Implicit override detection in C# emitter |
| 3 | Generic method lambda inference | Type arg substitution + CLR Action/Func detection |
| 4 | Extension method lambda inference | Receiver offset + overload selection by param count |
| 5 | Generic constructor param resolution | ConstructedType → base ClassType assignability |
| 6 | Constructed generic member resolution | Disconnected shell fallback for generic defs |
| 7 | Extension method ambiguity | ConstructedType base chain in IsReceiverAssignableTo |
| 8 | Expression<Func<T>> unwrapping | TryUnwrapExpressionFunc for nested lambda targets |
| 9 | HasMany/OwnsOne lambda inference | LookupMemberSymbolByParamCount + lambda-compatible preference |
| 11 | CRTP stack overflow | Cycle detection in ImplementsConstructedInterface |

---

## Issue 10: Chained Fluent API Type Parameter Propagation — REMAINING

### Symptom
```mist
entity.HasMany(e => e.WorkItems).WithOne().HasForeignKey(e => e.ReqId);
```
Error: `'TRelatedEntity' does not contain a definition for 'ReqId'`

The lambda param `e` in `HasForeignKey` is typed as the raw type parameter `TRelatedEntity` instead of the concrete `WI10`.

### What Works
- `entity.HasMany(e => e.WorkItems)` — lambda inference correct, return type correctly inferred as `CollectionNavigationBuilder<Req10, WI10>` ✓
- `nav.WithOne()` — resolves, member access on result works (`.Metadata.PrincipalEntityType.Name`) ✓
- `rel.HasForeignKey(e => e.ReqId)` — FAILS: `TRelatedEntity` not substituted ✗

### Root Cause (Confirmed via tracing)
The `TypeArgumentInferenceEngine.TryInfer` fails when trying to infer `TRelatedEntity` from the resolved lambda type. Specifically:

- **Param type**: `Expression<Func<TEntity, IEnumerable<TRelatedEntity>>> | void`
- **Arg type**: `Func<Req10, List<WI10> | Void>`

The inference engine's `Unify` method cannot:
1. **Unwrap `Expression<T>`** — it doesn't know that `Expression<Func<...>>` wraps a `Func`
2. **Match `List<WI10>` against `IEnumerable<TRelatedEntity>`** — it requires `ReferenceEquals(GenericDefinition)` but `List` ≠ `IEnumerable`; it doesn't handle interface implementation (List implements IEnumerable)
3. **Handle nullable unions** — `List<WI10> | Void` doesn't unify against `IEnumerable<TRelatedEntity>`

### Fix Direction
Extend `TypeArgumentInferenceEngine.Unify` (in `TypeArgumentInferenceEngine.cs`) to handle:
- Expression<T> unwrapping before unification
- Interface implementation: when arg is `List<T>` and param is `IEnumerable<T>`, find the matching interface and unify type args
- Nullable union unwrapping: strip `| Void` before unification

---

## What Currently Compiles ✅

```mist
void OnModelCreating(ModelBuilder modelBuilder)
{
    modelBuilder.Entity<Request>(entity =>
    {
        entity.HasKey(e => e.RequestId);
        entity.Property(e => e.RequestId).ValueGeneratedOnAdd();
        entity.Property(e => e.Type).HasConversion<string>();
        entity.HasIndex(e => e.State);
        entity.HasMany(e => e.WorkItems);
        entity.ToTable("Requests");
    });
}
```

### What's Blocked
- `entity.HasMany(e => e.WorkItems).WithOne().HasForeignKey(e => e.ReqId)` — Issue 10
