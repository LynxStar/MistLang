# Findings: Interop Blockers for EF Core DbContext Implementation

## Status: ALL ISSUES RESOLVED

## Task
3.1 — Implement EF Core DbContext and entity configurations

## Summary

All interop blockers are resolved. The full EF Core DbContext pattern — including derivation, implicit override, Fluent API configuration with generic lambdas, nested lambdas, Expression<Func<T>> parameters, chained fluent API with type argument inference through interface implementations, and CRTP-based types — works end-to-end.

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
| 10 | Chained fluent API type propagation | TypeArgumentInferenceEngine: Expression unwrapping, interface implementation unification, nullable union unwrapping |
| 11 | CRTP stack overflow | Cycle detection in ImplementsConstructedInterface |

---

## What Now Compiles ✅

```mist
void OnModelCreating(ModelBuilder modelBuilder)
{
    modelBuilder.Entity<Request>(entity =>
    {
        entity.HasKey(e => e.RequestId);
        entity.Property(e => e.RequestId).ValueGeneratedOnAdd();
        entity.Property(e => e.Type).HasConversion<string>();
        entity.HasIndex(e => e.State);
        entity.HasMany(e => e.WorkItems).WithOne().HasForeignKey(e => e.RequestId);
        entity.ToTable("Requests");
    });
}
```
