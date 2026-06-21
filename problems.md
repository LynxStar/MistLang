# Findings: Mist Cannot Inherit From CLR Types

## Status: FIXED (CLR inheritance) / INVESTIGATION NEEDED (extension methods)

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

## Remaining Issue: Extension Methods from NuGet

Extension methods from NuGet packages (e.g., `UseNpgsql` on `DbContextOptionsBuilder`) may still 
not resolve. This is a separate issue from base type resolution and requires further investigation:

```
error MIST_UNDEFINED_MEMBER: 'DbContextOptionsBuilder' does not contain a definition for 'UseNpgsql'.
error MIST_UNDEFINED_MEMBER: 'DbContextOptionsBuilder' does not contain a definition for 'EnableRetryOnFailure'.
```

The extension method registration system (`RegisterAmbientExtensions` / `AmbientExtensionScanner`) 
already scans imported namespaces for extension methods. The issue may be:
- The namespace containing `UseNpgsql` is not being imported (user needs correct `using` directive)
- The receiver type projection doesn't match (structural equality mismatch)
- The specific NuGet package assembly isn't being scanned correctly

This should be tested with the actual Pendragon project setup to determine if it's still an issue 
after the base type fix is in place.
