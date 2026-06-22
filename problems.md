# Findings: Interop Blockers for EF Core DbContext Implementation

## Status: ALL ISSUES RESOLVED

## Task
3.1 — Implement EF Core DbContext and entity configurations

## Summary

All five original interop blockers are resolved. Additionally, three follow-on issues discovered during testing (constructed generic member resolution, extension method ambiguity, and Expression<Func<T>> unwrapping for nested lambdas) are also fixed. The full EF Core DbContext pattern works end-to-end.

---

## Issue 1: `protected` Access Modifier — RESOLVED

Mist's `private` (no access modifier) already behaves like C#'s `protected`. The emitter maps Mist private → C# `protected` when emitting an implicit override.

---

## Issue 2: `override` Emits Wrong Access Level — RESOLVED

Implicit override detection in the C# emitter. When a Mist class inherits from a CLR base and defines a method matching a virtual/abstract slot, the emitter emits `protected override` (or `public override`) automatically with an info diagnostic.

---

## Issue 3: Generic Method Lambda Parameter Type Inference — RESOLVED

Full pipeline fix across semantic model builder, type checker, and overload resolver:

1. Method-level type arguments are substituted into parameter types before lambda target type inference
2. CLR Action/Func types projected as ClassType (not stdlib Func) are detected and used as lambda targets
3. Lambda resolves to proper stdlib Func type for overload matching
4. Explicit type args passed as synthetic inference results to the overload resolver
5. Cross-representation delegate compatibility: stdlib `Func<T, Void>` matches CLR `Action<T>` in union constituents

---

## Issue 4: Extension Method Nested Lambda Type Inference — RESOLVED

Fixed by: correct overload selection for ambient extension methods (prefer matching param count), receiver parameter offset for backend extensions (skip `this` param), and Expression<Func<T>> unwrapping for nested lambdas.

---

## Issue 5: Generic Constructor Parameter Type Resolution — RESOLVED

ConstructedType → non-generic base class subtype checking in `IsAssignable`, plus union constituent subtype fallback for nullable-projected CLR parameters.

---

## Additional Fixes (discovered during testing)

- **Constructed generic member resolution**: `EntityTypeBuilder<Request>` member scope was empty (disconnected shell). Fixed by resolving the generic definition through the type provider when it's a disconnected shell inside a ConstructedType.

- **Extension method ambiguity**: `EntityTypeBuilder<T>` extensions vs `EntityTypeBuilder` extensions. Fixed by handling ConstructedType in `IsReceiverAssignableTo` base chain walking.

- **Expression<Func<T>> unwrapping**: `HasKey(Expression<Func<TEntity, object?>>)` — the lambda target type detection now unwraps `Expression<T>` to get the inner Func for parameter inference.

---

## Current Working State

The full EF Core pattern compiles and produces correct C#:
```mist
using Microsoft.EntityFrameworkCore;

public class Request
{
    public int RequestId { get; set; }
}

public class PendragonDbContext : DbContext
{
    public PendragonDbContext(DbContextOptions<PendragonDbContext> options) : base(options)
    {
    }

    public DbSet<Request>? Requests { get; set; }

    void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Request>(entity =>
        {
            entity.HasKey(e => e.RequestId);
            entity.ToTable("Requests");
        });
    }
}
```

---

## Priority Assessment

| Issue | Status |
|-------|--------|
| Issue 1 (protected keyword) | ✅ RESOLVED |
| Issue 2 (override access level) | ✅ RESOLVED |
| Issue 3 (generic lambda inference) | ✅ RESOLVED |
| Issue 4 (nested lambda inference) | ✅ RESOLVED |
| Issue 5 (generic ctor param) | ✅ RESOLVED |
| Constructed generic member resolution | ✅ RESOLVED |
| Extension method ambiguity | ✅ RESOLVED |
| Expression<Func<T>> unwrapping | ✅ RESOLVED |
