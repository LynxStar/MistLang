# Findings: Multiple Interop Blockers for EF Core DbContext Implementation

## Status: BLOCKED

## Task
3.1 — Implement EF Core DbContext and entity configurations

## Summary

After confirming CLR inheritance and NuGet extension methods are fixed, three new interop limitations prevent implementing a fully functional EF Core `DbContext` with Fluent API configuration.

---

## Issue 1: `protected` Access Modifier Not Supported (Parser Level)

### Observed Behavior
Using `protected` in any position causes `MIST_PARSE_INCOMPLETE` at the class declaration line. The parser does not recognize `protected` as a valid keyword.

### Reproduction
```mist
public class PendragonDbContext : DbContext
{
    protected override void OnModelCreating(ModelBuilder modelBuilder) { }
}
// ERROR: MIST_PARSE_INCOMPLETE at line 4
```

Also fails with:
- `protected void Foo() { }` — same parse error
- `internal override void OnModelCreating(...)` — same parse error

### What Works
- `public override void OnModelCreating(...)` — parses fine, but C# rejects it (see Issue 2)
- `override void OnModelCreating(...)` — parses fine, emits `private override` which C# rejects
- `public void OnModelCreating(...)` — compiles but creates a NEW method (doesn't override)

### Impact
Cannot override any `protected` or `protected internal` virtual method from a CLR base class. This blocks `DbContext.OnModelCreating`, `DbContext.OnConfiguring`, `DbContext.ConfigureConventions`, and many other framework extension points.

### Desired Behavior
Either:
1. Support `protected` as a parsed access modifier, OR
2. When `override` is used without an explicit access modifier, automatically match the base method's access level in the emitted C#

Option 2 would be ideal since Mist could treat `override` as inheriting the base's visibility.

---

## Issue 2: `override` Emits Wrong Access Level for CLR Protected Methods

### Observed Behavior
When `public override` is used on a method whose base is `protected internal`, the emitted C# is:
```csharp
public override void OnModelCreating(ModelBuilder modelBuilder) { }
```
This produces C# error: `cannot change access modifiers when overriding 'protected internal' inherited member`.

When no access modifier is given with `override`, it emits:
```csharp
private override void OnModelCreating(ModelBuilder modelBuilder) { }
```
This produces: `virtual or abstract members cannot be private`.

### Expected Behavior
The emitter should detect that the base method is `protected internal` and emit:
```csharp
protected internal override void OnModelCreating(ModelBuilder modelBuilder) { }
```

### Suggested Fix
In the emitter, when a method has the `override` modifier, look up the base method's access level and emit that access level in C# instead of the Mist-declared one. This would completely solve the problem without needing to add `protected` as a Mist keyword.

---

## Issue 3: Generic Method Lambda Parameter Type Inference

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
All Fluent API entity configuration is blocked. Without this, even if override worked, we couldn't configure primary keys, relationships, indexes, or type conversions.

### Workaround Attempted
None available — explicit lambda parameter types might work but the syntax `(EntityTypeBuilder<Request> entity) => {...}` hasn't been tested.

---

## Issue 4: Extension Method Nested Lambda Type Inference (Minor)

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

## Issue 5: Generic Constructor Parameter Type Resolution (Minor, Has Workaround)

### Observed Behavior
```mist
public PendragonDbContext(DbContextOptions<PendragonDbContext> options) : base(options) { }
// ERROR: MIST_TC_0013: No constructor on 'DbContext' matches the chained arguments
```

### Workaround (Works)
```mist
public PendragonDbContext(DbContextOptions options) : base(options) { }
```

Using the non-generic `DbContextOptions` base type works because `DbContextOptions<T>` inherits from it. EF Core's DI still injects the correct generic type.

---

## Current Working State

The following compiles successfully:
```mist
using Microsoft.EntityFrameworkCore;

public class PendragonDbContext : DbContext
{
    public PendragonDbContext(DbContextOptions options) : base(options)
    {
    }

    public DbSet<Request>? Requests { get; set; }
    public DbSet<WorkItem>? WorkItems { get; set; }
    // ... all DbSet properties work with ? suffix
}
```

What's blocked:
- `OnModelCreating` override (Issues 1 + 2)
- Fluent API configuration inside it (Issue 3)
- `EnableRetryOnFailure` callback (Issue 4)

---

## Priority Assessment

| Issue | Severity | Suggested Fix Complexity |
|-------|----------|--------------------------|
| Issue 2 (override access level) | **Critical** | Low — emitter already has access to base method metadata |
| Issue 1 (protected keyword) | Critical | Medium — parser needs new keyword, OR Issue 2 fix makes this moot |
| Issue 3 (generic lambda inference) | **Critical** | Medium — type checker needs to resolve generic method → infer Action<T> parameter |
| Issue 4 (nested lambda inference) | High | Same root cause as Issue 3 |
| Issue 5 (generic ctor param) | Low | Already has workaround |

The minimum fix needed to unblock this task: **Issue 2 + Issue 3**. If the emitter matches base method access on override, and the type checker can infer lambda parameter types from generic CLR methods, the full DbContext implementation becomes possible.

---

## Test Case for Verification

Once fixed, this should compile and produce valid C#:

```mist
using Microsoft.EntityFrameworkCore;

public class PendragonDbContext : DbContext
{
    public PendragonDbContext(DbContextOptions options) : base(options)
    {
    }

    public DbSet<Request>? Requests { get; set; }

    public override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Request>(entity =>
        {
            entity.HasKey(e => e.RequestId);
            entity.Property(e => e.RequestId).ValueGeneratedOnAdd();
        });
    }
}
```

Expected emitted C#:
```csharp
protected internal override void OnModelCreating(ModelBuilder modelBuilder)
{
    modelBuilder.Entity<Request>(entity =>
    {
        entity.HasKey(e => e.RequestId);
        entity.Property(e => e.RequestId).ValueGeneratedOnAdd();
    });
}
```
