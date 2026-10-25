# Implementation Plan: Associated Types

## Overview

This plan implements associated types for Mist interfaces and traits — abstract type slots that implementors must fill with concrete types satisfying declared constraints. The implementation touches every compiler layer: grammar, parser, semantic model, type checker, C# emitter, pretty printer, TextMate grammar, diagnostics, sample app, and language book documentation.

The approach follows the existing multi-pass architecture and reuses established patterns (constraint resolution, interface satisfaction validation, generic lifting).

## Tasks

- [x] 1. Grammar and parser foundation
  - [x] 1.1 Add `AssociatedTypeSlotDeclaration` and `UnconstrainedAssociatedTypeSlotDeclaration` rules to `Source/Mist/Grammar/Mist.storm`
    - Add the two new grammar rules for constrained and unconstrained associated type slots
    - Add `InterfaceAssociatedTypeMember` and `InterfaceUnconstrainedAssociatedTypeMember` alternatives to `InterfaceMemberDeclaration`
    - Add `TraitAssociatedTypeMember` and `TraitUnconstrainedAssociatedTypeMember` alternatives to `TraitMemberDeclaration`
    - Rebuild the project to trigger Storm source generation of new node types
    - _Requirements: 1.1, 1.2, 1.3, 1.4, 1.7_

  - [x] 1.2 Update `MistPrettyPrinter.cs` to emit associated type slot declarations
    - Add `EmitAssociatedTypeSlotDeclaration` method formatting as `type {Name}{TypeParams} : {ConstraintList}{WhereClauses};`
    - Add `EmitUnconstrainedAssociatedTypeSlotDeclaration` method formatting as `type {Name}{TypeParams};`
    - Add cases to `EmitInterfaceMemberDeclaration` and `EmitTraitMemberDeclaration` switch statements
    - _Requirements: 7.1, 7.2, 7.3, 7.4_

  - [x] 1.3 Write property test: Pretty-print round-trip preserves structure (Property 1)
    - **Property 1: Pretty-print round-trip preserves structure**
    - Create `Test/Mist.Tests/Parsing/AssociatedTypeRoundTripPropertyTests.cs`
    - Generate random valid slot declarations (constrained, unconstrained, with type params, with where clauses)
    - Parse in-process, pretty-print, parse again, compare syntax trees
    - Zero process spawns
    - **Validates: Requirements 1.1, 1.2, 1.3, 1.5, 1.7, 7.1, 7.2, 7.3, 7.4**

  - [x] 1.4 Write property test: Parser disambiguation between abstract slots and concrete aliases (Property 2)
    - **Property 2: Parser disambiguation between abstract slots and concrete aliases**
    - Create `Test/Mist.Tests/Parsing/AssociatedTypeDisambiguationPropertyTests.cs`
    - Generate random names/constraints, create both `type Name : Constraints;` and `type Name = TypeExpr;` forms
    - Verify they produce different node types (`AssociatedTypeSlotDeclarationNode` vs type alias node)
    - Zero process spawns
    - **Validates: Requirements 1.4**

- [x] 2. Semantic model registration
  - [x] 2.1 Add `AssociatedTypeSlotType` to the semantic model
    - Create `AssociatedTypeSlotType` class implementing `IType` in `Source/Mist/Semantics/Model/`
    - Add `TypeKind.AssociatedTypeSlot` to the `TypeKind` enum
    - Add `BindingKind.AssociatedTypeSlot` to the `BindingKind` enum
    - Include properties: `DisplayName`, `DeclaringType`, `Constraints`, `TypeParameters`
    - _Requirements: 2.1, 2.2, 2.3, 2.4_

  - [x] 2.2 Implement `RegisterAssociatedTypeSlotDeclaration` in `SemanticModelBuilder.cs`
    - Add registration logic during Pass 1 interface/trait member processing
    - Create `AssociatedTypeSlotType` instances and register in the declaring type's member scope
    - Resolve constraint lists using existing `ResolveConstraintClause` infrastructure
    - Handle generic type parameters on slots (resolve where-clause constraints)
    - Resolve constraint references to enclosing type's type parameters
    - _Requirements: 2.1, 2.2, 2.3, 2.4, 2.5, 2.6_

  - [x] 2.3 Implement duplicate slot name detection (MIST0038)
    - After registering all slots in an interface/trait, check for duplicate names
    - Emit `MIST0038` diagnostic pointing to the second (duplicate) declaration
    - _Requirements: 2.7_

  - [x] 2.4 Implement modifier validation (MIST0039) and wrong-context validation (MIST_AT_0006 (historical))
    - Emit `MIST0039` if modifiers precede an associated type slot declaration
    - Emit `MIST_AT_0006 (historical)` if an abstract slot declaration appears in a class or struct body
    - _Requirements: 1.6, 1.8_

  - [x] 2.5 Write property test: Semantic registration records correct constraints (Property 5)
    - **Property 5: Semantic registration records correct constraints**
    - Create `Test/Mist.Tests/Semantics/AssociatedTypeSemanticPropertyTests.cs`
    - Generate interfaces with random slots (varying constraint lists: struct, class, interface refs, new(), combinations)
    - Compile in-process via shared `CompilerDriver`, inspect semantic model
    - Verify each slot is discoverable and its `TypeParameterConstraints` matches declared constraints
    - Zero process spawns
    - **Validates: Requirements 2.1, 2.2, 2.3, 2.4**

  - [x] 2.6 Write property tests: Diagnostics for duplicate, modifiers, and wrong context (Properties 3, 4, 6)
    - **Property 3: Abstract slot in class/struct emits diagnostic**
    - **Property 4: Modifiers on associated type slot emit diagnostic**
    - **Property 6: Duplicate slot names emit diagnostic**
    - Create `Test/Mist.Tests/Analysis/AssociatedTypeErrorPropertyTests.cs`
    - Generate invalid programs for each scenario, compile in-process, verify correct diagnostics
    - Zero process spawns
    - **Validates: Requirements 1.6, 1.8, 2.7**

- [x] 3. Checkpoint
  - Ensure all tests pass, ask the user if questions arise.

- [x] 4. Type checker — implementor obligations
  - [x] 4.1 Extend `Pass1_ValidateInterfaceSatisfaction` to check associated type slot obligations
    - For each interface implemented by a class/struct, collect all `AssociatedTypeSlotType` members
    - Look up matching concrete type members in the implementor's scope (by name and arity)
    - Emit `MIST_AT_0002 (historical)` for missing concrete type members
    - _Requirements: 3.1, 3.2, 3.9_

  - [x] 4.2 Implement constraint satisfaction validation for concrete type members
    - Verify `struct` kind constraint (MIST_AT_0003 (historical) if not a struct)
    - Verify `class` kind constraint (MIST_AT_0003 (historical) if not a class)
    - Verify interface/trait constraint references (MIST_AT_0003 (historical) if not implemented)
    - Verify `new()` constructor constraint (MIST_AT_0003 (historical) if no public parameterless constructor)
    - _Requirements: 3.3, 3.4, 3.5, 3.6, 3.7, 3.8_

  - [x] 4.3 Extend signature matching to handle associated type slot references in interface method signatures
    - When validating implementor method signatures against interface, substitute associated type slot names with concrete type members
    - Emit diagnostic if implementor method signature does not use the concrete type where the interface uses the slot name
    - _Requirements: 4.1, 4.2, 4.3, 4.4_

  - [x] 4.4 Write property test: Implementor obligation enforcement (Property 7)
    - **Property 7: Implementor obligation enforcement**
    - Create `Test/Mist.Tests/Analysis/AssociatedTypeObligationPropertyTests.cs`
    - Generate interface/implementor pairs with varying satisfaction (0 to N slots, some missing, some unsatisfied)
    - Compile in-process, verify exact diagnostic counts and IDs
    - Zero process spawns
    - **Validates: Requirements 3.1, 3.2, 3.3, 3.4, 3.5, 3.6, 3.7, 3.8, 3.9, 8.1, 8.2**

  - [x] 4.5 Write property test: Implementor signature substitution (Property 8)
    - **Property 8: Implementor signature substitution**
    - Create `Test/Mist.Tests/Analysis/AssociatedTypeSignaturePropertyTests.cs`
    - Generate interfaces with methods referencing slot names, implementors with correct/incorrect substitutions
    - Compile in-process, verify acceptance or diagnostic
    - Zero process spawns
    - **Validates: Requirements 4.1, 4.2, 4.3, 4.4**

- [x] 5. Type checker — generic context resolution
  - [x] 5.1 Implement `T.SlotName` resolution in generic contexts
    - When a type parameter `T` is constrained by an interface with associated types, resolve `T.SlotName` through the constraint's member scope
    - Return the `AssociatedTypeSlotType` carrying constraint information
    - Allow member access and method calls on values of type `T.SlotName` validated against the slot's constraints
    - _Requirements: 5.1, 5.2, 5.3_

  - [x] 5.2 Implement invalid associated type reference diagnostic (MIST_AT_0004 (historical))
    - When `T.X` is used in a type position and `X` is not an associated type slot of any constraining interface/trait, emit `MIST_AT_0004 (historical)`
    - _Requirements: 5.4, 8.3_

  - [x] 5.3 Write property test: Generic context resolution (Property 9)
    - **Property 9: Generic context resolution of associated types**
    - Create `Test/Mist.Tests/Analysis/AssociatedTypeGenericContextPropertyTests.cs`
    - Generate generic functions with constrained type parameters and slot references in various type positions
    - Compile in-process, verify acceptance
    - Zero process spawns
    - **Validates: Requirements 5.1, 5.2, 5.3**

  - [x] 5.4 Write property test: Invalid associated type reference emits diagnostic (Property 10)
    - **Property 10: Invalid associated type reference emits diagnostic**
    - Create `Test/Mist.Tests/Analysis/AssociatedTypeUnresolvedRefPropertyTests.cs`
    - Generate generic type parameters constrained by interfaces, reference non-existent slot names
    - Compile in-process, verify `MIST_AT_0004 (historical)` is emitted
    - Zero process spawns
    - **Validates: Requirements 5.4, 8.3**

- [x] 6. Checkpoint
  - Ensure all tests pass, ask the user if questions arise.

- [x] 7. C# emitter — generic lifting strategy
  - [x] 7.1 Implement interface/trait emission with generic lifting
    - Skip associated type slot declarations in emitted C# interface body
    - Add synthesized type parameter (prefixed `T` + slot name) to the interface's type parameter list
    - Emit corresponding `where` constraints from the slot's constraint list
    - _Requirements: 6.1, 6.3_

  - [x] 7.2 Implement implementor emission with concrete type substitution
    - When emitting the base list for a class/struct implementing an interface with associated types, substitute the concrete type as a type argument
    - Emit concrete nested types as standard C# nested type declarations
    - _Requirements: 6.2, 6.4, 6.6_

  - [x] 7.3 Implement generic context emission (generic functions constrained by interfaces with associated types)
    - Add synthesized type parameters to the function's generic parameter list
    - Propagate constraints from the associated type slot
    - Resolve `T.SlotName` references to the synthesized type parameter name in emitted code
    - _Requirements: 6.5_

  - [x] 7.4 Write property test: End-to-end emission correctness (Property 11)
    - **Property 11: End-to-end emission correctness**
    - Create `Test/Mist.Tests/Integration/AssociatedTypeEmissionPropertyTests.cs`
    - Generate valid Mist programs with interfaces, implementors, and generic functions using associated types
    - Compile via shared `CompilerDriver`, run once (batched), verify output matches expected semantics
    - 1 process spawn per test run (batched approach)
    - **Validates: Requirements 6.1, 6.2, 6.3, 6.4, 6.5, 6.6**

- [x] 8. TextMate grammar update
  - [x] 8.1 Add associated type slot pattern to `Editor/mist-syntax-highlighting/syntaxes/mist.tmLanguage.json`
    - Add pattern matching `type` followed by identifier and `:` (without `=`) in interface/trait body
    - Highlight `type` as `keyword.declaration.mist`
    - Highlight the identifier as `entity.name.type.alias.mist`
    - Highlight constraint keywords (`struct`, `class`) as `storage.modifier.mist`
    - Highlight interface references in constraint list as `entity.name.type.mist`
    - _Requirements: 9.1, 9.2, 9.3, 9.4_

- [x] 9. Sample application and AllFeatures integration
  - [x] 9.1 Create `samples/AssociatedTypes/Program.mist`
    - Demonstrate all syntax variants: single interface constraint, trait slot, `struct` kind constraint, `class` kind constraint, multiple constraints, generic type parameters with where clauses, unconstrained slot
    - Demonstrate all usage patterns: slot as return type, slot as parameter type, implementor with nested struct, implementor with nested class, generic function resolving associated type statically, interaction with existing generic constraints
    - Print labeled section headers (`--- Pattern Name ---`) followed by result values for each pattern
    - Include `public static void RunDemo()` entry point
    - _Requirements: 10.1, 10.2, 10.3, 10.5_

  - [x] 9.2 Update `samples/AllFeatures/Program.mist` to call `AssociatedTypes.Program.RunDemo()`
    - Add the call to the AllFeatures orchestrator
    - _Requirements: 10.4_

- [x] 10. Language book documentation
  - [x] 10.1 Create `book/src/type-system/associated-types.md`
    - Write subsections: declaration syntax, implementor satisfaction, generic context resolution, design rationale (zero-allocation iteration)
    - Include worked example: `Iterable<TValue>` interface, `IEnumerator<TValue>` constraint, concrete struct implementor, generic iteration function
    - Include cross-reference to Generic Constraints section
    - _Requirements: 11.1, 11.2, 11.3, 11.6_

  - [x] 10.2 Update `book/src/SUMMARY.md` to include associated types section after "Generic Constraints"
    - Add entry in the Type System chapter listing
    - _Requirements: 11.5_

  - [x] 10.3 Update `book/src/basics/control-flow.md` foreach loop note
    - Add note that the intended design is to back foreach using `Iterable<TValue>` and `IEnumerator<TValue>` with associated types
    - _Requirements: 11.4_

- [x] 11. Final checkpoint
  - Ensure all tests pass, ask the user if questions arise.

## Notes

- Tasks marked with `*` are optional and can be skipped for faster MVP
- Each task references specific requirements for traceability
- Checkpoints ensure incremental validation
- Property tests validate universal correctness properties from the design document
- All property tests use in-process compilation via shared `CompilerDriver` (zero process spawns) except Property 11 which uses the batched approach (1 process spawn per test run)
- The implementation language is C# (the Mist compiler is a .NET project)
- FsCheck is the property-based testing framework (already in use)

## Task Dependency Graph

```json
{
  "waves": [
    { "id": 0, "tasks": ["1.1"] },
    { "id": 1, "tasks": ["1.2", "2.1"] },
    { "id": 2, "tasks": ["1.3", "1.4", "2.2"] },
    { "id": 3, "tasks": ["2.3", "2.4"] },
    { "id": 4, "tasks": ["2.5", "2.6", "4.1"] },
    { "id": 5, "tasks": ["4.2", "4.3"] },
    { "id": 6, "tasks": ["4.4", "4.5", "5.1"] },
    { "id": 7, "tasks": ["5.2"] },
    { "id": 8, "tasks": ["5.3", "5.4", "7.1"] },
    { "id": 9, "tasks": ["7.2", "7.3"] },
    { "id": 10, "tasks": ["7.4", "8.1"] },
    { "id": 11, "tasks": ["9.1", "10.1", "10.2", "10.3"] },
    { "id": 12, "tasks": ["9.2"] }
  ]
}
```
