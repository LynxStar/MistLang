# AmbientTypeProvider V2 - Design

## Overview

The AmbientTypeProvider V2 integrates .NET assembly types into the ProgramModel as first-class `MistType` instances. Unlike v1 (which produced old-model `IType` objects and operated as a fallback), v2 projects CLR types directly into the same type graph that Mist source types live in. The ProgramModel is agnostic to a type's origin -- all types are `MistType` regardless of whether they came from `.mist` source or a .NET assembly.

## Motivation

V1 had several problems:
1. **Separate type graphs**: CLR types were `IType` (old semantic model); Mist types were also `IType` but different instances. Generics constructed across the boundary (e.g., `List<MyMistType>`) didn't intern properly.
2. **Alias asymmetry**: `Standard.String` -> `System.String` worked for emission, but `System.String` -> `Standard.String` during resolution was inconsistent, causing false type mismatches.
3. **Fallback model**: The ambient provider was only consulted when the Mist namespace didn't have the type. This prevented ambiguity detection and made the resolution order-dependent.
4. **Extension methods**: BCL extension methods (LINQ) required complex special-casing because they lived in a separate resolution path.

## Architecture

### Single Graph Principle

Once a CLR type is projected, it lives in `ProgramModel.Namespaces` alongside Mist source types. It's a `MistType` subclass with all its members (methods, properties, fields, constructors, indexers, operators) populated. The only distinction is an origin marker (`TypeOrigin.Source` vs `TypeOrigin.Assembly`) used by the emitter to know "don't emit this, just reference it."

### Integration Point

`TypeReferenceResolver.LookupInNamespace(namespaceName, typeName)` is the single integration point. When the Mist namespace registry doesn't have the type, the ambient provider is consulted. If the provider has it, the type is projected, added to the ProgramModel's namespace, and returned. Subsequent lookups find it directly in the namespace (no second provider call).

The ambient provider is NOT a fallback -- it's checked alongside the Mist namespace for ambiguity detection. Both Mist and CLR candidates are collected; duplicates are deduplicated by reference equality (alias map ensures `System.String` and `Standard.String` are the same instance).

### Alias Map (CLR <-> Mist)

The alias map lives INSIDE the ambient provider, not in the ProgramModel. When the provider is asked to project `System.String`, it checks: "does `Standard.String` already exist in the ProgramModel?" If yes, it returns that instance (no new type created). The ProgramModel never sees two String objects.

The map is derived from the same source of truth as v1 (`CSharpTypeMap.BackingTypes` / `IntrinsicTypeMap`):

| CLR Type | Mist Type |
|----------|-----------|
| System.Int32 | Standard.Int32 |
| System.String | Standard.String |
| System.Boolean | Standard.Boolean |
| System.Char | Standard.Char |
| System.Single | Standard.Single |
| System.Double | Standard.Double |
| System.Decimal | Standard.Decimal |
| System.Int64 | Standard.Int64 |
| System.Int16 | Standard.Int16 |
| System.Byte | Standard.Byte |
| System.SByte | Standard.SByte |
| System.UInt32 | Standard.UInt32 |
| System.UInt64 | Standard.UInt64 |
| System.UInt16 | Standard.UInt16 |
| System.Void | Standard.Void |
| System.Threading.Tasks.Task | Standard.Task |
| System.Threading.Tasks.ValueTask | Standard.ValueTask |

### Lazy Projection

Types are projected on demand, not eagerly. When `using System;` appears:
- The provider knows System exists and what type NAMES it contains (cheap namespace index)
- Individual types are only fully projected (members, generics, constraints) when actually referenced
- Once projected, the type is inserted into `ProgramModel.Namespaces[namespace].Types[name]` and never re-projected

## CLR Features Projected to Mist

### Type Kinds

| CLR Concept | Mist Projection |
|-------------|-----------------|
| Class (non-sealed, non-abstract) | `ClassType` |
| Abstract class | `ClassType` with `IsAbstract = true` |
| Sealed class | `ClassType` with `IsSealed = true` |
| Interface | `AbstractType` |
| Struct (value type) | `StructType` |
| Ref struct (IsByRefLike) | `StructType` with `IsRefStruct = true` |
| Enum | `EnumType` with underlying type + member values |
| Delegate (Func/Action families) | Mist lambda types (special projection) |
| Named delegates (Predicate, Comparison, etc.) | `DelegateType` preserving CLR identity |
| System.Delegate / MulticastDelegate | `BaseDelegateType` singleton |

### Member Kinds

| CLR Member | Mist Projection |
|------------|-----------------|
| Public instance/static methods | `Method` |
| Public constructors | `Constructor` |
| Public properties (get/set) | `Property` |
| Public fields (including readonly/const) | `Field` |
| Public indexers (parameterized properties) | `Indexer` |
| Operator overloads (op_Addition, etc.) | `Operator` |
| Protected virtual/abstract methods | `Method` with `IsVirtualOrAbstract` flag (for implicit override detection) |

### Generic Types

| CLR Concept | Mist Projection |
|-------------|-----------------|
| Generic type definition (List`1) | `MemberType` with `TypeParametersList` populated, registered with arity name (`List<>`) |
| Closed generic (List<int>) | `ConstructedType` via `TypeInterner` (same as Mist source generics) |
| Type parameters with constraints | `TypeParameter` with constraint info |
| Generic methods | `Method` with `TypeParametersList` |

### Type Parameter Constraints

| CLR Constraint | Mist Projection |
|----------------|-----------------|
| `where T : class` | Class constraint |
| `where T : struct` | Struct constraint |
| `where T : new()` | Constructor constraint (parameterless) |
| `where T : BaseClass` | Type constraint |
| `where T : IInterface` | Type constraint (abstract) |

### Special Type Handling

| CLR Pattern | Mist Behavior |
|-------------|---------------|
| Nullable<T> (value type) | Projected as `T \| null` (union type) |
| Nullable reference types | Reference types are implicitly nullable (no special projection) |
| Span<T> / ReadOnlySpan<T> | Projected as ref-struct `StructType` (usable, lifetime-checked by C# compiler) |
| IDisposable | Sets `HasDrop = true` on the type (mapped to Mist drop semantics) |
| IEnumerable<T> / duck-typed enumerables | Synthesize IEnumerable conformance for foreach support |
| Task<T> / ValueTask<T> | Mapped to Standard.Task / Standard.ValueTask |
| ref parameters | Projected with `ParameterPassingKind.Ref` |
| in parameters | Projected with `ParameterPassingKind.In` |
| out parameters | Projected with `ParameterPassingKind.Out` |
| ref returns (mutable) | Projected with `ReturnRefKind.Ref` |
| Extension methods | Collected per-namespace for extension method resolution |

## CLR Features Hidden (Not Projected)

| CLR Feature | Reason |
|-------------|--------|
| Non-public types | Not accessible from Mist code |
| Non-public members | Not accessible from Mist code |
| Events | Not supported in Mist |
| `ref readonly` return types | Not supported in Mist currently |
| Pointer types in signatures | Unsafe -- not projected |
| Members referencing excluded types | Cascading exclusion |

## Virtual/Override Handling

Mist does not have `virtual`/`override` keywords. However, when a Mist type inherits from a CLR class, the emitter needs to emit `override` on methods that match a base class virtual/abstract slot.

**In the ProgramModel**: Protected virtual/abstract methods from CLR base types are projected as `Method` with:
- `IsVirtualOrAbstract = true` (flag for the emitter)
- `ClrAccessModifier = "protected"` or `"public"` (tells the emitter what modifier to emit on the override)

**Resolution behavior**: These methods participate in member lookup for the derived Mist type, but only for override matching -- they're not directly callable unless they're public. The type checker validates that Mist method signatures match the CLR virtual slot signature.

**Emission**: The C# emitter sees a Mist method whose name and signature matches a `IsVirtualOrAbstract` member from the base, and emits it with `override` modifier.

## Interface (Contract)

```csharp
namespace Mist.ProgramModel.Construction;

/// <summary>
/// Resolves types from loaded .NET assemblies into MistType instances
/// that live in the ProgramModel alongside source-declared types.
/// </summary>
internal interface IAmbientTypeProvider
{
    /// <summary>
    /// Attempts to resolve a type by namespace and name (with arity suffix for generics).
    /// Returns the projected MistType if found, null otherwise.
    /// If the type maps to an existing Mist Standard Library type (alias), returns
    /// that instance instead of creating a new one.
    /// </summary>
    IMistType? ResolveType(string namespaceName, string typeName);

    /// <summary>
    /// Returns all type names available in a namespace from loaded assemblies.
    /// Used during ambiguity detection to know what names exist without
    /// fully projecting every type.
    /// </summary>
    IReadOnlyList<string> GetTypeNamesInNamespace(string namespaceName);

    /// <summary>
    /// Returns extension methods declared in the given namespace.
    /// Extension methods are collected during initial assembly scanning
    /// and made available for method resolution.
    /// </summary>
    IReadOnlyList<AmbientExtensionMethodInfo> GetExtensionMethodsInNamespace(string namespaceName);
}
```

## Integration Flow

```
1. Phase 1 (TypeBuilder):
   - Process using declarations
   - Register Mist source types in namespaces
   - The ambient provider is initialized with loaded assemblies

2. Phase 2 (TypeReferenceResolver):
   - LookupInNamespace called for each name lookup
   - Checks ProgramModel namespace first (covers Mist source + already-projected CLR types)
   - If not found, calls ambientProvider.ResolveType(namespace, name)
   - If found by provider:
     - Provider checks alias map (System.String -> return existing Standard.String)
     - If no alias, projects CLR type to MistType, adds to ProgramModel namespace
     - Returns the MistType
   - For ambiguity: both Mist namespace AND ambient provider are checked;
     multiple unique results = ambiguity error

3. Emission:
   - Types with TypeOrigin.Assembly are referenced, not emitted
   - Types with TypeOrigin.Source are emitted as normal
```

## Assembly Loading (Reused from V1)

The assembly loading infrastructure is retained:
- `MetadataLoadContext` for reflection-only loading
- `LoadedAssemblyInfo` records per loaded assembly
- Framework reference resolution via `FrameworkReferenceResolver`
- Package reference resolution via `PackageResolver`
- Namespace indexing (which assemblies contribute to which namespaces)

## Open Questions

1. **Performance**: Should we measure the cost of eager full-namespace projection vs lazy per-type projection? The lazy approach adds complexity but may not be necessary for typical project sizes.

2. **Cross-assembly base types**: When a CLR type's base class lives in a different assembly that isn't loaded, what happens? V1 recorded `UnresolvedRelationship` diagnostics. V2 should do the same -- leave `BaseType` as `ErrorType.Instance` and emit a diagnostic.

3. **Extension method resolution**: V1 collected extension methods per-namespace from assemblies. V2 needs a similar mechanism. Should these become `ExtensionBlock` instances in the ProgramModel, or remain a separate lookup path?

4. **Nested types**: CLR nested types (e.g., `List<T>.Enumerator`) need to be projected as nested `MistType` on the parent. The arity-based naming needs to handle this.

5. **Generic variance**: CLR `in`/`out` variance on type parameters (covariance/contravariance). V1 didn't project this. Should V2?
