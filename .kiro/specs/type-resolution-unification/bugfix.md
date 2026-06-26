# Bugfix Requirements Document

## Introduction

The `AssemblyTypeProvider`'s `TypeProjector` creates multiple disconnected instances of the same logical CLR type when projecting from assembly metadata. This only affects CLR-originated types (never Mist-defined types, which are guaranteed singleton instances through the scope chain and symbol table). The duplication occurs because `TypeProjector.ProjectTypeReference` — the method used for projecting member types, parameter types, generic arguments, and extension receiver types — uses a cache-only resolver (`_typeReferenceResolverCacheOnly`) that creates a fresh shell instance when the type hasn't yet been projected via the main `AssemblyTypeProvider.ResolveType()` cache path. This causes extension method resolution to fail for interface-typed receivers (e.g., `IServiceProvider`) because the `ExtensionRegistry` cannot match a receiver type instance against a different instance of the same logical type.

Additionally, the member lookup API in `SemanticModelBuilder` uses inconsistent overload selection: `LookupMemberType` uses `Scope.Lookup()` (returns first registered overload, which may be non-generic) while `LookupGenericMemberSymbol` uses `LookupAll()` with arity filtering. When a generic extension method like `GetRequiredService<T>()` is called with explicit type arguments, `LookupMemberType` returns the wrong overload's return type before `LookupGenericMemberSymbol` can correct it, resulting in `unknown` instead of the substituted concrete type.

## Bug Analysis

### Current Behavior (Defect)

1.1 WHEN a CLR interface type (e.g., `IServiceProvider`) is projected both as a property return type and as an extension method receiver type THEN the system creates two reference-distinct `AbstractType` instances for the same logical type because the `AssemblyTypeProvider`'s `TypeProjector` does not intern type references across all projection entry points

1.2 WHEN the `ExtensionRegistry` performs receiver matching using `GetExactExtensions` (dictionary lookup with reference equality) THEN the system fails to find extensions registered against a different instance of the same logical type

1.3 WHEN a generic extension method call with explicit type arguments (e.g., `GetRequiredService<MigrationRunner>()`) is resolved via `LookupMemberType` THEN the system uses `Scope.Lookup()` which returns the first registered overload (non-generic `GetRequiredService(Type)`) instead of the arity-matching generic overload

1.4 WHEN `LookupMemberType` returns the non-generic overload's return type and the call site has explicit type arguments THEN the system cannot perform type argument substitution on the return type, producing `unknown` instead of the expected concrete type (e.g., `MigrationRunner`)

1.5 WHEN `LookupGenericMemberSymbol` is called as a secondary lookup to find the generic overload THEN the system correctly uses `LookupAll()` with arity filtering on extension scopes, but by this point `LookupMemberType` has already returned the wrong type used for downstream inference

1.6 WHEN a method call is resolved on a target type THEN the system uses up to 5 separate lookup functions (`LookupMemberType`, `LookupMemberSymbol`, `LookupGenericMemberSymbol`, `LookupGenericMemberSymbolWithParams`, `LookupMemberSymbolByParamCount`) that each have different scope (extensions vs. no extensions), return type (IType vs. ISymbol), and overload selection strategy (first-match vs. arity-aware vs. param-count-aware)

1.7 WHEN `LookupMemberSymbol` is called for kind-aware validation (checking if a resolved name is a field/property vs. method) THEN it does NOT check extension methods, so it returns null for extension methods — causing the validation to silently pass rather than confirming the symbol kind

### Expected Behavior (Correct)

2.1 WHEN a CLR interface type is projected from any assembly entry point (property return type, extension method receiver, base type reference) THEN the system SHALL resolve to the same canonical `AbstractType` instance via type interning

2.2 WHEN the `ExtensionRegistry` performs receiver matching THEN the system SHALL find all applicable extensions regardless of whether the receiver type instance is the same object as the one extensions were registered with (either through type interning or consistent structural equality)

2.3 WHEN a generic extension method call with explicit type arguments is resolved THEN the system SHALL select the overload whose type parameter arity matches the provided type argument count, not the first-registered overload

2.4 WHEN a generic extension method with explicit type arguments is resolved and the correct overload is found THEN the system SHALL substitute the type arguments into the return type, yielding the concrete result type (e.g., `GetRequiredService<MigrationRunner>()` returns `MigrationRunner`)

2.5 WHEN member resolution encounters multiple overloads with the same name but different generic arities THEN the system SHALL use a single unified lookup path that considers own members, inherited members, and extension methods with consistent arity-aware overload selection

2.6 WHEN a method is looked up on a target type THEN the system SHALL provide a single unified method resolution function that searches own members, inherited members (base class chain), and extension methods — returning both the resolved symbol and its return type in one pass

2.7 WHEN the unified method lookup needs to select among multiple overloads THEN the system SHALL support filtering by type argument arity, parameter count, and lambda-compatible parameters within the same function rather than requiring separate specialized lookup functions

### Unchanged Behavior (Regression Prevention)

3.1 WHEN a non-generic extension method is called on a class-typed receiver (e.g., `string.Contains()`) THEN the system SHALL CONTINUE TO resolve the method correctly via the existing structural equality fallback for `ClassType`

3.2 WHEN a non-generic extension method is called on a struct-typed receiver THEN the system SHALL CONTINUE TO resolve the method correctly via the existing structural equality fallback for `StructType`

3.3 WHEN an extension method is called on a generic constructed type (e.g., `List<int>.Select()`) THEN the system SHALL CONTINUE TO resolve the method correctly via the `ConstructedType` generic definition matching

3.4 WHEN member access resolves to an own member (not an extension) on a class, struct, or interface type THEN the system SHALL CONTINUE TO return that member without consulting extensions

3.5 WHEN `LookupMemberType` resolves a non-generic method call (no type arguments provided) THEN the system SHALL CONTINUE TO return the first matching overload's declared type as it does today

3.6 WHEN universal extensions (receiver type = unknown) are registered THEN the system SHALL CONTINUE TO include them in applicability results for all receiver types

3.7 WHEN extension methods are filtered by imported namespaces THEN the system SHALL CONTINUE TO exclude extensions whose containing namespace is not imported at the call site

3.8 WHEN a Mist-defined type (class, struct, abstract, newtype, enum, type alias) is referenced in user code THEN the system SHALL CONTINUE TO resolve it through the scope chain and symbol table without involving the ambient type projection layer

3.9 WHEN a method call resolves to a name that is actually a field or property THEN the system SHALL CONTINUE TO emit a diagnostic error indicating the member cannot be invoked as a method

3.10 WHEN a generic method call with explicit type arguments has a type argument count mismatch THEN the system SHALL CONTINUE TO emit a diagnostic error indicating the arity mismatch

3.11 WHEN a method call has arguments that include lambda expressions THEN the system SHALL CONTINUE TO select the overload with lambda-compatible parameters (Expression, Func, Action, delegate) for target type inference
