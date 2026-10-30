# Implementation Plan: Deep Ownership Types

## Overview

This plan implements deep ownership guarantees for the Mist language by: (1) adding a new `OwnershipInferenceEngine` for struct inference, (2) modifying the `OwnershipAnalyzer` to implement the semantic shift and new validation passes, (3) extending `MethodFlowState` with binding-kind tracking and truth table enforcement, (4) annotating standard library primitives, and (5) creating a comprehensive sample application. All ownership is compile-time only — C# emission erases all annotations.

## Tasks

- [x] 1. Add AccessKind enum and BindingKind enum
  - [x] 1.1 Create `AccessKind` enum in `Source/Mist/Analysis/Builtin/`
    - Define enum with values: Read, Write, LiveView, Freeze, Borrow, Move
    - Add XML doc comments explaining each access kind's role in the truth table
    - _Requirements: 11.1–11.5_

  - [x] 1.2 Create `BindingKind` enum in `Source/Mist/Semantics/`
    - Define enum with values: Owned, Unrestricted
    - Add XML doc comments explaining the distinction
    - _Requirements: 4.1, 4.2, 7.1, 8.1_

- [x] 2. Implement OwnershipInferenceEngine
  - [x] 2.1 Create `OwnershipInferenceEngine` class in `Source/Mist/Semantics/OwnershipInferenceEngine.cs`
    - Model after `ReadonlyInferenceEngine` structure
    - Implement `RunInference(ISemanticModel model)` static method
    - Use three-state visited set (Unknown, InProgress, Resolved) for cycle detection
    - Infer `IsOwnedType = true` for structs where all fields are owned types
    - Zero-field structs are vacuously owned
    - Skip classes (only structs eligible for inference)
    - Skip generic structs with type-parameter fields
    - Conservatively mark cyclic structs as NOT owned
    - _Requirements: 3.1, 3.2, 3.3, 3.5, 3.6, 3.7_

  - [x] 2.2 Write property test for ownership inference (Property 3)
    - **Property 3: Ownership Inference for Structs**
    - Generate random struct declarations with various field configurations
    - Compile in-process via shared `CompilerDriver`
    - Verify `IsOwnedType` matches expectation based on field types
    - Test zero-field structs, all-owned-field structs, mixed-field structs, class declarations (never inferred)
    - **Validates: Requirements 3.1, 3.2, 3.3, 3.5, 3.6**

- [x] 3. Annotate standard library primitives with `owned` modifier
  - [x] 3.1 Update standard library type definitions to add `owned` modifier
    - Add `owned` to: Int32, Int64, Int16, UInt32, UInt64, UInt16, Byte, SByte, Single, Double, Decimal, Boolean, Char, String, Regex
    - Verify these types pass the owned-type field restriction (zero fields = vacuously satisfied)
    - _Requirements: 2.1, 2.2, 2.3, 2.4_

- [x] 4. Implement semantic shift — remove ValidateOwnedTypeUsages
  - [x] 4.1 Remove `ValidateOwnedTypeUsages` pass from `OwnershipAnalyzer.Analyze()`
    - Remove the call to `ValidateOwnedTypeUsages` in the `Analyze` method
    - Remove or mark as dead the `ValidateOwnedTypeUsages` method body
    - Update pass comments to reflect new pass numbering
    - _Requirements: 8.1, 8.2, 8.3, 8.4_

  - [x] 4.2 Write property test for semantic shift (Property 4)
    - **Property 4: Semantic Shift — Owned Types Usable in Any Binding Context**
    - Generate owned type instances in both owned and unrestricted binding contexts
    - Compile in-process, verify no errors reported
    - Test passing owned type instances to unrestricted parameters
    - **Validates: Requirements 8.1, 8.2, 8.3, 8.4, 9.2, 9.3**

- [x] 5. Checkpoint — Ensure all tests pass
  - Ensure all tests pass, ask the user if questions arise.

- [x] 6. Implement owned binding type validation (Pass 2b)
  - [x] 6.1 Add Pass 2b to `OwnershipAnalyzer` — owned binding type validation
    - Verify that `owned` bindings hold values of owned types (report `MIST_OWN_TYPE_REQUIRES_OWNED (historical)` if not)
    - Verify initializer yields unique ownership: `new`, owned-returning function, or move from another owned binding
    - Report error if initializer does not yield unique ownership
    - Wire into `Analyze()` method after Pass 1
    - _Requirements: 4.3, 4.4, 4.5, 4.6_

  - [x] 6.2 Write property test for owned binding validation (Property 5)
    - **Property 5: Owned Binding Requires Owned Type and Unique Initializer**
    - Generate owned bindings with valid/invalid types and initializers
    - Verify correct accept/reject behavior
    - **Validates: Requirements 4.3, 4.4, 4.5, 4.6**

- [x] 7. Implement owned field modifier validation (Pass 2c)
  - [x] 7.1 Add Pass 2c to `OwnershipAnalyzer` — owned field modifier validation
    - Verify fields marked `owned` have an owned type (report error if not)
    - Enforce assignment restrictions: source must be from owned binding, constructor call, or owned-returning function
    - Enforce access restrictions based on containing instance's binding kind
    - `readonly ref` access always permitted on owned fields
    - `borrowed`/`frozen` access permitted only when containing instance is in owned binding
    - `borrowed`/`frozen` access rejected when containing instance is in unrestricted binding
    - _Requirements: 10.1, 10.2, 10.3, 10.4, 10.5, 10.6, 10.7_

  - [x] 7.2 Write property test for owned field modifier rules (Property 10)
    - **Property 10: Owned Field Modifier Rules**
    - Generate owned field scenarios with various access patterns
    - Verify correct accept/reject for each rule
    - **Validates: Requirements 10.2, 10.3, 10.4, 10.5, 10.6**

- [x] 8. Extend MethodFlowState with BindingKind tracking
  - [x] 8.1 Add `BindingKinds` dictionary to `MethodFlowState`
    - Map each tracked variable to its `BindingKind` (Owned or Unrestricted)
    - Populate binding kind during variable declaration analysis
    - Ensure owned parameters are registered as `BindingKind.Owned`
    - _Requirements: 4.1, 4.2, 6.1, 7.1_

- [x] 9. Implement truth table enforcement in flow-sensitive analysis
  - [x] 9.1 Implement truth table lookup in `OwnershipAnalyzer` Pass 4
    - Add static truth table dictionary mapping `(VariableOwnershipState, AccessKind)` to `bool`
    - Classify each access attempt into an `AccessKind`
    - Look up permission in truth table before allowing the action
    - Report appropriate diagnostic when action is rejected
    - Add `LentReadonly` state handling (currently only `LentFrozen` and `LentRef` tracked)
    - _Requirements: 11.1, 11.2, 11.3, 11.4, 11.5_

  - [x] 9.2 Write property test for truth table enforcement (Property 8)
    - **Property 8: Truth Table Enforcement**
    - Generate all (state, action) pairs
    - Construct programs that put a variable in each state and attempt each action
    - Compile in-process, verify correct accept/reject per truth table
    - **Validates: Requirements 11.1, 11.2, 11.3, 11.4, 11.5, 12.1, 12.2, 12.3, 12.7, 12.8**

- [x] 10. Implement unrestricted binding restrictions
  - [x] 10.1 Add unrestricted binding restriction checks to Pass 4
    - Reject taking `borrowed` or `frozen` reference to any field in the object graph of an unrestricted binding
    - Permit taking `readonly ref` to direct (top-level) fields only
    - Reject passing unrestricted binding to `borrowed` or `frozen` parameter
    - Use `BindingKind` from `MethodFlowState` to determine which rules apply
    - _Requirements: 7.2, 7.3, 7.4, 12.5, 12.6_

  - [x] 10.2 Write property test for unrestricted binding restrictions (Property 9)
    - **Property 9: Unrestricted Binding Restrictions**
    - Generate unrestricted bindings with various borrow attempts
    - Verify correct accept/reject
    - **Validates: Requirements 7.2, 7.3, 7.4, 12.5, 12.6**

- [x] 11. Checkpoint — Ensure all tests pass
  - Ensure all tests pass, ask the user if questions arise.

- [x] 12. Extend move semantics for deep ownership
  - [x] 12.1 Extend move tracking in Pass 4 for owned-to-unrestricted moves
    - Moving owned binding to unrestricted binding marks source as Moved
    - Moving owned binding to another owned binding marks source as Moved
    - Passing owned binding to `owned` parameter marks source as Moved
    - Enforce: move only permitted from Live state
    - _Requirements: 5.1, 5.2, 5.3, 5.4, 5.8_

  - [x] 12.2 Implement control flow move tracking
    - If moved in one branch of conditional but not other, treat as Moved at join point
    - If moved inside loop body without re-assignment in same iteration, report error
    - _Requirements: 5.5, 5.6_

  - [x] 12.3 Write property test for move semantics (Property 6)
    - **Property 6: Move Semantics — Source Becomes Dead**
    - Generate move sequences followed by access
    - Verify use-after-move errors reported correctly
    - **Validates: Requirements 5.1, 5.2, 5.3, 5.4, 12.4**

  - [x] 12.4 Write property test for control flow moves (Property 7)
    - **Property 7: Control Flow Move Tracking**
    - Generate conditional/loop move patterns
    - Verify correct error reporting
    - **Validates: Requirements 5.5, 5.6**

- [x] 13. Implement owned type field restriction validation updates
  - [x] 13.1 Update Pass 1 to handle `owned` field specifier in field restriction check
    - A reference-type field in an owned type is valid if: field type is owned type OR field has `owned` specifier
    - Reject non-storable reference tiers (`readonly ref`, `frozen`, `borrowed`) as fields in owned types
    - Reject `owned` on interface/trait declarations
    - _Requirements: 1.3, 1.4, 1.5, 1.6, 1.7, 1.8, 13.6_

  - [x] 13.2 Write property test for valid fields accepted (Property 1)
    - **Property 1: Owned Type Field Restriction — Valid Fields Accepted**
    - Generate owned type declarations with valid field configs
    - Compile in-process, verify zero `MIST_OWN_FIELD_NOT_OWNED (historical)` diagnostics
    - **Validates: Requirements 1.3, 1.5, 1.6, 2.4, 9.1**

  - [x] 13.3 Write property test for invalid fields rejected (Property 2)
    - **Property 2: Owned Type Field Restriction — Invalid Fields Rejected**
    - Generate owned types with at least one invalid field
    - Compile in-process, verify `MIST_OWN_FIELD_NOT_OWNED (historical)` reported per violating field
    - **Validates: Requirements 1.4, 13.1, 13.6**

- [x] 14. Wire OwnershipInferenceEngine into CompilerDriver
  - [x] 14.1 Integrate `OwnershipInferenceEngine.RunInference()` into the compilation pipeline
    - Call after semantic model is built but before ownership analysis
    - Ensure inference runs before `OwnershipAnalyzer.Analyze()`
    - Verify existing tests still pass with inference active
    - _Requirements: 3.1, 3.2_

- [x] 15. Checkpoint — Ensure all tests pass
  - Ensure all tests pass, ask the user if questions arise.

- [x] 16. Verify C# emission erasure
  - [x] 16.1 Verify emitter handles new ownership patterns correctly
    - Confirm `owned` keyword does not appear in emitted C#
    - Confirm no runtime ownership tracking code is emitted
    - Confirm owned bindings emit as standard C# variable declarations
    - Confirm moves emit as standard assignments (no nulling of source)
    - _Requirements: 14.1, 14.2, 14.3, 14.4, 14.5_

  - [x] 16.2 Write property test for C# emission erasure (Property 11)
    - **Property 11: C# Emission Erasure**
    - Generate programs with owned annotations
    - Compile to C#, verify absence of "owned" in output
    - Verify no runtime tracking code emitted
    - **Validates: Requirements 14.1, 14.2, 14.3, 14.4, 14.5**

- [x] 17. Verify round-trip parsing
  - [x] 17.1 Verify owned declarations survive parse-print-parse cycles
    - Test `owned class Foo`, `owned struct Bar` round-trip
    - Test `owned var x = ...`, `owned Foo x = ...` round-trip
    - Confirm no parse errors on re-parse of pretty-printed output
    - _Requirements: 17.1, 17.2, 17.3_

  - [x] 17.2 Write property test for round-trip parsing (Property 12)
    - **Property 12: Round-Trip Parsing**
    - Generate random owned declarations
    - Parse, pretty-print, parse again, compare ASTs
    - **Validates: Requirements 17.1, 17.2, 17.3**

- [x] 18. Create sample application
  - [x] 18.1 Create `samples/DeepOwnershipTypes/mist.toml` project file
    - Set `name = "DeepOwnershipTypes"`
    - _Requirements: 16.1_

  - [x] 18.2 Create `samples/DeepOwnershipTypes/Program.mist` sample
    - Implement `public static void RunDemo()` entry point
    - Demonstrate owned type declarations with field restrictions
    - Demonstrate owned bindings with move semantics
    - Demonstrate unrestricted bindings to owned types (permitted and restricted operations)
    - Demonstrate ownership inference for structs
    - Demonstrate primitive bootstrapping (owned types containing only primitives)
    - Demonstrate nested owned types (owned type containing another owned type)
    - Demonstrate interaction with existing lending tiers (borrowed, frozen, readonly ref from owned bindings)
    - Demonstrate the semantic shift (owned type in unrestricted binding is permitted)
    - Demonstrate moving owned bindings into functions
    - Demonstrate truth table actions for Live, LentReadonly, LentFrozen states
    - Demonstrate borrowed recipient perspective
    - Print unique labeled prefix for each pattern (e.g., `[owned-type-fields]`, `[owned-binding-move]`)
    - _Requirements: 16.1, 16.2, 16.3, 16.4, 16.5, 16.7_

  - [x] 18.3 Integrate into AllFeatures orchestrator
    - Add `DeepOwnershipTypes.Program.RunDemo()` call to `samples/AllFeatures/Program.mist`
    - Add `[[references]]` entry with `path = "../DeepOwnershipTypes"` to `samples/AllFeatures/mist.toml`
    - _Requirements: 16.6_

- [x] 19. Final checkpoint — Ensure all tests pass and sample compiles
  - Ensure all tests pass, ask the user if questions arise.
  - Verify the sample application compiles and runs without errors or warnings.

## Notes

- Tasks marked with `*` are optional and can be skipped for faster MVP
- Each task references specific requirements for traceability
- Checkpoints ensure incremental validation
- Property tests validate universal correctness properties from the design document
- Unit tests validate specific examples and edge cases
- All property tests use in-process `CompilerDriver` (no per-iteration process spawning) per the test-process-spawning policy
- The `OwnershipInferenceEngine` is modeled after the existing `ReadonlyInferenceEngine`
- The semantic shift (removing `ValidateOwnedTypeUsages`) is the key behavioral change — existing tests that relied on that check will need updating
- C# emission requires zero changes since ownership is already erased; task 16 is verification only

## Task Dependency Graph

```json
{
  "waves": [
    { "id": 0, "tasks": ["1.1", "1.2", "3.1"] },
    { "id": 1, "tasks": ["2.1", "4.1", "8.1"] },
    { "id": 2, "tasks": ["2.2", "4.2", "6.1", "7.1", "14.1"] },
    { "id": 3, "tasks": ["6.2", "7.2", "9.1", "10.1"] },
    { "id": 4, "tasks": ["9.2", "10.2", "12.1", "13.1"] },
    { "id": 5, "tasks": ["12.2", "12.3", "13.2", "13.3"] },
    { "id": 6, "tasks": ["12.4", "16.1", "17.1"] },
    { "id": 7, "tasks": ["16.2", "17.2", "18.1"] },
    { "id": 8, "tasks": ["18.2"] },
    { "id": 9, "tasks": ["18.3"] }
  ]
}
```
