# Implementation Plan: ProgramModel Type Resolution

## Overview

This plan adds Phase 2 to the ProgramModel builder: after all type shells are registered (Phase 1), we walk every namespace → every type → resolve member signatures by reading AST member nodes and resolving their type annotations into `ITypeReference` instances. We also resolve base types for classes and structs.

The approach is infrastructure-first, then member-by-member. Each member kind (fields, properties, indexers, constructors, methods, operators, cast operators) is tackled end-to-end before moving to the next, so every task produces a self-contained, testable increment.

**Excluded from this plan** (per scope decision):
- Type arguments / generic type parameters on the owning type
- Refinement type references
- Associative type slots
- Expression bodies, initializers, method/constructor bodies
- `ISpanTypeReference` (deferred)
- `ITypeParameterReference` (deferred)

## Tasks

- [x] 1. Infrastructure: Type reference implementations
  - [x] 1.1 Create `Model/TypeReferences/NamedTypeReference.cs`
    - Implements `INamedTypeReference`
    - Holds a resolved `ITypeDefinition` reference
    - `DisplayName` returns the type's simple name
  - [x] 1.2 Create `Model/TypeReferences/ArrayTypeReference.cs`
    - Implements `IArrayTypeReference`
    - Holds an `ITypeReference ElementType`
    - `DisplayName` returns `"{ElementType.DisplayName}[]"`
  - [x] 1.3 Create `Model/TypeReferences/UnionTypeReference.cs`
    - Implements `IUnionTypeReference`
    - Holds `IReadOnlyList<ITypeReference> Constituents`
    - `DisplayName` joins constituents with `" | "`
  - [x] 1.4 Create `Model/TypeReferences/TupleTypeReference.cs`
    - Implements `ITupleTypeReference`
    - Holds `IReadOnlyList<ITypeReference> ElementTypes`
    - `DisplayName` returns `"({joined elements})"`
  - [x] 1.5 Create `Model/TypeReferences/DelegateTypeReference.cs`
    - Implements `IDelegateTypeReference`
    - Holds parameter types and return type
    - `DisplayName` returns `"({params}) -> {return}"`
  - [x] 1.6 Create `Model/TypeReferences/VoidTypeReference.cs`
    - Implements `IVoidTypeReference`
    - Singleton instance
    - `DisplayName` returns `"void"`
  - [x] 1.7 Create `Model/TypeReferences/UnknownTypeReference.cs`
    - Implements `IUnknownTypeReference`
    - Singleton instance
    - `DisplayName` returns `"unknown"`
  - [x] 1.8 Create `Model/TypeReferences/ErrorTypeReference.cs`
    - Implements `IErrorTypeReference`
    - Singleton instance
    - `DisplayName` returns `"<error>"`

- [x] 2. Infrastructure: Member definition models
  - [x] 2.1 Create `Contracts/IFieldDefinition.cs`
    - Properties: `string Name`, `ITypeReference Type`, `OwnershipKind Ownership`, `bool IsReadonly`, `bool IsStatic`
  - [x] 2.2 Create `Contracts/IPropertyDefinition.cs`
    - Properties: `string Name`, `ITypeReference Type`, `bool HasGetter`, `bool HasSetter`, `bool IsStatic`
  - [x] 2.3 Create `Contracts/IIndexerDefinition.cs`
    - Properties: `ITypeReference Type`, `IReadOnlyList<IParameterDefinition> Parameters`, `bool HasGetter`, `bool HasSetter`
  - [x] 2.4 Create `Contracts/IParameterDefinition.cs`
    - Properties: `string Name`, `ITypeReference Type`, `ParameterPassingKind PassingKind`, `bool HasDefaultValue`
  - [x] 2.5 Create `Contracts/IMethodDefinition.cs`
    - Properties: `string Name`, `ITypeReference ReturnType`, `IReadOnlyList<IParameterDefinition> Parameters`, `bool IsStatic`, `bool IsAsync`
  - [x] 2.6 Create `Contracts/IConstructorDefinition.cs`
    - Properties: `IReadOnlyList<IParameterDefinition> Parameters`
  - [x] 2.7 Create `Contracts/IOperatorDefinition.cs`
    - Properties: `string OperatorToken`, `ITypeReference ReturnType`, `IReadOnlyList<IParameterDefinition> Parameters`
  - [x] 2.8 Create `Contracts/ICastOperatorDefinition.cs`
    - Properties: `ITypeReference TargetType`, `IParameterDefinition SourceParameter`, `bool IsImplicit`
  - [x] 2.9 Create `Model/FieldDefinition.cs` implementing `IFieldDefinition`
  - [x] 2.10 Create `Model/PropertyDefinition.cs` implementing `IPropertyDefinition`
  - [x] 2.11 Create `Model/IndexerDefinition.cs` implementing `IIndexerDefinition`
  - [x] 2.12 Create `Model/ParameterDefinition.cs` implementing `IParameterDefinition`
  - [x] 2.13 Create `Model/MethodDefinition.cs` implementing `IMethodDefinition`
  - [x] 2.14 Create `Model/ConstructorDefinition.cs` implementing `IConstructorDefinition`
  - [x] 2.15 Create `Model/OperatorDefinition.cs` implementing `IOperatorDefinition`
  - [x] 2.16 Create `Model/CastOperatorDefinition.cs` implementing `ICastOperatorDefinition`

- [x] 3. Infrastructure: Expand TypeDefinition to hold members and base types
  - [x] 3.1 Add to `ITypeDefinition`: `IReadOnlyList<ITypeReference> BaseTypes`, `IReadOnlyList<IFieldDefinition> Fields`, `IReadOnlyList<IPropertyDefinition> Properties`, `IReadOnlyList<IIndexerDefinition> Indexers`, `IReadOnlyList<IMethodDefinition> Methods`, `IReadOnlyList<IConstructorDefinition> Constructors`, `IReadOnlyList<IOperatorDefinition> Operators`, `IReadOnlyList<ICastOperatorDefinition> CastOperators`
  - [x] 3.2 Add mutable lists to `TypeDefinition` base class that back the interface properties
  - [x] 3.3 Add `internal Storm.Runtime.SyntaxNode? DeclarationNode` to `TypeDefinition` for Phase 2 AST access
  - [x] 3.4 Update `TypeBuilder` to stash the AST declaration node on each created `TypeDefinition`

- [x] 4. Infrastructure: TypeReferenceResolver
  - [x] 4.1 Create `Construction/TypeReferenceResolver.cs`
    - Constructor takes `BuilderContext`
    - Main method: `ITypeReference Resolve(TypeReferenceNode typeRef, Scope scope)` 
    - Handles union types (multiple SingleTypeRefs) → `UnionTypeReference`
  - [x] 4.2 Implement single type ref dispatch in `TypeReferenceResolver`
    - `VoidSingleTypeRefSingleTypeRefNode` → `VoidTypeReference.Instance`
    - `UnknownSingleTypeRefSingleTypeRefNode` → `UnknownTypeReference.Instance`
    - `PrimitiveSingleTypeRefSingleTypeRefNode` → look up primitive name in well-known types
    - `NamedSingleTypeRefSingleTypeRefNode` → scope-based lookup (task 4.3)
    - `TupleSingleTypeRefSingleTypeRefNode` → resolve each element → `TupleTypeReference`
    - `FuncSingleTypeRefSingleTypeRefNode` → resolve type args → `DelegateTypeReference`
    - `ParenthesizedTypeRefSingleTypeRefNode` → unwrap and resolve inner
    - Unknown node types → `ErrorTypeReference.Instance` + diagnostic
  - [x] 4.3 Implement name lookup in `TypeReferenceResolver`
    - Walk the scope's namespace first (same-namespace types)
    - Walk imported namespaces in order
    - Check each namespace's types dictionary in the ProgramModel
    - Unresolved → `ErrorTypeReference.Instance` + diagnostic
  - [x] 4.4 Implement array rank handling in `TypeReferenceResolver`
    - If `SingleTypeRefNode` has a non-null `ArrayRank`, wrap the resolved inner type in `ArrayTypeReference`
  - [x] 4.5 Handle primitive type keywords in `TypeReferenceResolver`
    - `PrimitiveSingleTypeRefSingleTypeRefNode` is resolved directly by the resolver as a hardcoded alias mapping (int → Standard.Int32, string → Standard.String, etc.)
    - These are always in scope and cannot be shadowed — they bypass normal scope lookup entirely
    - The resolver produces a `NamedTypeReference` pointing to the stdlib's type definition if loaded, or a special primitive `NamedTypeReference` if the stdlib type isn't available yet
    - Primitive keywords: int, uint, long, ulong, short, ushort, byte, sbyte, float, double, decimal, bool, char, string, object

- [x] 5. Infrastructure: MemberBuilder + Phase 2 wiring
  - [x] 5.1 Create `Construction/MemberBuilder.cs`
    - Constructor takes `BuilderContext` and `TypeReferenceResolver`
    - Main method: `void BuildMembers(TypeDefinition typeDef)` — reads `typeDef.DeclarationNode`, dispatches to per-member-kind handlers
    - Initially a skeleton that switches on `DeclarationNode` type to get the members array
  - [x] 5.2 Wire Phase 2 into `ProgramModelBuilder.Build()`
    - After the per-unit loop, create a `TypeReferenceResolver` and `MemberBuilder`
    - Walk all namespaces → all types → call `MemberBuilder.BuildMembers(typeDef)` for each
    - Recurse into nested types

- [x] 6. Checkpoint — Build the solution and verify zero warnings
  - All new files compile
  - Existing tests still pass
  - No references to `Mist.Semantics.*`
  - Run `dotnet format --verify-no-changes --include Source/Mist/ProgramModel/`

- [x] 7. Base types resolution
  - [x] 7.1 In `MemberBuilder.BuildMembers`, resolve base types for classes
    - Read `ClassDeclarationNode.BaseList`
    - If present, resolve the base type name via `TypeReferenceResolver` name lookup
    - Add to `typeDef.BaseTypes`
  - [x] 7.2 Resolve base types for structs
    - Read `StructDeclarationNode.Abstracts`
    - If present, resolve via `TypeReferenceResolver`
    - Add to `typeDef.BaseTypes`
  - [x] 7.3 Resolve abstract implementations list
    - Read the abstracts list from class/struct declarations (the `with` clause base list entries after the first)
    - Resolve each via `TypeReferenceResolver`
    - Add to `typeDef.BaseTypes`

- [ ] 8. Fields
  - [ ] 8.1 Implement field resolution in `MemberBuilder`
    - Handle `FieldMemberMemberDeclarationNode`
    - Read field name from `FieldDeclarationNode.Name`
    - Resolve field type from `FieldDeclarationNode.Type` via `TypeReferenceResolver`
    - Determine ownership from field modifiers (owned keyword)
    - Determine readonly from field modifiers
    - Determine static from field modifiers
    - Create `FieldDefinition` and add to `typeDef.Fields`
  - [ ] 8.2 Verify fields work for classes, structs (enums and abstracts don't have fields in this grammar)

- [ ] 9. Checkpoint — Build and verify zero warnings after fields

- [ ] 10. Properties
  - [ ] 10.1 Implement property resolution in `MemberBuilder`
    - Handle `PropertyMemberMemberDeclarationNode`
    - Dispatch on property variant: `BlockPropertyPropertyDeclarationNode`, `ExpressionBodiedPropertyPropertyDeclarationNode`
    - Resolve property type via `TypeReferenceResolver`
    - Determine getter/setter presence from accessor list
    - Determine static from modifiers
    - Create `PropertyDefinition` and add to `typeDef.Properties`
  - [ ] 10.2 Verify properties work for classes, structs, and abstracts (abstract properties)

- [ ] 11. Indexers
  - [ ] 11.1 Implement indexer resolution in `MemberBuilder`
    - Handle `IndexerMemberMemberDeclarationNode`
    - Resolve indexer type via `TypeReferenceResolver`
    - Resolve each parameter's type
    - Determine getter/setter presence
    - Create `IndexerDefinition` and add to `typeDef.Indexers`

- [ ] 12. Constructors
  - [ ] 12.1 Implement constructor resolution in `MemberBuilder`
    - Handle `ConstructorMemberMemberDeclarationNode`
    - Resolve each parameter's type via `TypeReferenceResolver`
    - Create `ConstructorDefinition` and add to `typeDef.Constructors`

- [ ] 13. Methods
  - [ ] 13.1 Implement method resolution in `MemberBuilder`
    - Handle `MethodMemberMemberDeclarationNode`
    - Resolve return type via `TypeReferenceResolver`
    - Resolve each parameter's type
    - Determine static, async modifiers
    - Create `MethodDefinition` and add to `typeDef.Methods`
  - [ ] 13.2 Handle abstract method slots
    - Handle `AbstractMethodMemberAbstractMemberDeclarationNode` for abstract types
    - Resolve return type and parameters the same way
    - Create `MethodDefinition` and add to the abstract's methods list
  - [ ] 13.3 Handle abstract default methods
    - Handle `AbstractDefaultMethodMemberAbstractMemberDeclarationNode`
    - Same resolution as regular methods

- [ ] 14. Operators
  - [ ] 14.1 Implement operator resolution in `MemberBuilder`
    - Handle `OperatorMemberMemberDeclarationNode`
    - Read operator token
    - Resolve return type via `TypeReferenceResolver`
    - Resolve parameter types
    - Create `OperatorDefinition` and add to `typeDef.Operators`

- [ ] 15. Cast operators
  - [ ] 15.1 Implement cast operator resolution in `MemberBuilder`
    - Handle `CastOperatorMemberMemberDeclarationNode`
    - Resolve target type via `TypeReferenceResolver`
    - Resolve source parameter
    - Determine implicit/explicit from modifier
    - Create `CastOperatorDefinition` and add to `typeDef.CastOperators`

- [ ] 16. Abstract properties
  - [ ] 16.1 Handle `AbstractPropertyMemberAbstractMemberDeclarationNode`
    - Resolve property type
    - Create `PropertyDefinition` and add to abstract type's properties

- [ ] 17. Enum members
  - [ ] 17.1 Handle enum member declarations
    - Read enum member names
    - If the enum has an explicit underlying type, resolve it
    - Store enum members (may need a lightweight `EnumMemberDefinition` or reuse fields)

- [ ] 18. Final checkpoint
  - Build the solution with zero warnings
  - Run `dotnet format --verify-no-changes --include Source/Mist/ProgramModel/`
  - Verify no references to `Mist.Semantics.*`
  - All existing tests pass

## Task Dependency Graph

```json
{
  "waves": [
    {"tasks": [1, 2]},
    {"tasks": [3]},
    {"tasks": [4]},
    {"tasks": [5]},
    {"tasks": [6]},
    {"tasks": [7]},
    {"tasks": [8]},
    {"tasks": [9]},
    {"tasks": [10, 11, 12]},
    {"tasks": [13]},
    {"tasks": [14, 15, 16, 17]},
    {"tasks": [18]}
  ]
}
```

Tasks 1 and 2 are independent infrastructure (type references and member models). Task 3 depends on both (expands TypeDefinition with the new types). Task 4 (resolver) depends on 1 (type reference implementations). Task 5 (MemberBuilder + wiring) depends on 3 and 4. After checkpoint 6, the member-by-member tasks (7–17) are mostly sequential, though properties/indexers/constructors can be parallelized, as can operators/cast operators/abstract properties/enums.

## Notes

- The ProgramModel isolation rule applies: no `using Mist.Semantics.*` anywhere under `Source/Mist/ProgramModel/`
- `TypeDefinition.Scope.Parent` already chains up to the file scope, so no separate scope storage is needed for Phase 2
- The `DeclarationNode` stored on `TypeDefinition` is the AST node from Phase 1 — it gives Phase 2 access to the member list without re-walking compilation units
- Type arguments on generic type references (e.g., `List<int>`) are out of scope — `NamedTypeReference` resolves the bare name only; generic instantiation is a later task
- Primitive type keywords (int, string, etc.) are hardcoded aliases in the `TypeReferenceResolver` — they map to their Standard library type names (Standard.Int32, Standard.String, etc.), are always in scope, and cannot be shadowed by user types
- Error recovery: unresolvable types produce `ErrorTypeReference` + a diagnostic, but don't halt construction
- All member definition models are simple data holders — no logic beyond storage
