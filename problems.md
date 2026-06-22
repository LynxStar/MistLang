# Findings: Interop Blockers for EF Core DbContext Implementation

## Status: Issues 12 & 13 STILL PRESENT (with improved workarounds)

## Task
3.1 — Implement EF Core DbContext and entity configurations

## Summary

Issues 12 and 13 remain, but Issue 13 now has a workaround that allows full OwnsOne property configuration.

---

## Build Result: SUCCESSFUL ✅

The final implementation compiles successfully using a combination of lambda-based and string-based patterns:

```
Build succeeded  0 error(s), 0 warning(s)  Total: 1.89s
```

---

## What Was Tested (Full Lambda Attempt)

### Attempt 1: Full lambda-based Fluent API (FAILED — 7 errors)

```mist
entity.HasMany(e => e.WorkItems).WithOne().HasForeignKey(e => e.RequestId);
entity.OwnsOne(e => e.QualityTier, qt =>
{
    qt.Property(q => q.Resolution).HasConversion<int>();
    qt.Property(q => q.Type).HasConversion<int>();
});
```

**Full error output:**
```
Build failed  7 error(s), 0 warning(s)  Total: 1.45s

error MIST_UNDEFINED_MEMBER: 'TRelatedEntity' does not contain a definition for 'RequestId'.
error MIST_UNDEFINED_MEMBER: 'TRelatedEntity' does not contain a definition for 'RequestId'.
error MIST_UNDEFINED_MEMBER: 'TRelatedEntity' does not contain a definition for 'WorkItemId'.
error MIST_UNDEFINED_MEMBER: 'TRelatedEntity' does not contain a definition for 'WorkItemId'.
error MIST_UNDEFINED_MEMBER: 'TRelatedEntity' does not contain a definition for 'Resolution'.
error MIST_UNDEFINED_MEMBER: 'TRelatedEntity' does not contain a definition for 'SearchAttemptId'.
error MIST_UNDEFINED_MEMBER: 'TRelatedEntity' does not contain a definition for 'Resolution'.
```

**Analysis:** Issue 12 (HasForeignKey lambda TRelatedEntity not resolved) is still present and also affects lambda parameters inside OwnsOne builders (`qt.Property(q => q.Resolution)` — the `q` is typed as `TRelatedEntity` instead of `QualityTier`).

---

### Attempt 2: String FK + OwnsOne lambda without explicit type (FAILED — 4 errors)

```mist
entity.HasMany(e => e.WorkItems).WithOne().HasForeignKey("RequestId");  // ✅ works
entity.OwnsOne(e => e.QualityTier, qt =>
{
    qt.Property("Resolution").HasConversion<int>();
    qt.Property("Type").HasConversion<int>();
});
```

**Full error output:**
```
Build failed  4 error(s), 0 warning(s)  Total: 2.10s

error MIST_CS_ERROR: 'DownloadAttempt' does not contain a definition for 'Property' and no accessible extension method 'Property' accepting a first argument of type 'DownloadAttempt' could be found
error MIST_CS_ERROR: 'DownloadAttempt' does not contain a definition for 'Property' and no accessible extension method 'Property' accepting a first argument of type 'DownloadAttempt' could be found
error MIST_CS_ERROR: 'SeasonPackSighting' does not contain a definition for 'Property' and no accessible extension method 'Property' accepting a first argument of type 'SeasonPackSighting' could be found
error MIST_CS_ERROR: 'SeasonPackSighting' does not contain a definition for 'Property' and no accessible extension method 'Property' accepting a first argument of type 'SeasonPackSighting' could be found
```

**Analysis:** Issue 13 still present — `qt` parameter is typed as the parent entity (`DownloadAttempt`) instead of `OwnedNavigationBuilder`. The compiler selects an overload that doesn't have an `Action<OwnedNavigationBuilder>` parameter, or infers the lambda param type incorrectly.

---

### Attempt 3: Explicit generic OwnedNavigationBuilder type annotation (FAILED — 2 errors)

```mist
using Microsoft.EntityFrameworkCore.Metadata.Builders;
// ...
entity.OwnsOne(e => e.QualityTier, (OwnedNavigationBuilder<DownloadAttempt, QualityTier> qt) =>
{
    qt.Property("Resolution").HasConversion<int>();
    qt.Property("Type").HasConversion<int>();
});
```

**Full error output:**
```
Build failed  2 error(s), 0 warning(s)  Total: 1.89s

error MIST_CS_ERROR: The type 'QualityTier' must be a reference type in order to use it as parameter 'TDependentEntity' in the generic type or method 'OwnedNavigationBuilder<TOwnerEntity, TDependentEntity>'
error MIST_CS_ERROR: The type 'QualityTier' must be a reference type in order to use it as parameter 'TRelatedEntity' in the generic type or method 'EntityTypeBuilder<DownloadAttempt>.OwnsOne<TRelatedEntity>(string, Action<OwnedNavigationBuilder<DownloadAttempt, TRelatedEntity>>)'
```

**Analysis:** This is NOT a Mist bug — it's an EF Core constraint. `OwnedNavigationBuilder<TOwner, TDependent>` requires `TDependent : class`. Since `QualityTier` is a struct, the generic version can't be used. Must use non-generic `OwnedNavigationBuilder`.

---

### Attempt 4: Non-generic OwnedNavigationBuilder + string overload (SUCCESS ✅)

```mist
entity.OwnsOne("Pendragon.Models.QualityTier", "QualityTier", (OwnedNavigationBuilder qt) =>
{
    qt.Property("Resolution").HasConversion<int>();
    qt.Property("Type").HasConversion<int>();
});
```

**Result:**
```
Build succeeded  0 error(s), 0 warning(s)  Total: 1.89s
```

**Key insight:** Using the 3-arg string overload `OwnsOne(typeName, navigation, builderAction)` with explicit `(OwnedNavigationBuilder qt)` type annotation on the lambda parameter resolves Issue 13 AND confirms Issue 14 is fixed (HasConversion<int>() with 1 type arg is no longer ambiguous).

---

## Issue Status Update

### Issue 12: HasForeignKey Lambda — STILL BROKEN ❌

**Code that fails:**
```mist
entity.HasMany(e => e.WorkItems).WithOne().HasForeignKey(e => e.RequestId);
```

**Error:**
```
error MIST_UNDEFINED_MEMBER: 'TRelatedEntity' does not contain a definition for 'RequestId'.
```

**Root cause:** The fluent chain `.HasMany(...).WithOne()` returns `ReferenceCollectionBuilder<TEntity, TRelatedEntity>` but when `.HasForeignKey(e => e.Foo)` is called, the compiler does not substitute `TRelatedEntity` with the concrete dependent entity type. The lambda parameter `e` remains typed as the unresolved generic parameter `TRelatedEntity`.

**Workaround:** Use string overload `HasForeignKey("RequestId")` — compiles successfully.

---

### Issue 13: OwnsOne Nested Builder Lambda — STILL BROKEN ❌ (but NEW WORKAROUND available)

**Code that fails (untyped lambda):**
```mist
entity.OwnsOne(e => e.QualityTier, qt =>
{
    qt.Property("Resolution").HasConversion<int>();
});
```

**Error:**
```
error MIST_CS_ERROR: 'DownloadAttempt' does not contain a definition for 'Property'
```

**Root cause:** When using the 2-arg `OwnsOne(navExpr, builderAction)` overload, the compiler infers `qt` as the parent entity type instead of `OwnedNavigationBuilder`.

**NEW WORKAROUND (full property configuration now possible):**
```mist
using Microsoft.EntityFrameworkCore.Metadata.Builders;
// ...
entity.OwnsOne("Pendragon.Models.QualityTier", "QualityTier", (OwnedNavigationBuilder qt) =>
{
    qt.Property("Resolution").HasConversion<int>();
    qt.Property("Type").HasConversion<int>();
});
```

Use the 3-arg string overload with explicit `(OwnedNavigationBuilder qt)` type annotation. This correctly types `qt` as `OwnedNavigationBuilder` and allows `.Property(...)` calls inside the lambda body.

**Note:** The generic `OwnedNavigationBuilder<TOwner, TDependent>` type annotation can't be used with struct owned types due to an EF Core `class` constraint on `TDependent`. Use non-generic `OwnedNavigationBuilder` instead.



---

## Current Compilable State (Final)

The DbContext compiles successfully with:
- All 7 DbSets ✅
- Keys, ValueGeneratedOnAdd ✅
- HasConversion<string> on generic PropertyBuilder<T> (lambda accessor) ✅
- HasConversion<int> on non-generic PropertyBuilder (string accessor) ✅
- HasIndex ✅
- HasMany/WithOne/HasForeignKey using **string** FK names ✅
- OwnsOne using **string overload + explicit OwnedNavigationBuilder type annotation** with full property configuration ✅ **← NEW workaround**
- EnableRetryOnFailure with **explicit type annotation** + using ✅

**What's still blocked:**
- Lambda-based HasForeignKey (Issue 12) — use string workaround
- OwnsOne with untyped lambda parameter (Issue 13) — use explicit `(OwnedNavigationBuilder qt)` type annotation + string overload

---

## Final Compiled Code

```mist
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Pendragon.Models;

public class PendragonDbContext : DbContext
{
    public PendragonDbContext(DbContextOptions<PendragonDbContext> options) : base(options)
    {
    }

    public DbSet<Request>? Requests { get; set; }
    public DbSet<WorkItem>? WorkItems { get; set; }
    public DbSet<DownloadAttempt>? DownloadAttempts { get; set; }
    public DbSet<SearchAttempt>? SearchAttempts { get; set; }
    public DbSet<SeasonPackSighting>? SeasonPackSightings { get; set; }
    public DbSet<ProviderState>? ProviderStates { get; set; }
    public DbSet<ProviderResult>? ProviderResults { get; set; }

    void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Request>(entity =>
        {
            entity.HasKey(e => e.RequestId);
            entity.Property(e => e.RequestId).ValueGeneratedOnAdd();
            entity.Property(e => e.Type).HasConversion<string>();
            entity.HasMany(e => e.WorkItems).WithOne().HasForeignKey("RequestId");
            entity.HasMany(e => e.SeasonPackSightings).WithOne().HasForeignKey("RequestId");
        });

        modelBuilder.Entity<WorkItem>(entity =>
        {
            entity.HasKey(e => e.WorkItemId);
            entity.Property(e => e.WorkItemId).ValueGeneratedOnAdd();
            entity.Property(e => e.State).HasConversion<string>();
            entity.Property(e => e.MediaType).HasConversion<string>();
            entity.HasMany(e => e.SearchAttempts).WithOne().HasForeignKey("WorkItemId");
            entity.HasMany(e => e.DownloadAttempts).WithOne().HasForeignKey("WorkItemId");
            entity.HasIndex(e => e.State);
            entity.HasIndex(e => e.NextSearchAt);
        });

        modelBuilder.Entity<DownloadAttempt>(entity =>
        {
            entity.HasKey(e => e.DownloadAttemptId);
            entity.Property(e => e.DownloadAttemptId).ValueGeneratedOnAdd();
            entity.Property(e => e.Outcome).HasConversion<string>();
            entity.OwnsOne("Pendragon.Models.QualityTier", "QualityTier", (OwnedNavigationBuilder qt) =>
            {
                qt.Property("Resolution").HasConversion<int>();
                qt.Property("Type").HasConversion<int>();
            });
        });

        modelBuilder.Entity<SearchAttempt>(entity =>
        {
            entity.HasKey(e => e.SearchAttemptId);
            entity.Property(e => e.SearchAttemptId).ValueGeneratedOnAdd();
            entity.HasMany(e => e.ProviderResults).WithOne().HasForeignKey("SearchAttemptId");
        });

        modelBuilder.Entity<SeasonPackSighting>(entity =>
        {
            entity.HasKey(e => e.SightingId);
            entity.Property(e => e.SightingId).ValueGeneratedOnAdd();
            entity.OwnsOne("Pendragon.Models.QualityTier", "QualityTier", (OwnedNavigationBuilder qt) =>
            {
                qt.Property("Resolution").HasConversion<int>();
                qt.Property("Type").HasConversion<int>();
            });
        });

        modelBuilder.Entity<ProviderState>(entity =>
        {
            entity.HasKey(e => e.ProviderName);
            entity.HasIndex(e => e.RetryAt);
        });

        modelBuilder.Entity<ProviderResult>(entity =>
        {
            entity.HasKey(e => e.ProviderResultId);
            entity.Property(e => e.ProviderResultId).ValueGeneratedOnAdd();
        });
    }
}
```

---

## Reproduction Context

The entity and model definitions needed to reproduce Issues 12 & 13:

### mist.toml

```toml
name = "Pendragon"

[global_usings]
namespaces = ["Standard", "System", "System.Collections.Generic"]

[packages]
"Npgsql.EntityFrameworkCore.PostgreSQL" = "9.0.4"

[framework_references]
"Microsoft.AspNetCore.App" = true
```

### Models/QualityTier.mist

```mist
public struct QualityTier
{
    public ResolutionTier Resolution;
    public TypeTier Type;
}
```

### Models/ResolutionTier.mist

```mist
public enum ResolutionTier : int
{
    R2160p = 0,
    R1080p = 1,
    R720p = 2,
    R480p = 3
}
```

### Models/TypeTier.mist

```mist
public enum TypeTier
{
    Remux,
    Bluray,
    WebDownload,
    Encode
}
```

### Models/WorkItemState.mist

```mist
public enum WorkItemState : string
{
    Pending,
    Searching,
    Downloading,
    Moving,
    Complete,
    Failed,
    WaitingRetry
}
```

### Models/MediaType.mist

```mist
public enum MediaType : string
{
    Movie,
    LiveAction,
    Anime
}
```

### Models/DownloadOutcome.mist

```mist
public enum DownloadOutcome : string
{
    Success,
    Failure
}
```

### Entities/Request.mist

```mist
using Pendragon.Models;

public class Request
{
    public Guid RequestId;
    public string Title;
    public MediaType Type;
    public string Year;
    public string ProviderId;
    public string PosterImage;
    public string Overview;
    public DateTime CreatedAt;

    public List<WorkItem> WorkItems = new List<WorkItem>();
    public List<SeasonPackSighting> SeasonPackSightings = new List<SeasonPackSighting>();
}
```

### Entities/WorkItem.mist

```mist
using Pendragon.Models;

public class WorkItem
{
    public Guid WorkItemId;
    public MediaType MediaType;
    public WorkItemState State;
    public int? SeasonNumber;
    public int? EpisodeNumber;
    public DateTime CreatedAt;
    public DateTime LastUpdated;
    public DateTime? NextSearchAt;
    public string? SabnzbdJobId;

    public Guid RequestId;
    public Request? Request;

    public List<SearchAttempt> SearchAttempts = new List<SearchAttempt>();
    public List<DownloadAttempt> DownloadAttempts = new List<DownloadAttempt>();
}
```

### Entities/DownloadAttempt.mist

```mist
using Pendragon.Models;

public class DownloadAttempt
{
    public Guid DownloadAttemptId;
    public string ProviderName;
    public QualityTier? QualityTier;
    public string NzbUrl;
    public string? SabnzbdJobId;
    public DownloadOutcome Outcome;
    public DateTime AttemptedAt;

    public Guid WorkItemId;
    public WorkItem? WorkItem;
}
```

### Entities/SearchAttempt.mist

```mist
public class SearchAttempt
{
    public Guid SearchAttemptId;
    public DateTime SearchedAt;

    public Guid WorkItemId;
    public WorkItem? WorkItem;

    public List<ProviderResult> ProviderResults = new List<ProviderResult>();
}
```

### Entities/SeasonPackSighting.mist

```mist
using Pendragon.Models;

public class SeasonPackSighting
{
    public Guid SightingId;
    public string ProviderName;
    public string Title;
    public QualityTier? QualityTier;
    public DateTime SpottedAt;

    public Guid RequestId;
    public Request? Request;
}
```

### Entities/ProviderResult.mist

```mist
public class ProviderResult
{
    public Guid ProviderResultId;
    public string ProviderName;
    public bool Responded;
    public int ResultCount;

    public Guid SearchAttemptId;
    public SearchAttempt? SearchAttempt;
}
```

### Entities/ProviderState.mist

```mist
public class ProviderState
{
    public string ProviderName;
    public DateTime? FailedAt;
    public DateTime? RetryAt;
    public string? ErrorType;
    public int? HttpStatus;
}
```

### Minimal Reproduction for Issue 12

Given the entities above, this should compile but fails:

```mist
using Microsoft.EntityFrameworkCore;
using Pendragon.Models;

public class TestDbContext : DbContext
{
    public TestDbContext(DbContextOptions<TestDbContext> options) : base(options)
    {
    }

    public DbSet<Request>? Requests { get; set; }
    public DbSet<WorkItem>? WorkItems { get; set; }

    void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Request>(entity =>
        {
            entity.HasKey(e => e.RequestId);
            // This line fails — e is typed as TRelatedEntity instead of WorkItem:
            entity.HasMany(e => e.WorkItems).WithOne().HasForeignKey(e => e.RequestId);
        });
    }
}
```

**Expected:** `e` in `HasForeignKey(e => e.RequestId)` should be `WorkItem`
**Actual:** `e` is `TRelatedEntity` (unresolved generic parameter)

### Minimal Reproduction for Issue 13

```mist
using Microsoft.EntityFrameworkCore;
using Pendragon.Models;

public class TestDbContext : DbContext
{
    public TestDbContext(DbContextOptions<TestDbContext> options) : base(options)
    {
    }

    public DbSet<DownloadAttempt>? DownloadAttempts { get; set; }

    void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<DownloadAttempt>(entity =>
        {
            entity.HasKey(e => e.DownloadAttemptId);
            // This line fails — qt is typed as DownloadAttempt instead of OwnedNavigationBuilder:
            entity.OwnsOne(e => e.QualityTier, qt =>
            {
                qt.Property("Resolution").HasConversion<int>();
            });
        });
    }
}
```

**Expected:** `qt` should be `OwnedNavigationBuilder` (or generic variant)
**Actual:** `qt` is `DownloadAttempt` (the parent entity type)

---

## Resolved Issues (1-11, from previous investigation)

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

