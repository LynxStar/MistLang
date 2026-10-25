# Implementation Plan: Traits

## Overview

Add traits — a structural typing mechanism — to the Mist programming language compiler. The implementation proceeds bottom-up through the compiler pipeline: grammar and keywords first, then parsing and pretty-printing, semantic model construction (trait type registration, `with` block resolution, structural match computation, trait satisfaction), type checker extensions, C# emitter extensions (lowering traits to interfaces with explicit implementations), syntax highlighting, documentation, and a sample application. Each task builds on the previous ones so that the compiler remains buildable at every step.

## Tasks

- [x] 1. Add `TRAIT`, `WITH`, `AS` keywords and grammar rules
  - [x] 1.1 Add keyword tokens and grammar rules to `keywords.storm` and `Mist.storm`
    - Add `token TRAIT = "trait";` to `Source/Mist/Grammar/keywords.storm`
    - Add `token WITH = "with";` to `Source/Mist/Grammar/keywords.storm`
    - Add `token AS = "as";` to `Source/Mist/Grammar/keywords.storm`
    - Add `TraitDeclaration` rule with `TRAIT`, name identifier, left brace, zero or more `TraitMemberDeclaration`, right brace
    - Add `TraitMemberDeclaration` rule with variants: `TraitFieldMember`, `TraitMethodMember`, `TraitPropertyMember`
    - Add `TraitFieldDeclaration` rule with type reference, name identifier, semicolon
    - Add `TraitMethodDeclaration` rule with return type, name identifier, parameter list, semicolon
    - Add `TraitPropertyDeclaration` rule with type reference, name identifier, accessor declarations (reuse `InterfaceAccessorDeclaration`)
    - Add `TraitType` variant to the `TypeDeclaration` rule
    - Add `WithDeclaration` rule with `WITH`, type name identifier, colon, trait name identifier, left brace, one or more `MemberMapping`, right brace
    - Add `MemberMapping` rule with source name identifier, `AS`, target name identifier, semicolon
    - Add `WithItem` variant to the `TopLevelItem` rule
    - _Requirements: 1.1, 1.2, 1.3, 1.4, 1.5, 1.6, 2.1, 2.2, 2.3, 2.4_

  - [x] 1.2 Verify the project builds with the new grammar
    - Run `dotnet build` to confirm the Storm source generator produces the new AST node types (`TraitDeclarationNode`, `TraitFieldDeclarationNode`, `TraitMethodDeclarationNode`, `TraitPropertyDeclarationNode`, `TraitTypeTypeDeclarationNode`, `WithDeclarationNode`, `MemberMappingNode`, `WithItemTopLevelItemNode`)
    - _Requirements: 1.7, 1.8, 2.5, 2.6_

- [x] 2. Add pretty-printer support for traits and `with` blocks
  - [x] 2.1 Add trait and `with` block formatting to `MistPrettyPrinter`
    - Add `TraitTypeTypeDeclarationNode` case to `EmitTypeDeclaration`
    - Implement `EmitTraitDeclaration` to format `trait Name { ... }` with proper indentation
    - Implement `EmitTraitFieldDeclaration` to format `type name;`
    - Implement `EmitTraitMethodDeclaration` to format `returnType name(parameters);`
    - Implement `EmitTraitPropertyDeclaration` to format `type name { get; set; }` (or applicable accessor combination)
    - Add `WithItemTopLevelItemNode` case to the top-level item loop in `EmitCompilationUnit`
    - Implement `EmitWithDeclaration` to format `with TypeName : TraitName { ... }`
    - Implement `EmitMemberMapping` to format `sourceName as targetName;`
    - _Requirements: 3.1, 3.2, 3.3, 3.4, 3.5, 3.6_

  - [x] 2.2 Write property test for trait and with-block round-trip
    - **Property 1: Trait and with-block declaration round-trip**
    - **Validates: Requirements 1.2, 1.3, 1.4, 1.5, 1.6, 1.7, 1.8, 2.2, 2.3, 2.4, 2.5, 2.6, 3.1, 3.2, 3.3, 3.4, 3.5, 3.6, 3.7**

- [x] 3. Add `TraitType` to the semantic model
  - [x] 3.1 Create `TraitType` class and extend type system enums
    - Create `Source/Mist/Semantics/Model/Types/TraitType.cs` implementing `IType` with `DisplayName`, `Kind => TypeKind.Trait`, `MemberScope`, `ContainingNamespace`
    - Add `Trait` variant to `TypeKind` enum in `Source/Mist/Semantics/Contracts/IType.cs`
    - Add `Trait` variant to `SymbolKind` enum in `Source/Mist/Semantics/Contracts/ISymbol.cs`
    - _Requirements: 4.1, 4.4, 4.5_

  - [x] 3.2 Register trait types in `SemanticModelBuilder` Pass 1
    - Add `TraitTypeTypeDeclarationNode` case to `RegisterTypeDeclaration`
    - Implement `RegisterTraitDeclaration` to create `TraitType`, register `SymbolKind.Trait` symbol, and register each trait member: `SymbolKind.Field` for field members, `SymbolKind.Method` for method members, `SymbolKind.Property` for property members
    - Add `WithItemTopLevelItemNode` handling in `Pass1_RegisterTypes` to collect `WithDeclaration` nodes for later processing
    - _Requirements: 4.1, 4.2, 4.3_

  - [x] 3.3 Write property test for trait type registration
    - **Property 2: Trait type registration**
    - **Validates: Requirements 4.1, 4.2, 4.3**

- [x] 4. Implement `with` block resolution and validation
  - [x] 4.1 Implement `Pass1_ResolveWithBlocks` sub-pass
    - Add new sub-pass after all types are registered and interface lists are resolved
    - For each `WithDeclaration`: resolve type name to `ClassType`/`StructType`, resolve trait name to `TraitType`
    - Emit `MIST_TRAIT_WITH_UNDEFINED_TYPE (historical)` when type name is not found
    - Emit `MIST_TRAIT_WITH_UNDEFINED_TRAIT (historical)` when trait name is not found
    - Emit `MIST_TRAIT_DUPLICATE_WITH (historical)` for duplicate type-trait pairs
    - For each `MemberMapping`: validate source member exists as public on the type, validate target member exists in the trait, validate type compatibility
    - Emit `MIST_TRAIT_WITH_UNDEFINED_SOURCE (historical)`, `MIST_TRAIT_WITH_UNDEFINED_TARGET (historical)`, `MIST_TRAIT_WITH_TYPE_MISMATCH (historical)` as appropriate
    - Store validated mappings in a `WithBlockRegistry` for use by the satisfaction pass
    - _Requirements: 6.1, 6.2, 6.3, 6.4, 6.5, 6.6, 6.9_

  - [x] 4.2 Write property test for with-block validation diagnostics
    - **Property 4: With-block validation diagnostics**
    - **Validates: Requirements 6.2, 6.3, 6.4, 6.5, 6.6, 6.9, 10.2, 10.3, 10.4**

- [x] 5. Implement structural match resolution and trait satisfaction
  - [x] 5.1 Implement `Pass1_ResolveTraitSatisfaction` sub-pass
    - Add new sub-pass after `with` block resolution
    - Create `TraitSatisfactionMap` and `TraitSatisfaction` data structures
    - For each trait and each class/struct: compute structural match by comparing trait members against type's public members
    - Field matching: accept public fields or public properties with compatible type
    - Method matching: require same name, return type, and parameter types
    - Property matching: require same name, compatible type, and at least the trait's accessors
    - Exclude private members from matching
    - Emit `MIST_TRAIT_MEMBER_VISIBILITY (historical)` when a private member matches a trait member name
    - Combine structural matches with `with` block mappings
    - Emit `MIST_TRAIT_WITH_UNSATISFIED (historical)` when a `with` block doesn't cover all unmapped members
    - Register complete satisfactions in the `TraitSatisfactionMap`
    - Store the `TraitSatisfactionMap` on the `SemanticModel` and expose via `ISemanticModel`
    - _Requirements: 5.1, 5.2, 5.3, 5.4, 5.5, 5.6, 5.7, 6.7, 6.8, 10.5_

  - [x] 5.2 Write property test for structural match correctness
    - **Property 3: Structural match correctness**
    - **Validates: Requirements 5.1, 5.2, 5.3, 5.4, 5.5, 5.6, 5.7**

  - [x] 5.3 Write property test for with-block satisfaction
    - **Property 5: Trait satisfaction via with-block**
    - **Validates: Requirements 6.1, 6.7, 6.8**

- [x] 6. Checkpoint — Ensure all tests pass
  - Run `dotnet test` and verify all existing and new tests pass. Ask the user if questions arise.

- [x] 7. Extend Pass 2 type resolution and type checker for trait assignability
  - [x] 7.1 Extend Pass 2 to resolve `TraitType` references
    - In `SemanticModelBuilder` Pass 2, extend type reference resolution to handle `SymbolKind.Trait` symbols, returning their `TraitType`
    - Ensure `TraitType` is handled in scope lookups and `FindDeclaringType`
    - _Requirements: 4.1_

  - [x] 7.2 Extend `LookupMemberType` to handle `TraitType`
    - Add `TraitType tt => tt.MemberScope` to the `memberScope` switch in `LookupMemberType`
    - This enables member access on trait-typed variables
    - _Requirements: 8.1, 8.2, 8.3_

  - [x] 7.3 Write property test for trait member access
    - **Property 7: Trait member access**
    - **Validates: Requirements 8.1, 8.2, 8.3**

  - [x] 7.4 Extend `TypeCheckerAnalyzer.IsAssignable` for trait assignability
    - Add trait assignability logic: when target is `TraitType`, check if source satisfies it via `TraitSatisfactionMap`
    - Implement `SatisfiesTrait` helper that queries the `TraitSatisfactionMap` on the semantic model
    - Extend `CheckAssignability` to emit `MIST_TRAIT_UNSATISFIED (historical)` diagnostic with trait name, type name, and unsatisfied member list when assignment fails
    - _Requirements: 7.1, 7.2, 7.3, 7.4, 7.5, 10.1_

  - [x] 7.5 Write property test for trait assignability
    - **Property 6: Trait assignability**
    - **Validates: Requirements 7.1, 7.2, 7.3, 7.4, 7.5**

- [x] 8. Extend C# emitter for trait code generation
  - [x] 8.1 Add trait emission to `CSharpSourceWriter`
    - Add `TraitTypeTypeDeclarationNode` case to `EmitTypeDeclaration`
    - Implement `EmitTraitDeclaration` to emit C# interface: field members as properties with getters (`int X { get; }`), method members as method signatures, property members as property signatures with matching accessors
    - Extend `CSharpTypeMap.GetTypeName` to handle `TraitType`
    - Extend `GetTypeContainingNamespace` to handle `TraitType`
    - Extend `CollectBackendNamespaces` to handle `TraitType`
    - _Requirements: 9.1, 9.2, 9.3, 9.4_

  - [x] 8.2 Add trait interface to satisfying types' inheritance lists
    - Extend `EmitClassDeclaration` to consult `TraitSatisfactionMap` and append trait interface names to the inheritance list
    - Extend `EmitStructDeclaration` similarly
    - _Requirements: 9.5_

  - [x] 8.3 Generate explicit interface implementations for `with` mappings
    - For each `with`-mapped member on a satisfying type, emit explicit interface implementations inside the class/struct body
    - Field/property mappings: `int ITraitName.x => this.a;`
    - Method mappings: `int ITraitName.calculate(int a) => this.compute(a);`
    - Only emit explicit implementations for `with`-mapped members, not structurally matched ones
    - _Requirements: 9.6, 9.7_

  - [x] 8.4 Write property test for trait C# emission correctness
    - **Property 8: Trait C# emission correctness**
    - **Validates: Requirements 9.1, 9.2, 9.3, 9.4, 9.5, 9.6, 9.7**

- [x] 9. Write property test for trait and interface coexistence
  - [x] 9.1 Write property test for coexistence
    - **Property 9: Trait and interface coexistence**
    - **Validates: Requirements 11.1, 11.2, 11.3**

- [x] 10. Checkpoint — Ensure all tests pass
  - Run `dotnet test` and verify all existing and new tests pass. Ask the user if questions arise.

- [x] 11. Update syntax highlighting and documentation
  - [x] 11.1 Update TextMate grammar for `trait`, `with`, and `as` keywords
    - Add `trait` to the `keyword.declaration.mist` pattern in `Editor/mist-syntax-highlighting/syntaxes/mist.tmLanguage.json` alongside `class`, `struct`, and `interface`
    - Add a `declaration-names` pattern matching `trait` followed by an identifier with scope `entity.name.type.trait.mist`
    - Add `with` and `as` to keyword patterns so they are highlighted as keywords
    - _Requirements: 12.1, 12.2, 12.3, 12.4_

  - [x] 11.2 Update `CSHARP_FEATURES.md`
    - Add `Traits (structural typing)` entry marked as ✅ Implemented in the `Type Declarations` section
    - Add a `Structural Typing` section documenting trait declarations, structural matching, and `with` blocks
    - Update the `Summary` table counts to reflect the new implementations
    - _Requirements: 13.1, 13.2, 13.3_

- [x] 12. Create trait sample application
  - [x] 12.1 Create `samples/Traits/` with Mist source files
    - Demonstrate declaring a trait with field members
    - Demonstrate a type that structurally satisfies a trait without any explicit declaration
    - Demonstrate a `with` block that maps a type's members to a trait when the names differ
    - Demonstrate passing both structurally matched and `with`-mapped types to a function that accepts a trait-typed parameter
    - Demonstrate a compile-time error case (commented out) showing a type that does not satisfy the trait
    - Include a `static void Main` entry point that exercises all features and prints output via `Console.WriteLine`
    - _Requirements: 14.1, 14.2, 14.3, 14.4, 14.5, 14.6, 14.7, 14.8_

- [x] 13. Final checkpoint — Ensure all tests pass
  - Run `dotnet test` and verify all existing and new tests pass. Ask the user if questions arise.

## Notes

- Each task references specific requirements for traceability
- Checkpoints ensure incremental validation
- Property tests validate universal correctness properties from the design document
- The implementation language is C#, matching the existing codebase and design document
- The Storm source generator auto-generates AST node types from grammar changes — a successful build after task 1 confirms the new nodes are available
- Traits reuse the existing `InterfaceAccessorDeclaration` grammar rule for property accessor signatures to avoid duplication
- The `TraitSatisfactionMap` is the central data structure that connects the semantic model, type checker, and emitter — it is computed once and read by both downstream stages
- `with` blocks are top-level declarations, not nested inside type bodies, which allows them to be defined in separate files from the types they map
