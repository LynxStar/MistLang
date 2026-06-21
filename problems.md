# Findings: Mist Cannot Inherit From CLR Types

## Status: FIXED (CLR inheritance) / FIXED (extension methods)

## Task
3.1 — Implement EF Core DbContext and entity configurations

## Fix Applied

The root cause was that `Pass1_ResolveBaseTypes` in `SemanticModelBuilder.cs` used `LookupWithUsings` 
which only searched user-defined scopes — it never queried the `IAmbientTypeResolver` for CLR types 
from SDK/NuGet assemblies. Regular type references in Pass 2 used `ResolveNamedType` which does fall 
back to the ambient resolver, which is why `mist inspect` could find the types.

### Changes Made:
1. Added optional `IAmbientTypeResolver?` parameter to `LookupWithUsings` with ambient fallback logic
2. Threaded the ambient resolver through `Pass1_ResolveBaseTypes`, `Pass1_ResolveStructBaseTypes`, 
   `Pass1_ResolveInterfaceLists`, and all sub-methods
3. Added `MIST_SEALED_INHERITANCE` diagnostic for attempting to inherit from sealed CLR types

### What Now Works:
- `public class MyDbContext : DbContext { }` — inheriting from NuGet package types
- `public class MyException : Exception { }` — inheriting from SDK types
- `public class MyStream : Stream { }` — inheriting from SDK types
- Implementing CLR interfaces in base type positions

## Extension Methods from NuGet — FIXED

### Root Cause

`ExtensionRegistry.IsApplicable` relied on `ReferenceEquals` to match extension method receiver 
types against value types in user code. However, ambient extension receivers are projected via 
`TypeProjector.ProjectTypeReference` during Pass 2e (Phase A) — BEFORE Pass 2 resolves user code. 
At that point, the receiver type (e.g., `DbContextOptionsBuilder`) hasn't been resolved and cached 
yet by the `AssemblyTypeProvider`, so the projector falls through to `ProjectSimpleTypeReference`, 
creating a disconnected shell `ClassType` instance.

Later, when Pass 2 resolves the user's code, `ResolveType` creates and caches a DIFFERENT 
`ClassType` instance for the same type. The two instances have the same `DisplayName` and 
`ContainingNamespace` but are not reference-equal, so `IsApplicable` returned `false` and the 
extension method was reported as `MIST_UNDEFINED_MEMBER`.

### Fix Applied

Added structural equality checks to `ExtensionRegistry.IsApplicable` for `ClassType` and 
`StructType` (non-generic): when both the extension receiver and value type share the same 
`DisplayName` and `ContainingNamespace`, they are considered applicable. Also added a 
`GenericDefinitionsMatch` helper for `ConstructedType` comparisons where generic definitions 
may be disconnected shells.

### What Now Works:
- Extension methods targeting non-generic class receivers from NuGet packages
- Extension methods targeting non-generic struct receivers from NuGet packages
- ConstructedType-based extensions where generic definitions are disconnected shells
- All pre-existing extension tests continue to pass (4,463 total tests green)
