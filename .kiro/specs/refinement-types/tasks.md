# Implementation Plan: Refinement Types

## Overview

This plan implements refinement types for the Mist language across the full compiler pipeline. The implementation builds incrementally: grammar changes first, then AST generation, pretty-printing, semantic model, type checking (enforcement, narrowing, subset proving), C# transpilation, and editor support. Tests are interleaved close to the code they validate.

## Tasks

- [x] 1. Extend grammar for refinement type aliases
  - [x] 1.1 Update `TypeAliasDeclaration` rule in `Source/Mist/Grammar/Mist.storm`
    - Replace the `type:TypeReference` field with `type:RefinableTypeExpression`
    - Add `RefinableTypeExpression` rule with union and single variants
    - Add `RefinableUnionTypeExpr` rule: `first:RefinableSingleTypeExpr (PIPE rest:RefinableSingleTypeExpr)+`
    - Add `RefinableSingleTypeExpr` rule: `type:SingleTypeRef refinement:RefinementPredicate?`
    - Add `RefinementPredicate` rule: `predicate:AndPattern`
    - Ensure refinement predicate binds tighter than `|` (it's part of the single type expr)
    - _Requirements: 1.1, 1.2, 1.3, 1.5, 1.6, 1.7_

- [x] 2. Rebuild generated AST nodes
  - [x] 2.1 Build the project to trigger Storm source generator
    - Run `dotnet build Source/Mist/Mist.csproj` to regenerate the lexer and parser
    - Verify that `RefinableTypeExpressionNode`, `RefinableSingleTypeExprNode`, `RefinementPredicateNode` are generated
    - Fix any build errors from the grammar changes (existing code referencing `TypeAliasDeclarationNode.Type` as `TypeReferenceNode` will need updating)
    - _Requirements: 1.1, 1.4_

- [x] 3. Pretty printer support
  - [x] 3.1 Update type alias emission in `Source/Mist/Parsing/MistPrettyPrinter.cs`
    - Update `EmitTypeAliasDeclaration` to handle `RefinableTypeExpressionNode`
    - Add `EmitRefinableTypeExpression` method dispatching to union/single variants
    - Add `EmitRefinableSingle` method: emit type ref, then space + predicate if present
    - Reuse existing `EmitPattern` for the refinement predicate (it's an `AndPatternNode`)
    - _Requirements: 2.1, 2.2, 2.3, 2.4_
  - [x] 3.2 Write property test for pretty-print round-trip
    - **Property 2: Pretty-print round-trip**
    - Create `Test/Mist.Tests/Parsing/RefinementTypeRoundTripPropertyTests.cs`
    - Generate random valid refinement type alias source strings (simple relational, compound `and`, union with refinements)
    - Verify `print(parse(print(parse(s)))) == print(parse(s))` (idempotence)
    - **Validates: Requirements 2.1, 2.2, 2.3, 2.4**

- [x] 4. Checkpoint — Verify parsing and pretty-printing
  - Ensure all tests pass. Ask the user if questions arise.

- [x] 5. Semantic model changes
  - [x] 5.1 Create `RefinementType` in `Source/Mist/Semantics/Model/Types/RefinementType.cs`
    - Implement `IType` interface with `Name`, `BaseType`, `Predicate` properties
    - `DisplayName` returns `Name`; `Kind` delegates to `BaseType.Kind`
    - _Requirements: 4.1_
  - [x] 5.2 Create `RefinementPredicate` in `Source/Mist/Semantics/Model/Types/RefinementPredicate.cs`
    - Store the `AndPatternNode` AST reference
    - Implement `Description` property (human-readable predicate string)
    - Implement `Evaluate(object constantValue): bool` for compile-time checking
    - Implement `Implies(RefinementPredicate other): bool` for subset proving
    - _Requirements: 4.4, 5.4, 9.1, 9.2, 9.5, 13.1, 13.2, 13.3_
  - [x] 5.3 Register refinement types in `Source/Mist/Semantics/Model/SemanticModelBuilder.cs`
    - Update Pass 2 handling of `TypeAliasDeclarationNode` to detect `RefinementPredicate`
    - When a refinement is present: resolve the base `SingleTypeRef`, construct `RefinementType`
    - For union types with refinements: create a `UnionType` whose constituents are `RefinementType` instances
    - Validate predicate compatibility with base type (emit `MIST_TC_0072` if incompatible)
    - Store `RefinementType` as the alias's `DeclaredType`
    - _Requirements: 4.1, 4.2, 4.3, 4.5, 10.1_
  - [x] 5.4 Write unit tests for semantic model registration
    - Create `Test/Mist.Tests/Semantics/RefinementTypeRegistrationTests.cs`
    - Test simple refinement type creates `RefinementType` with correct base type
    - Test compound refinement type stores predicate correctly
    - Test union with refinements creates `UnionType` of `RefinementType` constituents
    - Test incompatible predicate (e.g., `>= 18` on `string`) produces diagnostic
    - **Validates: Requirements 4.1, 4.2, 4.5, 10.1**

- [x] 6. Checkpoint — Verify semantic model
  - Ensure all tests pass. Ask the user if questions arise.

- [x] 7. Type checker — Compile-time enforcement
  - [x] 7.1 Implement literal evaluation against refinement predicates in `Source/Mist/Analysis/Builtin/TypeCheckerAnalyzer.cs`
    - When assigning a literal to a `RefinementType` variable, call `Predicate.Evaluate(literalValue)`
    - If evaluation returns false, emit `MIST_TC_0070`
    - When assigning a non-constant expression of the base type to a `RefinementType`, emit `MIST_TC_0071`
    - When assigning a value already typed as the same `RefinementType`, accept without re-checking
    - _Requirements: 5.1, 5.2, 5.3, 5.4, 5.5_
  - [x] 7.2 Implement widening (refinement → base type) acceptance
    - When the source type is a `RefinementType` and the target is its `BaseType`, accept the assignment
    - _Requirements: 8.3_
  - [x] 7.3 Implement arithmetic erasure
    - When a `RefinementType` value participates in arithmetic (+, -, *, /, %), produce the `BaseType` as the result type
    - When a `RefinementType` value participates in comparison, use `BaseType` semantics
    - _Requirements: 8.1, 8.2_
    - Standard-pipeline unary correction (2026-10-03): `OperatorAdmissibility.UnaryResultType` follows the selected unary return contract or the supported built-in base result. Negating an integral refinement or applying `!` to a Boolean refinement drops the input predicate, including lifted/ad-hoc forms on the same path. Refined returns, initialized locals and arguments require proof of the transformed value; explicitly declared refined operator returns and supported constant proofs remain valid. The existing real/string literal evaluator controls remain supported. Dynamic transformed values without a supported proof remain unproven; no solver expansion is claimed. See `backlog/analysis-v2-capability-progression.yaml`, `refinement-proving`, for verification evidence and remaining limits. _Requirements: 8.1, 8.4._
  - [x] 7.4 Write property test for compile-time enforcement
    - **Property 3: Compile-time enforcement correctness**
    - Create `Test/Mist.Tests/Analysis/RefinementEnforcementPropertyTests.cs`
    - Generate random refinement types (`int >= N`) and random integer literals
    - Build semantic model and run type checker; verify acceptance iff literal satisfies predicate
    - **Validates: Requirements 5.1, 5.2, 5.4**
  - [x] 7.5 Write property test for widening and arithmetic erasure
    - **Property 4: Widening always accepted** and **Property 6: Arithmetic produces base type**
    - Create `Test/Mist.Tests/Analysis/RefinementWideningPropertyTests.cs`
    - Generate programs assigning refinement-typed values to base-typed variables; verify zero diagnostics
    - Generate programs with arithmetic on refinement-typed values; verify result type is base type
    - **Validates: Requirements 8.1, 8.2, 8.3, 9.4**

- [x] 8. Checkpoint — Verify compile-time enforcement
  - Ensure all tests pass. Ask the user if questions arise.

- [x] 9. Type checker — Subset proving and assignability
  - [x] 9.1 Implement `RefinementSolver` for subset relationship proving
    - Create helper class or static methods within the type checker
    - Prove `>= a` implies `>= b` when `a >= b`
    - Prove `<= a` implies `<= b` when `a <= b`
    - Prove `> a` implies `>= b` when `a >= b`
    - Prove `< a` implies `<= b` when `a <= b`
    - Prove compound: `(>= a and <= b)` implies `(>= c and <= d)` when `a >= c` and `b <= d`
    - _Requirements: 9.1, 9.2, 9.5, 13.2, 13.3_
  - [x] 9.2 Integrate subset proving into assignability checks
    - When assigning `RefinementType A` to `RefinementType B` (same base type), call `RefinementSolver.Implies(A.Predicate, B.Predicate)`
    - If implies returns true, accept; otherwise emit `MIST_TC_0074`
    - _Requirements: 9.1, 9.3_
  - [x] 9.3 Write property test for subset proving
    - **Property 5: Subset relationship correctness**
    - Create `Test/Mist.Tests/Analysis/RefinementSubsetPropertyTests.cs`
    - Generate pairs of `>= N` predicates; verify solver accepts iff source N >= target N
    - Generate compound predicate pairs; verify solver correctness
    - **Validates: Requirements 9.1, 9.2, 9.5**

- [x] 10. Checkpoint — Verify subset proving
  - Ensure all tests pass. Ask the user if questions arise.

- [x] 11. Type checker — Readonly validation
  - [x] 11.1 Implement readonly member validation for property pattern refinements
    - When a `RefinementPredicate` contains property patterns, resolve each referenced member
    - Verify each member has the `readonly` modifier; emit `MIST_TC_0073` if not
    - Apply recursively for nested property patterns
    - _Requirements: 3.1, 3.2, 3.3, 3.4_
  - [x] 11.2 Write unit tests for readonly validation
    - Create `Test/Mist.Tests/Analysis/RefinementReadonlyTests.cs`
    - Test property pattern referencing readonly field: accepted
    - Test property pattern referencing mutable field: produces `MIST_TC_0073`
    - Test primitive base type with relational pattern: no readonly check needed
    - Test nested property pattern with mutable inner field: produces `MIST_TC_0073`
    - **Validates: Requirements 3.1, 3.2, 3.3, 3.4**

- [x] 12. Type checker — Pattern matching integration
  - [x] 12.1 Implement `is` expression support for refinement types
    - When `x is RefinementTypeName varName` is encountered, introduce `varName` with the `RefinementType` in the then-branch scope
    - The `is` expression evaluates the refinement predicate at runtime
    - Support refinement types in switch expression arms
    - _Requirements: 6.2, 6.3, 6.4, 14.1, 14.2, 14.4_
  - [x] 12.2 Write unit tests for pattern matching with refinement types
    - Create `Test/Mist.Tests/Analysis/RefinementPatternMatchTests.cs`
    - Test `is RefinementType varName` introduces variable with correct type
    - Test `is RefinementType` (bare, no variable) is valid
    - Test switch expression with refinement type patterns
    - Test non-constant assigned without pattern matching produces `MIST_TC_0071`
    - **Validates: Requirements 6.1, 6.2, 14.1, 14.2, 14.4**

- [x] 13. Type checker — Flow-sensitive narrowing
  - [x] 13.1 Implement flow narrowing for refinement types
    - When an `if` condition proves a refinement predicate for a local variable, narrow the variable's type to the refinement type within the then-branch
    - Create an anonymous `RefinementType` scoped to the branch
    - Only narrow local primitive variables that are not captured by closures
    - Invalidate narrowing on reassignment
    - _Requirements: 7.1, 7.2, 7.3, 7.4, 7.5, 7.6, 7.7, 7.8_
  - [x] 13.2 Write unit tests for flow narrowing
    - Create `Test/Mist.Tests/Analysis/RefinementFlowNarrowingTests.cs`
    - Test `if (x >= 18)` narrows `x` to `AdultAge` in then-branch
    - Test `if (x is >= 18)` narrows `x` to `AdultAge` in then-branch
    - Test narrowing allows assignment to `AdultAge` variable without error
    - Test reassignment within branch invalidates narrowing
    - Test closure-captured variable is NOT narrowed
    - Test condition that doesn't match any refinement type does NOT narrow
    - **Validates: Requirements 7.1, 7.2, 7.3, 7.4, 7.5, 7.6, 7.7**

- [x] 14. Checkpoint — Verify type checker completeness
  - Ensure all tests pass. Ask the user if questions arise.

- [x] 15. C# transpiler changes
  - [x] 15.1 Emit refinement types as base types in `Source/Mist/Backends/CSharpTranspiler/CSharpEmitter.cs`
    - When `CSharpTypeMap` encounters a `RefinementType`, resolve to `BaseType` and delegate to existing logic
    - Variable declarations, parameters, return types, fields all emit the base type
    - _Requirements: 12.1, 12.3_
  - [x] 15.2 Emit runtime predicate checks for pattern matching
    - When emitting `x is RefinementType varName`, emit the predicate as a C# conditional with variable binding
    - For simple relational predicates: `if (x >= 18) { var varName = x; ... }`
    - For compound predicates: combine conditions with `&&`
    - For property patterns: emit property access checks
    - _Requirements: 12.2, 14.3_
  - [x] 15.3 Emit runtime guards for method parameters
    - When a method parameter has a `RefinementType`, emit a guard at method entry
    - Guard throws `ArgumentException` with predicate description if the value fails
    - _Requirements: 12.4_
  - [x] 15.4 Write property test for transpilation correctness
    - **Property 7: Transpilation correctness**
    - Create `Test/Mist.Tests/Backend/RefinementTypeEmissionPropertyTests.cs`
    - Generate valid Mist programs with refinement types (literal assignments, widening, pattern matching)
    - Compile in-process using `SharedDriver`; verify zero diagnostics and valid C# output
    - Use **batched approach** (1 compile, 1 run) per workspace rules
    - **Validates: Requirements 12.1, 12.3, 12.5**

- [x] 16. Checkpoint — Verify C# emission
  - Ensure all tests pass. Ask the user if questions arise.

- [x] 17. Editor syntax highlighting
  - [x] 17.1 Verify/update TextMate grammar in `Editor/mist-syntax-highlighting/syntaxes/mist.tmLanguage.json`
    - Verify existing patterns cover `type` keyword, alias name, relational operators, and `and` in refinement context
    - If needed, add or adjust patterns to ensure refinement predicates within type alias declarations are highlighted correctly
    - _Requirements: 15.1, 15.2, 15.3, 15.4_

- [x] 18. Error diagnostics unit tests
  - [x] 18.1 Write comprehensive diagnostic tests
    - Create `Test/Mist.Tests/Analysis/RefinementDiagnosticTests.cs`
    - Test `MIST_TC_0070`: literal violating predicate shows value, predicate, type name, and definition location
    - Test `MIST_TC_0071`: non-constant assignment shows suggestion to use pattern matching
    - Test `MIST_TC_0072`: incompatible predicate (relational on non-comparable type)
    - Test `MIST_TC_0073`: mutable member in predicate
    - Test `MIST_TC_0074`: non-provable subset relationship
    - **Validates: Requirements 11.1, 11.2, 11.3, 11.4, 11.5**

- [x] 19. Union type integration tests
  - [x] 19.1 Write tests for refinement types in union contexts
    - Create `Test/Mist.Tests/Analysis/RefinementUnionTests.cs`
    - Test union with refinements on each constituent: semantic model creates correct structure
    - Test literal assignment to union-of-refinements: accepted if satisfies any constituent
    - Test literal violating all constituents: rejected
    - Test pattern matching narrows to specific constituent
    - **Validates: Requirements 10.1, 10.2, 10.3, 10.4**

- [x] 20. Feature coverage documentation
  - [x] 20.1 Update `CSHARP_FEATURES.md`
    - Add `Refinement Types` entry marking it as implemented
    - Update the `Summary` table counts
    - _Requirements: 16.1, 16.2_

- [x] 21. Sample application
  - [x] 21.1 Create `samples/RefinementTypes/Program.mist`
    - Demonstrate simple refinement type (`type AdultAge = int >= 18;`)
    - Demonstrate compound refinement (`type Percentage = int >= 0 and <= 100;`)
    - Demonstrate union with refinements (`type AgeRange = int >= 3 and <= 8 | byte >= 0 and < 64;`)
    - Demonstrate property pattern refinement on readonly type
    - Demonstrate compile-time accepted literal assignments
    - Demonstrate runtime validation via pattern matching (`if (x is AdultAge age)`)
    - Demonstrate refinement type as method parameter, return type, and field type
    - Demonstrate flow-sensitive narrowing (`if (x >= 18)` then use as `AdultAge`)
    - Demonstrate arithmetic producing base type
    - Demonstrate subset assignability between refinement types
    - Demonstrate widening from refinement to base type
    - Each permutation prints labeled output
    - Include `public static void RunDemo()` entry point
    - _Requirements: 17.1–17.20_
  - [x] 21.2 Update `samples/AllFeatures/Program.mist` and `mist.toml`
    - Add `RefinementTypes.Program.RunDemo()` call to `Main`
    - Add `[[references]]` entry for the RefinementTypes project
    - _Requirements: 17.17, 17.18_
  - [x] 21.3 Verify sample compiles and runs
    - Build and run the sample through the Mist compiler
    - Verify correct output
    - _Requirements: 17.20_

- [x] 22. Final checkpoint — Full test suite
  - Run all tests. Ensure zero failures and zero warnings. Ask the user if questions arise.

## Notes

- The design uses C# throughout (this is a C# compiler project)
- Property tests use FsCheck (already in the test project)
- Process spawning follows the batched approach per workspace rules (max 1 compile + 1 run per test class)
- Each task references specific requirements for traceability
- Checkpoints ensure incremental validation at natural boundaries
- The `SharedDriver` pattern is used for in-process compilation in tests
- Flow narrowing (task 13) is the most complex piece — it requires tracking variable modification and closure capture. Consider implementing a basic version first (only simple `x >= N` conditions on non-captured locals) and expanding later.
- The `RefinementSolver` (task 9) starts with simple relational implications and can be extended to handle more complex patterns in future iterations.
