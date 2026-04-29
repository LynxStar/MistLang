# Implementation Plan: Class Inheritance

## Overview

Add single class inheritance to the Mist programming language. This involves changes across the entire compiler pipeline: grammar/lexer (new `base` keyword for constructor chaining, inheritance clause), parser/pretty-printer, semantic model builder (base type resolution, cycle detection, inherited member lookup, additive-only constraint), type checker (subtype assignability), and C# emitter (`: BaseClass`, `: base()` constructor chains). Implementation proceeds bottom-up from grammar through emission, with testing at each layer.

## Tasks

- [x] 1. Grammar and keyword changes
  - [x] 1.1 Add `base` keyword to `keywords.storm` and update `Mist.storm` grammar
    - Add `token BASE = "base";` to `keywords.storm`
    - Modify `ClassDeclaration` rule to include optional inheritance clause: `baseClass:(COLON baseClassName:IDENTIFIER)?` between the class name and `LEFT_BRACE`
    - Update `ConstructorChain` rule to support both `this` and `base` chaining: add `| COLON BASE LEFT_PARENTHESIS arguments:ArgumentList? RIGHT_PARENTHESIS as BaseChain` variant
    - Regenerate the Storm parser to produce updated AST node types (`BaseChainConstructorChainNode`, updated `ClassDeclarationNode`)
    - _Requirements: 1.3, 3.1_

- [x] 2. Semantic model: ClassType and base type resolution
  - [x] 2.1 Add `BaseType` property to `ClassType`
    - Add `public ClassType? BaseType { get; internal set; }` to `ClassType.cs`
    - Initialize `BaseType = null` in the constructor
    - _Requirements: 2.4_

  - [x] 2.2 Implement base type resolution in `SemanticModelBuilder` Pass 1
    - After all types are registered in `Pass1_RegisterTypes`, add a new step that iterates over all `ClassDeclarationNode` ASTs with a `baseClassName`
    - Look up the base class name in the parent scope
    - If not found, emit `MIST_UNDEFINED_BASE_TYPE` diagnostic
    - If found but is a `StructType`, emit `MIST_CANNOT_INHERIT_STRUCT` diagnostic
    - If found and is a `ClassType`, set `classType.BaseType = resolvedClassType`
    - Ensure forward references are supported (all types registered before resolution)
    - _Requirements: 2.1, 2.2, 2.3, 2.5_

  - [x] 2.3 Implement cycle detection in `SemanticModelBuilder`
    - After all base types are resolved, walk each class's `BaseType` chain
    - If a class is revisited (cycle detected, including self-inheritance), emit `MIST_CIRCULAR_INHERITANCE` diagnostic and set `BaseType = null` to break the cycle
    - _Requirements: 3.2, 3.3_

  - [x] 2.4 Write property test for base type resolution correctness
    - **Property 2: Base type resolution correctness**
    - Generate random class declaration sets with inheritance clauses (including forward references) → build semantic model → assert `BaseType` is correctly set or null
    - **Validates: Requirements 2.1, 2.4, 2.5**

  - [x] 2.5 Write property test for cycle detection
    - **Property 3: Cycle detection**
    - Generate random cyclic inheritance chains (A→B→A, A→A, A→B→C→A) → build semantic model → assert `MIST_CIRCULAR_INHERITANCE` diagnostic is emitted
    - **Validates: Requirements 3.2, 3.3**

- [x] 3. Checkpoint - Ensure all tests pass
  - Ensure all tests pass, ask the user if questions arise.

- [x] 4. Semantic model: inherited member lookup and additive-only constraint
  - [x] 4.1 Implement inherited member lookup in `SemanticModelBuilder` Pass 2
    - Update `LookupMemberType` (or the member access resolution logic) to walk the `BaseType` chain when a member is not found in the current class's `MemberScope`
    - If the member is not found in any class in the chain, emit the existing `MIST_UNDEFINED_MEMBER` diagnostic
    - Ensure `this.member` in a derived class constructor/method resolves inherited members through the chain
    - _Requirements: 4.1, 4.2, 4.5, 6.2_

  - [x] 4.2 Implement additive-only constraint (no member redeclaration)
    - During Pass 1 member registration for a class with a `BaseType`, walk the ancestor chain
    - If any ancestor's `MemberScope` contains a member with the same name as the member being registered, emit `MIST_MEMBER_REDECLARATION` diagnostic
    - Check both fields and methods
    - _Requirements: 4.3, 4.4_

  - [x] 4.3 Write property test for inherited member resolution
    - **Property 4: Inherited member resolution via chain walk**
    - Generate random class hierarchies (depth 1-3) with fields/methods at various levels → build semantic model → resolve member access → assert correct type returned or undefined-member diagnostic
    - **Validates: Requirements 4.1, 4.2, 4.5, 6.2**

  - [x] 4.4 Write property test for additive-only constraint
    - **Property 5: Additive-only constraint (no redeclaration)**
    - Generate random derived classes that redeclare a field or method from an ancestor → build semantic model → assert `MIST_MEMBER_REDECLARATION` diagnostic is emitted
    - **Validates: Requirements 4.3, 4.4**

- [x] 5. Type checker: subtype assignability
  - [x] 5.1 Update `IsAssignable` in `TypeCheckerAnalyzer` for subtype assignability
    - Add logic to walk the `BaseType` chain: if `source` is a `ClassType`, walk its `BaseType` chain to check if `target` is an ancestor
    - This enables derived → base assignment for variables, parameters, and return types
    - Rejects base → derived and unrelated class → class assignments (existing behavior, now correctly handled)
    - _Requirements: 5.1, 5.2, 5.3, 5.4, 5.5, 6.1_

  - [x] 5.2 Write property test for subtype assignability
    - **Property 6: Subtype assignability (derived assignable to base)**
    - Generate random class hierarchies → create derived-to-base assignments → run type checker → assert no `MIST_TC_0001` diagnostic
    - **Validates: Requirements 5.1, 5.3, 5.4**

  - [x] 5.3 Write property test for non-assignability of incompatible types
    - **Property 7: Non-assignability of incompatible class types**
    - Generate random unrelated class pairs or base-to-derived assignments → run type checker → assert `MIST_TC_0001` diagnostic is emitted
    - **Validates: Requirements 5.2, 5.5**

- [x] 6. Checkpoint - Ensure all tests pass
  - Ensure all tests pass, ask the user if questions arise.

- [x] 7. Pretty-printer and C# emitter updates
  - [x] 7.1 Update `MistPrettyPrinter` for inheritance clause and base constructor chain
    - Update `EmitClassDeclaration` to emit `: BaseName` when the class has an inheritance clause
    - Update `EmitConstructorDeclaration` to handle `BaseChain` constructor chaining (`: base(args)`)
    - _Requirements: 1.4, 1.5_

  - [x] 7.2 Update `CSharpEmitter` for inheritance clause and constructor chains
    - Update `EmitClassDeclaration` to emit `class DerivedName : BaseName` when the class has an inheritance clause
    - Update `EmitConstructorDeclaration` to emit `: base(args)` for `BaseChain` constructor chaining
    - _Requirements: 7.1, 7.2, 8.2_

  - [x] 7.3 Write property test for parse/print round-trip with inheritance
    - **Property 1: Parse/print round-trip for inheritance clauses**
    - Generate random `ClassDeclarationNode` ASTs with/without inheritance clauses → pretty-print → parse → pretty-print → compare strings for equality
    - **Validates: Requirements 1.1, 1.2, 1.4, 1.5, 1.6**

  - [x] 7.4 Write property test for C# emission preserving inheritance structure
    - **Property 8: C# emission preserves inheritance structure**
    - Generate random Mist programs with inheritance → emit C# → assert output contains `class DerivedName : BaseName` pattern for derived classes and `class Name` without base clause for non-derived classes
    - **Validates: Requirements 7.1, 7.2**

- [x] 8. Constructor behavior and base constructor chaining
  - [x] 8.1 Implement base constructor validation in `SemanticModelBuilder`
    - When a derived class is constructed, check if the base class has an explicit constructor with required parameters
    - If the base class requires constructor arguments and the derived class does not provide them (via `: base(args)`), emit `MIST_BASE_CTOR_UNSATISFIED` diagnostic
    - Allow `new DerivedClass()` when the base class has a parameterless constructor or no explicit constructor
    - _Requirements: 8.1, 8.3_

  - [x] 8.2 Write unit tests for constructor behavior
    - Test parameterless derived class construction with parameterless base
    - Test `: base(args)` constructor chaining
    - Test diagnostic when base constructor args are unsatisfied
    - _Requirements: 8.1, 8.2, 8.3_

- [x] 9. Checkpoint - Ensure all tests pass
  - Ensure all tests pass, ask the user if questions arise.

- [x] 10. Update CSHARP_FEATURES.md documentation
  - Update `Inheritance & Polymorphism` section: mark `Class inheritance (: BaseClass)` as ✅ Implemented
  - Update `Constructors` section: mark `Base constructor calls (: base(...))` as ✅ Implemented
  - Update the `Summary` table to reflect new implementation counts
  - _Requirements: 9.1, 9.2, 9.3_

- [x] 11. Create inheritance sample application
  - [x] 11.1 Create `samples/Inheritance/` directory with Mist source files demonstrating inheritance
    - Create a base class with fields, a constructor, and methods (e.g., `Animal` with `name` field and `Describe()` method)
    - Create a single-level derived class that adds new fields and methods (e.g., `Dog : Animal` with `breed` field)
    - Create a multi-tier derived class at least three levels deep (e.g., `GuideDog : Dog` with `handler` field) that accesses members from all ancestors
    - Demonstrate subtype assignability by passing derived instances to a method accepting a base class parameter
    - Demonstrate base constructor chaining with `: base(args)` syntax
    - Include a `Program` class with `static void Main` that exercises all features and prints output via `Console.WriteLine`
    - _Requirements: 10.1, 10.2, 10.3, 10.4, 10.5, 10.6, 10.7_
  - [x] 11.2 Verify the sample compiles and runs through the Mist compiler pipeline
    - Compile the sample using the Mist CLI
    - Verify the emitted C# compiles and produces correct output
    - _Requirements: 10.6_

- [x] 12. Final checkpoint - Ensure all tests pass
  - Ensure all tests pass, ask the user if questions arise.

## Notes

- Tasks marked with `*` are optional and can be skipped for faster MVP
- Each task references specific requirements for traceability
- Checkpoints ensure incremental validation
- Property tests validate universal correctness properties from the design document (Properties 1-8)
- The project uses FsCheck 3.1.0 with xUnit for property-based testing (C#)
- The Storm parser generator auto-generates AST node types from the grammar — task 1.1 must complete before any downstream tasks
- All new diagnostics follow the existing pattern: `DiagnosticId`, `DiagnosticSeverity.Error`, appropriate `PipelineStage`, human-readable message
