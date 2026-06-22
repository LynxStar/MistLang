# Findings: Interop Blockers for EF Core DbContext Implementation

## Status: PARTIALLY BLOCKED

## Task
3.1 — Implement EF Core DbContext and entity configurations

## Summary

Three issues block the full Fluent API configuration. Keys, auto-generation, enum conversions, and indexes all work. Relationships, owned types, and `EnableRetryOnFailure` are blocked.

---

## Issue 9: Extension Method Lambda Inference for HasMany/OwnsOne

### Symptom
```mist
entity.HasMany(e => e.WorkItems)
```
Error: `'EntityTypeBuilder<Request>' does not contain a definition for 'WorkItems'.`

The lambda parameter `e` is being resolved as `EntityTypeBuilder<Request>` (the receiver type) instead of `Request` (the `TEntity` generic argument).

### Root Cause
`HasMany` is an extension method on `EntityTypeBuilder<TEntity>` with signature:
```csharp
CollectionNavigationBuilder<TEntity, TRelated> HasMany<TRelated>(
    Expression<Func<TEntity, IEnumerable<TRelated>?>> navigationExpression)
```

The type inference needs to:
1. Resolve `TEntity` from the receiver (`EntityTypeBuilder<Request>` → `TEntity = Request`)
2. Use `TEntity` as the lambda parameter type in `Expression<Func<TEntity, ...>>`

Step (2) fails — the lambda parameter type defaults to the receiver type rather than extracting the generic argument.

### Same Issue Affects
- `entity.HasMany(e => e.WorkItems)` — `e` typed as `EntityTypeBuilder<Request>`
- `entity.OwnsOne(e => e.QualityTier, ...)` — `e` typed as `EntityTypeBuilder<DownloadAttempt>`

### Contrast With Working Pattern
`entity.HasKey(e => e.RequestId)` works because `HasKey` is a direct instance method on `EntityTypeBuilder<TEntity>` (not an extension method) where the lambda target type `Expression<Func<TEntity, object?>>` is directly resolved from the class's own type parameter.

### Expected Behavior
For extension methods on `EntityTypeBuilder<TEntity>`, the `TEntity` should be extracted from the constructed receiver type and substituted into the extension method's parameter types before lambda target type inference.

---

## Issue 10: Chained Fluent API Lambda Return-Type Propagation

### Symptom
```mist
entity.HasMany(e => e.WorkItems).WithOne(e => e.Request).HasForeignKey(e => e.RequestId)
```
Errors on `WithOne` and `HasForeignKey` lambdas — the return types of the chained methods aren't providing correct lambda parameter types.

### Root Cause
Even if Issue 9 were fixed, the return type of `HasMany(...)` is `CollectionNavigationBuilder<TEntity, TRelated>`. The subsequent `.WithOne(...)` method needs to infer its lambda parameter type from the `TRelated` generic argument of that return type. This is a multi-hop inference chain.

### Expected Behavior
Each method in the fluent chain should produce a correctly-typed generic return value, and the next method's lambda parameters should be inferred from that return type's generic arguments.

---

## Issue 11: NpgsqlDbContextOptionsBuilder Stack Overflow in Type Checker

### Symptom
Using `NpgsqlDbContextOptionsBuilder` as a lambda parameter type annotation causes the compiler to stack overflow:
```mist
options.UseNpgsql(
    connectionString,
    (NpgsqlDbContextOptionsBuilder npgsqlOptions) => npgsqlOptions.EnableRetryOnFailure(3)
);
```

Stack trace shows infinite recursion:
```
Repeated 3916 times:
  at TypeCheckerAnalyzer.IsAssignable(IType, IType, ISemanticModel)
  at TypeCheckerAnalyzer.IsAssignable(IType, IType)
  at TypeCheckerAnalyzer.ImplementsConstructedInterface(IType, ConstructedType)
```

### Root Cause
`NpgsqlDbContextOptionsBuilder` extends `RelationalDbContextOptionsBuilder<NpgsqlDbContextOptionsBuilder, NpgsqlOptionsExtension>` — a **Curiously Recurring Template Pattern (CRTP)**. The `IsAssignable` check enters infinite recursion when walking the type's interface/base chain because the type refers back to itself as a generic argument.

### Expected Behavior
CRTP patterns in CLR types should be handled with cycle detection in `IsAssignable`/`ImplementsConstructedInterface`.

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
    });
}
```

## What's Missing (blocked):
- Relationships: `HasMany(...).WithOne(...).HasForeignKey(...)` — Issues 9 + 10
- Owned types: `OwnsOne(e => e.QualityTier, qt => { ... })` — Issue 9
- EnableRetryOnFailure: `npgsqlOptions.EnableRetryOnFailure(3)` — Issue 11

---

## Priority Assessment

| Issue | Severity | Suggested Fix |
|-------|----------|---------------|
| **Issue 9** | High | Extract `TEntity` from constructed receiver type for extension method lambda inference |
| **Issue 10** | High | May auto-resolve with Issue 9; otherwise needs return-type generic arg propagation |
| **Issue 11** | Medium | Add cycle detection in `IsAssignable`/`ImplementsConstructedInterface` for CRTP |
