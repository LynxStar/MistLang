# Implementation Plan: Readonly Contexts

## Overview

This plan implements readonly contexts for the Mist programming language, spanning grammar changes, semantic model extensions, inference, analysis, parameter enforcement, generic constraints, interface contracts, C# emission, TextMate grammar, and a sample application. Tasks are ordered for incremental progress — each builds on the previous.

## Tasks

- [x] 1. Grammar and parsing changes
  - [x] 1.1 Add `readonly` and `mutable` keyword tokens to `keywords.storm`
    - Add `token READONLY = "readonly";` and `token MUTABLE = "mutable";` to `Source/Mist/Grammar/keywords.storm`
    - _Requirements: 3.1, 3.2_

  - [x] 1.2 Extend the `Modifier` rule to include `ReadonlyModifier`
    - Add `| READONLY as ReadonlyModifier` to the `Modifier` rule in `Source/Mist/Grammar/Mist.storm`
    - This automatically enables `readonly` on all declarations that accept `modifiers:Modifier*` (fields, properties, methods, constructors, classes, structs, interfaces, traits, indexers)
    - _Requirements: 1.1, 1.2, 1.3, 1.4, 1.5, 1.6, 1.7, 1.8, 1.9, 1.10_

  - [x] 1.3 Add `ReadonlyBlockStatement` to the `Statement` rule
    - Add `rule ReadonlyBlockStatement = READONLY body:Block;` to `Mist.storm`
    - Add `| readonlyBlock:ReadonlyBlockStatement as ReadonlyBlockStatement` to the `Statement` rule, placed before `ExpressionStatement` to avoid ambiguity
    - _Requirements: 2.1, 2.2, 2.3, 2.4_

  - [x] 1.4 Extend the `Parameter` rule for readonly parameters
    - Change `rule Parameter = type:TypeReference name:IDENTIFIER;` to `rule Parameter = modifier:READONLY? type:TypeReference name:IDENTIFIER;`
    - _Requirements: 14.1_

  - [x] 1.5 Extend the `Constraint` rule for readonly/mutable constraints
    - Add `| READONLY as ReadonlyConstraint` and `| MUTABLE as MutableConstraint` alternatives to the `Constraint` rule in `Mist.storm`
    - _Requirements: 12.1, 12.2_

  - [x] 1.6 Rebuild the solution and verify Storm source generator produces new node types
    - Run `dotnet build` to confirm the grammar changes compile and the source generator emits the expected node types (`ReadonlyModifierModifierNode`, `ReadonlyBlockStatementStatementNode`, `ReadonlyConstraintConstraintNode`, `MutableConstraintConstraintNode`)
    - Fix any build errors from the grammar changes
    - _Requirements: 1.1–1.10, 2.1–2.4, 3.1–3.4, 14.1_

- [x] 2. Checkpoint — Ensure the solution builds cleanly after grammar changes
  - Ensure all tests pass, ask the user if questions arise.

- [x] 3. Semantic model extensions
  - [x] 3.1 Add `ReadonlyStatus` enum
    - Create `Source/Mist/Semantics/Model/ReadonlyStatus.cs` with values: `Unknown`, `ExplicitReadonly`, `InferredReadonly`, `ImplicitReadonly`, `Mutable`
    - _Requirements: 10.1_

  - [x] 3.2 Add `ReadonlyStatus` to symbol types
    - Add `ReadonlyStatus ReadonlyStatus` property to `FieldSymbol`, `PropertyInfo`, `IndexerInfo`, `MethodSymbol`, `ConstructorSymbol` (or their equivalent representations in the semantic model)
    - Add `bool IsReadonly` to `ParameterSignature` in `Source/Mist/Semantics/Model/ParameterSignature.cs`
    - _Requirements: 14.1, 14.7_

  - [x] 3.3 Add `IsReadonly` to type representations
    - Add `bool IsReadonly` property to `ClassType`, `StructType`, `InterfaceType`, `TraitType` in `Source/Mist/Semantics/Model/Types/`
    - _Requirements: 8.1_

  - [x] 3.4 Extend `TypeParameterConstraints` with readonly/mutable flags
    - Add `bool HasReadonlyConstraint` and `bool HasMutableConstraint` to `Source/Mist/Semantics/Model/Types/TypeParameterConstraints.cs`
    - _Requirements: 12.1, 12.2_

  - [x] 3.5 Update `SemanticModelBuilder` to populate readonly status from AST
    - Detect `ReadonlyModifier` on declarations and set `ReadonlyStatus.ExplicitReadonly`
    - Detect `readonly` on type declarations and set `IsReadonly = true`, then set all members to `ReadonlyStatus.ImplicitReadonly`
    - Detect `readonly` on parameters and set `ParameterSignature.IsReadonly = true`
    - Detect `ReadonlyConstraint`/`MutableConstraint` in constraint clauses and populate `TypeParameterConstraints`
    - _Requirements: 1.1–1.10, 8.1, 12.1, 12.2, 14.1_

- [x] 4. Checkpoint — Ensure the solution builds and existing tests pass after semantic model changes
  - Ensure all tests pass, ask the user if questions arise.

- [x] 5. Readonly Inference Engine
  - [x] 5.1 Create `ReadonlyInferenceEngine` class
    - Create `Source/Mist/Semantics/ReadonlyInferenceEngine.cs`
    - Implement the `IReadonlyInferenceEngine` interface (or integrate as a post-processing pass on the semantic model)
    - The engine runs after the semantic model is built but before analyzers
    - _Requirements: 10.1_

  - [x] 5.2 Implement member classification logic
    - For each member not already `ExplicitReadonly` or `ImplicitReadonly`, examine the body
    - Classify as `InferredReadonly` if body contains only: literal expressions, field reads, local variable operations, calls to readonly methods/getters, allocations via readonly constructors
    - Classify as `Mutable` if body contains: field assignments, calls to non-readonly methods, calls to non-readonly getters, calls to any setter, allocations via non-readonly constructors
    - _Requirements: 10.2, 10.3, 10.4, 10.5, 10.6_

  - [x] 5.3 Implement constructor inference
    - A constructor is inferred readonly when it only assigns to its own fields, does not call non-readonly methods/getters, does not call setters (other than readonly property/indexer setters on `this`), and all `new` expressions target readonly constructors
    - _Requirements: 10.7_

  - [x] 5.4 Implement fixed-point iteration (two-pass)
    - First pass classifies members in declaration order; if a called method in the same type hasn't been classified yet, treat it conservatively as mutable
    - Second pass re-evaluates members that depend on same-type members, using first-pass results
    - _Requirements: 10.8_

  - [x] 5.5 Integrate inference engine into the compiler pipeline
    - Call the inference engine in `CompilerDriver` after `SemanticModelBuilder` completes and before `AnalyzerRunner` runs
    - _Requirements: 10.1_

- [x] 6. Checkpoint — Ensure the solution builds and existing tests pass after inference engine
  - Ensure all tests pass, ask the user if questions arise.

- [x] 7. Readonly Analyzer — base rules
  - [x] 7.1 Create `ReadonlyAnalyzer` implementing `IAnalyzer`
    - Create `Source/Mist/Analysis/Builtin/ReadonlyAnalyzer.cs`
    - Register it in the analyzer pipeline alongside `TypeCheckerAnalyzer`
    - Implement context stack with `ContextKind` enum: `Normal`, `ReadonlyMethod`, `ReadonlyGetter`, `ReadonlySetter`, `ReadonlyIndexerSetter`, `ReadonlyConstructor`, `ReadonlyBlock`
    - _Requirements: 11.1–11.10_

  - [x] 7.2 Implement base rule enforcement for readonly methods and getters
    - Permit: field reads, readonly method calls, readonly property/indexer getter calls, local variable declarations/assignments, `new` with readonly constructors
    - Prohibit: field assignments (`MIST_RO_0001`), non-readonly method calls (`MIST_RO_0002`), non-readonly property/indexer getter calls (`MIST_RO_0003`), any property/indexer setter calls (`MIST_RO_0004`), `new` with non-readonly constructors (`MIST_RO_0005`)
    - _Requirements: 7.1, 11.1–11.10_

  - [x] 7.3 Implement constructor privilege (exception 11)
    - When `ContextKind == ReadonlyConstructor`: permit field assignment to `this.field` and invocation of the declaring type's own readonly property/indexer setters
    - _Requirements: 4.2, 5.2, 8.2, 11.11_

  - [x] 7.4 Implement backing field exemption (exception 12)
    - When `ContextKind == ReadonlySetter`: permit `field = value` assignment
    - All other field assignments remain prohibited
    - _Requirements: 5.3, 5.4, 11.12_

  - [x] 7.5 Implement readonly block override (exceptions 13–14)
    - When a `readonly { }` block is encountered inside a constructor, push `ContextKind.ReadonlyBlock` — constructor privilege is suspended
    - Base rules apply without exception within the block
    - Emit `MIST_RO_0010` for field assignments within readonly blocks in constructors
    - _Requirements: 4.3, 9.1, 9.2, 9.3, 11.13, 11.14_

  - [x] 7.6 Implement readonly field assignment prohibition outside constructors
    - When a readonly field is assigned outside a constructor, emit `MIST_RO_0009`
    - _Requirements: 4.1, 4.4_

  - [x] 7.7 Implement readonly property/indexer setter restriction outside constructors
    - When a readonly property/indexer setter is called outside a constructor of the declaring type, emit `MIST_RO_0004`
    - _Requirements: 5.1, 6.1, 6.2_

- [x] 8. Readonly parameter enforcement
  - [x] 8.1 Implement readonly parameter analysis in `ReadonlyAnalyzer`
    - When a non-readonly method is called on a readonly parameter → `MIST_RO_0006`
    - When a field of a readonly parameter is assigned → `MIST_RO_0007`
    - When a non-readonly setter is called on a readonly parameter → `MIST_RO_0008`
    - _Requirements: 14.2, 14.3, 14.4_

  - [x] 8.2 Implement call-site checking for readonly type arguments in `TypeCheckerAnalyzer`
    - If the argument's type is a readonly type (`IsReadonly == true`), the corresponding parameter MUST be marked `readonly`; emit `MIST_TC_0073` if not
    - If the argument's type is mutable, either parameter kind is acceptable
    - _Requirements: 14.5, 14.6_

- [x] 9. Generic constraint validation
  - [x] 9.1 Implement type argument validation in `TypeCheckerAnalyzer`
    - When a type parameter has `HasReadonlyConstraint` and the type argument is not readonly → emit `MIST_TC_0070`
    - When a type parameter has `HasMutableConstraint` and the type argument is readonly → emit `MIST_TC_0071`
    - _Requirements: 12.1, 12.2, 12.7_

  - [x] 9.2 Implement member access rules on constrained type parameters in `ReadonlyAnalyzer`
    - When `T` has a readonly constraint, treat all member access as if the receiver is in a readonly context — non-readonly methods/getters are prohibited
    - When `T` has a mutable constraint (or no constraint), normal member access rules apply
    - _Requirements: 12.3, 12.4, 12.5, 12.6_

- [x] 10. Interface/trait readonly contract checking
  - [x] 10.1 Implement contract verification in `TypeCheckerAnalyzer`
    - When an interface or trait is marked `readonly`, all its members are implicitly readonly
    - For each readonly interface/trait member, verify the implementing member is also readonly (explicit or inferred)
    - If not → emit `MIST_TC_0072`
    - The implementing type itself does not need to be readonly — only the specific members
    - _Requirements: 13.1, 13.2, 13.3, 13.4, 13.5, 13.6_

- [x] 11. Checkpoint — Ensure all tests pass after analysis and type checking changes
  - Ensure all tests pass, ask the user if questions arise.

- [x] 12. C# emission updates
  - [x] 12.1 Emit `readonly` modifier on fields
    - When a field has `ReadonlyStatus != Mutable`, emit the C# `readonly` modifier
    - _Requirements: 17.1_

  - [x] 12.2 Emit `init` accessor for readonly properties with setters
    - When a readonly property has a setter, emit `init` instead of `set` in the C# output
    - _Requirements: 17.2_

  - [x] 12.3 Emit `readonly struct` for readonly struct types
    - When a struct type has `IsReadonly == true`, emit `readonly struct` in C#
    - _Requirements: 17.4_

  - [x] 12.4 Emit `readonly` on struct methods
    - When a method on a struct has `ReadonlyStatus != Mutable`, emit the C# `readonly` modifier on the method
    - _Requirements: 17.5_

  - [x] 12.5 Erase readonly blocks, readonly parameters, and generic constraints
    - Readonly blocks emit as regular blocks (no C# equivalent)
    - Readonly parameters emit as regular parameters (enforcement is compile-time only)
    - `where T : readonly` and `where T : mutable` constraints are erased (no C# equivalent)
    - _Requirements: 17.3_

- [x] 13. TextMate grammar update
  - [x] 13.1 Add `readonly` and `mutable` to the storage modifier pattern
    - Update `Editor/mist-syntax-highlighting/syntaxes/mist.tmLanguage.json` to include `readonly` and `mutable` in the `storage.modifier.mist` scope pattern
    - _Requirements: 18.1, 18.2, 18.3, 18.4, 18.5_

- [x] 14. Sample application
  - [x] 14.1 Create `samples/ReadonlyContexts/Program.mist`
    - Demonstrate all syntax variants: readonly fields, readonly properties (auto-getter, custom getter/setter), readonly methods, readonly types (class and struct), readonly constructors, readonly blocks (in methods and constructors), readonly inference, readonly parameters, generic constraints (`where T : readonly`, `where T : mutable`), allocation within readonly contexts, readonly interface contract enforcement
    - Each permutation prints labeled output to stdout
    - Include `public static void RunDemo()` entry point
    - _Requirements: 19.1, 19.2, 19.3, 19.5_

  - [x] 14.2 Integrate into `samples/AllFeatures/Program.mist`
    - Add `ReadonlyContexts.Program.RunDemo()` call
    - _Requirements: 19.4_

- [x] 15. Checkpoint — Full build and sample compilation
  - Ensure all tests pass and the sample compiles and runs without errors, ask the user if questions arise.

- [x] 16. Property-based tests for parsing
  - [x] 16.1 Write property test for readonly modifier parsing preservation
    - **Property 1: Readonly Modifier Parsing Preservation**
    - Generate valid declarations with various modifier combinations including `readonly`; verify AST contains `ReadonlyModifier`
    - Use batched compilation approach (generate all scenarios, compile once, run once)
    - **Validates: Requirements 1.1, 1.2, 1.3, 1.4, 1.5, 1.6, 1.7, 1.8, 1.9**

  - [x] 16.2 Write property test for readonly block parsing
    - **Property 2: Readonly Block Parsing**
    - Generate valid statement lists wrapped in `readonly { ... }` blocks; verify AST produces `ReadonlyBlockStatement` with correct body
    - **Validates: Requirements 2.1**

  - [x] 16.3 Write property test for round-trip parsing
    - **Property 19: Round-Trip Parsing**
    - Generate valid readonly syntax constructs; parse → print → parse and verify equivalent syntax trees
    - **Validates: Requirements 20.1, 20.2**

- [x] 17. Property-based tests for readonly analyzer
  - [x] 17.1 Write property test for readonly field assignment prohibition
    - **Property 3: Readonly Field Assignment Prohibition**
    - Generate programs with readonly fields and assignments outside constructors; verify diagnostic errors
    - **Validates: Requirements 4.1, 4.4**

  - [x] 17.2 Write property test for constructor privilege
    - **Property 4: Constructor Privilege for Readonly Fields**
    - Generate programs with readonly fields assigned within constructors (outside readonly blocks); verify no errors
    - **Validates: Requirements 4.2, 5.2**

  - [x] 17.3 Write property test for permitted operations in readonly contexts
    - **Property 5: Readonly Context Permitted Operations**
    - Generate readonly methods/getters/blocks with only permitted operations; verify no diagnostics
    - **Validates: Requirements 11.1, 11.2, 11.3, 11.4, 11.5**

  - [x] 17.4 Write property test for prohibited operations in readonly contexts
    - **Property 6: Readonly Context Prohibited Operations**
    - Generate readonly methods/getters/blocks with prohibited operations; verify each produces a diagnostic
    - **Validates: Requirements 11.6, 11.7, 11.8, 11.9, 11.10**

  - [x] 17.5 Write property test for readonly block overriding constructor privilege
    - **Property 7: Readonly Block Overrides Constructor Privilege**
    - Generate constructors of readonly types with `readonly { }` blocks containing field assignments; verify errors
    - **Validates: Requirements 9.2, 11.13, 11.14**

  - [x] 17.6 Write property test for backing field exemption
    - **Property 8: Backing Field Exemption**
    - Generate readonly properties with setter bodies using `field = value`; verify permitted. Generate other field assignments in same setter; verify errors
    - **Validates: Requirements 5.3, 5.4, 11.12**

  - [x] 17.7 Write property test for readonly method body enforcement
    - **Property 9: Readonly Method Body Enforcement**
    - Generate readonly methods with non-readonly operations; verify diagnostics for each violation
    - **Validates: Requirements 7.1**

  - [x] 17.8 Write property test for readonly type implicit member readonly
    - **Property 10: Readonly Type Implicit Member Readonly**
    - Generate readonly types; verify all members are treated as implicitly readonly and constructor has constructor privilege
    - **Validates: Requirements 8.1, 8.2**

  - [x] 17.9 Write property test for readonly parameter enforcement
    - **Property 13: Readonly Parameter Enforcement**
    - Generate methods with readonly parameters and operations that violate readonly rules on those parameters; verify diagnostics
    - **Validates: Requirements 14.2, 14.3, 14.4**

- [x] 18. Property-based tests for inference engine
  - [x] 18.1 Write property test for inference locality
    - **Property 11: Readonly Inference Locality**
    - Generate methods that call other methods; verify inference uses only the called method's status, not its body
    - **Validates: Requirements 10.1, 10.8**

  - [x] 18.2 Write property test for inference classification
    - **Property 12: Readonly Inference Classification**
    - Generate members with bodies containing only readonly-safe operations; verify classification as `InferredReadonly`
    - **Validates: Requirements 10.2, 10.3, 10.4, 10.5, 10.6, 10.7**

- [x] 19. Property-based tests for generics and interfaces
  - [x] 19.1 Write property test for readonly type argument passing
    - **Property 14: Readonly Type Argument Passing**
    - Generate call sites passing readonly types to non-readonly parameters; verify errors. Generate passing non-readonly types; verify acceptance
    - **Validates: Requirements 14.5, 14.6**

  - [x] 19.2 Write property test for generic readonly/mutable constraint checking
    - **Property 15: Generic Readonly/Mutable Constraint Checking**
    - Generate generic instantiations with readonly constraints and non-readonly type arguments (and vice versa); verify errors
    - **Validates: Requirements 12.1, 12.2, 12.7**

  - [x] 19.3 Write property test for generic readonly constraint member access
    - **Property 16: Generic Readonly Constraint Member Access**
    - Generate generic bodies accessing non-readonly members on `T` with readonly constraint; verify errors. With mutable constraint; verify acceptance
    - **Validates: Requirements 12.3, 12.4**

  - [x] 19.4 Write property test for interface/trait readonly contract enforcement
    - **Property 17: Interface/Trait Readonly Contract Enforcement**
    - Generate types implementing readonly interfaces with non-readonly implementations; verify errors
    - **Validates: Requirements 13.2, 13.3, 13.6**

- [x] 20. Property-based tests for C# emission
  - [x] 20.1 Write property test for C# emission of readonly constructs
    - **Property 18: C# Emission of Readonly Constructs**
    - Generate readonly fields, properties with setters, readonly structs, and readonly struct methods; verify emitted C# contains correct modifiers (`readonly`, `init`, `readonly struct`, `readonly` on methods)
    - **Validates: Requirements 17.1, 17.2, 17.4, 17.5**

- [x] 21. Final checkpoint — Ensure all tests pass
  - Ensure all tests pass, ask the user if questions arise.

## Notes

- Tasks marked with `*` are optional and can be skipped for faster MVP
- Each task references specific requirements for traceability
- Checkpoints ensure incremental validation
- Property tests validate universal correctness properties from the design document using the batched compilation approach (1 compile, 1 run, N output lines)
- The implementation language is C# (matching the existing compiler codebase)
- Grammar changes trigger the Storm source generator to produce new AST node types automatically
