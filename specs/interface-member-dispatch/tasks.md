# Implementation Plan: Interface Member Dispatch

## Overview

This plan implements end-to-end interface member dispatch in the Mist compiler. The work spans three areas: (1) fixing `LookupMemberType` in `SemanticModelBuilder` to resolve members on `InterfaceType` including transitive inherited interfaces, (2) fixing `EmitClassDeclaration` and `EmitStructDeclaration` in `CSharpEmitter` to emit full inheritance clauses using the semantic model, and (3) adding AllFeatures sample files that exercise interfaces. All code is C# targeting .NET, with FsCheck 3.1.0 + xUnit for property-based tests.

## Tasks

- [x] 1. Fix `LookupMemberType` to handle `InterfaceType`
  - [x] 1.1 Add `InterfaceType` case to the switch in `SemanticModelBuilder.LookupMemberType`
    - In `Source/Mist/Semantics/Model/SemanticModelBuilder.cs`, add `InterfaceType it => it.MemberScope` to the `targetType` switch (replacing the `_ => null` fallback that currently swallows `InterfaceType`)
    - After the existing `memberScope.Lookup(memberName)` block, add a fallback for `InterfaceType` that calls a new `LookupInheritedInterfaceMember` helper to walk inherited interfaces transitively
    - _Requirements: 1.1, 1.2, 1.3_

  - [x] 1.2 Add `LookupInheritedInterfaceMember` and `LookupInheritedInterfaceMemberCore` helper methods
    - Add two new `private static` methods to `SemanticModelBuilder`
    - `LookupInheritedInterfaceMember` initializes a `HashSet<InterfaceType>` with `ReferenceEqualityComparer.Instance` and delegates to the core method
    - `LookupInheritedInterfaceMemberCore` recursively walks `InterfaceType.Interfaces`, using the visited set to handle diamond inheritance and prevent infinite loops
    - Returns `member.DeclaredType ?? UnknownType.Instance` when found, `null` when not found
    - _Requirements: 1.2, 1.5, 6.1_

  - [x] 1.3 Write property test: Interface member lookup resolves correctly through hierarchy
    - **Property 1: Interface member lookup resolves correctly through hierarchy**
    - Create `Test/Mist.Tests/Semantics/InterfaceMemberLookupPropertyTests.cs`
    - Generate `InterfaceType` hierarchies (varying depth, width, diamond patterns) with random methods in `MemberScope`
    - For any member name that exists in the hierarchy, verify `LookupMemberType` returns the member's declared type and emits no `MIST_UNDEFINED_MEMBER` diagnostic
    - Minimum 100 iterations
    - **Validates: Requirements 1.1, 1.2, 1.3, 5.1, 6.1**

  - [x] 1.4 Write property test: Interface member lookup emits diagnostic for missing members
    - **Property 2: Interface member lookup emits diagnostic for missing members**
    - Create `Test/Mist.Tests/Semantics/InterfaceMemberLookupMissingPropertyTests.cs`
    - Generate `InterfaceType` hierarchies and member names that do NOT exist anywhere in the hierarchy
    - Verify `LookupMemberType` emits exactly one `MIST_UNDEFINED_MEMBER` diagnostic and returns `UnknownType`
    - Minimum 100 iterations
    - **Validates: Requirements 1.4**

- [x] 2. Checkpoint — Verify semantic model changes
  - Ensure all tests pass, ask the user if questions arise.

- [x] 3. Fix `EmitClassDeclaration` to emit full inheritance clause
  - [x] 3.1 Update `EmitClassDeclaration` in `CSharpEmitter.cs`
    - In `Source/Mist/Backends/CSharpTranspiler/CSharpEmitter.cs`, replace the AST-based `baseClause` logic with semantic model lookup
    - Look up the class symbol via `_model.GetDeclaredSymbol(classDecl)`, cast to `ClassType`
    - Build the inheritance clause: base class `DisplayName` (if `BaseType` is not null) followed by all `Interfaces` `DisplayName` values, comma-separated
    - Keep the existing AST-based approach as a fallback if the semantic model lookup fails
    - _Requirements: 2.1, 2.2, 2.3, 2.4_

  - [x] 3.2 Update `EmitStructDeclaration` in `CSharpEmitter.cs`
    - Replace the AST-based single `InterfaceName.Lexeme` logic with semantic model lookup
    - Look up the struct symbol via `_model.GetDeclaredSymbol(structDecl)`, cast to `StructType`
    - Build the interface list from `StructType.Interfaces` `DisplayName` values, comma-separated
    - Keep the existing AST-based approach as a fallback
    - _Requirements: 2.1, 2.4_

  - [x] 3.3 Write property test: Class C# emission includes complete inheritance clause
    - **Property 3: Class C# emission includes complete inheritance clause**
    - Create `Test/Mist.Tests/Backend/ClassInterfaceEmissionPropertyTests.cs`
    - Generate `ClassType` instances with random combinations of `BaseType` and `Interfaces`
    - Verify the emitted C# class declaration contains the correct inheritance clause: base class first (if present), then all interface names in order
    - When both `BaseType` is null and `Interfaces` is empty, verify no inheritance clause is emitted
    - Minimum 100 iterations
    - **Validates: Requirements 2.1, 2.2, 2.3, 2.4**

- [x] 4. Checkpoint — Verify emitter changes
  - Ensure all tests pass, ask the user if questions arise.

- [x] 5. Add AllFeatures sample files for interfaces
  - [x] 5.1 Create `samples/AllFeatures/IShape.mist`
    - Declare `IShape` interface with `string Describe()` and `int Area()` methods
    - Declare `IResizable` interface extending `IShape` with `int ResizedArea(int factor)` method
    - Use `int` types to match the existing AllFeatures conventions
    - _Requirements: 3.1_

  - [x] 5.2 Create `samples/AllFeatures/Circle.mist`
    - Declare `Circle` class implementing `IShape` in namespace `AllFeatures`
    - Constructor accepting an `int radius` parameter, stored in a private `_radius` field
    - Implement `Describe()` returning `$"Circle(radius={_radius})"` and `Area()` returning `_radius * _radius`
    - _Requirements: 3.2_

  - [x] 5.3 Create `samples/AllFeatures/Rectangle.mist`
    - Declare `Rectangle` class implementing `IResizable` in namespace `AllFeatures`
    - Constructor accepting `int width` and `int height`, stored in private fields
    - Implement `Describe()` returning `$"Rectangle(width={_width}, height={_height})"`, `Area()` returning `_width * _height`, and `ResizedArea(int factor)` returning `_width * factor * _height * factor`
    - _Requirements: 3.3_

  - [x] 5.4 Update `samples/AllFeatures/Program.mist` with section 25
    - Add section 25 (interfaces) after section 24 and before the final `"=== Done! ==="` line
    - Create `Circle` and `Rectangle` instances, call methods directly on concrete types
    - Assign to `IShape`-typed variables, call `Describe` and `Area` through interface references
    - Assign `Rectangle` to `IResizable`-typed variable, call `ResizedArea`
    - Call inherited interface member `Describe` through `IResizable` reference
    - Add a `private static string DescribeShape(IShape shape)` helper method that calls `shape.Describe()` and `shape.Area()`
    - Call `DescribeShape` with both `Circle` and `Rectangle` arguments
    - _Requirements: 4.1, 4.2, 4.3, 4.4, 4.5, 4.6, 5.2, 5.3, 6.2_

- [x] 6. Checkpoint — Verify AllFeatures compilation
  - Ensure all tests pass, ask the user if questions arise.

- [x] 7. Write integration tests for AllFeatures interface dispatch
  - Create or extend `Test/Mist.Tests/Integration/EndToEndCompilationTests.cs` with interface-specific tests
  - Test that compiling all `samples/AllFeatures/*.mist` files through the full pipeline produces zero error diagnostics
  - Test that the compiled output runs and produces expected stdout lines demonstrating interface dispatch (e.g., `circle=Circle(radius=`, `shape1=Circle(radius=`, `resizable desc=Rectangle(`)
  - _Requirements: 3.4, 4.5, 4.6_

- [x] 8. Final checkpoint — Ensure all tests pass
  - Ensure all tests pass, ask the user if questions arise.

## Notes

- Tasks marked with `*` are optional and can be skipped for faster MVP
- Each task references specific requirements for traceability
- Checkpoints ensure incremental validation after each major component change
- Property tests validate universal correctness properties from the design document
- The AllFeatures sample files use `int` types (not `double`) to match the existing sample conventions
- The `LookupInheritedInterfaceMember` helper uses the same visited-set pattern as `InterfaceListContains` in the type checker
