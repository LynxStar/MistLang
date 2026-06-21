# Findings: Interop Blockers for EF Core DbContext Implementation

## Status: PARTIALLY UNBLOCKED

## Task
3.1 — Implement EF Core DbContext and entity configurations

## Summary

Issues 1 and 2 (override/protected) are resolved via implicit override detection in the C# emitter. Issues 3 and 4 (lambda type inference) remain blocking for Fluent API configuration.

---

## Issue 1: `protected` Access Modifier — RESOLVED

**Resolution:** Mist's `private` (no access modifier) already behaves like C#'s `protected`. The emitter now automatically maps Mist private → C# `protected` when emitting an implicit override. No new keyword needed.

---

## Issue 2: `override` Emits Wrong Access Level — RESOLVED

**Resolution:** Implicit override detection added to the C# emitter. When a Mist class inherits from a CLR base and defines a method matching a virtual/abstract slot:
- Name + signature matching against CLR base virtual/abstract methods
- Emitter automatically prepends `protected override` (or `public override` for public virtuals)
- Info diagnostic `MIST_BE_INFO_OVERRIDE` emitted to notify the author
- No Mist syntax changes — purely a backend concern

**Working example:**
```mist
public class TestDbContext : DbContext
{
    public TestDbContext(DbContextOptions options) : base(options) { }

    void OnModelCreating(ModelBuilder modelBuilder)
    {
        // emits: protected override void OnModelCreating(...)
    }
}
```

---

## Issue 3: Generic Method Lambda Parameter Type Inference — STILL BLOCKING

### Observed Behavior
Calling a generic CLR method that takes `Action<TBuilder>` as a parameter with a lambda fails:
```mist
modelBuilder.Entity<Request>(entity =>
{
    entity.HasKey(e => e.RequestId);
});
// ERROR: MIST_TC_0116: Cannot infer lambda parameter types
// ERROR: MIST_TC_0031: No overload for 'Entity' matches argument types (unknown)
```

### Expected Behavior
The type checker should infer that `entity` is `EntityTypeBuilder<Request>` from the generic method signature `Entity<T>(Action<EntityTypeBuilder<T>>)`.

### Impact
All Fluent API entity configuration is blocked. Without this, even though override now works, we can't configure primary keys, relationships, indexes, or type conversions inside `OnModelCreating`.

### Workaround Attempted
None available — explicit lambda parameter types might work but the syntax `(EntityTypeBuilder<Request> entity) => {...}` hasn't been tested.

---

## Issue 4: Extension Method Nested Lambda Type Inference (Minor) — STILL BLOCKING

### Observed Behavior
```mist
options.UseNpgsql(connectionString, npgsqlOptions => npgsqlOptions.EnableRetryOnFailure(3));
// ERROR: 'DbContextOptionsBuilder' does not contain a definition for 'EnableRetryOnFailure'
```

The `UseNpgsql` extension method's second parameter is `Action<NpgsqlDbContextOptionsBuilder>`, but the compiler types `npgsqlOptions` as `DbContextOptionsBuilder` (the receiver type) rather than the declared lambda parameter type.

### What Works
```mist
options.UseNpgsql(connectionString);  // Works fine without the callback
```

### Impact
Cannot configure `EnableRetryOnFailure(3)` for connection resilience.

---

## Issue 5: Generic Constructor Parameter Type Resolution (Minor, Has Workaround) — LOW PRIORITY

### Observed Behavior
```mist
public PendragonDbContext(DbContextOptions<PendragonDbContext> options) : base(options) { }
// ERROR: MIST_TC_0013: No constructor on 'DbContext' matches the chained arguments
```

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

public class PendragonDbContext : DbContext
{
    public PendragonDbContext(DbContextOptions options) : base(options)
    {
    }

    public DbSet<Request>? Requests { get; set; }
    public DbSet<WorkItem>? WorkItems { get; set; }

    void OnModelCreating(ModelBuilder modelBuilder)
    {
        // Emits: protected override void OnModelCreating(ModelBuilder modelBuilder)
        // BUT: can't call Fluent API methods due to Issue 3
    }
}
```

---

## Priority Assessment

| Issue | Status | Severity |
|-------|--------|----------|
| Issue 1 (protected keyword) | ✅ RESOLVED | — |
| Issue 2 (override access level) | ✅ RESOLVED | — |
| Issue 3 (generic lambda inference) | ❌ BLOCKING | Critical |
| Issue 4 (nested lambda inference) | ❌ BLOCKING | High (same root cause as 3) |
| Issue 5 (generic ctor param) | ⚠️ HAS WORKAROUND | Low |

**Next blocker to address:** Issue 3 — generic method lambda parameter type inference.
