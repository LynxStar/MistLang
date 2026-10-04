# Implementation Plan: AmbientTypeProvider V2

## Overview

Implement the AmbientTypeProvider V2 which projects .NET assembly types into the ProgramModel as first-class `IMistType` instances. The implementation follows the design's deployment topology: interface in `ProgramModel/Contracts/`, implementation in `Backends/CSharpTranspiler/`, with modifications to `TypeReferenceResolver` for peer-source resolution. Assembly loading infrastructure is reused from V1.

The plan progresses through: interface definition → TypeReferenceResolver ambiguity fix → assembly loading/namespace index → visibility filtering → alias map → type kind projection → member projection → generic projection → special type handling → extension methods → integration testing → sample ingestion validation.

## Tasks

- [x] 1. Define IAmbientTypeProvider interface and supporting types
  - [x] 1.1 Create `IAmbientTypeProvider.cs` in `Source/Mist/ProgramModel/Contracts/`
    - Define the 4-method interface: `ResolveType(string, string)`, `ResolveType(string, string, int)`, `GetTypeNamesInNamespace(string)`, `GetExtensionMethodsInNamespace(string)`
    - Return types use only `IMistType` and `IReadOnlyList<>` — no references to `Mist.Semantics.*`
    - Include XML doc comments matching the design specification
    - _Requirements: 1.1, 1.2, 1.3, 1.4, 1.5_

  - [x] 1.2 Create `AmbientExtensionMethodInfo` record for V2 in `Source/Mist/ProgramModel/Contracts/`
    - Define the record with: `MethodName`, `ReceiverClrType` (System.Type), `ContainingClassName`, `RemainingParameters`, `ReturnClrType`, `IsGenericMethodDefinition`, `GenericTypeParameters`
    - This is a ProgramModel-level descriptor — V1's `AmbientExtensionMethodInfo` in `Backends/CSharpTranspiler/` remains unchanged
    - _Requirements: 1.4, 12.3_

  - [x] 1.3 Add `TypeOrigin` enum/property to the ProgramModel type system
    - Add `TypeOrigin` enum (`Source`, `Assembly`) if not already present
    - Ensure `IMistType` or concrete `MistType` base has an `Origin` property
    - _Requirements: 7.1, 7.3_

- [x] 2. Implement peer-source resolution in TypeReferenceResolver
  - [x] 2.1 Add `IAmbientTypeProvider?` dependency to `TypeReferenceResolver`
    - Add an optional `IAmbientTypeProvider` field to `TypeReferenceResolver` (nullable, injected via `BuilderContext` or constructor)
    - When null, the resolver behaves identically to today (backward compatible)
    - _Requirements: 2.1, 15.7_

  - [x] 2.2 Implement peer-source candidate collection in `LookupInNamespace`
    - Modify `LookupInNamespace` to query BOTH the Namespace_Registry AND the ambient provider
    - Collect all candidates from both sources at each scope level
    - Deduplicate by reference equality (alias-mapped types collapse to one)
    - 0 unique candidates → return null (continue scope walk)
    - 1 unique candidate → return it
    - 2+ unique candidates → emit ambiguity diagnostic, return `ErrorType`
    - _Requirements: 2.1, 2.2, 2.3, 2.4, 2.5_

  - [x] 2.3 Wire ambient provider through `BuilderContext` and `ProgramModelBuilder`
    - Add `IAmbientTypeProvider?` to `BuilderContext`
    - Thread it from `ProgramModelBuilder` initialization into `TypeReferenceResolver`
    - Ensure the experimental pipeline can pass null (no provider yet) without breaking
    - _Requirements: 2.6, 2.7, 2.8_

- [x] 3. Checkpoint - Ensure build succeeds with no regressions
  - Ensure all tests pass, ask the user if questions arise.

- [x] 4. Implement VisibilityFilterV2
  - [x] 4.1 Create `VisibilityFilterV2.cs` in `Source/Mist/Backends/CSharpTranspiler/`
    - Port visibility logic from V1's `VisibilityFilter` but produce NO references to `Mist.Semantics.*`
    - Implement `IsTypeExcluded(Type)`: non-public types, non-public nesting chain
    - Implement `IsMemberExcluded(MemberInfo)`: non-public, events, pointer signatures, ref readonly returns
    - Implement cascading exclusion: member signature references excluded type → member excluded
    - Implement named delegate exclusion: delegate with excluded Invoke → treated as excluded for cascading
    - No override detection on excluded types
    - _Requirements: 13.1, 13.2, 13.3, 13.4, 13.5, 13.6, 13.7, 13.8_

  - [x] 4.2 Write unit tests for VisibilityFilterV2
    - Test non-public type exclusion (top-level and nested)
    - Test event exclusion, pointer type exclusion, ref readonly return exclusion
    - Test cascading exclusion through member signatures
    - Test that IntPtr/UIntPtr are preserved (not excluded)
    - _Requirements: 13.1, 13.2, 13.3, 13.4, 13.5, 13.6_

- [x] 5. Implement alias map and namespace index
  - [x] 5.1 Create the alias map infrastructure in `AmbientTypeProviderV2.cs`
    - Create skeleton `AmbientTypeProviderV2` class in `Source/Mist/Backends/CSharpTranspiler/`
    - Implement `Dictionary<(string Namespace, string TypeName), (string MistNamespace, string MistTypeName)>` alias map
    - Populate with all 17 alias entries (11 numeric + String, Boolean, Char, Void, Task, ValueTask)
    - Implement alias lookup in `ResolveType`: check alias map first, return existing Standard Library instance
    - _Requirements: 3.1, 3.2_

  - [x] 5.2 Implement namespace index construction
    - Build `Dictionary<string, HashSet<string>>` namespace index from loaded assemblies' `ExportedTypes`
    - Scan names only (no full type projection during init)
    - Implement `GetTypeNamesInNamespace` returning the cached name set
    - _Requirements: 6.1, 1.3_

  - [x] 5.3 Wire assembly loading into AmbientTypeProviderV2
    - Accept `ImmutableArray<LoadedAssemblyInfo>` or assembly paths in the constructor
    - Create `MetadataLoadContext` with `PathAssemblyResolver` for all assembly paths
    - Load assemblies with error handling (emit diagnostic on failure, skip, continue)
    - Build namespace index from successfully loaded assemblies
    - _Requirements: 14.1, 14.2, 14.3, 14.4, 14.5_

  - [x] 5.4 Write property test for alias identity preservation
    - **Property 1: Alias identity preservation**
    - For all 17 alias entries, verify `ResolveType` returns reference-equal Standard Library instance
    - **Validates: Requirements 3.1, 2.2, 7.4**

- [x] 6. Implement type kind projection
  - [x] 6.1 Create `TypeProjectorV2.cs` in `Source/Mist/Backends/CSharpTranspiler/`
    - Implement type kind determination: CLR TypeAttributes → correct MistType subclass
    - Non-sealed non-abstract class → `ClassType`
    - Abstract class → `ClassType` with `IsAbstract = true`
    - Sealed class → `ClassType` with `IsSealed = true`
    - Interface → `AbstractType`
    - Struct → `StructType` (IsRef = false)
    - Ref struct (IsByRefLike) → `StructType` (IsRef = true)
    - Enum → `EnumType` with underlying type + enum members
    - Unrecognized → return null
    - Mark all projected types with `TypeOrigin.Assembly`
    - _Requirements: 8.1, 8.2, 8.3, 8.4, 8.5, 8.6, 8.7, 8.11, 7.1_

  - [x] 6.2 Implement projection cache and lazy projection flow
    - Implement `ConcurrentDictionary<(string, string), IMistType>` projection cache
    - Insert type shell into cache BEFORE projecting members (recursion safety)
    - Handle concurrent resolution race: losing thread discards work, returns cached instance
    - Implement `ResolveType` flow: alias check → visibility check → create shell → cache → project → insert into ProgramModel namespace
    - _Requirements: 6.2, 6.3, 6.4, 2.8_

  - [x] 6.3 Write property test for type kind projection correctness
    - **Property 7: Type kind projection correctness**
    - For projected CLR types from loaded assemblies, verify correct MistType subclass mapping
    - **Validates: Requirements 8.1, 8.2, 8.3, 8.4, 8.5, 8.6, 8.7**

- [x] 7. Implement base type and interface resolution
  - [x] 7.1 Implement base type resolution in TypeProjectorV2
    - Resolve base class through projection cache (recursive)
    - Exclude `System.Object` as base type (implicit)
    - If base class unresolvable → hide entire type (remove from cache, return null)
    - Set `BaseType` field on projected type
    - _Requirements: 17.1, 17.3, 17.4, 17.5_

  - [x] 7.2 Implement interface resolution in TypeProjectorV2
    - Resolve each implemented interface through projection cache
    - Add to `BaseAbstractTypes` in CLR metadata declaration order
    - If ANY interface unresolvable → hide entire type
    - Map `IEnumerable<T>` → `Standard.IEnumerable<T>` and `IEnumerator<T>` → `Standard.IEnumerator<T>`
    - _Requirements: 17.2, 17.3, 5.1, 5.2, 5.3_

  - [x] 7.3 Write property test for base type cache coherence
    - **Property 15: Base type resolution cache coherence**
    - For projected classes with non-Object base, verify `BaseType` is reference-equal to independent `ResolveType` call
    - **Validates: Requirements 17.1, 17.4**

  - [x] 7.4 Write property test for interface order preservation
    - **Property 16: Interface resolution order preservation**
    - For types implementing multiple interfaces, verify `BaseAbstractTypes` matches CLR declaration order
    - **Validates: Requirements 17.2**

- [x] 8. Checkpoint - Ensure all tests pass
  - Ensure all tests pass, ask the user if questions arise.

- [x] 9. Implement member projection
  - [x] 9.1 Implement method and constructor projection in TypeProjectorV2
    - Project public instance/static methods as `Method` instances (name, parameters, return type, generics, IsStatic)
    - Project public constructors as `Constructor` instances (parameter list)
    - Project protected virtual/abstract methods with `IsVirtualOrAbstract = true` and `ClrAccessModifier`
    - Skip members whose signatures reference excluded types
    - Apply alias substitution for all type references in signatures
    - _Requirements: 9.1, 9.2, 9.7, 9.8, 3.3_

  - [x] 9.2 Implement property, field, indexer, and operator projection in TypeProjectorV2
    - Project public properties as `Property` (type, getter, setter, IsStatic)
    - Project public fields (including readonly/const) as `Field` (type, IsStatic, IsReadonly)
    - Project public indexers as `Indexer` (element type, parameters, getter, setter)
    - Project public operator overloads as `Operator` (OperatorKind, return type, parameters)
    - _Requirements: 9.3, 9.4, 9.5, 9.6_

  - [x] 9.3 Write property test for member projection completeness
    - **Property 8: Member projection completeness**
    - For projected types, verify all public non-excluded members appear in projected member lists
    - **Validates: Requirements 9.1, 9.2, 9.3, 9.4, 9.5, 9.6**

- [x] 10. Implement generic type projection
  - [x] 10.1 Implement generic type definition projection
    - Populate `TypeParametersList` matching CLR generic parameter count
    - Register with arity-bearing name: `List<>` for arity 1, `Dictionary<,>` for arity 2
    - Project type parameter constraints (class, struct, new(), base type, interface)
    - If ANY constraint type unresolvable → hide entire type silently
    - _Requirements: 10.1, 10.2, 10.3_

  - [x] 10.2 Implement constructed type creation and method-level generics
    - Use `TypeInterner.GetOrCreateConstructedType(definition, typeArgs)` for type references with arguments
    - Ensure identical definition+args at different sites resolve to same instance
    - Project method-level generic type parameters with constraints
    - If method-level constraint unresolvable → hide that method only
    - _Requirements: 10.4, 10.5_

  - [x] 10.3 Write property test for generic arity correctness
    - **Property 9: Generic arity correctness**
    - For projected generic definitions, verify `TypeParametersList.Count` matches CLR generic parameter count
    - Verify arity-bearing name registration format
    - **Validates: Requirements 10.1, 10.2, 10.5**

  - [x] 10.4 Write property test for constructed type interning
    - **Property 11: Constructed type interning**
    - Verify same definition + type arguments from different resolution paths → same reference
    - **Validates: Requirements 10.4**

- [x] 11. Implement Func/Action and special type handling
  - [x] 11.1 Implement Func/Action delegate projection
    - `System.Func<>` family (arity 1-17) → `ConstructedType(Standard.Func<...>, typeArgs)`
    - `System.Action` family (arity 0-16) → `ConstructedType(Standard.Func<...>, [..., Standard.Void])`
    - Named delegates → `DelegateType` with `ClrFullName` + `FuncSignature`
    - `System.Delegate` / `MulticastDelegate` → `BaseDelegateType.Instance`
    - _Requirements: 4.1, 4.2, 4.3, 4.4, 8.8, 8.9, 8.10_

  - [x] 11.2 Implement Nullable, IDisposable, ref/in/out, and by-ref return handling
    - `Nullable<T>` → `UnionType(T, Standard.Void)` via TypeInterner
    - IDisposable → `HasDrop = true`, determine `IsDisposableDrop` by checking for explicit `Drop()` method
    - `ref` parameter → `RefKind.Ref`, `ClrCallingConvention.Ref`
    - `in` parameter → `RefKind.Ref`, `ClrCallingConvention.In`
    - `out` parameter → `RefKind.Ref`, `ClrCallingConvention.Out`
    - By-ref return → `ReturnRefKind = RefKind.Ref`, unwrap element type
    - Unwrap Nullable/ByRef outer wrapper before projecting inner type
    - _Requirements: 11.1, 11.2, 11.3, 11.4, 11.5, 11.6, 11.7, 11.8, 11.9_

  - [x] 11.3 Write property test for delegate family projection
    - **Property 3: Delegate family projection**
    - For CLR Func/Action types at all arities, verify correct `Standard.Func` constructed type with correct arguments
    - **Validates: Requirements 4.1, 4.2, 4.4**

  - [x] 11.4 Write property test for standard interface mapping
    - **Property 4: Standard interface mapping**
    - For types referencing `IEnumerable<T>` or `IEnumerator<T>`, verify Mist Standard equivalents used
    - **Validates: Requirements 5.1, 5.2, 5.3**

- [x] 12. Implement nested type and extension method support
  - [x] 12.1 Implement nested type projection
    - Project nested types with `DeclaringType` set to parent
    - Register with correct dot-separated key format: `Outer.Inner`, `List<>.Enumerator`, `Outer<>.Inner<>`
    - Skip nested types whose parent cannot be resolved
    - Assign `TypeOrigin.Assembly` to nested types
    - _Requirements: 16.1, 16.2, 16.3, 16.4, 16.5, 7.1_

  - [x] 12.2 Implement extension method scanning via `AmbientExtensionScanner` adaptation
    - Implement `GetExtensionMethodsInNamespace` with lazy per-namespace scanning
    - Identify extension methods: public, static, `[ExtensionAttribute]`, at least one parameter
    - Cache results per namespace
    - Return empty collection for namespaces with no extensions
    - _Requirements: 12.1, 12.2, 12.3, 12.4_

  - [x] 12.3 Write property test for nested type key format
    - **Property 14: Nested type key format**
    - For projected nested types, verify correct dot-separated key with parent arity notation
    - **Validates: Requirements 16.1, 16.2, 16.3, 16.4**

- [x] 13. Checkpoint - Ensure all tests pass
  - Ensure all tests pass, ask the user if questions arise.

- [x] 14. Wire provider into the experimental pipeline
  - [x] 14.1 Integrate AmbientTypeProviderV2 into `ExperimentalPipeline`
    - Instantiate `AmbientTypeProviderV2` with loaded assemblies during pipeline initialization
    - Pass the provider into `ProgramModelBuilder` → `BuilderContext` → `TypeReferenceResolver`
    - Ensure the provider is disposed when the pipeline completes
    - _Requirements: 14.1, 14.2, 2.1_

  - [x] 14.2 Verify empty-program baseline still produces zero diagnostics
    - Run the experimental pipeline on an empty program + stdlib
    - Confirm zero ProgramModel-construction diagnostics (regression floor)
    - _Requirements: 15.7_

- [x] 15. Integration testing with real assemblies
  - [x] 15.1 Write integration tests: alias resolution and reference equality
    - Load real BCL assemblies via MetadataLoadContext
    - Verify all 17 alias entries resolve to Standard Library instances
    - Verify repeated `ResolveType` calls return same reference
    - **Property 5: Resolution idempotence and reference equality**
    - **Validates: Requirements 6.3, 6.4**

  - [x] 15.2 Write integration tests: type projection inserts into ProgramModel
    - Verify projected types appear in `ProgramModel.Namespaces[ns].Types[name]`
    - Verify `TypeOrigin.Assembly` is assigned
    - **Property 6: Projection inserts into ProgramModel namespace**
    - **Property 13: TypeOrigin.Assembly assignment**
    - **Validates: Requirements 2.6, 2.8, 7.1**

  - [x] 15.3 Write integration tests: visibility filtering
    - Verify non-public types return null from `ResolveType`
    - Verify excluded members do not appear in projected types
    - **Property 12: Visibility filtering excludes non-public types and members**
    - **Validates: Requirements 13.1, 13.2, 13.3, 13.4, 13.5, 13.6, 13.7, 13.8**

  - [x] 15.4 Write integration tests: unresolvable inheritance hides type
    - Test types with unresolvable base classes → null return
    - Test types with unresolvable interfaces → null return
    - Test types with unresolvable constraints → null return
    - **Property 10: Unresolvable constraints hide type**
    - **Property 17: Unresolvable inheritance hides type**
    - **Validates: Requirements 10.3, 17.3**

  - [x] 15.5 Write property test for alias substitution in member signatures
    - **Property 2: Alias substitution in member signatures**
    - For projected methods with aliased types in signatures, verify Standard Library references used
    - **Validates: Requirements 3.3**

  - [x] 15.6 Write concurrency test: parallel resolution produces same references
    - Spawn parallel tasks resolving same type concurrently
    - Verify all receive the same reference-equal instance
    - **Property 5: Resolution idempotence and reference equality** (concurrent variant)
    - **Validates: Requirements 6.3, 6.4**

- [x] 16. Sample ingestion validation
  - [x] 16.1 Run existing sample programs through the experimental pipeline
    - Feed samples through stages 0-4 (lex, parse, ProgramModel construction with AmbientTypeProvider)
    - Verify samples ingest without unresolved-type diagnostics for CLR types
    - Verify `System.Console`, `List<T>`, and other commonly-referenced BCL types resolve
    - Fix ProgramModel bugs surfaced by real programs (document in backlog)
    - _Requirements: 2.7, 14.1_

  - [x] 16.2 Validate AllFeatures sample ingestion as exit criterion
    - Progress toward `AllFeatures` ingesting with zero ProgramModel-construction diagnostics
    - Track remaining unresolved types and fix forward
    - This is the exit criterion for frontend integration completeness
    - _Requirements: 2.1, 2.7_

- [x] 17. Final checkpoint - Ensure all tests pass
  - Ensure all tests pass, ask the user if questions arise.

## Notes

- Tasks marked with `*` are optional and can be skipped for faster MVP
- Each task references specific requirements for traceability
- Checkpoints ensure incremental validation
- Property tests validate universal correctness properties (Properties 1-17 from design)
- The design uses C# throughout — no language selection needed
- Tests MUST NOT spawn processes per iteration — use batching or in-process compilation per project policy
- FsCheck is already in the project for property-based tests
- ProgramModel has no existing tests — integration tests for V2 double as ProgramModel validation
- When integration tests surface ProgramModel bugs, fix forward and document in `backlog/issues.yaml`
- The experimental pipeline (`--pipeline experimental`) runs stages 0-4 and no-ops 5-6
- V2 MUST NOT reference `Mist.Semantics.*` — only `Mist.ProgramModel.*` types

## Task Dependency Graph

```json
{
  "waves": [
    { "id": 0, "tasks": ["1.1", "1.2", "1.3"] },
    { "id": 1, "tasks": ["2.1", "4.1"] },
    { "id": 2, "tasks": ["2.2", "4.2", "5.1"] },
    { "id": 3, "tasks": ["2.3", "5.2", "5.3"] },
    { "id": 4, "tasks": ["5.4", "6.1"] },
    { "id": 5, "tasks": ["6.2"] },
    { "id": 6, "tasks": ["6.3", "7.1"] },
    { "id": 7, "tasks": ["7.2"] },
    { "id": 8, "tasks": ["7.3", "7.4", "9.1"] },
    { "id": 9, "tasks": ["9.2"] },
    { "id": 10, "tasks": ["9.3", "10.1"] },
    { "id": 11, "tasks": ["10.2"] },
    { "id": 12, "tasks": ["10.3", "10.4", "11.1"] },
    { "id": 13, "tasks": ["11.2"] },
    { "id": 14, "tasks": ["11.3", "11.4", "12.1"] },
    { "id": 15, "tasks": ["12.2"] },
    { "id": 16, "tasks": ["12.3", "14.1"] },
    { "id": 17, "tasks": ["14.2"] },
    { "id": 18, "tasks": ["15.1", "15.2", "15.3", "15.4", "15.5", "15.6"] },
    { "id": 19, "tasks": ["16.1"] },
    { "id": 20, "tasks": ["16.2"] }
  ]
}
```
