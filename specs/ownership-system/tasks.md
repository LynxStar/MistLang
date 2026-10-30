# Implementation Plan: Ownership System

## Overview

This plan implements a compile-time ownership and reference system for the Mist language. The implementation proceeds bottom-up through the compiler pipeline: lexer keywords → grammar rules → semantic model extensions → ownership analyzer (flow-sensitive analysis) → pretty printer → TextMate grammar → sample application. All enforcement is compile-time; the C# emitter requires no changes.

## Tasks

- [x] 1. Add ownership keywords and grammar rules
  - [x] 1.1 Add `OwnershipSpecifier` grammar rule to `Mist.storm`
    - Add the `OwnershipSpecifier` rule with four alternatives: `OwnedSpecifier`, `FrozenRefSpecifier`, `ReadonlyRefSpecifier`, `RefSpecifier`
    - Integrate `ownership:OwnershipSpecifier?` into variable declarations, parameters, return types, and field declarations
    - Add `OWNED` to the `Modifier` rule for type declarations (`owned class`)
    - Verify the Storm source generator produces the expected AST node types
    - _Requirements: 1.4, 1.5, 1.6, 1.7, 1.8, 1.9, 2.1, 2.2, 2.3, 2.4, 2.5, 8.1, 8.2_

  - [x] 1.2 Extend `MistPrettyPrinter` to emit ownership specifiers
    - Emit `owned`, `ref`, `readonly ref`, `frozen ref` before type references in variable declarations
    - Emit ownership specifiers before parameter types in method signatures
    - Emit `owned` before return types where applicable
    - Emit `owned` as a modifier on type declarations
    - _Requirements: 20.1, 20.2, 20.3_

  - [x]* 1.3 Write property test for ownership-annotated round-trip parsing
    - **Property 1: Ownership-annotated round-trip parsing**
    - Create `MistOwnershipGenerator` in `Test/Mist.Tests/Generators/` with generators for ownership specifiers, annotated declarations, and owned type declarations
    - Create `Test/Mist.Tests/Parsing/OwnershipRoundTripPropertyTests.cs`
    - Verify parse → pretty-print → parse → pretty-print produces identical output
    - Use shared `CompilerDriver` for in-process compilation (no process spawning)
    - **Validates: Requirements 20.1, 20.2, 20.3, 2.1, 2.2, 2.3, 2.4, 2.5**

- [x] 2. Extend semantic model with ownership metadata
  - [x] 2.1 Add `OwnershipTier` enum and symbol/type extensions
    - Create `Source/Mist/Semantics/Model/OwnershipTier.cs` with enum values: `Unrestricted`, `ReadonlyRef`, `FrozenRef`, `Ref`, `Owned`
    - Add `OwnershipTier OwnershipTier` property to the `Symbol` class
    - Add `bool IsOwnedType` property to type declarations
    - _Requirements: 3.1, 8.1, 8.2_

  - [x] 2.2 Wire `SemanticModelBuilder` to populate ownership metadata from AST
    - Read `OwnershipSpecifier` nodes from variable declarations, parameters, return types, and fields
    - Map AST specifier nodes to `OwnershipTier` enum values on symbols
    - Set `IsOwnedType = true` for type declarations with the `OWNED` modifier
    - _Requirements: 1.4, 1.5, 1.6, 1.7, 2.1, 2.2, 2.3, 8.1, 8.2_

- [x] 3. Checkpoint - Ensure all tests pass
  - Ensure all tests pass, ask the user if questions arise.

- [x] 4. Implement ownership analyzer — declaration validation
  - [x] 4.1 Create `OwnershipAnalyzer` skeleton implementing `IAnalyzer`
    - Create `Source/Mist/Analysis/Builtin/OwnershipAnalyzer.cs`
    - Implement `IAnalyzer` with `AnalyzerId = "OWNERSHIP"`
    - Register the analyzer in the `CompilerDriver` analyzer list (after `ReadonlyInferenceEngine`)
    - Define all 15 `DiagnosticId` constants (`MIST_OWN_USE_AFTER_MOVE (historical)`, `MIST_OWN_REF_ESCAPE (historical)`, etc.)
    - _Requirements: 16.1, 16.2, 16.3, 16.4, 16.5, 16.6, 16.7_

  - [x] 4.2 Implement owned type declaration validation
    - Check that all reference-type fields in an `owned class`/`owned struct` are themselves `owned` or of an owned type (emit `MIST_OWN_FIELD_NOT_OWNED (historical)`)
    - Check that owned types do not implement interfaces or satisfy traits (emit `MIST_OWN_INTERFACE_FORBIDDEN (historical)`)
    - Check that instances of owned types are always held in `owned` variables (emit `MIST_OWN_TYPE_REQUIRES_OWNED (historical)`)
    - Reject `owned` on interface/trait declarations (emit `MIST_OWN_INTERFACE_FORBIDDEN (historical)`)
    - _Requirements: 8.3, 8.4, 8.5, 8.6, 8.7, 8.8, 8.9_

  - [x]* 4.3 Write property test for owned type field constraints
    - **Property 7: Owned type field constraints**
    - Create `Test/Mist.Tests/Analysis/OwnedTypeConstraintPropertyTests.cs`
    - Generate owned type declarations with varying field configurations
    - Verify diagnostics are emitted for non-owned reference-type fields, interface implementations, and non-owned variable holders
    - Use shared `CompilerDriver` for in-process compilation
    - **Validates: Requirements 8.3, 8.4, 8.5, 8.6, 8.7, 8.8, 8.9, 16.5**

  - [x] 4.4 Implement ownership signature matching validation
    - Check interface method implementations match ownership specifiers on parameters exactly (emit `MIST_OWN_SIGNATURE_MISMATCH (historical)`)
    - Check return type ownership specifiers match between interface and implementation
    - Check structural trait matching for ownership specifier consistency
    - Reject non-owned specifiers on return types (emit `MIST_OWN_INVALID_RETURN_SPEC (historical)`)
    - _Requirements: 9.1, 9.2, 9.3, 9.4, 2.5_

  - [x]* 4.5 Write property test for ownership signature matching
    - **Property 8: Ownership signature matching**
    - Create `Test/Mist.Tests/Analysis/OwnershipSignaturePropertyTests.cs`
    - Generate interface/implementation pairs with matching and mismatching ownership specifiers
    - Verify correct acceptance/rejection
    - Use shared `CompilerDriver` for in-process compilation
    - **Validates: Requirements 9.1, 9.2, 9.3, 9.4**

- [x] 5. Checkpoint - Ensure all tests pass
  - Ensure all tests pass, ask the user if questions arise.

- [x] 6. Implement ownership analyzer — flow-sensitive analysis
  - [x] 6.1 Implement `VariableOwnershipState` tracking and `BorrowGraph`
    - Create `VariableOwnershipState` enum (`Live`, `Moved`, `LentRef`, `LentFrozen`, `LentReadonly`)
    - Create `BorrowEntry` record tracking lender, borrower, tier, and scope depth
    - Create `MethodFlowState` class with variable states map, move locations, active borrows, and scope depth
    - Implement scope-exit logic to release borrows and restore lender states
    - _Requirements: 11.1, 11.2, 11.3, 13.6, 13.7_

  - [x] 6.2 Implement move tracking and use-after-move detection
    - Track owned variable moves (assignment to another owned variable, passing to owned parameter)
    - Mark source variable as `Moved` after transfer
    - Emit `MIST_OWN_USE_AFTER_MOVE (historical)` on access to moved variables
    - Handle conditional moves: clone state at branch points, merge conservatively at joins
    - Emit `MIST_OWN_CONDITIONAL_MOVE (historical)` for access in branches where variable was moved
    - Emit `MIST_OWN_LOOP_MOVE (historical)` for moves in loops without re-assignment
    - _Requirements: 10.1, 10.2, 10.3, 10.4, 10.5, 10.6, 7.4, 7.5, 7.9_

  - [x]* 6.3 Write property test for move tracking and use-after-move
    - **Property 4: Move tracking and use-after-move detection**
    - Create `Test/Mist.Tests/Analysis/OwnershipMovePropertyTests.cs`
    - Generate programs with move patterns (simple moves, conditional moves, loop moves, re-assignments)
    - Verify correct diagnostics for use-after-move and correct acceptance for valid patterns
    - Use shared `CompilerDriver` for in-process compilation
    - **Validates: Requirements 7.4, 7.5, 7.9, 10.1, 10.2, 10.3, 10.4, 10.5, 10.7, 16.1**

  - [x] 6.4 Implement lending compatibility and borrow checking
    - Enforce the lending compatibility matrix: owned→{owned,ref,readonly ref,frozen ref}, ref→{ref,readonly ref,frozen ref}, readonly ref→{readonly ref}, frozen ref→{frozen ref,readonly ref}, unrestricted→{readonly ref,unrestricted}
    - Emit `MIST_OWN_INCOMPATIBLE_LEND (historical)` for invalid tier combinations
    - Emit `MIST_OWN_UNRESTRICTED_TO_REF (historical)` when unrestricted is passed to ref/frozen ref/owned
    - Handle value types: always satisfy any tier requirement
    - _Requirements: 3.5, 3.6, 3.7, 3.8, 12.1, 12.2, 12.3, 12.4, 12.5, 12.6, 14.1, 14.2, 15.1, 15.3_

  - [x]* 6.5 Write property test for lending compatibility matrix
    - **Property 2: Lending compatibility matrix**
    - Create `Test/Mist.Tests/Analysis/OwnershipLendingPropertyTests.cs`
    - Generate (source tier, target tier, program) triples covering all combinations
    - Verify acceptance for permitted pairs and rejection for forbidden pairs
    - Verify value types always pass
    - Use shared `CompilerDriver` for in-process compilation
    - **Validates: Requirements 3.5, 3.6, 3.7, 3.8, 6.5, 6.6, 6.7, 7.6, 7.7, 7.8, 12.1, 12.2, 12.3, 12.4, 12.5, 12.6, 14.1, 14.2, 15.1, 15.3**

  - [x] 6.6 Implement non-storability enforcement
    - Reject assignment of `ref`, `readonly ref`, or `frozen ref` to fields or collection elements (emit `MIST_OWN_NOT_STORABLE (historical)`)
    - Reject returning `ref`, `readonly ref`, or `frozen ref` from methods (emit `MIST_OWN_REF_ESCAPE (historical)`)
    - Verify ref lifetime does not exceed declaring scope
    - _Requirements: 4.4, 4.8, 5.2, 5.8, 6.2, 6.9, 11.1, 11.4, 11.5_

  - [x]* 6.7 Write property test for non-storability enforcement
    - **Property 3: Non-storability enforcement**
    - Create `Test/Mist.Tests/Analysis/OwnershipStorabilityPropertyTests.cs`
    - Generate programs attempting to store refs in fields, collections, and return positions
    - Verify diagnostics are emitted for all non-storable violations
    - Use shared `CompilerDriver` for in-process compilation
    - **Validates: Requirements 4.4, 4.8, 5.2, 5.8, 6.2, 6.9, 11.1, 11.4, 11.5**

  - [x] 6.8 Implement mutation blocking under frozen ref
    - When a frozen ref is outstanding from an owner, block mutation through the owner (emit `MIST_OWN_MUTATION_BLOCKED (historical)`)
    - When a frozen ref is outstanding from a ref holder, block mutation through the holder
    - Restore mutation access when all frozen refs are returned (scope exit)
    - _Requirements: 5.3, 5.4, 5.6, 6.7, 7.8, 7.11, 13.1, 13.2, 13.6_

  - [x]* 6.9 Write property test for mutation blocking under frozen ref
    - **Property 5: Mutation blocking under frozen ref**
    - Create `Test/Mist.Tests/Analysis/OwnershipFrozenBlockingPropertyTests.cs`
    - Generate programs with frozen ref loans and mutation attempts during/after the loan
    - Verify mutation is blocked during loan and restored after scope exit
    - Use shared `CompilerDriver` for in-process compilation
    - **Validates: Requirements 5.3, 5.4, 5.6, 6.7, 7.8, 7.11, 13.1, 13.2, 13.6**

  - [x] 6.10 Implement ref exclusivity enforcement
    - When a ref is outstanding from an owner, block all owner access (emit `MIST_OWN_EXCLUSIVE_VIOLATION (historical)`)
    - Enforce only one ref outstanding from an owner at a time (emit `MIST_OWN_EXCLUSIVE_VIOLATION (historical)`)
    - Restore full access when the ref is returned (scope exit)
    - Handle re-lending: original holder loses access until re-lend returns
    - _Requirements: 6.3, 6.4, 6.5, 7.12, 13.3, 13.7, 16.7_

  - [x]* 6.11 Write property test for ref exclusivity
    - **Property 6: Ref exclusivity**
    - Create `Test/Mist.Tests/Analysis/OwnershipExclusivityPropertyTests.cs`
    - Generate programs with single and multiple ref loans, owner access during loans
    - Verify correct diagnostics for exclusivity violations and correct acceptance for valid patterns
    - Use shared `CompilerDriver` for in-process compilation
    - **Validates: Requirements 6.3, 6.4, 7.12, 13.3, 13.7, 16.7**

- [x] 7. Checkpoint - Ensure all tests pass
  - Ensure all tests pass, ask the user if questions arise.

- [x] 8. Verify emission erasure and update TextMate grammar
  - [x] 8.1 Verify C# emitter ignores ownership annotations (no code changes expected)
    - Confirm `OwnershipSpecifier` AST nodes are not visited during C# emission
    - Confirm variables emit as standard C# references regardless of ownership tier
    - Confirm moves emit as standard assignments (no nulling of source)
    - If any emitter code visits ownership nodes, remove that visitation
    - _Requirements: 17.1, 17.2, 17.3, 17.4, 17.5_

  - [x]* 8.2 Write property test for emission erasure
    - **Property 9: Emission erasure**
    - Create `Test/Mist.Tests/Backend/OwnershipErasurePropertyTests.cs`
    - Generate valid programs with and without ownership annotations
    - Compile both versions in-process and compare emitted C# output
    - Verify identical output (no runtime tracking, no state fields, no assertions)
    - Use shared `CompilerDriver` for in-process compilation
    - **Validates: Requirements 17.1, 17.2, 17.3, 17.4, 17.5**

  - [x] 8.3 Update TextMate grammar for ownership keywords
    - Add `owned`, `frozen`, and `ref` to the `storage.modifier.mist` scope in `Editor/mist-syntax-highlighting/syntaxes/mist.tmLanguage.json`
    - Ensure keywords match at word boundaries only
    - Verify `readonly ref` and `frozen ref` highlight as compound modifiers
    - _Requirements: 18.1, 18.2, 18.3, 18.4, 18.5_

- [x] 9. Create sample application
  - [x] 9.1 Create `samples/OwnershipSystem/` sample project
    - Create `samples/OwnershipSystem/mist.toml` project file
    - Create `samples/OwnershipSystem/Program.mist` with `public static void RunDemo()`
    - Demonstrate: unrestricted default behavior, `readonly ref` parameters and locals, `frozen ref` with mutation blocking, `ref` borrowing with re-lending, `owned` with move semantics, owned type declarations with field restrictions, lending compatibility, value types satisfying all tiers, tier transitions
    - Each pattern prints labeled output (e.g., `[readonly-ref]`, `[frozen-blocks-mutation]`)
    - _Requirements: 19.1, 19.2, 19.3, 19.5_

  - [x] 9.2 Integrate sample into `AllFeatures` orchestrator
    - Add `OwnershipSystem.Program.RunDemo()` call to `samples/AllFeatures/Program.mist`
    - Add project reference to `samples/AllFeatures/mist.toml`
    - _Requirements: 19.4_

  - [x]* 9.3 Write integration test for sample compilation
    - Create `Test/Mist.Tests/Integration/OwnershipSampleCompileTest.cs`
    - Compile and run the sample project, verify exit code 0 and no warnings
    - This is the only test that spawns a process (1 spawn total)
    - _Requirements: 19.5_

- [x] 10. Final checkpoint - Ensure all tests pass
  - Ensure all tests pass, ask the user if questions arise.

## Notes

- Tasks marked with `*` are optional and can be skipped for faster MVP
- Each task references specific requirements for traceability
- Checkpoints ensure incremental validation
- Property tests validate universal correctness properties from the design document
- Unit tests validate specific examples and edge cases
- All property tests use the shared `CompilerDriver` for in-process compilation (no per-iteration process spawning)
- The only process spawn is in the sample integration test (task 9.3): 1 compile + 1 run
- The C# emitter requires no code changes — task 8.1 is a verification/confirmation step
- The `OwnershipAnalyzer` runs after `ReadonlyInferenceEngine` and before/alongside `TypeCheckerAnalyzer`

## Task Dependency Graph

```json
{
  "waves": [
    { "id": 0, "tasks": ["1.1", "2.1"] },
    { "id": 1, "tasks": ["1.2", "2.2"] },
    { "id": 2, "tasks": ["1.3", "4.1"] },
    { "id": 3, "tasks": ["4.2", "4.4"] },
    { "id": 4, "tasks": ["4.3", "4.5", "6.1"] },
    { "id": 5, "tasks": ["6.2", "6.4", "6.6", "6.8", "6.10"] },
    { "id": 6, "tasks": ["6.3", "6.5", "6.7", "6.9", "6.11"] },
    { "id": 7, "tasks": ["8.1", "8.3"] },
    { "id": 8, "tasks": ["8.2", "9.1"] },
    { "id": 9, "tasks": ["9.2", "9.3"] }
  ]
}
```
