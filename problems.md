# Findings: Interop Blockers for EF Core DbContext Implementation

## Status: MOSTLY UNBLOCKED

## Task
3.1 — Implement EF Core DbContext and entity configurations

## Summary

Issues 1, 2, and 3 are resolved. The core DbContext pattern (derive, override OnModelCreating, use Fluent API with generic lambda inference) now works end-to-end. Issue 4 may also be fixed (same root cause as 3). Issue 5 has a workaround.

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

**Working example:**
```mist
void OnModelCreating(ModelBuilder modelBuilder)
{
    modelBuilder.Entity<Request>(entity =>
    {
        Console.WriteLine("configuring entity");
    });
}
```

---

## Issue 4: Extension Method Nested Lambda Type Inference — NEEDS TESTING

### Original Problem
```mist
options.UseNpgsql(connectionString, npgsqlOptions => npgsqlOptions.EnableRetryOnFailure(3));
```

This may now work since the root cause (lambda parameter inference from CLR method signatures) was the same as Issue 3. Needs verification once the full DbContext sample is wired up.

---

## Issue 5: Generic Constructor Parameter Type Resolution — LOW PRIORITY (Has Workaround)

### Workaround (Works)
```mist
public PendragonDbContext(DbContextOptions options) : base(options) { }
```

Using the non-generic `DbContextOptions` base type works because `DbContextOptions<T>` inherits from it.

---

## Current Working State

The following compiles and produces correct C#:
```mist
using Microsoft.EntityFrameworkCore;

public class Request
{
    public int RequestId { get; set; }
}

public class PendragonDbContext : DbContext
{
    public PendragonDbContext(DbContextOptions options) : base(options)
    {
    }

    public DbSet<Request>? Requests { get; set; }

    void OnModelCreating(ModelBuilder modelBuilder)
    {
        // Emits: protected override void OnModelCreating(ModelBuilder modelBuilder)
        modelBuilder.Entity<Request>(entity =>
        {
            // Lambda param 'entity' correctly typed as EntityTypeBuilder<Request>
            Console.WriteLine("configuring Request");
        });
    }
}
```

---

## Remaining Known Limitation

Calling methods on the inferred lambda parameter (e.g., `entity.HasKey(...)`) may still fail due to member resolution on `EntityTypeBuilder<Request>` (constructed generic type member lookup). This is a separate issue from lambda inference — it's about how generic type member scopes propagate through type argument substitution.

---

## Priority Assessment

| Issue | Status |
|-------|--------|
| Issue 1 (protected keyword) | ✅ RESOLVED |
| Issue 2 (override access level) | ✅ RESOLVED |
| Issue 3 (generic lambda inference) | ✅ RESOLVED |
| Issue 4 (nested lambda inference) | ⚠️ LIKELY FIXED — needs testing |
| Issue 5 (generic ctor param) | ⚠️ HAS WORKAROUND |
