# Implementation Plan: Assembly Type Provider

## Overview

This plan replaces the manually coded `CSharpAmbientTypeResolver` with an automatic metadata-reading system that loads .NET assembly DLLs via `System.Reflection.MetadataLoadContext` and projects their public types into the Mist semantic model. The implementation proceeds bottom-up: infrastructure and data models first, then core projection logic, then integration with the compiler pipeline, and finally emitter updates, tests, sample app, and documentation.

## Tasks

- [ ] 1. Project infrastructure and data models
  - [ ] 1.1 Add NuGet package references to Mist.csproj
    - Add `System.Reflection.MetadataLoadContext` package reference
    - Add `NuGet.Protocol` and `NuGet.Packaging` package references for package resolution
    - _Requirements: 3.1, 9.1_

  - [ ] 1.2 Extend ProjectFileParser to parse `[packages]` table
    - Add `NuGetPackageReference` record to `Driver/Contracts/`
    - Add `Packages` property to `ProjectConfiguration`
    - Parse `[packages]` table entries as name (key) → version (string value)
    - Validate package name matches `[a-zA-Z0-9\-_\.]+` and ≤128 chars
    - Validate version is non-empty, non-whitespace string
    - Emit `MIST_PKG_INVALID_NAME`, `MIST_PKG_INVALID_VERSION`, `MIST_PKG_TABLE_EXPECTED` diagnostics
    - Handle absent `[packages]` table as empty list (no error)
    - Handle non-string version values with error diagnostic
    - _Requirements: 1.1, 1.2, 1.3, 1.4, 1.5, 1.6, 1.7, 1.8_

  - [ ] 1.3 Create NullableProjection data model
    - Create `NullableOrigin` enum (`NullableValueType`, `ReferenceType`) in `Semantics/Model/`
    - Create `NullableProjectionInfo` class with `Origin` and `InnerType` properties
    - Add `NullableProjectionInfo?` metadata slot to `UnionType` (or use a side dictionary on the provider)
    - _Requirements: 14.1, 14.2, 14.6_

  - [ ] 1.4 Create `LoadedAssemblyInfo` data model
    - Create `LoadedAssemblyInfo` class in `Driver/` with `FilePath`, `MetadataAssembly`, and `NamespaceIndex` properties
    - `NamespaceIndex` is `IReadOnlyDictionary<string, IReadOnlyList<string>>` built from `ExportedTypes`
    - _Requirements: 3.2, 3.3_

- [ ] 2. SDK discovery and package resolution
  - [ ] 2.1 Implement `SdkDiscovery`
    - Create `SdkDiscovery` class in `Driver/`
    - Implement `FindReferenceAssemblyPath(string targetFramework, DiagnosticBag diagnostics)` returning `string?`
    - Run `dotnet --list-sdks` with 10-second timeout to find installed SDKs
    - Parse output to find highest version supporting target framework
    - Construct reference assembly path: `{sdkRoot}/packs/Microsoft.NETCore.App.Ref/{version}/ref/{tfm}/`
    - Fall back to well-known paths (`/usr/share/dotnet`, `C:\Program Files\dotnet`, `~/.dotnet`)
    - Implement disk cache at `~/.mist/sdk-ref-path.cache`: read cached path first, validate with `Directory.Exists`, fall through to full discovery on miss/stale
    - Implement in-process static cache: avoid even the disk read on repeated calls within the same process
    - Implement `RegenerateDiskCache()` for the publish script to call
    - Emit `MIST_SDK_NOT_FOUND` and `MIST_SDK_NO_FRAMEWORK` diagnostics on failure
    - _Requirements: 2.1, 2.2, 2.4, 2.5, 2.6_

  - [ ] 2.2 Implement `PackageResolver`
    - Create `PackageResolver` class in `Driver/`
    - Implement `ResolvePackages(ImmutableArray<NuGetPackageReference> packages, string targetFramework, DiagnosticBag diagnostics)` returning `ImmutableArray<string>`
    - Use `NuGet.Protocol` for package download from nuget.org
    - Use local cache at `~/.nuget/packages/`
    - Implement TFM preference: net10.0 > net9.0 > net8.0 > netstandard2.1 > netstandard2.0
    - Resolve transitive dependencies with highest-compatible-version semantics
    - Enforce exact version matching (no ranges)
    - 60-second timeout per package download
    - Emit `MIST_PKG_NOT_FOUND`, `MIST_PKG_NO_COMPATIBLE_TFM`, `MIST_PKG_NETWORK_ERROR` diagnostics
    - _Requirements: 9.1, 9.2, 9.3, 9.4, 9.5, 9.6, 9.7, 9.8_

- [ ] 3. Checkpoint - Ensure infrastructure builds cleanly
  - Ensure all tests pass, ask the user if questions arise.

- [ ] 4. Core type projection components
  - [ ] 4.1 Implement `IntrinsicTypeMap`
    - Create `IntrinsicTypeMap` class in `Backends/CSharpTranspiler/`
    - Build reversed map from `CSharpTypeMap.BackingTypes` (CLR name → Mist Standard Library name)
    - Implement `GetMistTypeName(string clrFullName)` returning `string?`
    - Implement `SubstituteIntrinsics(IType projectedType, IStdlibTypeProvider stdlib)` with recursive generic argument substitution
    - Single source of truth — no separate mapping table
    - _Requirements: 7.1, 7.4, 7.5, 7.6_

  - [ ] 4.2 Implement `VisibilityFilter`
    - Create `VisibilityFilter` class in `Backends/CSharpTranspiler/`
    - Implement `IsTypeExcluded(Type clrType)` — excludes ref structs, non-public types
    - Implement `IsMemberExcluded(MemberInfo member)` — excludes: non-public, events, out params, ref params (not `in`), ref returns, pointer types, signatures referencing excluded types
    - Evaluation order: accessibility → events → ref struct → out → ref → ref return → pointers → excluded type references
    - `in` parameters treated as pass-by-value (NOT filtered)
    - _Requirements: 6.1, 6.2, 6.3, 6.4, 6.5, 6.6, 6.7_

  - [ ] 4.3 Implement `TypeProjector`
    - Create `TypeProjector` class in `Backends/CSharpTranspiler/`
    - Implement `ProjectType(Type clrType, IStdlibTypeProvider stdlib)` returning `(IType, ImmutableArray<ISymbol>)`
    - CLR class → `ClassType` with namespace, base type, interfaces, type parameters
    - CLR struct → `StructType`
    - CLR interface → `InterfaceType`
    - CLR enum → `EnumType` with members and underlying type
    - Strip generic arity suffix (`` `N ``) from DisplayName
    - Project generic type parameters with names and ordinals
    - Translate generic constraints: `class` → HasClassConstraint, `struct` → HasStructConstraint, `new()` → constructor constraint, base type/interface → projected Mist type
    - Omit constraints with no Mist equivalent
    - Project inheritance chains (base class ≠ Object → set BaseType)
    - Project interface implementations
    - All projected symbols use `SymbolOrigin.Backend`
    - _Requirements: 4.1, 4.2, 4.3, 4.4, 4.5, 4.6, 4.7, 4.8, 10.1, 10.2, 10.3, 10.4, 10.5_

  - [ ] 4.4 Implement member projection in `TypeProjector`
    - Project public instance/static methods as method symbols with translated parameter/return types
    - Project public properties (get-only → read-only, get+public set → read-write, non-public set → read-only)
    - Project public fields (readonly/const → read-only)
    - Project public constructors
    - Project public indexers (parameterized `Item` properties)
    - Project optional parameters with defaults
    - Project generic method type parameters and constraints
    - Apply `VisibilityFilter` to each member before projection
    - Apply `IntrinsicTypeMap.SubstituteIntrinsics` to all types in signatures
    - _Requirements: 5.1, 5.2, 5.3, 5.4, 5.5, 5.6, 5.7, 5.8, 5.9_

  - [ ] 4.5 Implement nullable projection in `TypeProjector`
    - `Nullable<T>` → `UnionType([T_mist, VoidType])` with `NullableOrigin.NullableValueType`
    - Reference types (class, interface, string, array, delegate) → `UnionType([T_mist, VoidType])` with `NullableOrigin.ReferenceType`
    - Non-nullable value types → plain Mist type (no union)
    - Ignore NRT annotations (`NullableAttribute`, `NullableContextAttribute`) — conservative projection
    - Track origin via `NullableProjectionInfo` for emitter use
    - _Requirements: 14.1, 14.2, 14.5, 14.6_

  - [ ] 4.6 Implement operator and conversion projection
    - Map `op_Addition`, `op_Subtraction`, `op_Multiply`, `op_Division`, `op_Modulus` → binary operator symbols
    - Map `op_Equality`, `op_Inequality`, `op_LessThan`, `op_GreaterThan`, `op_LessThanOrEqual`, `op_GreaterThanOrEqual` → comparison operator symbols
    - Map `op_Implicit`, `op_Explicit` → cast operator symbols
    - Map `op_UnaryNegation`, `op_UnaryPlus` → unary operator symbols
    - Project operand types and return types through intrinsic substitution
    - _Requirements: 12.1, 12.2, 12.3, 12.4_

  - [ ] 4.7 Implement IEnumerable/IEnumerator interop projection
    - Detect `IEnumerable<T>` implementation on CLR types
    - Project as implementing `Standard.IEnumerable<TElement>` (ignore non-generic `IEnumerable`)
    - Synthesize `Enumerator` associated type slot from `GetEnumerator()` return type
    - If return type is concrete (e.g., `List<T>.Enumerator`) → use concrete type
    - If return type is `IEnumerator<T>` → use `Standard.IEnumerator<TElement>`
    - Handle multiple `IEnumerable<T>` implementations (project all)
    - _Requirements: 18.1, 18.2, 18.3, 18.4, 18.6_

- [ ] 5. Checkpoint - Ensure projection components build cleanly
  - Ensure all tests pass, ask the user if questions arise.

- [ ] 6. AssemblyTypeProvider core implementation
  - [ ] 6.1 Implement `AssemblyTypeProvider` class
    - Create `AssemblyTypeProvider` class in `Backends/CSharpTranspiler/` implementing `IAmbientTypeResolver`
    - Accept assembly paths from `SdkDiscovery` + `PackageResolver`
    - Create `MetadataLoadContext` with all assembly paths
    - Build namespace index by scanning all assemblies' `ExportedTypes` (names only)
    - Implement `HasNamespace(string)` — check namespace index
    - Implement `ResolveType(string namespace, string typeName)` — lazy projection on first access
    - Implement `GetMembers(IType type)` — return cached member symbols
    - Implement `GetTypeNamesInNamespace(string namespace)` — return from namespace index
    - Cache resolved types: `(namespace, typeName) → (IType, ImmutableArray<ISymbol>)`
    - Ensure reference equality for repeated resolutions of same type
    - _Requirements: 3.1, 3.2, 3.3, 3.4, 3.5_

  - [ ] 6.2 Wire `AssemblyTypeProvider` into `CSharpTranspilerBackend`
    - Modify `CSharpTranspilerBackend.GetAmbientTypeResolver()` to return `AssemblyTypeProvider` instead of `CSharpAmbientTypeResolver`
    - Implement static provider instance caching in `CSharpTranspilerBackend`: reuse the same `AssemblyTypeProvider` when assembly paths are identical across `Compile()` calls (avoids recreating MetadataLoadContext + reloading 167 DLLs per compilation)
    - Implement static SDK assembly path caching in `CompilerDriver`: cache `Directory.GetFiles(sdkRefPath, "*.dll")` result so it's computed once per process
    - Pass SDK assembly paths and NuGet package assembly paths to the provider
    - Integrate with `CompilerDriver` — call `SdkDiscovery` and `PackageResolver` during Stage 1 (Loading)
    - Pass `ProjectConfiguration.Packages` from `ProjectFileParser` to `PackageResolver`
    - Handle `MIST_SDK_NOT_FOUND` gracefully (fall back to no assembly types)
    - Add SDK cache regeneration step to `publish.sh`
    - _Requirements: 2.1, 2.3, 2.4, 15.4_

- [x] 7. SemanticModelBuilder integration
  - [x] 7.1 Update namespace resolution for CLR namespaces
    - Update `SemanticModelBuilder` Pass 2 to query `IAmbientTypeResolver.HasNamespace()` for `using` directives
    - Make all public types from imported CLR namespaces available for unqualified name resolution
    - Resolve Mist-declared types first; fall through to CLR namespaces if no match
    - Support fully-qualified CLR type references without `using` directive
    - Emit `MIST_NS_NOT_FOUND` for `using` directives referencing non-existent namespaces
    - Emit `MIST_TYPE_AMBIGUOUS` when type name is ambiguous across multiple imported namespaces
    - _Requirements: 8.1, 8.2, 8.3, 8.4, 8.5_

  - [x] 7.2 Add abstract/sealed type validation
    - Emit `MIST_ABSTRACT_INSTANTIATION` when `new` is used on an abstract CLR type
    - Emit `MIST_SEALED_INHERITANCE` when a Mist type inherits from a sealed CLR type
    - _Requirements: 13.4, 13.5_

  - [x] 7.3 Update overload resolution for projected methods
    - Ensure all visible overloads from CLR types are available for overload resolution
    - Verify filtering one overload does not affect projection of others
    - Ensure generic constraint violations produce error diagnostics
    - _Requirements: 11.1, 11.2, 11.3, 10.6_

  - [x] 7.4 Integrate `foreach` resolution for CLR IEnumerable types
    - Resolve `IEnumerable<TElement>` implementation on CLR types during `foreach` type inference
    - Infer element type using same logic as Mist-native enumerables
    - Require explicit type annotation when multiple `IEnumerable<T>` implementations exist
    - _Requirements: 18.4, 18.6_

- [ ] 8. CSharp emitter updates
  - [ ] 8.1 Update generic type argument emission
    - When Mist code constructs a generic CLR type (e.g., `new List<Standard.Int32>()`), emit with backing C# type arguments (e.g., `new List<int>()`)
    - When Mist code invokes a generic CLR method with explicit type arguments, emit with backing C# type arguments
    - Apply `CSharpTypeMap` to all type arguments in emitted generic constructions
    - _Requirements: 10.8, 10.9_

  - [ ] 8.2 Update nullable emission
    - When emitting calls to CLR methods expecting `Nullable<T>`: narrowed `T` → `new Nullable<T>(value)`, narrowed `void` → `default(Nullable<T>)`
    - When emitting calls to CLR methods expecting reference type: narrowed `T` → emit directly, narrowed `void` → emit `null`
    - Use `NullableProjectionInfo.Origin` to determine which emission pattern to use
    - _Requirements: 14.3, 14.4_

  - [ ] 8.3 Update `foreach` emission for CLR collections
    - Emit `foreach` over CLR collection types as plain C# `foreach` statement
    - Do NOT emit explicit `GetEnumerator()`/`MoveNext()`/`Current` calls for CLR types
    - _Requirements: 18.5_

  - [ ] 8.4 Update array interop emission
    - When `Standard.Array<T, Length>` is passed to CLR method expecting `T[]`, emit array value directly
    - Dependent length parameter is erased at CLR level
    - _Requirements: 7.3_

- [ ] 9. Checkpoint - Ensure full pipeline compiles and existing tests pass
  - Ensure all tests pass, ask the user if questions arise.

- [ ] 10. Test fixture assembly
  - [ ] 10.1 Create `Mist.Tests.InteropFixtures` project
    - Create new class library project at `Test/Mist.Tests.InteropFixtures/`
    - Add project reference from `Mist.Tests` to `Mist.Tests.InteropFixtures`
    - Include curated CLR types covering all projection scenarios:
      - `SimpleClass`, `SimpleStruct`, `ISimpleInterface`, `SimpleEnum`
      - `GenericClass<T> where T : class`, `MultiConstraint<T> where T : struct, IComparable<T>`
      - `HasOutParam`, `HasRefParam`, `HasInParam` (in should NOT be filtered)
      - `RefStructType` (ref struct)
      - `Operatable` struct with `op_Addition`, `op_Equality`, `op_Implicit`
      - `Base`, `Middle : Base`, `Derived : Middle` inheritance chain
      - `CustomCollection : IEnumerable<int>` with concrete enumerator
      - Types with indexers, optional parameters, multiple overloads
      - Types with nullable return values and `Nullable<T>` parameters
    - _Requirements: All (test infrastructure)_

- [ ] 11. Unit tests
  - [ ]* 11.1 Write unit tests for ProjectFileParser `[packages]` parsing
    - Test absent `[packages]` table → empty package list
    - Test non-table `[packages]` → error diagnostic
    - Test non-string version value → error diagnostic
    - Test valid package entries parse correctly
    - Test invalid package name characters → error diagnostic
    - Test package name exceeding 128 chars → error diagnostic
    - Test empty/whitespace version → error diagnostic
    - _Requirements: 1.1, 1.2, 1.3, 1.4, 1.5, 1.6, 1.7, 1.8_

  - [ ]* 11.2 Write unit tests for VisibilityFilter
    - Test `in` parameters treated as pass-by-value (not filtered)
    - Test events hidden from projection
    - Test ref struct types excluded entirely
    - Test out parameters exclude method
    - Test ref parameters (non-in) exclude method
    - Test ref return excludes method/property
    - Test pointer types exclude member
    - Test non-public members excluded
    - Test signature referencing excluded type → member excluded
    - _Requirements: 6.1, 6.2, 6.3, 6.4, 6.5, 6.6, 6.7_

  - [ ]* 11.3 Write unit tests for TypeProjector
    - Test class → ClassType projection
    - Test struct → StructType projection
    - Test interface → InterfaceType projection
    - Test enum → EnumType with members and underlying type
    - Test generic type parameter projection
    - Test generic constraint translation
    - Test inheritance chain projection
    - Test interface implementation projection
    - Test optional parameters with defaults projected as optional
    - Test generic method type parameters projected
    - _Requirements: 4.1–4.8, 5.1–5.9, 10.1–10.5_

  - [ ]* 11.4 Write unit tests for IntrinsicTypeMap
    - Test all BackingTypes entries reverse correctly
    - Test recursive substitution in nested generics
    - Test non-intrinsic types pass through unchanged
    - _Requirements: 7.1, 7.4, 7.5, 7.6_

  - [ ]* 11.5 Write unit tests for nullable projection
    - Test `Nullable<T>` → `T | void` with NullableValueType origin
    - Test reference type → `T | void` with ReferenceType origin
    - Test non-nullable value type → plain type (no union)
    - _Requirements: 14.1, 14.2, 14.5, 14.6_

  - [ ]* 11.6 Write unit tests for error diagnostics
    - Test corrupted assembly file → error diagnostic, compilation continues
    - Test abstract type instantiation → error diagnostic
    - Test sealed type inheritance → error diagnostic
    - Test ambiguous type name → error diagnostic with namespace list
    - Test non-existent namespace in `using` → error diagnostic
    - _Requirements: 3.5, 13.4, 13.5, 8.4, 8.5, 15.1–15.5_

- [ ] 12. Property-based tests
  - [ ]* 12.1 Write property test for package name validation (Property 1)
    - **Property 1: Package Name Validation Round-Trip**
    - Generate random strings, verify acceptance matches regex `[a-zA-Z0-9\-_\.]+` and ≤128 chars
    - Use FsCheck with minimum 100 iterations
    - **Validates: Requirements 1.2, 1.3, 1.8**

  - [ ]* 12.2 Write property test for type kind projection (Property 2)
    - **Property 2: Type Kind Projection Correctness**
    - Generate random type selections from fixture assembly, verify kind mapping (class→ClassType, struct→StructType, interface→InterfaceType, enum→EnumType)
    - **Validates: Requirements 4.1, 4.2, 4.3, 4.4**

  - [ ]* 12.3 Write property test for visibility filter completeness (Property 3)
    - **Property 3: Visibility Filter Completeness**
    - Generate random member selections from fixture assembly, verify filter decisions match exclusion rules
    - **Validates: Requirements 6.1, 6.2, 6.3, 6.4, 6.5, 6.6, 6.7**

  - [ ]* 12.4 Write property test for intrinsic type substitution (Property 4)
    - **Property 4: Intrinsic Type Substitution Round-Trip**
    - Generate random CLR type names from BackingTypes map, verify round-trip produces correct Standard Library IType
    - **Validates: Requirements 7.1, 7.4, 7.6**

  - [ ]* 12.5 Write property test for recursive generic substitution (Property 5)
    - **Property 5: Recursive Generic Argument Substitution**
    - Generate random nested generic constructions with intrinsic arguments, verify all levels substituted
    - **Validates: Requirements 7.5, 10.7**

  - [ ]* 12.6 Write property test for member projection completeness (Property 6)
    - **Property 6: Member Projection Completeness**
    - For random public members passing visibility filter, verify corresponding Mist symbol exists with correct parameter/return types
    - **Validates: Requirements 5.1, 5.2, 5.3, 5.4, 5.5, 5.9**

  - [ ]* 12.7 Write property test for nullable projection (Property 7)
    - **Property 7: Nullable and Reference Type Projection**
    - Generate random signature positions, verify T|void vs plain projection based on type category
    - **Validates: Requirements 14.1, 14.2, 14.5**

  - [ ]* 12.8 Write property test for inheritance chain (Property 8)
    - **Property 8: Inheritance Chain Projection**
    - For random multi-level inheritance chains, verify full chain projected and assignability holds
    - **Validates: Requirements 4.7, 4.8, 13.1, 13.2, 13.6**

  - [ ]* 12.9 Write property test for operator projection (Property 9)
    - **Property 9: Operator Projection**
    - For CLR types with operator methods, verify corresponding Mist operator symbols with correct types
    - **Validates: Requirements 12.1, 12.2, 12.3, 12.4**

  - [ ]* 12.10 Write property test for generic constraint translation (Property 10)
    - **Property 10: Generic Constraint Translation**
    - For random type parameters with constraints, verify translation rules (class→HasClassConstraint, struct→HasStructConstraint, etc.)
    - **Validates: Requirements 4.6, 10.2, 10.3, 10.4, 10.5**

  - [ ]* 12.11 Write property test for caching identity (Property 11)
    - **Property 11: Type Resolution Caching Identity**
    - Resolve same type multiple times, verify reference equality
    - **Validates: Requirements 3.4**

  - [ ]* 12.12 Write property test for overload preservation (Property 12)
    - **Property 12: Overload Preservation**
    - For random overload sets with some filtered, verify remaining are projected independently
    - **Validates: Requirements 11.1, 11.2**

  - [ ]* 12.13 Write property test for IEnumerable mapping (Property 13)
    - **Property 13: IEnumerable Interface Mapping**
    - For CLR types implementing IEnumerable<T>, verify Standard.IEnumerable<TElement> projection and Enumerator slot synthesis
    - **Validates: Requirements 18.1, 18.2, 18.3**

  - [ ]* 12.14 Write property test for SDK version selection (Property 14)
    - **Property 14: SDK Version Selection**
    - Generate random version lists, verify highest compatible version is selected
    - **Validates: Requirements 2.4, 2.6**

  - [ ]* 12.15 Write property test for TFM selection (Property 15)
    - **Property 15: NuGet TFM Selection**
    - Generate random TFM lists, verify nearest compatible is selected per preference order
    - **Validates: Requirements 9.5**

- [ ] 13. Checkpoint - Ensure all tests pass
  - Ensure all tests pass, ask the user if questions arise.

- [ ] 14. Sample application
  - [ ] 14.1 Create sample project at `samples/AssemblyTypeProvider/`
    - Create `samples/AssemblyTypeProvider/mist.toml` with project configuration
    - Create `samples/AssemblyTypeProvider/Program.mist` with the proof code verbatim from Requirements §16
    - Demonstrate: importing CLR namespaces, constructing CLR types, calling instance/static methods, accessing properties, accessing indexers, using generic CLR types, handling nullable return values, passing Mist intrinsic types to CLR methods, array interop
    - Each permutation prints labeled output in `Key=Value` format
    - Expected output must match exactly: `Count=3`, `Item0=10`, `Found=42`, `Length=12`, `Upper=HELLO, WORLD`, `Abs=5`, `ArrLen=3`
    - _Requirements: 16.1, 16.2, 16.3, 16.5_

  - [ ] 14.2 Integrate sample into AllFeatures orchestrator
    - Add `AssemblyTypeProvider.Program.RunDemo()` call to `samples/AllFeatures/Program.mist`
    - _Requirements: 16.4_

- [ ] 15. Book documentation
  - [ ] 15.1 Create book chapter at `book/src/advanced/dotnet-interop.md`
    - Cover: how Assembly Type Provider works, adding NuGet packages in `mist.toml`, SDK assemblies available by default, CLR-to-Mist type mapping, reference types as `T | void` unions, narrowing before use
    - Explain Visibility Filter exclusions (out params, ref params, ref structs) and why
    - Use `{{#tabs}}` / `{{#include}}` pattern for all code examples
    - _Requirements: 17.1, 17.5, 17.6_

  - [ ] 15.2 Add chapter to `book/src/SUMMARY.md`
    - List under "Advanced Features" section
    - _Requirements: 17.2_

  - [ ] 15.3 Create book snippets in `book/snippets/advanced/dotnet-interop/`
    - Snippet (a): importing and using a CLR collection type (complete standalone program)
    - Snippet (b): handling a nullable return value via pattern matching (complete standalone program)
    - Each snippet must compile and run without errors through the Mist CLI
    - Generate corresponding `.cs` files via SnippetEmitter tool
    - _Requirements: 17.3, 17.4_

- [ ] 16. Final checkpoint - Ensure all tests pass and sample compiles
  - Ensure all tests pass, ask the user if questions arise.

## Notes

- Tasks marked with `*` are optional and can be skipped for faster MVP
- Each task references specific requirements for traceability
- Checkpoints ensure incremental validation
- Property tests validate universal correctness properties from the design document
- Unit tests validate specific examples and edge cases
- The test fixture assembly (`Mist.Tests.InteropFixtures`) provides controlled CLR types for deterministic testing
- Property tests use in-process compilation via `CompilerDriver.Compile()` and direct `AssemblyTypeProvider` API calls — no external process spawning per iteration (batched approach)
- The `IntrinsicTypeMap` derives its mapping by reversing `CSharpTypeMap.BackingTypes` — no separate table

## Task Dependency Graph

```json
{
  "waves": [
    { "id": 0, "tasks": ["1.1", "1.3", "1.4"] },
    { "id": 1, "tasks": ["1.2", "4.1", "4.2"] },
    { "id": 2, "tasks": ["2.1", "2.2", "4.3"] },
    { "id": 3, "tasks": ["4.4", "4.5", "4.6", "4.7"] },
    { "id": 4, "tasks": ["6.1"] },
    { "id": 5, "tasks": ["6.2", "10.1"] },
    { "id": 6, "tasks": ["7.1", "7.2", "7.3", "7.4"] },
    { "id": 7, "tasks": ["8.1", "8.2", "8.3", "8.4"] },
    { "id": 8, "tasks": ["11.1", "11.2", "11.3", "11.4", "11.5", "11.6"] },
    { "id": 9, "tasks": ["12.1", "12.2", "12.3", "12.4", "12.5", "12.6", "12.7", "12.8", "12.9", "12.10", "12.11", "12.12", "12.13", "12.14", "12.15"] },
    { "id": 10, "tasks": ["14.1", "15.1", "15.2", "15.3"] },
    { "id": 11, "tasks": ["14.2"] }
  ]
}
```
