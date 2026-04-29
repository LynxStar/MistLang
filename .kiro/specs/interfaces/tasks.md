# Implementation Plan: Interfaces

## Overview

Add interface types to the Mist programming language compiler. The implementation proceeds bottom-up through the compiler pipeline: grammar and keywords first, then parsing and pretty-printing, semantic model construction (type registration, resolution, cycle detection, satisfaction validation), type checker extensions, C# emitter extensions, syntax highlighting, documentation, and a sample application. Each task builds on the previous ones so that the compiler remains buildable at every step.

## Tasks

- [x] 1. Add `INTERFACE` keyword and grammar rules
  - [x] 1.1 Add `INTERFACE` token to `keywords.storm` and grammar rules to `Mist.storm`
    - Add `token INTERFACE = "interface";` to `Source/Mist/Grammar/keywords.storm`
    - Add `InterfaceDeclaration` rule with name, optional interface list, left brace, zero or more `InterfaceMethodDeclaration`, right brace
    - Add `InterfaceMethodDeclaration` rule with return type, name, parameter list, semicolon
    - Add `InterfaceType` variant to the `TypeDeclaration` rule alongside `ClassType` and `StructType`
    - Extend `ClassDeclaration` base clause to support comma-separated interface names after the base class (e.g., `class Dog : Animal, IWalkable, IFeedable`)
    - Extend `StructDeclaration` to include an optional interface list (e.g., `struct Point : IComparable`)
    - _Requirements: 1.1, 1.2, 1.3, 1.4, 2.1, 2.2, 2.3, 2.4_

  - [x] 1.2 Verify the project builds with the new grammar
    - Run `dotnet build` to confirm the Storm source generator produces the new AST node types (`InterfaceDeclarationNode`, `InterfaceMethodDeclarationNode`, `InterfaceTypeTypeDeclarationNode`)
    - _Requirements: 1.5, 1.6, 2.5, 2.6_

- [x] 2. Add `InterfaceType` to the semantic model and extend `ClassType`/`StructType`
  - [x] 2.1 Create `InterfaceType` class and extend type system enums
    - Create `Source/Mist/Semantics/Model/Types/InterfaceType.cs` implementing `IType` with `DisplayName`, `Kind => TypeKind.Interface`, `MemberScope`, `ContainingNamespace`, and `Interfaces` property (`IReadOnlyList<InterfaceType>`)
    - Add `Interface` variant to `TypeKind` enum in `Source/Mist/Semantics/Contracts/IType.cs`
    - Add `Interface` variant to `SymbolKind` enum in `Source/Mist/Semantics/Contracts/ISymbol.cs`
    - Add `Interfaces` property (`IReadOnlyList<InterfaceType>`) to `ClassType` and `StructType`
    - _Requirements: 4.1, 4.4_

  - [x] 2.2 Register interface types in `SemanticModelBuilder` Pass 1
    - Add `InterfaceTypeTypeDeclarationNode` case to `RegisterTypeDeclaration`
    - Implement `RegisterInterfaceDeclaration` to create `InterfaceType`, register `SymbolKind.Interface` symbol, and register each `InterfaceMethodDeclaration` as `SymbolKind.Method` in the interface's member scope
    - _Requirements: 4.1, 4.2, 4.3_

  - [x] 2.3 Write property test for interface type registration
    - **Property 2: Interface type registration**
    - **Validates: Requirements 4.1, 4.2, 4.3, 4.4**

- [x] 3. Implement interface resolution and cycle detection in `SemanticModelBuilder`
  - [x] 3.1 Implement Pass 1b extension: resolve interface lists on all types
    - After existing `Pass1_ResolveBaseTypes`, add `Pass1_ResolveInterfaceLists` to resolve interface names on classes, structs, and interfaces
    - For classes: parse the base clause to separate base class from interface names; resolve each interface name to `InterfaceType`; store on `ClassType.Interfaces`
    - For structs: resolve each interface name to `InterfaceType`; store on `StructType.Interfaces`
    - For interfaces: resolve each interface name to `InterfaceType`; store on `InterfaceType.Interfaces`
    - Emit `MIST_UNDEFINED_INTERFACE` when an interface name is not found
    - Emit `MIST_NOT_AN_INTERFACE` when a name resolves to a class or struct
    - _Requirements: 5.1, 5.2, 5.3, 5.4, 5.5_

  - [x] 3.2 Write property test for interface resolution
    - **Property 3: Interface resolution on implementing types**
    - **Validates: Requirements 5.1, 5.4, 5.5**

  - [x] 3.3 Write property test for invalid interface reference diagnostics
    - **Property 4: Invalid interface reference diagnostic**
    - **Validates: Requirements 5.2, 5.3**

  - [x] 3.4 Implement Pass 1c extension: detect interface inheritance cycles
    - Add `Pass1_DetectInterfaceCycles` after interface list resolution
    - Walk each interface's `Interfaces` chain using a visited set
    - Emit `MIST_CIRCULAR_INTERFACE_INHERITANCE` when a cycle is detected
    - Break cycles by removing the back-edge from the `Interfaces` list
    - _Requirements: 6.1, 6.2, 6.3_

  - [x] 3.5 Write property test for interface cycle detection
    - **Property 5: Interface inheritance cycle detection**
    - **Validates: Requirements 6.1, 6.2, 6.3**

  - [x] 3.6 Implement Pass 1d: validate interface satisfaction
    - Add `Pass1_ValidateInterfaceSatisfaction` after cycle detection
    - For each class and struct implementing interfaces, collect all required methods transitively (using visited set for diamond inheritance)
    - For each required method, search the implementing type's member scope (and base type chain for classes) for a matching method (name, return type, parameter types)
    - Emit `MIST_MISSING_INTERFACE_METHOD` for each unmatched method
    - Emit `MIST_INTERFACE_METHOD_SIGNATURE_MISMATCH` when a method name matches but signature differs
    - _Requirements: 7.1, 7.2, 7.3, 7.4, 7.5_

  - [x] 3.7 Write property test for interface satisfaction
    - **Property 6: Interface satisfaction**
    - **Validates: Requirements 7.1, 7.2, 7.3, 7.4**

  - [x] 3.8 Write property test for interface method signature mismatch
    - **Property 7: Interface method signature mismatch diagnostic**
    - **Validates: Requirements 7.5**

- [x] 4. Checkpoint - Ensure all tests pass
  - Ensure all tests pass, ask the user if questions arise.

- [x] 5. Extend Pass 2 type resolution and type checker for interface assignability
  - [x] 5.1 Extend Pass 2 to resolve `InterfaceType` references
    - In `SemanticModelBuilder` Pass 2, extend type reference resolution to handle `SymbolKind.Interface` symbols, returning their `InterfaceType`
    - Ensure `InterfaceType` is handled in `FindDeclaringType` and scope lookups
    - _Requirements: 5.1, 5.5_

  - [x] 5.2 Extend `TypeCheckerAnalyzer.IsAssignable` for interface subtype relationships
    - Add interface assignability logic: when target is `InterfaceType`, check if source implements it (directly or transitively)
    - Implement `ImplementsInterface` helper that checks `ClassType.Interfaces` (including base type chain), `StructType.Interfaces`, and `InterfaceType.Interfaces` recursively
    - _Requirements: 8.1, 8.2, 8.3, 8.4, 8.5_

  - [x] 5.3 Write property test for interface subtype assignability
    - **Property 8: Interface subtype assignability**
    - **Validates: Requirements 8.1, 8.3, 8.4, 8.5**

  - [x] 5.4 Write property test for non-implementing type not assignable to interface
    - **Property 9: Non-implementing type is not assignable to interface**
    - **Validates: Requirements 8.2**

- [x] 6. Extend pretty-printer for interface declarations and interface lists
  - [x] 6.1 Add interface support to `MistPrettyPrinter`
    - Add `InterfaceTypeTypeDeclarationNode` case to `EmitTypeDeclaration`
    - Implement `EmitInterfaceDeclaration` to format `interface IName { ... }` with proper indentation
    - Implement `EmitInterfaceMethodDeclaration` to format `returnType methodName(parameters);`
    - Extend `EmitClassDeclaration` to format interface lists (with or without base class): `class Name : Base, IFoo, IBar {` or `class Name : IFoo, IBar {`
    - Extend `EmitStructDeclaration` to format interface lists: `struct Name : IFoo {`
    - Format interface-to-interface inheritance: `interface IBar : IFoo {`
    - _Requirements: 3.1, 3.2, 3.3, 3.4, 3.5, 3.6_

  - [x] 6.2 Write property test for interface declaration round-trip
    - **Property 1: Interface declaration round-trip**
    - **Validates: Requirements 1.2, 1.3, 1.5, 1.6, 2.1, 2.2, 2.3, 2.4, 2.5, 2.6, 3.1, 3.2, 3.3, 3.4, 3.5, 3.6, 3.7**

- [x] 7. Extend C# emitter for interface code generation
  - [x] 7.1 Add interface emission to `CSharpEmitter` / `CSharpSourceWriter`
    - Add `InterfaceTypeTypeDeclarationNode` case to `EmitTypeDeclaration`
    - Implement `EmitInterfaceDeclaration` to emit C# `interface IName { ... }` with optional inherited interface list
    - Implement `EmitInterfaceMethodDeclaration` to emit C# interface method signatures: `returnType MethodName(parameters);`
    - Extend `EmitClassDeclaration` to include interface lists in the inheritance clause (with or without base class)
    - Extend `EmitStructDeclaration` to include interface lists
    - _Requirements: 9.1, 9.2, 9.3, 9.4, 9.5, 9.6_

  - [x] 7.2 Extend `CSharpTypeMap` to handle `InterfaceType`
    - Add `InterfaceType i => i.DisplayName` case to `GetTypeName`
    - _Requirements: 9.1_

  - [x] 7.3 Write property test for interface C# emission correctness
    - **Property 10: Interface C# emission correctness**
    - **Validates: Requirements 9.1, 9.2, 9.3, 9.4, 9.5, 9.6**

- [x] 8. Checkpoint - Ensure all tests pass
  - Ensure all tests pass, ask the user if questions arise.

- [x] 9. Update syntax highlighting and documentation
  - [x] 9.1 Update TextMate grammar for `interface` keyword
    - Add `interface` to the `keyword.declaration.mist` pattern in `Editor/mist-syntax-highlighting/syntaxes/mist.tmLanguage.json`
    - Add a `declaration-names` pattern matching `interface` followed by an identifier with scope `entity.name.type.interface.mist`
    - _Requirements: 12.1, 12.2, 12.3, 12.4_

  - [x] 9.2 Update `CSHARP_FEATURES.md`
    - Mark `Interfaces` as ✅ Implemented in the `Type Declarations` section
    - Mark `Interface implementation (: IFoo)` and `Multiple interface implementation` as ✅ Implemented in the `Inheritance & Polymorphism` section
    - Update the `Summary` table counts to reflect the new implementations
    - _Requirements: 10.1, 10.2, 10.3_

- [x] 10. Create interface sample application
  - [x] 10.1 Create `samples/Interfaces/` with Mist source files
    - Create sample files demonstrating: interface with multiple methods, class implementing single interface, class implementing multiple interfaces, interface-to-interface inheritance, class inheriting from base class and implementing interfaces (with some methods satisfied by base class), assigning implementing type to interface-typed variable and calling methods through interface reference
    - Include a `static void Main` entry point that exercises all features and prints output via `Console.WriteLine`
    - _Requirements: 11.1, 11.2, 11.3, 11.4, 11.5, 11.6, 11.7, 11.8, 11.9_

- [x] 11. Final checkpoint - Ensure all tests pass
  - Ensure all tests pass, ask the user if questions arise.

## Notes

- Tasks marked with `*` are optional and can be skipped for faster MVP
- Each task references specific requirements for traceability
- Checkpoints ensure incremental validation
- Property tests validate universal correctness properties from the design document
- The implementation language is C#, matching the existing codebase and design document
- The Storm source generator auto-generates AST node types from grammar changes — a successful build after task 1 confirms the new nodes are available
