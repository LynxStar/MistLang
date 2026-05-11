# Implementation Plan: Adhoc Refinement Constraints

## Overview

This plan implements ad-hoc refinement constraints for the Mist language — `where` clauses that attach refinement predicates to dependent type arguments on generic declarations and to parameters on constructors, methods, and indexers. The implementation proceeds layer-by-layer through the compiler pipeline: grammar → parser/AST → pretty-printer → semantic model → type checker → emitter → sample app → documentation.

## Tasks

- [x] 1. Grammar and AST foundation
  - [x] 1.1 Add the `WhereClause` rule to `Mist.storm` and update all affected declaration rules
    - Replace the existing `ConstraintClause` references with a unified `WhereClause` rule that has two alternatives: `GenericConstraintClause` (existing `where Name : ConstraintList`) and `AdhocRefinementConstraint` (`where Name AndPattern`)
    - Update `ClassDeclaration`, `StructDeclaration`, `InterfaceDeclaration`, `TraitDeclaration`, `TypeAliasDeclaration`, `MethodDeclaration`, `InterfaceMethodDeclaration` to use `constraints:WhereClause*`
    - Add `constraints:WhereClause*` to `ConstructorDeclaration` (after parameter list, before constructor chain)
    - Add `constraints:WhereClause*` to `IndexerDeclaration` (after bracket-enclosed parameters, before accessor block)
    - Verify the grammar compiles via the Storm source generator without errors
    - _Requirements: 1.1, 1.2, 1.3, 1.4, 1.5, 1.6, 1.7, 1.8, 2.1, 2.2, 2.3, 2.4, 2.5, 2.6, 2.7, 10.1, 10.5_

  - [x] 1.2 Update generated extension files and fix compilation after grammar change
    - Regenerate or update `CompilationUnitNodeExtensions.cs`, `TypeArgumentListNodeExtensions.cs`, `TypeParameterNodeExtensions.cs` and any other generated files affected by the grammar change
    - Fix all compilation errors in the `Mist` project caused by the `ConstraintClause` → `WhereClause` migration (parser references, semantic model references, emitter references, etc.)
    - Ensure the solution builds with zero warnings
    - _Requirements: 1.1, 1.6, 10.1_

- [x] 2. Checkpoint - Ensure the solution builds cleanly
  - Ensure all tests pass, ask the user if questions arise.

- [x] 3. Pretty-printer support
  - [x] 3.1 Update `MistPrettyPrinter` to emit `WhereClause` nodes
    - Add `EmitWhereClause` method that dispatches on `GenericConstraintClauseWhereClauseNode` vs `AdhocRefinementConstraintWhereClauseNode`
    - For generic constraints: emit ` where {Name} : {ConstraintList}` (existing behavior)
    - For adhoc refinement constraints: emit ` where {Name} {AndPattern}` with correct spacing
    - Replace existing `EmitConstraintClauses` calls with the new unified `EmitWhereClauses` that iterates over `WhereClauseNode` arrays
    - Handle compound predicates with `and` combinators (single space before and after `and`)
    - Ensure constructor and indexer declarations emit their new constraint clauses
    - _Requirements: 3.1, 3.2, 3.3, 3.4, 3.5, 3.6, 10.2_

  - [x] 3.2 Write property test for pretty-print round-trip (Property 1)
    - **Property 1: Pretty-print round-trip (idempotence)**
    - Create `AdhocRefinementRoundTripPropertyTests` in `Test/Mist.Tests/`
    - Generate random Mist source fragments with adhoc refinement constraints (single/multiple, compound predicates, mixed generic+refinement, all declaration kinds)
    - Parse, pretty-print, parse again, pretty-print again — assert identical output
    - Use in-process compilation via `CompilerDriver` (no process spawning per iteration)
    - Minimum 100 iterations
    - **Validates: Requirements 1.1, 1.3, 1.4, 1.5, 1.6, 2.1, 2.2, 2.3, 2.4, 2.5, 2.6, 3.1, 3.2, 3.3, 3.4, 3.5, 3.6, 10.1, 10.2, 10.5**

- [x] 4. Semantic model registration
  - [x] 4.1 Extend `SemanticModelBuilder` to process `AdhocRefinementConstraintWhereClauseNode`
    - Add `RefinementPredicate? AdhocConstraint` property to `DependentTypeParameterType`
    - Add a parameter refinement lookup (e.g., `Dictionary<ISymbol, RefinementPredicate>`) to the semantic model
    - Add `GetParameterRefinement(ISymbol)` method to the semantic model interface
    - During the constraint-resolution pass, for each `AdhocRefinementConstraintWhereClauseNode`:
      - Look up the name in dependent type parameters → attach predicate to `DependentTypeParameterType.AdhocConstraint`
      - Look up the name in value parameters → store in parameter refinement map
      - If not found → emit `MIST_REFINEMENT_0001` diagnostic
      - If found as regular type parameter → emit `MIST_REFINEMENT_0002` diagnostic
      - If duplicate → emit `MIST_REFINEMENT_0003` diagnostic
    - _Requirements: 4.1, 4.2, 4.3, 4.4, 4.5, 4.6, 9.6, 10.3_

  - [x] 4.2 Write property test for semantic model predicate attachment (Property 2)
    - **Property 2: Semantic model predicate attachment and evaluation**
    - Create `AdhocRefinementSemanticModelPropertyTests` in `Test/Mist.Tests/`
    - Generate random declarations with valid constraint targets
    - Build semantic model in-process, assert predicate attachment and `Evaluate` correctness
    - Minimum 100 iterations
    - **Validates: Requirements 4.1, 4.2, 4.3, 9.6**

  - [x] 4.3 Write property test for semantic model invalid-name diagnostics (Property 3)
    - **Property 3: Semantic model invalid-name diagnostics**
    - Generate declarations with invalid constraint targets (unknown names, regular type parameters, duplicates)
    - Build semantic model in-process, assert correct diagnostics emitted and no predicate attached
    - Minimum 100 iterations
    - **Validates: Requirements 4.4, 4.5, 4.6**

- [x] 5. Checkpoint - Ensure all tests pass
  - Ensure all tests pass, ask the user if questions arise.

- [x] 6. Type checker — compile-time enforcement
  - [x] 6.1 Implement compile-time enforcement for dependent argument refinement constraints
    - In `TypeCheckerAnalyzer`, when constructing a type with a `DependentTypeParameterType` that has an `AdhocConstraint`:
      - Evaluate the predicate against the compile-time constant value
      - If violation → emit `MIST_REFINEMENT_0004` diagnostic
      - If forwarding from another dependent parameter → use `RefinementSolver.Implies` to verify source constraint implies target
      - If implication fails → emit `MIST_REFINEMENT_0006` diagnostic
      - If source has no constraint and target requires one → emit `MIST_REFINEMENT_0008` diagnostic
    - Handle inheritance scenarios: verify fixed dependent values satisfy base type constraints
    - _Requirements: 5.1, 5.2, 5.3, 5.4, 5.5, 5.6, 5.7, 9.1, 9.2, 9.3, 9.4, 9.5, 13.1, 13.7, 13.8_

  - [x] 6.2 Implement compile-time enforcement for parameter refinement constraints
    - In `TypeCheckerAnalyzer`, when calling a method/constructor/indexer with a parameter that has an adhoc refinement:
      - If argument is a compile-time constant → evaluate predicate, emit `MIST_REFINEMENT_0005` on violation
      - If argument is non-constant → accept (defer to runtime guard)
      - If argument's type is a named refinement type → use `RefinementSolver.Implies` to check if argument's predicate implies parameter's constraint
    - Apply equivalently to constructor and indexer parameters
    - _Requirements: 6.1, 6.2, 6.3, 6.4, 6.5, 6.6, 6.7, 13.2_

  - [x] 6.3 Implement predicate-type compatibility validation
    - Validate that relational predicates are only applied to comparable types (int, uint, long, ulong, short, ushort, byte, sbyte, char, float, double, decimal)
    - Reject relational predicates on `bool` and `string` → emit `MIST_REFINEMENT_0007` diagnostic
    - Accept constant patterns and `and` combinators on all equality-comparable types
    - _Requirements: 11.1, 11.2, 11.3, 11.4, 11.5, 11.6, 11.7, 13.6_

  - [x] 6.4 Implement flow narrowing from parameter refinement constraints
    - Within a method body, treat a parameter with an adhoc refinement constraint as having an anonymous `RefinementType` (base type + predicate)
    - Allow assignment to named refinement types whose predicate is implied by the parameter's constraint
    - Allow widening to base type without refinement
    - Reject assignment to refinement types whose predicate is NOT implied → emit diagnostic
    - On reassignment or compound assignment of the parameter, invalidate the refinement narrowing for subsequent statements
    - _Requirements: 8.1, 8.2, 8.3, 8.4, 8.5_

  - [x] 6.5 Write property tests for compile-time enforcement (Properties 4, 5)
    - **Property 4: Compile-time acceptance of satisfying constants**
    - **Property 5: Compile-time rejection of violating constants**
    - Create `AdhocRefinementEnforcementPropertyTests` in `Test/Mist.Tests/`
    - Generate random predicates and constant values (both satisfying and violating)
    - Build full programs in-process, assert correct acceptance/rejection
    - Minimum 100 iterations
    - **Validates: Requirements 5.1, 5.2, 6.1, 6.2, 6.5, 6.6, 6.7**

  - [x] 6.6 Write property test for predicate implication (Property 6)
    - **Property 6: Predicate implication correctness**
    - Create `RefinementImplicationPropertyTests` in `Test/Mist.Tests/`
    - Generate random pairs of relational predicates
    - Test `RefinementPredicate.Implies` against a brute-force oracle (evaluate both predicates over a range of values)
    - Minimum 100 iterations
    - **Validates: Requirements 5.4, 5.5, 5.6, 6.4, 8.1, 8.2, 8.3, 9.2, 9.3, 9.5**

  - [x] 6.7 Write property test for flow narrowing invalidation (Property 7)
    - **Property 7: Flow narrowing invalidation on reassignment**
    - Create `AdhocRefinementFlowNarrowingPropertyTests` in `Test/Mist.Tests/`
    - Generate methods with parameter constraints followed by reassignment
    - Verify type checker rejects post-reassignment refined usage
    - Minimum 100 iterations
    - **Validates: Requirements 8.5**

  - [x] 6.8 Write property test for predicate-type compatibility (Property 8)
    - **Property 8: Predicate-type compatibility validation**
    - Create `PredicateCompatibilityPropertyTests` in `Test/Mist.Tests/`
    - Generate predicate+type combinations across all primitive types
    - Verify acceptance for compatible combinations and rejection for incompatible ones
    - Minimum 100 iterations
    - **Validates: Requirements 11.1, 11.2, 11.3, 11.4, 11.5, 11.6, 11.7**

- [x] 7. Checkpoint - Ensure all tests pass
  - Ensure all tests pass, ask the user if questions arise.

- [x] 8. C# emission
  - [x] 8.1 Implement runtime guard generation for parameter refinement constraints
    - Extend `EmitMethodBlockWithRefinementGuards` (or equivalent) to collect adhoc refinement constraints from the parameter refinement map
    - Generate `if (!(condition)) throw new ArgumentException(...)` guards at method entry
    - Emit guards in left-to-right parameter declaration order
    - Emit compound predicates as a single `&&`-joined boolean expression
    - Apply to methods, constructors, and indexers
    - Ensure generic constraints are emitted as C# `where` clauses on the signature and parameter refinements as runtime guards in the body
    - _Requirements: 7.1, 7.2, 7.4, 7.5, 12.1, 12.2, 12.4, 12.5, 12.6_

  - [x] 8.2 Implement guard elision optimization
    - Track whether all call sites for a method pass only validated constants or arguments with provably-satisfying refinement types
    - If so, elide the runtime guard for that parameter
    - Dependent argument refinements produce no runtime code (compile-time only)
    - _Requirements: 7.3, 12.3_

  - [x] 8.3 Write unit tests for C# emission of adhoc refinement guards
    - Test that emitted C# compiles without errors
    - Test guard structure matches expected pattern (negated condition + throw)
    - Test compound predicates emit as single boolean expression
    - Test guard elision when all call sites are validated
    - Test that dependent argument refinements produce no runtime code
    - _Requirements: 12.1, 12.2, 12.3, 12.4, 12.5, 12.6_

- [x] 9. Checkpoint - Ensure all tests pass
  - Ensure all tests pass, ask the user if questions arise.

- [x] 10. TextMate grammar update
  - [x] 10.1 Update `mist.tmLanguage.json` for adhoc refinement constraint highlighting
    - Add a pattern matching the adhoc refinement constraint form (`where <name> <relational_operator>`)
    - Highlight `where` as `keyword.other.mist`
    - Highlight the constrained name as `entity.name.type.parameter.mist`
    - Highlight relational operators (`>=`, `<=`, `>`, `<`) as `keyword.operator.comparison.mist`
    - Highlight `and` as `keyword.operator.logical.mist`
    - Order the new pattern before the generic constraint pattern so colon-based generic constraints take priority
    - _Requirements: 14.1, 14.2, 14.3, 14.4, 14.5_

- [x] 11. Sample application
  - [x] 11.1 Create `samples/AdhocRefinementConstraints/Program.mist`
    - Demonstrate single dependent argument refinement (e.g., `class Array<TValue, Size:int> where Size >= 0`)
    - Demonstrate multiple dependent argument refinements (e.g., `class Matrix<Rows:int, Cols:int> where Rows >= 1 where Cols >= 1`)
    - Demonstrate mixed generic + dependent argument refinement (e.g., `class Container<T, Size:int> where T : class where Size >= 0`)
    - Demonstrate method with single parameter refinement (e.g., `void Foo(int x) where x < 100`)
    - Demonstrate method with multiple parameter refinements
    - Demonstrate constructor with parameter refinement
    - Demonstrate indexer with parameter refinement
    - Demonstrate compound predicate with `and`
    - Demonstrate constructing a type with valid dependent value
    - Demonstrate calling a method with valid literal argument
    - Demonstrate method with both generic constraint and parameter refinement
    - Demonstrate flow narrowing within method body
    - Demonstrate inheritance with fixed dependent value satisfying base constraint
    - Demonstrate forwarding dependent parameter with implication
    - Each demonstration prints labeled output to stdout
    - Include `public static void RunDemo()` entry point
    - _Requirements: 15.1, 15.2, 15.3, 15.4, 15.5, 15.6, 15.7, 15.8, 15.9, 15.10, 15.11, 15.12, 15.13, 15.14, 15.15, 15.16, 15.17, 15.19_

  - [x] 11.2 Integrate into `samples/AllFeatures/Program.mist`
    - Add `AdhocRefinementConstraints.Program.RunDemo()` call to the AllFeatures orchestrator
    - Verify the full-suite sample run completes without errors
    - _Requirements: 15.18, 15.19_

- [x] 12. Documentation update
  - [x] 12.1 Update `CSHARP_FEATURES.md` with Adhoc Refinement Constraints section
    - Add `Adhoc Refinement Constraints` section after the existing `Refinement Types` section
    - Include feature table with columns `Feature | Status | Notes` listing each sub-capability with `✅` status
    - Update the `Summary` table at the bottom with accurate counts
    - Use the same table format and status legend as other sections
    - _Requirements: 16.1, 16.2, 16.3_

- [x] 13. Final checkpoint - Ensure all tests pass
  - Ensure all tests pass, ask the user if questions arise.

- [x] 14. Interface/trait method refinement constraint satisfaction (Liskov Substitution)
  - [x] 14.1 Add `MIST_REFINEMENT_0009` diagnostic constant
    - Add the diagnostic ID `MIST_REFINEMENT_0009` to the diagnostics constants file
    - Message template: "Implementation of '{Method}' on '{Type}' has a stronger refinement constraint '{ImplPredicate}' on parameter '{Name}' than the interface '{Interface}' requires '{InterfacePredicate}'. The implementation must accept all values the interface promises to accept."
    - _Requirements: 17.4_

  - [x] 14.2 Implement `ValidateRefinementConstraintSatisfaction` in `SemanticModelBuilder`
    - In `Pass1_ValidateInterfaceSatisfaction` (for interfaces) and `Pass1_ResolveTraitSatisfaction` (for traits), after method signature matching succeeds, add refinement constraint compatibility validation
    - For each matched method pair (interface/trait method → implementation method):
      - For each parameter position `i`:
        - Look up adhoc refinement constraint on interface/trait method parameter via `GetParameterRefinement`
        - Look up adhoc refinement constraint on implementation method parameter via `GetParameterRefinement`
        - If interface predicate is null → skip (implementation may freely add or omit constraints)
        - If interface predicate is non-null and impl predicate is null → accept (no constraint means accepts all values)
        - If both are non-null → check `RefinementSolver.Implies(interfacePredicate, implPredicate)`:
          - If true → accept
          - If false → emit `MIST_REFINEMENT_0009` diagnostic
      - Validate each parameter independently (violation on one does not suppress others)
    - Apply the same algorithm to indexer parameters
    - _Requirements: 17.1, 17.2, 17.3, 17.4, 17.5, 17.6, 17.7, 17.8, 17.9_

  - [x] 14.3 Write property test for interface/trait refinement constraint satisfaction (Property 9)
    - **Property 9: Interface/trait method refinement constraint satisfaction (Liskov Substitution)**
    - Create `InterfaceTraitRefinementSatisfactionPropertyTests` in `Test/Mist.Tests/`
    - Generate random interface/trait method declarations with parameter refinement constraints
    - Pair with implementing methods that have: (a) same predicate, (b) no predicate, (c) weaker predicate (implication holds), (d) stronger predicate (implication fails)
    - Build full programs in-process via `CompilerDriver.Compile()`, verify acceptance for cases (a)–(c) and `MIST_REFINEMENT_0009` diagnostic for case (d)
    - Cover both interfaces and traits, single and multiple parameters, and indexer parameters
    - Generate cases where the interface has no constraint (implementation may add any constraint freely)
    - Uses in-process compilation (no process spawning per iteration)
    - Minimum 100 iterations
    - **Validates: Requirements 17.1, 17.2, 17.3, 17.4, 17.5, 17.6, 17.7, 17.8, 17.9**

  - [x] 14.4 Update sample app to demonstrate interface/trait refinement satisfaction scenarios
    - Add demonstrations to `samples/AdhocRefinementConstraints/Program.mist` for:
      - An interface with a method that has a Parameter_Refinement, and a class implementing with the same constraint (valid — same predicate) (Requirement 15 criterion 16)
      - An interface with a method that has a Parameter_Refinement, and a class implementing with no constraint (valid — weaker, accepts everything) (Requirement 15 criterion 17)
      - An interface with a method that has a Parameter_Refinement, and a class implementing with a weaker constraint (valid — accepts a superset of values) (Requirement 15 criterion 18)
    - Each demonstration prints labeled output to stdout with a section header
    - Ensure the sample compiles with zero diagnostics and runs without exceptions
    - _Requirements: 15.16, 15.17, 15.18, 15.19, 15.22_

- [x] 15. Checkpoint - Ensure all tests pass
  - Ensure all tests pass, ask the user if questions arise.

## Notes

- Tasks marked with `*` are optional and can be skipped for faster MVP
- Each task references specific requirements for traceability
- Checkpoints ensure incremental validation
- Property tests validate universal correctness properties from the design document
- Unit tests validate specific examples and edge cases
- All property-based tests use in-process compilation (no process spawning per iteration) per the test-process-spawning policy
- The implementation language is C# (matching the existing compiler codebase)
- The grammar uses the Storm source generator — changes to `Mist.storm` trigger code generation

## Task Dependency Graph

```json
{
  "waves": [
    { "id": 0, "tasks": ["1.1"] },
    { "id": 1, "tasks": ["1.2"] },
    { "id": 2, "tasks": ["3.1", "4.1", "10.1"] },
    { "id": 3, "tasks": ["3.2", "4.2", "4.3"] },
    { "id": 4, "tasks": ["6.1", "6.2", "6.3"] },
    { "id": 5, "tasks": ["6.4", "6.5", "6.6", "6.8"] },
    { "id": 6, "tasks": ["6.7", "8.1"] },
    { "id": 7, "tasks": ["8.2", "8.3"] },
    { "id": 8, "tasks": ["11.1", "12.1"] },
    { "id": 9, "tasks": ["11.2"] },
    { "id": 10, "tasks": ["14.1"] },
    { "id": 11, "tasks": ["14.2"] },
    { "id": 12, "tasks": ["14.3", "14.4"] }
  ]
}
```
