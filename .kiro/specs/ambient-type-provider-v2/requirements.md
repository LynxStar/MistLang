# Requirements Document

## Introduction

AmbientTypeProvider V2 integrates .NET assembly types into the ProgramModel as first-class `IMistType` instances. CLR types are projected into the same namespace/type graph that Mist source types occupy, enabling unified type resolution, proper generic interning, and ambiguity detection between source and ambient types. This replaces the V1 provider which projected into the legacy `IType`/`ISymbol` system.

## Glossary

- **Ambient_Provider**: The `IAmbientTypeProvider` implementation that resolves CLR types from loaded .NET assemblies into `IMistType` instances within the ProgramModel. The implementation resides in `Backends/CSharpTranspiler/` because it depends on `MetadataLoadContext` and `System.Reflection` (backend concerns).
- **IAmbientTypeProvider**: The interface contract consumed by the TypeReferenceResolver, residing in `ProgramModel/Contracts/`. Decouples the ProgramModel from assembly-loading internals.
- **ProgramModel**: The new frontend semantic graph (`Mist.ProgramModel`) containing all resolved types, namespaces, and members.
- **TypeReferenceResolver**: The component in `ProgramModel/Construction/` responsible for resolving AST type reference nodes into `IMistType` by performing scope-based name lookup.
- **Namespace_Registry**: The `ProgramModel.Namespaces` dictionary mapping fully qualified namespace names to `Namespace` objects containing their types.
- **Alias_Map**: A mapping from CLR type identifiers (e.g., `System.String`) to their corresponding Mist Standard Library types (e.g., `Standard.String`), ensuring no duplicate type instances.
- **TypeOrigin**: A marker distinguishing types originating from source code (`Source`) versus .NET assemblies (`Assembly`).
- **Namespace_Index**: A lightweight structure mapping namespace names to type names available from loaded assemblies, built at initialization without fully projecting types.
- **Type_Projection**: The process of converting a CLR `System.Type` into a `MistType` subclass with all public members populated.
- **TypeInterner**: The ProgramModel component that ensures structurally identical constructed types and union types share a single instance.
- **MetadataLoadContext**: The .NET reflection-only assembly loading mechanism reused from V1.
- **Extension_Method_Info**: A descriptor for a CLR extension method including its receiver type, namespace, and method metadata.

## Requirements

### Requirement 1: Ambient Provider Interface

**User Story:** As a compiler developer, I want a well-defined interface for the ambient type provider, so that the TypeReferenceResolver can query CLR types without coupling to assembly loading internals.

#### Acceptance Criteria

1. THE IAmbientTypeProvider SHALL expose a `ResolveType` method accepting a namespace name and type name, returning an `IMistType` when the type exists in loaded assemblies or null when it does not.
2. THE IAmbientTypeProvider SHALL expose an arity-aware overload of `ResolveType` accepting a namespace name, type name, and integer type-parameter count, so that generic types (e.g., `List` with arity 1) are distinguished from non-generic types sharing the same base name.
3. THE IAmbientTypeProvider SHALL expose a `GetTypeNamesInNamespace` method returning all type names available in a given namespace from loaded assemblies, including CLR arity-suffixed names (e.g., `List`1`, `Dictionary`2`) so that callers can detect name existence without triggering full type projection.
4. THE IAmbientTypeProvider SHALL expose a `GetExtensionMethodsInNamespace` method returning extension method descriptors for a given namespace, where each descriptor identifies at minimum the method name, the extended type (first parameter type), and the containing static class name.
5. THE IAmbientTypeProvider interface SHALL reside in `ProgramModel/Contracts/` and reference only types from the `Mist.ProgramModel` namespace hierarchy (no references to `Mist.Semantics.*`, `Mist.Backends.*`, or old-model types).
6. THE Ambient_Provider implementation SHALL reside in `Backends/CSharpTranspiler/` because it depends on `MetadataLoadContext` and `System.Reflection` which are backend concerns.

### Requirement 2: TypeReferenceResolver Integration

**User Story:** As a compiler developer, I want the TypeReferenceResolver to consult the ambient provider as a peer source alongside ProgramModel namespaces, so that CLR types participate in unified resolution with proper ambiguity detection.

#### Acceptance Criteria

1. WHEN resolving a type name at a given scope level, THE TypeReferenceResolver SHALL collect ALL candidates from BOTH the Namespace_Registry AND the Ambient_Provider for every namespace checked at that scope level, treating the Ambient_Provider as a peer source (not a fallback).
2. WHEN candidates are collected, THE TypeReferenceResolver SHALL deduplicate by reference equality, so that alias-mapped types (e.g., `System.String` and `Standard.String` resolving to the same instance) count as one unique candidate.
3. WHEN exactly one unique candidate is found across all checked namespaces at a scope level, THE TypeReferenceResolver SHALL return that candidate.
4. WHEN zero candidates are found at a scope level, THE TypeReferenceResolver SHALL continue scope-chain walking to the parent scope.
5. WHEN two or more unique candidates are found at a scope level, THE TypeReferenceResolver SHALL return an `ErrorType` and emit an ambiguity diagnostic listing the conflicting candidates.
6. WHEN the Ambient_Provider returns a projected type for a namespace, THE TypeReferenceResolver SHALL insert the type into the Namespace_Registry under the queried namespace, so that subsequent lookups find it directly without re-querying the provider.
7. IF a type name cannot be resolved after exhausting all scope levels (including Ambient_Provider queries for each), THEN THE TypeReferenceResolver SHALL report an unresolved-type diagnostic.
8. THE Ambient_Provider SHALL be responsible for inserting projected types into the appropriate ProgramModel namespace. The TypeReferenceResolver SHALL NOT be responsible for deciding which namespace a projected type belongs to.

### Requirement 3: Alias Map

**User Story:** As a compiler developer, I want CLR primitive types to resolve to their existing Mist Standard Library counterparts, so that `System.String` and `Standard.String` are the same type instance throughout the program.

#### Acceptance Criteria

1. WHEN the Ambient_Provider is asked to project a CLR type that has an entry in the Alias_Map, THE Ambient_Provider SHALL silently return the existing `IMistType` instance from the Namespace_Registry instead of creating a new type, such that the returned reference is identity-equal to the Standard Library type instance, without logging, failing, or emitting diagnostics.
2. THE Alias_Map SHALL contain mappings for the 11 numeric types (Int32, Int64, Int16, Byte, SByte, UInt32, UInt64, UInt16, Single, Double, Decimal), plus String, Boolean, Char, Void, Task, and ValueTask.
3. WHEN a CLR method signature references an aliased type in any position (parameter type, return type, or generic type argument), THE Ambient_Provider SHALL resolve that type reference to the corresponding Standard Library instance from the Alias_Map.

### Requirement 4: Func/Action Delegate Projection

**User Story:** As a compiler developer, I want CLR `System.Func<>` and `System.Action<>` delegate families projected as Mist's `Standard.Func` type, so that Mist code can seamlessly work with .NET delegate-accepting APIs using Mist lambda syntax.

#### Acceptance Criteria

1. WHEN a CLR type matches the `System.Func<>` family (any arity from `Func<TResult>` through `Func<T1,...,T16,TResult>`), THE Ambient_Provider SHALL project it as a constructed `Standard.Func` type with the matching type arguments.
2. WHEN a CLR type matches the `System.Action` family (any arity from `Action` through `Action<T1,...,T16>`), THE Ambient_Provider SHALL project it as a constructed `Standard.Func` type whose return type is `Standard.Void`, treating Action as Func with void return.
3. THE Ambient_Provider SHALL perform structural transformation based on the delegate's generic arity rather than a simple alias-map lookup, because each arity produces a differently-constructed `Standard.Func` instance.
4. WHEN a CLR method parameter type or return type is `Func<>` or `Action<>`, THE Ambient_Provider SHALL apply the same Func projection, so that Mist code can pass lambdas to .NET APIs expecting these delegate types.

### Requirement 5: IEnumerable/IEnumerator Projection

**User Story:** As a compiler developer, I want CLR `IEnumerable<T>` and `IEnumerator<T>` projected to Mist's Standard equivalents, so that foreach works seamlessly with .NET collections.

#### Acceptance Criteria

1. WHEN a CLR type reference to `System.Collections.Generic.IEnumerable<T>` is encountered, THE Ambient_Provider SHALL resolve it to Mist's `Standard.IEnumerable<T>` type instance.
2. WHEN a CLR type reference to `System.Collections.Generic.IEnumerator<T>` is encountered, THE Ambient_Provider SHALL resolve it to Mist's `Standard.IEnumerator<T>` type instance.
3. WHEN a projected CLR type implements `IEnumerable<T>`, THE Ambient_Provider SHALL record the conformance using the Mist `Standard.IEnumerable<T>` type in the projected type's `BaseAbstractTypes` list, ensuring foreach resolution works against the Mist interface.
4. THE Ambient_Provider SHALL apply this mapping transitively -- if a projected member's return type or parameter type is `IEnumerable<T>` or `IEnumerator<T>`, the corresponding Mist Standard type SHALL be used.

### Requirement 6: Lazy Projection

**User Story:** As a compiler developer, I want types to be projected on demand rather than eagerly, so that compilation startup is fast even when many assemblies are loaded.

#### Acceptance Criteria

1. WHEN the Ambient_Provider is initialized, THE Ambient_Provider SHALL build only the Namespace_Index (namespace name to type name set) from all loaded assemblies without performing full Type_Projection for any type.
2. WHEN ResolveType is called for a type that has not yet been projected, THE Ambient_Provider SHALL perform full Type_Projection for that type including all its public members before returning the result.
3. WHEN ResolveType is called for a type that has already been projected, THE Ambient_Provider SHALL return the same instance (reference equality) without re-projecting.
4. IF two threads call ResolveType concurrently for the same type that has not yet been projected, THEN THE Ambient_Provider SHALL ensure only one projected instance is retained and both callers receive the same instance (reference equality preserved).
5. THE Ambient_Provider SHALL project only public members during Type_Projection. Private and internal members SHALL be excluded.

### Requirement 7: TypeOrigin Marker

**User Story:** As a compiler developer, I want projected CLR types to carry an origin marker, so that the emitter can distinguish types that need C# code generation from types that are merely referenced.

#### Acceptance Criteria

1. THE Ambient_Provider SHALL assign `TypeOrigin.Assembly` to every type it projects from a .NET assembly, except when the type matches an alias entry (e.g., `System.String` maps to `Standard.String`), in which case it SHALL return the existing `TypeOrigin.Source` instance without creating a new type. THE Ambient_Provider SHALL assign `TypeOrigin.Assembly` to nested types and constructed types that originate from .NET assemblies. Generic type parameters SHALL NOT receive a TypeOrigin marker as they are structural placeholders, not standalone types.
2. WHEN the emitter encounters a type with `TypeOrigin.Assembly`, THE emitter SHALL skip emitting a type definition (class, struct, or interface body) for that type and SHALL emit only a namespace `using` directive or fully-qualified reference as needed. THE emitter SHALL emit `using` directives or fully-qualified references for Assembly-origin types only when the type is actually referenced in the emitted compilation unit.
3. THE ProgramModel SHALL treat types with `TypeOrigin.Assembly` identically to types with `TypeOrigin.Source` during member resolution, generic instantiation, overload resolution, and conformance checking -- the origin marker SHALL NOT influence any semantic analysis outcome.
4. IF the Ambient_Provider resolves a CLR type whose namespace and name match an alias-mapped Standard Library type, THEN the Ambient_Provider SHALL return the pre-existing Standard Library `MistType` instance rather than projecting a new type.

### Requirement 8: Type Kind Projection

**User Story:** As a compiler developer, I want all relevant CLR type kinds projected as their corresponding `MistType` subclasses, so that Mist code can use .NET class hierarchies, interfaces, structs, enums, and delegates.

#### Acceptance Criteria

1. WHEN a CLR non-sealed non-abstract class is projected, THE Ambient_Provider SHALL produce a `ClassType` instance whose `Modifiers` has `IsAbstract = false` and `IsSealed = false`.
2. WHEN a CLR abstract class is projected, THE Ambient_Provider SHALL produce a `ClassType` instance whose `Modifiers` has `IsAbstract = true`. CLR abstract classes are NOT projected as `AbstractType` -- the Mist `abstract` keyword in the grammar is reserved for interfaces (`AbstractType`).
3. WHEN a CLR sealed class is projected, THE Ambient_Provider SHALL produce a `ClassType` instance whose `Modifiers` has `IsSealed = true`.
4. WHEN a CLR interface is projected, THE Ambient_Provider SHALL produce an `AbstractType` instance with its method and property slots populated from the interface's public members.
5. WHEN a CLR value type (struct) is projected, THE Ambient_Provider SHALL produce a `StructType` instance whose `Modifiers` has `IsRef = false`.
6. WHEN a CLR ref struct (IsByRefLike attribute present) is projected, THE Ambient_Provider SHALL produce a `StructType` instance whose `IsRef` property returns `true`.
7. WHEN a CLR enum is projected, THE Ambient_Provider SHALL produce an `EnumType` instance with `UnderlyingType` set to the resolved Mist equivalent of the CLR underlying integral type (e.g., `System.Int32` maps to `Standard.Int32`) and `MembersList` containing one `EnumMember` per declared constant with its numeric value.
8. WHEN a CLR delegate whose full name matches the pattern `System.Func` or `System.Action` (any arity) is projected, THE Ambient_Provider SHALL produce a Mist `Standard.Func` constructed type rather than a nominal delegate type (per Requirement 4).
9. WHEN a CLR named delegate that is not in the Func or Action family (e.g., `System.Predicate<T>`, `System.Comparison<T>`, `Microsoft.AspNetCore.Http.RequestDelegate`) is projected, THE Ambient_Provider SHALL produce a `DelegateType` instance whose `ClrFullName` preserves the original CLR namespace-qualified type name (for emission) and whose `FuncSignature` holds the structurally equivalent `Standard.Func` constructed type. The CLR name is preserved solely for the emitter; semantically the delegate behaves as its Func equivalent.
10. WHEN `System.Delegate` or `System.MulticastDelegate` is projected, THE Ambient_Provider SHALL return the `BaseDelegateType.Instance` singleton.
11. IF a CLR type's `TypeAttributes` do not match any of the recognized categories (class, interface, struct, enum, or delegate), THEN THE Ambient_Provider SHALL return null and not insert any type into the ProgramModel namespace.

### Requirement 9: Member Kind Projection

**User Story:** As a compiler developer, I want CLR type members projected as ProgramModel member instances, so that Mist code can call .NET methods, access properties, and use constructors.

#### Acceptance Criteria

1. WHEN a CLR type is projected, THE Ambient_Provider SHALL project all public instance and static methods declared on that type as `Method` instances, each carrying the method name, parameter list (with types and passing kinds), return type, generic type parameters (if any), and a static/instance flag via `MemberModifiers.IsStatic`.
2. WHEN a CLR type is projected, THE Ambient_Provider SHALL project all public constructors declared on that type as `Constructor` instances, each carrying the parameter list with types and passing kinds.
3. WHEN a CLR type is projected, THE Ambient_Provider SHALL project all public properties declared on that type as `Property` instances, each carrying the property type, a `Getter` accessor (or null if write-only), a `Setter` accessor (or null if read-only), and a static/instance flag via `MemberModifiers.IsStatic`.
4. WHEN a CLR type is projected, THE Ambient_Provider SHALL project all public fields (including readonly and const) declared on that type as `Field` instances, each carrying the field type and a static/instance flag via `MemberModifiers.IsStatic`.
5. WHEN a CLR type is projected, THE Ambient_Provider SHALL project all public indexers declared on that type as `Indexer` instances, each carrying the element type, a parameter list (the index parameters with types), a `Getter` accessor (or null), and a `Setter` accessor (or null).
6. WHEN a CLR type is projected, THE Ambient_Provider SHALL project all public operator overloads declared on that type as `Operator` instances, each carrying the `OperatorKind`, return type, and parameter list (1 parameter for unary, 2 for binary).
7. WHEN a CLR type has protected virtual or abstract methods, THE Ambient_Provider SHALL project those methods as `Method` instances with `IsVirtualOrAbstract = true` and a `ClrAccessModifier` set to `"protected"` or `"public"` (reflecting the CLR accessibility). This flag exists purely for the emitter: when a Mist method's name and signature match a base class virtual/abstract slot, the emitter adds `override` to the C# output. Mist has no virtual/override/abstract keywords for methods.
8. IF a public member's signature references an excluded type (pointer types, or other unsupported CLR features), THEN THE Ambient_Provider SHALL skip that member entirely rather than projecting an incomplete instance.

### Requirement 10: Generic Type Projection

**User Story:** As a compiler developer, I want CLR generic types projected with their type parameters and constraints, so that Mist code can instantiate and constrain generic .NET types.

#### Acceptance Criteria

1. WHEN a CLR generic type definition is projected, THE Ambient_Provider SHALL populate the `TypeParametersList` with one `TypeParameter` entry per CLR generic parameter, in declaration order, matching the CLR type's generic arity.
2. WHEN a CLR generic type definition is projected, THE Ambient_Provider SHALL register the type with an arity-bearing name using angle brackets and commas (e.g., `List<>` for arity 1, `Dictionary<,>` for arity 2).
3. WHEN a CLR type parameter has constraints, THE Ambient_Provider SHALL project each constraint (class, struct, new(), base type, interface) onto the corresponding `TypeParameter`. IF ANY constraint references a type that cannot be resolved from loaded assemblies, THEN THE Ambient_Provider SHALL immediately hide the entire type (not project it) without processing remaining constraints, silently without emitting a diagnostic.
4. WHEN a type reference to a generic definition includes type arguments, THE Ambient_Provider SHALL use the TypeInterner to create or retrieve the `ConstructedType`, such that identical definition-and-type-argument combinations at different call sites resolve to the same `ConstructedType` instance.
5. WHEN a CLR method has generic type parameters, THE Ambient_Provider SHALL populate the method's `TypeParametersList` with one `TypeParameter` entry per CLR method-level generic parameter, in declaration order. IF a method-level constraint references a type that cannot be resolved, THEN THE Ambient_Provider SHALL hide that method (not project it) silently without emitting a diagnostic.

### Requirement 11: Special Type Handling

**User Story:** As a compiler developer, I want CLR types with special semantics (nullable, disposable, span) projected with their Mist equivalents, so that Mist code integrates naturally with .NET patterns.

#### Acceptance Criteria

1. WHEN a CLR method signature contains a parameter or return type of `Nullable<T>` (where T is a value type), THE Ambient_Provider SHALL project it as a union type `T | void` by calling `TypeInterner.GetOrCreateUnionType` with the projected inner type and `Standard.Void`. Mist does not have null; `void` serves as the absent-value sentinel.
2. WHEN a CLR type implements `System.IDisposable` and does NOT declare an explicit `Drop()` method, THE Ambient_Provider SHALL set `HasDrop = true` and `IsDisposableDrop = true` on the projected type, causing the emitter to emit `.Dispose()` calls at drop sites.
3. WHEN a CLR type implements `System.IDisposable` and DOES declare an explicit `Drop()` method, THE Ambient_Provider SHALL set `HasDrop = true` and `IsDisposableDrop = false` on the projected type, causing the emitter to emit `.Drop()` calls at drop sites.
4. WHEN a CLR type carries the `System.Runtime.CompilerServices.IsByRefLikeAttribute` custom attribute (e.g., `Span<T>`, `ReadOnlySpan<T>`), THE Ambient_Provider SHALL produce a `StructType` with `IsRefStruct = true`.
5. WHEN a CLR method has a `ref` parameter (IsByRef=true, IsIn=false, IsOut=false), THE Ambient_Provider SHALL project that parameter symbol with `RefKind = RefKind.Ref` and `ClrCallingConvention = ClrCallingConvention.Ref`.
6. WHEN a CLR method has an `in` parameter (IsByRef=true, IsIn=true), THE Ambient_Provider SHALL project that parameter symbol with `RefKind = RefKind.Ref` and `ClrCallingConvention = ClrCallingConvention.In`.
7. WHEN a CLR method has an `out` parameter (IsByRef=true, IsOut=true), THE Ambient_Provider SHALL project that parameter symbol with `RefKind = RefKind.Ref` and `ClrCallingConvention = ClrCallingConvention.Out`.
8. WHEN a CLR method has a by-ref return type (ReturnType.IsByRef=true), THE Ambient_Provider SHALL set `ReturnRefKind = RefKind.Ref` on the projected method symbol and use the unwrapped element type (not the by-ref wrapper) as the method's declared return type.
9. WHEN a CLR parameter or return type is `Nullable<T>` or a by-ref type, THE Ambient_Provider SHALL unwrap the outer wrapper (Nullable or ByRef) before projecting the inner type, applying intrinsic substitution to the inner type (e.g., `System.Int32` becomes `Standard.Int32`).

### Requirement 12: Extension Method Resolution

**User Story:** As a compiler developer, I want CLR extension methods (e.g., LINQ) discoverable per-namespace, so that Mist code can use BCL extension methods when the declaring namespace is imported.

#### Acceptance Criteria

1. WHEN `GetExtensionMethodsInNamespace` is called for a namespace, THE Ambient_Provider SHALL scan loaded assemblies for public static classes in that namespace decorated with `[System.Runtime.CompilerServices.ExtensionAttribute]` and return extension methods from those classes, caching results for subsequent calls to the same namespace.
2. THE Ambient_Provider SHALL identify a method as an extension method only if it is public, static, decorated with `[ExtensionAttribute]`, and has at least one parameter.
3. THE Extension_Method_Info SHALL include: the receiver type (the CLR type of the first parameter), the method name, the remaining parameter types (excluding the receiver), the return type, whether the method is a generic method definition, and the generic type parameters of the method.
4. IF `GetExtensionMethodsInNamespace` is called with a namespace that contains no extension methods, THEN THE Ambient_Provider SHALL return an empty collection.
5. WHEN a namespace is imported in Mist source, THE TypeReferenceResolver SHALL retrieve extension methods for that namespace from the Ambient_Provider and collect them alongside any source-defined extension methods as peer candidates during member lookup. The Ambient_Provider participates as a peer source for extensions, not as a fallback consulted only when source extensions are absent.

### Requirement 13: Visibility Filtering

**User Story:** As a compiler developer, I want non-public CLR types and members excluded from projection, so that Mist code cannot reference inaccessible .NET internals.

#### Acceptance Criteria

1. THE Ambient_Provider SHALL exclude types where IsPublic is false (for top-level types) or where any type in the nesting chain is not NestedPublic (for nested types) from the Namespace_Index and from projection.
2. THE Ambient_Provider SHALL exclude members that are not IsPublic when projecting a type's members, except for protected virtual or abstract methods which SHALL be projected for override detection (per Requirement 9.7).
3. THE Ambient_Provider SHALL exclude CLR events (EventInfo members) from projection.
4. THE Ambient_Provider SHALL exclude members whose signature contains an unsafe pointer type (CLR IsPointer types, i.e. T*) in any position (return type, parameter type, property type, or field type), while preserving members that use IntPtr or UIntPtr.
5. IF a CLR member's signature references an excluded type in any position (return type, parameter types, property type, field type) -- including through by-ref wrappers, array element types, and generic type arguments recursively -- THEN THE Ambient_Provider SHALL exclude that entire member from projection rather than projecting it with an error-type substitution.
6. THE Ambient_Provider SHALL project a member whose result is by reference with `MemberModifiers.IsRef`, and one whose result is ref readonly (a by-ref return marked with IsReadOnlyAttribute) also with `MemberModifiers.ReturnsReadonlyRef`, so the result is a readonly reference as clr-ref-interop Requirement 6 defines it.
7. IF a CLR type is a named delegate (inherits MulticastDelegate, excluding System.Func, System.Action, System.Delegate, and System.MulticastDelegate themselves) whose Invoke method signature contains a pointer type, a ref readonly return, or a ref-struct parameter/return, THEN THE Ambient_Provider SHALL treat that delegate type as excluded for purposes of cascading exclusion in criterion 5.
8. THE Ambient_Provider SHALL NOT perform override detection analysis on excluded (non-public) types. Only types that pass visibility filtering participate in virtual/abstract method projection.

### Requirement 14: Assembly Loading

**User Story:** As a compiler developer, I want V2 to reuse the existing assembly loading infrastructure, so that framework and package assemblies are loaded consistently with V1.

#### Acceptance Criteria

1. THE Ambient_Provider SHALL receive assembly paths by calling `FrameworkReferenceResolver.Resolve()` and `PackageResolver.ResolvePackages()`, combining both results into a single `ImmutableArray<string>` of DLL paths before loading.
2. THE Ambient_Provider SHALL create a single `MetadataLoadContext` instance initialized with a `PathAssemblyResolver` containing all combined assembly paths, and use it for all subsequent reflection-only assembly loading.
3. IF an assembly fails to load via `MetadataLoadContext.LoadFromAssemblyPath`, THEN THE Ambient_Provider SHALL emit a diagnostic identifying the failing assembly path and the exception message, skip that assembly, and continue loading remaining assemblies without aborting the loading phase.
4. WHEN an assembly loads successfully, THE Ambient_Provider SHALL construct a `LoadedAssemblyInfo` instance for it, populating the `FilePath`, `MetadataAssembly`, and `NamespaceIndex` properties by scanning `Assembly.ExportedTypes`.
5. THE Ambient_Provider SHALL expose the loaded assemblies as an `ImmutableArray<LoadedAssemblyInfo>` available to downstream consumers for namespace and type resolution.

### Requirement 15: Isolation from Legacy System

**User Story:** As a compiler developer, I want V2's implementation isolated from the old semantics system, with a clean contract boundary between the backend implementation and the ProgramModel consumer.

#### Acceptance Criteria

1. THE Ambient_Provider implementation SHALL reside under `Source/Mist/Backends/CSharpTranspiler/` (same location as V1), because it depends on `MetadataLoadContext` and `System.Reflection` which are backend concerns.
2. THE IAmbientTypeProvider interface (the contract consumed by the TypeReferenceResolver) SHALL reside under `Source/Mist/ProgramModel/Contracts/`.
3. THE Ambient_Provider implementation SHALL not reference any type from `Mist.Semantics`, `Mist.Semantics.Contracts`, or `Mist.Semantics.Model`, either directly via `using` directives or transitively through types it consumes.
4. THE Ambient_Provider SHALL produce only `IMistType` and ProgramModel member types (Method, Property, Field, Constructor, Indexer, Operator).
5. THE Ambient_Provider SHALL not share any mutable runtime state with the V1 `AssemblyTypeProvider`, including cache dictionaries, `MetadataLoadContext` instances, or static fields; shared read-only source patterns (such as `LoadedAssemblyInfo`) defined in a neutral location outside both `ProgramModel/` and `Backends/CSharpTranspiler/` are permitted.
6. IF a utility type is shared between the Ambient_Provider and V1 `AssemblyTypeProvider`, THEN THE shared type SHALL itself contain no references to `Mist.Semantics`, `Mist.Semantics.Contracts`, or `Mist.Semantics.Model`.
7. THE Ambient_Provider SHALL be permitted to produce no types during initialization or transient error states (e.g., when no assemblies load successfully). Consumers SHALL handle an empty provider gracefully.

### Requirement 16: Nested Type Projection

**User Story:** As a compiler developer, I want CLR nested types (e.g., `List<T>.Enumerator`) projected as nested `MistType` on their parent, so that Mist code can reference nested .NET types.

#### Acceptance Criteria

1. WHEN Mist code references a CLR nested type by qualified name, THE Ambient_Provider SHALL project that nested type as a `MistType` instance with `DeclaringType` set to the already-projected parent type.
2. WHEN a nested type inside a non-generic parent is projected, THE Ambient_Provider SHALL register it in the parent's namespace types dictionary using the dot-separated key format `Outer.Inner`.
3. WHEN a nested type inside a generic parent is projected, THE Ambient_Provider SHALL include the parent's arity in the qualified key using angle-bracket notation (e.g., `List<>.Enumerator`). WHEN a non-generic nested type is inside a generic parent, THE Ambient_Provider SHALL still register it in the parent's namespace types dictionary using the parent's arity notation (e.g., `List<>.Enumerator`).
4. WHEN a nested type is itself generic, THE Ambient_Provider SHALL include both the parent's arity and the nested type's arity in the qualified key (e.g., `Outer<>.Inner<>`).
5. IF a nested type's parent type cannot be resolved from any loaded assembly, THEN THE Ambient_Provider SHALL skip projection of that nested type silently.

### Requirement 17: Base Type and Interface Resolution

**User Story:** As a compiler developer, I want projected CLR types to have their inheritance relationships resolved, so that Mist code can use polymorphism with .NET types.

#### Acceptance Criteria

1. WHEN a CLR class has a base class other than `System.Object`, THE Ambient_Provider SHALL set the projected type's `BaseType` to the projected base type instance obtained through the projection cache. The `BaseType` field SHALL remain null until resolution of the base class completes successfully. `System.Object` SHALL be excluded as a base type (it is implicit and adds no value).
2. WHEN a CLR type implements one or more interfaces, THE Ambient_Provider SHALL add each projected interface instance to the type's `BaseAbstractTypes` list, preserving the declaration order from the CLR metadata.
3. IF a CLR type's base class or implemented interface cannot be resolved from any loaded assembly, THEN THE Ambient_Provider SHALL hide the entire type (not project it at all) silently without emitting a diagnostic. The type simply does not appear in the ProgramModel.
4. WHEN resolving base types or implemented interfaces, THE Ambient_Provider SHALL use the same projection cache used for top-level type resolution, so that a base type or interface reference is reference-equal to the standalone projected type returned by `ResolveType`.
5. WHEN projecting a type that has circular or recursive inheritance references (e.g., a base type whose own base type is already being projected), THE Ambient_Provider SHALL insert the in-progress type instance into the projection cache before projecting its members, so that a recursive lookup returns the in-progress instance instead of triggering infinite recursion.
