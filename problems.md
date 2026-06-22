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

### Root Cause
`HasForeignKey` is likely declared on a non-generic or differently-parameterized base class of `ReferenceNavigationBuilder<TEntity, TRelatedEntity>`. When `LookupMemberType` resolves `HasForeignKey` through the base class chain, the type parameters at the base level have different names/ordinals than the ones at the `ReferenceNavigationBuilder` level, so the ConstructedType substitution doesn't reach them.

This is a CLR base class type parameter forwarding problem: when `ClassA<T1, T2> : ClassB<T2>`, calling a method on `ClassA<Req, WI>` that's defined on `ClassB<TDependentEntity>` needs `TDependentEntity → WI` (mapping through the intermediate inheritance).

### Impact
Relationship configuration via fluent API chains is blocked:
- `HasMany(...).WithOne(...).HasForeignKey(...)` ✗
- Individual calls that don't depend on type param forwarding through base classes work ✓

### Potential Fix Direction
In `LookupMemberType`, when walking the base type chain of a ConstructedType, substitute the type arguments at each level through the base type's type argument mapping. E.g., if `ReferenceNavigationBuilder<TEntity, TRelatedEntity>` extends `ReferenceNavigationBuilder<TRelatedEntity>` (forwarding the second type arg as the first arg to the base), the substitution chain needs to carry `TRelatedEntity → WI10` from the derived level down into the base's member scope.

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
