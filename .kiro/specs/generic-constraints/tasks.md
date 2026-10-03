# Implementation Plan: Generic Constraints

## Overview

This plan implements generic constraints (`where` clauses) for the Mist programming language. The implementation follows the compiler pipeline order: Grammar → Pretty Printer → Semantic Model → Type Checker → C# Emitter → TextMate Grammar → Sample App → Documentation. Each task builds incrementally on the previous, ensuring no orphaned code.

## Tasks

- [x] 1. Grammar: Add WHERE keyword and constraint clause rules
  - [x] 1.1 Add `WHERE` keyword token to `Source/Mist/Grammar/keywords.storm`
    - Add `token WHERE = "where";` to the keywords file
    - _Requirements: 1.1_

  - [x] 1.2 Add `ConstraintClause`, `Constraint`, and `ConstraintParameterTypeList` rules to `Source/Mist/Grammar/Mist.storm`
    - Define `ConstraintClause = WHERE name:IDENTIFIER COLON constraints:ConstraintList;`
    - Define `ConstraintList = Constraint (COMMA Constraint)*;`
    - Define `Constraint` with variants: `ClassConstraint`, `StructConstraint`, `ConstructorConstraint`, `TypeConstraint`
    - Define `ConstraintParameterTypeList = TypeReference (COMMA TypeReference)*;`
    - _Requirements: 1.2, 1.3_

  - [x] 1.3 Extend declaration rules to accept constraint clauses
    - Extend `ClassDeclaration` to include `constraints:ConstraintClause*` after base clause, before left brace
    - Extend `StructDeclaration` to include `constraints:ConstraintClause*` after interface list, before left brace
    - Extend `InterfaceDeclaration` to include `constraints:ConstraintClause*` after interface list, before left brace
    - Extend `MethodDeclaration` to include `constraints:ConstraintClause*` after parameter list, before method body
    - Extend `InterfaceMethodDeclaration` to include `constraints:ConstraintClause*` after parameter list, before semicolon
    - Extend `TypeAliasDeclaration` to include `constraints:ConstraintClause*` after aliased type reference, before semicolon
    - _Requirements: 1.4, 1.5, 1.6, 1.7, 1.8, 1.9_

  - [x] 1.4 Write parser unit tests for constraint clause parsing
    - Test parsing single constraint clause with each constraint variant (class, struct, new(), new(int), type reference)
    - Test parsing multiple constraint clauses on a single declaration
    - Test parsing constraint clauses on each declaration type (class, struct, interface, method, interface method, type alias)
    - Test that declarations without constraint clauses still parse correctly (backward compatibility)
    - Test that constructor constraints can appear at any position in the constraint list
    - _Requirements: 1.9, 1.10, 9.2_

- [x] 2. Pretty Printer: Format constraint clauses
  - [x] 2.1 Extend `MistPrettyPrinter` in `Source/Mist/Parsing/MistPrettyPrinter.cs` to emit constraint clauses
    - Add formatting logic for `ConstraintClause` nodes
    - Format `class` constraint as keyword `class`
    - Format `struct` constraint as keyword `struct`
    - Format constructor constraints as `new()` or `new(Type1, Type2, ...)`
    - Format type constraints using standard type reference formatting
    - Preserve source order of constraints (do NOT reorder)
    - Format multiple constraint clauses as separate `where` clauses
    - _Requirements: 2.1, 2.2, 2.3, 2.4, 2.5, 2.6, 9.3_

  - [x] 2.2 Write property test for constraint clause round-trip
    - **Property 1: Constraint Clause Round-Trip**
    - Generate random valid Mist declarations with constraint clauses, parse → pretty-print → re-parse and verify AST equivalence
    - Use FsCheck with batched compilation (single process spawn per test class)
    - **Validates: Requirements 2.7, 9.2, 9.3**

- [x] 3. Checkpoint - Ensure grammar and pretty printer work correctly
  - Ensure all tests pass, ask the user if questions arise.

- [x] 4. Semantic Model: TypeParameterConstraints and constraint resolution
  - [x] 4.1 Create `TypeParameterConstraints` class in `Source/Mist/Semantics/Model/Types/`
    - Create new file `TypeParameterConstraints.cs`
    - Implement `HasClassConstraint`, `HasStructConstraint`, `BaseClassConstraint`, `InterfaceConstraints`, `TraitConstraints`, `ConstructorConstraints` properties
    - `ConstructorConstraints` stores `IReadOnlyList<IReadOnlyList<IType>>`, every signature required
    - _Requirements: 3.1, 3.5, 3.6, 3.7, 3.8_

  - [x] 4.2 Extend `TypeParameterType` in `Source/Mist/Semantics/Model/Types/TypeParameterType.cs`
    - Add `public TypeParameterConstraints? Constraints { get; internal set; }` property
    - Null means unconstrained (backward compatible)
    - _Requirements: 3.1_

  - [x] 4.3 Implement constraint resolution in `SemanticModelBuilder` Pass 1b
    - In `Source/Mist/Semantics/Model/SemanticModelBuilder.cs`, add constraint resolution sub-pass after base types/interface lists are resolved
    - For each declaration with constraint clauses: validate type parameter name exists, resolve each constraint type reference, classify as base class / interface / trait constraint, store on `TypeParameterType.Constraints`
    - Handle `class` → `HasClassConstraint`, `struct` → `HasStructConstraint`, `new(...)` → `ConstructorConstraints`, type reference → classify and store
    - Emit `MIST_CONSTRAINT_0001` for undefined type parameter references
    - Emit `MIST_CONSTRAINT_0007` for duplicate `where` clauses on same type parameter
    - Emit `MIST_CONSTRAINT_0008` for unresolvable constraint types
    - _Requirements: 3.1, 3.2, 3.3, 3.4, 3.9_

  - [x] 4.4 Implement constraint validation rules in `SemanticModelBuilder`
    - Emit `MIST_CONSTRAINT_0002` for `class` + `struct` mutual exclusion
    - Emit `MIST_CONSTRAINT_0003` (warning) for `struct` + `new()` redundancy
    - Emit `MIST_CONSTRAINT_0004` for multiple base class constraints
    - Emit `MIST_CONSTRAINT_0005` for struct used as base class constraint
    - Emit `MIST_CONSTRAINT_0006` for base class + `struct` incompatibility
    - _Requirements: 4.1, 4.2, 4.3, 4.4, 4.5, 4.6, 4.7_

  - [x] 4.5 Implement constrained member access in semantic model member lookup
    - Extend member lookup logic (e.g., `LookupMemberType` or equivalent) to check `TypeParameterType.Constraints` when receiver is a `TypeParameterType`
    - If interface constraints → expose interface members
    - If trait constraints → expose trait members
    - If base class constraint → expose base class members (including inherited)
    - If no constraint provides the member → emit diagnostic
    - _Requirements: 6.1, 6.2, 6.3, 6.4, 6.7_

  - [x] 4.6 Write unit tests for constraint resolution and validation
    - Test each validation rule produces the correct diagnostic
    - Test constraint resolution for each constraint kind
    - Test constrained member access for interface, trait, and base class constraints
    - Test that unconstrained generics still work (backward compatibility)
    - _Requirements: 3.9, 4.1, 4.2, 4.3, 4.4, 4.5, 4.6, 4.7, 10.9, 10.10_

- [x] 5. Checkpoint - Ensure semantic model changes are solid
  - Ensure all tests pass, ask the user if questions arise.

- [x] 6. Type Checker: Constraint satisfaction and new T(...) validation
  - [x] 6.1 Implement `CheckConstraintSatisfaction` in `TypeCheckerAnalyzer`
    - In `Source/Mist/Analysis/Builtin/TypeCheckerAnalyzer.cs`, add method to verify a type argument satisfies all constraints on a type parameter
    - Check `class` constraint: type argument must have `TypeKind.Class` or `TypeKind.Interface`
    - Check `struct` constraint: type argument must have `TypeKind.Struct`
    - Check base class constraint: type argument derives from base class
    - Check interface constraints: type argument implements each interface
    - Check trait constraints: type argument satisfies each trait
    - Check constructor constraints (conjunctive): type argument has a matching public constructor for every declared signature
    - Emit diagnostics `MIST_CONSTRAINT_0009` through `MIST_CONSTRAINT_0015` as appropriate
    - _Requirements: 5.1, 5.2, 5.3, 5.4, 5.5, 5.6, 5.7, 5.8, 5.9, 10.1, 10.2, 10.3, 10.4, 10.5, 10.6, 10.7_

  - [x] 6.2 Wire constraint satisfaction checking into type argument usage sites
    - Call `CheckConstraintSatisfaction` from object creation expressions (constructing generic types with type arguments)
    - Call from generic method invocations with explicit type arguments
    - Call from inheritance clauses where constrained generics are used as base types
    - _Requirements: 5.1, 5.10, 5.11_

  - [x] 6.3 Implement `new T(...)` validation against constructor constraints
    - When a `new T(...)` expression is encountered where `T` is a `TypeParameterType`:
      - Verify `T` has at least one constructor constraint
      - Collect argument types of the `new T(...)` call
      - Check if any declared constructor constraint signature matches
      - Emit `MIST_CONSTRAINT_0016` if no match
    - _Requirements: 6.5, 6.6, 10.8_

  - [x] 6.4 Implement constraint propagation through inheritance
    - When a derived type inherits from a constrained generic base, verify derived type's constraints are at least as restrictive
    - Emit `MIST_CONSTRAINT_0017` when constraints are insufficient
    - Handle both base class and interface inheritance scenarios
    - _Requirements: 7.1, 7.2, 7.3, 7.4_

  - [x] 6.5 Write property test for constraint satisfaction correctness
    - **Property 2: Constraint Satisfaction Correctness**
    - Generate constrained generic types and type arguments, verify accept/reject matches constraint rules
    - Use FsCheck with batched compilation
    - **Validates: Requirements 5.1, 5.2, 5.3, 5.4, 5.5, 5.6, 5.7, 5.8, 5.9, 5.10**

  - [x] 6.6 Write property test for constructor constraint new T(...) validation
    - **Property 4: Constructor Constraint new T(...) Validation**
    - Generate generic bodies with `new T(...)` expressions and constructor constraints, verify accept/reject correctness
    - Use FsCheck with batched compilation
    - **Validates: Requirements 6.5, 6.6**

  - [x] 6.7 Write property test for constraint propagation through inheritance
    - **Property 6: Constraint Propagation Through Inheritance**
    - Generate derived types inheriting constrained bases, verify accept/reject based on "at least as restrictive" semantics
    - Use FsCheck with batched compilation
    - **Validates: Requirements 7.1, 7.2, 7.3**

  - [x] 6.8 Write unit tests for type checker constraint diagnostics
    - Test each diagnostic message format (MIST_CONSTRAINT_0009 through MIST_CONSTRAINT_0017)
    - Test that multiple constructor constraints each require a matching constructor
    - Test constraint satisfaction for generic method invocations
    - Test constraint propagation error cases
    - _Requirements: 10.1, 10.2, 10.3, 10.4, 10.5, 10.6, 10.7, 10.8_

- [x] 7. Checkpoint - Ensure type checker is complete
  - Ensure all tests pass, ask the user if questions arise.

- [x] 8. C# Emitter: Constraint emission and factory delegate lowering
  - [x] 8.1 Implement standard constraint emission in `CSharpEmitter`
    - In `Source/Mist/Backends/CSharpTranspiler/CSharpEmitter.cs`, emit `where` clauses for class, struct, interface, base class, trait, and `new()` constraints
    - Reorder constraints to C#'s required order: `class`/`struct` → base class → interfaces/traits → `new()` last
    - Emit trait constraints as their corresponding C# interface name
    - Emit multiple `where` clauses (one per type parameter) as separate C# `where` clauses
    - _Requirements: 8.1, 8.2, 8.3, 8.5, 8.6, 8.7, 8.8, 8.9, 9.1_

  - [x] 8.2 Implement factory delegate lowering for parameterized constructor constraints
    - For generic types with parameterized `new(...)` constraints:
      - Emit a `private readonly Func<TArgs..., T>` field for each parameterized constructor constraint
      - Add corresponding constructor parameter to accept the factory delegate
      - Store factory delegate in the field
    - For generic methods with parameterized `new(...)` constraints:
      - Add `Func<TArgs..., T>` as an additional method parameter
    - `new()` (parameterless) emits as native C# `new()` constraint, NOT as factory delegate
    - Multiple parameterized constructor constraints emit multiple factory delegates
    - _Requirements: 8.4_

  - [x] 8.3 Implement `new T(...)` emission as factory delegate invocation
    - When emitting `new T(args)` where `T` has a parameterized constructor constraint, emit as factory delegate invocation (e.g., `_factory_int(value)`)
    - When emitting `new T()` where `T` has only a `new()` constraint, emit as standard `new T()`
    - _Requirements: 8.4_

  - [x] 8.4 Implement call-site transformation for factory delegates
    - When constructing a type that has parameterized constructor constraints, emit lambda expressions as factory delegate arguments at the call site
    - E.g., `new Factory<MyClass>()` → `new Factory<MyClass>((arg0) => new MyClass(arg0))`
    - Handle method invocations similarly: append factory lambda as additional argument
    - _Requirements: 8.4_

  - [x] 8.5 Handle constrained type alias emission (erasure)
    - Type aliases with constraints are erased during emission (constraints checked at compile time only)
    - Ensure no `where` clause is emitted for type aliases since they resolve to their underlying type
    - _Requirements: 8.4_

  - [x] 8.6 Write property test for constraint emission ordering
    - **Property 3: Constraint Emission Ordering**
    - Generate declarations with constraints in random order, verify emitted C# always has correct order: class/struct → base class → interfaces/traits → new() last
    - Use FsCheck with batched compilation
    - **Validates: Requirements 8.8, 9.1**

  - [x] 8.7 Write unit tests for C# emission
    - Test standard constraint emission for each constraint kind
    - Test factory delegate lowering for parameterized constructor constraints
    - Test call-site transformation with factory lambdas
    - Test multiple parameterized constructor constraints emit multiple delegates
    - Test `new T()` with parameterless constraint emits standard `new T()`
    - Test constraint ordering is correct regardless of source order
    - _Requirements: 8.1, 8.2, 8.3, 8.4, 8.5, 8.6, 8.7, 8.8, 8.9_

- [x] 9. Checkpoint - Ensure emitter produces valid C#
  - Ensure all tests pass, ask the user if questions arise.

- [x] 10. TextMate Grammar: Syntax highlighting for constraint clauses
  - [x] 10.1 Update `Editor/mist-syntax-highlighting/syntaxes/mist.tmLanguage.json`
    - Add `where` as a keyword with scope `keyword.other.mist` in constraint clause context
    - Highlight `class` and `struct` within constraint clauses with scope `storage.type.mist`
    - Highlight `new` within constructor constraints with scope `keyword.other.mist`
    - Highlight the type parameter name following `where` with scope `entity.name.type.parameter.mist`
    - _Requirements: 11.1, 11.2, 11.3, 11.4_

- [x] 11. Sample Application and Documentation
  - [x] 11.1 Create `samples/GenericConstraints/Program.mist`
    - Demonstrate `class` constraint restricting to reference types
    - Demonstrate `struct` constraint restricting to value types
    - Demonstrate interface constraint with constrained method call
    - Demonstrate trait constraint with constrained member access
    - Demonstrate base class constraint with base class member access
    - Demonstrate parameterless `new()` constraint with `new T()` usage
    - Demonstrate parameterized `new(int)` constraint with `new T(someInt)` usage
    - Demonstrate multiple constructor constraints (`new(), new(int), new(bool)`) with each variant
    - Demonstrate multiple constraints on single type parameter (`class, IComparable<T>, new()`)
    - Demonstrate multiple `where` clauses on multi-parameter generic
    - Demonstrate constraints on generic methods
    - Demonstrate constraint propagation through inheritance
    - Demonstrate constructor constraints at any position in constraint list
    - Demonstrate constraints on generic type alias
    - Each permutation prints labeled output to stdout
    - Include `public static void RunDemo()` entry point
    - _Requirements: 12.1, 12.2, 12.3, 12.4, 12.5, 12.6, 12.7, 12.8, 12.9, 12.10, 12.11, 12.12, 12.13, 12.14, 12.15, 12.16, 12.17_

  - [x] 11.2 Update `samples/AllFeatures/Program.mist` to call `GenericConstraints.Program.RunDemo()`
    - _Requirements: 12.17_

  - [x] 11.3 Update `CSHARP_FEATURES.md`
    - Mark `Type constraints (where T : class)` as ✅ Implemented in the Generics section
    - Mark `Generic constraints (where T : ...)` as ✅ Implemented in the Type Declarations section
    - Update the Summary table counts
    - _Requirements: 13.1, 13.2, 13.3_

- [x] 12. Write property test for constrained member accessibility
  - **Property 5: Constrained Member Accessibility**
  - Generate type parameters with interface/trait/base class constraints, verify exactly those members (and only those) are accessible
  - Use FsCheck with batched compilation
  - **Validates: Requirements 6.1, 6.2, 6.3, 6.4, 6.7**

- [x] 13. Final checkpoint - Ensure all tests pass and sample compiles
  - Ensure all tests pass, ask the user if questions arise.

## Notes

- All tasks are required
- Each task references specific requirements for traceability
- Checkpoints ensure incremental validation
- Property tests validate universal correctness properties from the design document
- Unit tests validate specific examples and edge cases
- All property tests use FsCheck with batched compilation (single process spawn per test class) per the test-process-spawning policy
- The C# emitter reorders constraints to C#'s required order; the pretty printer preserves source order
- Parameterized constructor constraints (`new(int)`, `new(int, string)`) use factory delegate lowering since C# doesn't support them natively
- Type alias constraints are enforced at compile time but erased during emission
