# Design Document

## Overview

This design fixes three interrelated architectural defects in type projection and member resolution: duplicate type instances from disconnected shell projection, overload selection inconsistency between lookup functions, and fragmented lookup API with divergent behavior. The fix is delivered in two phases — Phase A (type interning via `ProjectMemberType` for extension receivers) and Phase B (unified `ResolveMethodMember` replacing fragmented lookups).

## Glossary

- **Type Interning**: Ensuring that a single canonical instance of each projected CLR type exists, so reference equality checks succeed across all usage sites.
- **Disconnected Shell**: A freshly-created type instance (ClassType, StructType, AbstractType) with an empty member scope, produced by `ProjectSimpleTypeReference` when the cache-only resolver misses.
- **Full Resolver** (`_typeReferenceResolver`): Triggers `ResolveTypeCore` — creates the type if not cached, populates members, returns canonical instance. Safe only when the owning type is already cached (no recursion).
- **Cache-Only Resolver** (`_typeReferenceResolverCacheOnly`): Returns the type only if already in the cache. Never triggers projection. Safe everywhere but may produce disconnected shells on cache miss.
- **Extension Receiver Type**: The CLR type that an ambient extension method's first (`this`) parameter targets. Projected during `RegisterAmbientExtensions` and stored on `ExtensionBlockInfo.ReceiverType`.
- **Unified Method Resolution**: A single function that searches own members, inherited members, and extensions with consistent overload selection — replacing the current fragmented lookup API.

## Bug Details

The defect has three layers:

**Layer 1 — Duplicate Type Instances**: `RegisterAmbientExtensions` projects extension receiver types via `projector.ProjectTypeReference(clrType, stdlib)`. This uses `_typeReferenceResolverCacheOnly`, which returns null for types not yet in the cache. On cache miss, `ProjectTypeReference` falls through to `ProjectSimpleTypeReference`, creating a fresh disconnected shell. Later, when user code triggers `ResolveType("System", "IServiceProvider")`, a *different* canonical instance is created and cached. The extension's receiver and the call site's receiver are now reference-distinct.

**Layer 2 — Overload Ambiguity in LookupMemberType**: When multiple overloads share a name (generic `GetRequiredService<T>()` and non-generic `GetRequiredService(Type)`), `LookupMemberType` uses `Scope.Lookup()` which returns the first registered overload — typically the non-generic one (registered first in the member scope). Its return type is `unknown` (since the CLR signature returns `object`). The subsequent `LookupGenericMemberSymbol` correctly finds the generic overload, but the substitution operates on the *wrong* return type that `LookupMemberType` already established.

**Layer 3 — Fragmented Lookup API**: Five lookup functions with different scope (extensions vs. no extensions), return type (IType vs. ISymbol), and overload selection strategy (first-match vs. arity-aware). When they disagree, results are inconsistent.

## Expected Behavior

- A CLR type projected from any entry point (property return type, extension receiver, base type reference, field type) resolves to the same canonical instance.
- The `ExtensionRegistry` finds applicable extensions regardless of which projection path produced the receiver type.
- Generic extension methods with explicit type arguments have the correct overload selected and the return type correctly substituted.
- A single resolution path handles own members, inherited members, and extensions with consistent overload selection.

## Hypothesized Root Cause

The root cause is that `RegisterAmbientExtensions` uses `projector.ProjectTypeReference` (cache-only fallback → disconnected shell) instead of `projector.ProjectMemberType` (which has a second-chance full resolve for disconnected shells). This single API choice causes the receiver type to be a disconnected shell, which then cascades through the `ExtensionRegistry`'s reference-equality-first matching, the `LookupGenericMemberSymbol` extension fallback (can't find applicable extensions), and ultimately the type-argument substitution path.

The overload selection bug is a direct consequence: once the correct extension block IS found (via structural equality band-aid), `LookupMemberType`'s `Scope.Lookup()` returns the wrong overload because it doesn't consider type argument arity.

## Fix Implementation

### Phase A: Extension Receiver Type Interning

**Change**: In `SemanticModelBuilder.RegisterAmbientExtensions`, replace:
```csharp
var receiverType = projector.ProjectTypeReference(group[0].ReceiverClrType, stdlib);
```
with:
```csharp
var receiverType = projector.ProjectMemberType(group[0].ReceiverClrType, stdlib);
```

**Why `ProjectMemberType` is safe here**: It runs the full resolver for disconnected shells, which calls `ResolveTypeCore`. This is safe because:
1. The `AssemblyTypeProvider` is fully constructed and wired.
2. The receiver type (e.g., `IServiceProvider`) is never the type currently being projected (extension registration is a separate pass that runs after all types have been cached).
3. `ProjectMemberType` is already used for return types and parameter types without recursion issues.

**Effect**: After this change, the extension's `ReceiverType` is the same canonical instance that `ResolveType("System", "IServiceProvider")` returns. Reference equality in `ExtensionRegistry` succeeds without needing structural fallbacks.

**Files**:
- `Source/Mist/Semantics/Model/SemanticModelBuilder.cs` — line ~2364 in `RegisterAmbientExtensions`

### Phase B: Unified Method Resolution

**New types**:
```csharp
private readonly record struct MethodResolutionHints(
    int? TypeArgCount,
    int? ArgumentCount,
    bool PreferLambdaCompatible);

private readonly record struct MethodResolutionResult(
    ISymbol Symbol,
    IType ReturnType,
    bool IsExtension,
    ExtensionBlockInfo? ExtensionBlock,
    IReadOnlyList<TypeParameterType>? ExtensionTypeParams);
```

**New function**: `ResolveMethodMember` searches in priority order:
1. Own member scope (class: walk base chain; other types: direct scope lookup)
2. Ambient members via resolver
3. Default methods on abstracts
4. Extension registry (applicable, namespace-filtered)

At each level, applies consistent overload selection based on hints.

**Migration**: The `MethodCallSuffix` handler replaces the `LookupMemberType` + `LookupGenericMemberSymbol` pair with a single `ResolveMethodMember` call. Old functions are deprecated but not deleted until all callers migrate.

**Files**:
- `Source/Mist/Semantics/Model/SemanticModelBuilder.cs`

### Cleanup: Remove Structural Equality Band-Aids

After Phase A is verified, the structural equality checks in `ExtensionRegistry.IsApplicable` for `AbstractType` (and potentially `ClassType`/`StructType`) can be removed since type interning ensures reference equality. This is a follow-up cleanup, not a requirement for correctness.

**Files**:
- `Source/Mist/Semantics/Model/ExtensionRegistry.cs`

## Correctness Properties

### Property 1: Reference Identity

For any CLR type T projected through any path in a single compilation, all projection results SHALL be `ReferenceEquals` to the same canonical instance.

### Property 2: Overload Determinism

For any method call with N explicit type arguments, the resolved symbol SHALL have exactly N type parameters (or emit a diagnostic if no such overload exists).

### Property 3: Substitution Completeness

For any resolved generic method symbol with DeclaredType containing TypeParameterType instances, the substituted return type SHALL contain zero TypeParameterType instances after type argument substitution.

### Property 4: Extension Parity

`ResolveMethodMember` SHALL find all methods that `LookupMemberType`'s extension fallback currently finds, and additionally SHALL prefer the arity-matching overload when type arguments are provided.

### Property 5: No Silent Fallthrough

If `ResolveMethodMember` cannot find any matching method, it SHALL return null (never silently return a raw TypeParameterType or the wrong overload's return type).

## Testing Strategy

### Phase A Tests
- Verify that after `RegisterAmbientExtensions`, the extension block's `ReceiverType` for a non-generic interface (e.g., `IServiceProvider`) is reference-equal to the instance returned by `assemblyProvider.ResolveType`.
- All existing tests (3867) continue passing.
- The Pendragon project builds successfully with `GetRequiredService<MigrationRunner>()` returning `MigrationRunner`.

### Phase B Tests
- Verify `ResolveMethodMember` selects the generic overload when type args are provided.
- Verify `ResolveMethodMember` selects the non-generic overload when no type args are provided.
- Verify `ResolveMethodMember` prefers lambda-compatible overloads when `PreferLambdaCompatible` hint is set.
- Verify kind validation (field/property detection) still works via the unified path.
- Full regression: all tests + samples integration test.
