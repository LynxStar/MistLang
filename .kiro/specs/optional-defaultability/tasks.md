# Implementation Plan: Optional Defaultability

## Overview

This plan implements optional defaultability for the Mist compiler — making it so types with constructors are no longer required to be defaultable. The implementation spans parser changes (default constraint, enum default modifier), semantic model updates (ComputeDefaultability rewrite, enum/union defaultability), type checker additions (definite assignment analysis, constraint satisfaction), emitter gating, standard library constraint, TextMate grammar, book documentation, and the sample application.

## Tasks

- [ ] 1. Parser: Add default constraint and enum default modifier
  - [ ] 1.1 Add `DefaultConstraintConstraintNode` to constraint hierarchy and parse `default` in constraint position
    - Add the new node class to the AST hierarchy (Storm grammar or manual node)
    - Modify the constraint parsing logic to recognize `default` as a keyword in constraint position (after `:` in `where` clause)
    - Ensure it doesn't conflict with `default` in expression contexts
    - _Requirements: 5.1, 5.2_

  - [ ] 1.2 Add `IsDefault` flag to `EnumMemberDeclarationNode` and parse `default` modifier before enum variant names
    - Modify enum member parsing to recognize `default` keyword before the variant identifier
    - Store the boolean on the AST node
    - Ensure multiple `default` modifiers on different variants parse without error (semantic check later)
    - _Requirements: 6.2_

  - [ ] 1.3 Update pretty-printer to emit `default` in constraint clauses and enum variant modifier
    - Pretty-print `DefaultConstraintConstraintNode` as `default` in the constraint list
    - Pretty-print `default` before enum variant names when `IsDefault` is true
    - _Requirements: 5.7, 5.8_

  - [ ]* 1.4 Write property test: Default constraint round-trip (Property 3)
    - **Property 3: Default constraint round-trip**
    - Generate valid Mist ASTs with `where T : default` constraints, pretty-print, re-parse, pretty-print again, verify idempotence
    - Place in `Test/Mist.Tests/Parsing/DefaultConstraintRoundTripPropertyTests.cs`
    - **Validates: Requirements 5.8**

- [ ] 2. Semantic Model: Modify ComputeDefaultability for constructor-aware classification
  - [ ] 2.1 Modify `ComputeDefaultabilityForType` (ClassType and StructType overloads) to check for user-defined constructors
    - In `SemanticModelBuilder.cs`, after the explicit default block check, add constructor-presence detection
    - If type has constructors and no default block → set `NotDefaultable` WITHOUT emitting `MIST_DEFAULT_REQUIRES_BINDING`
    - If type has constructors and has default block → keep `Explicit` (existing path)
    - If type has no constructors and has non-defaultable fields → emit `MIST_DEFAULT_REQUIRES_BINDING` as before
    - Update diagnostic message to suggest adding a constructor as an alternative
    - _Requirements: 1.1, 1.2, 1.3, 4.1, 4.2, 4.3, 4.4_

  - [ ] 2.2 Add `HasDefaultConstraint` to `TypeParameterConstraints` and wire it from parser
    - Add `bool HasDefaultConstraint` property to the constraints record/class
    - In the semantic model builder's constraint resolution pass, detect `DefaultConstraintConstraintNode` and set the flag
    - _Requirements: 5.3_

  - [ ] 2.3 Add `IsDefault` to `EnumMemberInfo` and implement `ComputeDefaultabilityForEnum`
    - Add `IsDefault` bool field to `EnumMemberInfo`
    - Add `Defaultability` and `DefaultMember` properties to `EnumType`
    - Wire `IsDefault` from the parsed AST node during enum member registration
    - Implement `ComputeDefaultabilityForEnum`: scan members for `IsDefault`, set `Explicit` if exactly one, emit `MIST_ENUM_MULTIPLE_DEFAULTS` if more than one, set `NotDefaultable` if zero
    - Call `ComputeDefaultabilityForEnum` from the `ComputeDefaultability` pass
    - _Requirements: 6.1, 6.3, 6.4, 6.7_

  - [ ] 2.4 Implement union type defaultability based on void membership
    - In the defaultability/type-checking logic (`IsTypeDefaultable` or equivalent), add a check for `UnionType`: defaultable iff constituents contain `VoidType`
    - _Requirements: 7.1, 7.2_

  - [ ]* 2.5 Write property tests: Classification (Properties 1, 2)
    - **Property 1: Constructor presence suppresses defaultability diagnostic**
    - **Property 2: Constructor-less types with non-defaultable fields produce an error**
    - Place in `Test/Mist.Tests/Semantics/OptionalDefaultabilityClassificationPropertyTests.cs`
    - Use in-process `SemanticModelBuilder` directly (no process spawning)
    - Generate random type shapes with/without constructors, verify status and diagnostics
    - **Validates: Requirements 1.1, 1.2, 1.3, 4.2, 11.1**

  - [ ]* 2.6 Write property tests: Enum defaultability (Property 8)
    - **Property 8: Enum default variant designation**
    - Place in `Test/Mist.Tests/Semantics/EnumDefaultabilityPropertyTests.cs`
    - Generate enums with 0, 1, or multiple `default` variants; verify classification and diagnostics
    - **Validates: Requirements 6.3, 6.4, 6.5, 6.6**

  - [ ]* 2.7 Write property tests: Union defaultability (Property 9)
    - **Property 9: Union defaultability based on void membership**
    - Place in `Test/Mist.Tests/Semantics/UnionDefaultabilityPropertyTests.cs`
    - Generate union types with and without void constituent; verify defaultability classification
    - **Validates: Requirements 7.1, 7.2, 7.3, 7.4**

- [ ] 3. Checkpoint - Ensure all tests pass
  - Ensure all tests pass, ask the user if questions arise.

- [ ] 4. Type Checker: Definite assignment analysis for constructors
  - [ ] 4.1 Implement definite assignment analysis pass for constructor bodies
    - Add a new analysis in `TypeCheckerAnalyzer` that runs on each constructor body
    - Compute initial set: fields with initializers + fields with defaultable types are pre-assigned
    - Forward dataflow: track `this.field = expr` assignments, intersect at control-flow merges
    - At every exit point: verify all fields are in the definitely-assigned set
    - Emit `MIST_FIELD_NOT_DEFINITELY_ASSIGNED` for unassigned non-defaultable fields
    - Add new `DiagnosticId` entries: `FieldNotDefinitelyAssigned`
    - _Requirements: 3.1, 3.2, 3.3, 3.4, 3.5, 3.6_

  - [ ]* 4.2 Write property tests: Definite assignment (Properties 4, 5, 6)
    - **Property 4: Definite assignment completeness** — all fields assigned → no diagnostic
    - **Property 5: Definite assignment error detection** — missing assignment → diagnostic
    - **Property 6: Synthetic default injection for defaultable fields** — defaultable uninitialized fields don't require assignment
    - Place in `Test/Mist.Tests/Semantics/DefiniteAssignmentPropertyTests.cs`
    - Use in-process compilation via shared `CompilerDriver`, generate Mist source strings with various constructor bodies
    - **Validates: Requirements 3.1, 3.3, 3.4, 3.5, 3.6**

- [ ] 5. Type Checker: Default constraint satisfaction and default(T) validation
  - [ ] 5.1 Update `default(T)` validation for type parameters with `default` constraint
    - When validating `default(T)` where `T` is a type parameter, check `HasDefaultConstraint` — if true, allow it
    - Existing `MIST_DEFAULT_ON_TYPE_PARAMETER` behavior unchanged for unconstrained type params
    - _Requirements: 5.4, 5.5_

  - [ ] 5.2 Add constraint satisfaction checking for `where T : default` at instantiation sites
    - When a generic with `where T : default` is instantiated, check that the type argument is defaultable
    - Emit `MIST_DEFAULT_CONSTRAINT_NOT_SATISFIED` if not satisfied
    - Add new `DiagnosticId` entry: `DefaultConstraintNotSatisfied`
    - _Requirements: 5.6, 8.5, 9.2_

  - [ ] 5.3 Update `default(EnumType)` and `default(UnionType)` validation
    - For enums: valid only if `Defaultability == Explicit`; emit `MIST_DEFAULT_NOT_SUPPORTED` otherwise
    - For unions: valid only if contains `VoidType`; emit `MIST_DEFAULT_NOT_SUPPORTED` otherwise
    - _Requirements: 6.5, 6.6, 7.3, 7.4_

  - [ ]* 5.4 Write property tests: Default constraint satisfaction (Property 7)
    - **Property 7: Default constraint satisfaction**
    - Place in `Test/Mist.Tests/Semantics/DefaultConstraintSatisfactionPropertyTests.cs`
    - Generate generic instantiations with defaultable and non-defaultable type args; verify diagnostics
    - **Validates: Requirements 5.6, 9.2**

- [ ] 6. Checkpoint - Ensure all tests pass
  - Ensure all tests pass, ask the user if questions arise.

- [ ] 7. Emitter: Gate __CreateDefault and emit constructors correctly
  - [ ] 7.1 Gate `__CreateDefault()` emission by defaultability status
    - Ensure the emitter skips `__CreateDefault()` for types with `NotDefaultable` status
    - Ensure it still emits for `Inferred` and `Explicit` types (existing behavior, verify no regression)
    - Emit constructors as normal C# constructors for non-defaultable types
    - _Requirements: 12.1, 12.2, 12.3_

  - [ ] 7.2 Emit `__CreateDefault()` for enums with a designated default variant
    - When an `EnumType` has `Defaultability == Explicit`, emit `__CreateDefault()` returning the default variant's backing value
    - _Requirements: 12.6_

  - [ ] 7.3 Emit `default` constraint as a C# comment
    - When emitting generic type/method declarations with `HasDefaultConstraint`, add a comment: `// where T : default (Mist constraint — enforced at compile time)`
    - _Requirements: 12.5_

  - [ ] 7.4 Emit both constructors and `__CreateDefault()` for types with both
    - When a type has constructors AND is defaultable (`Explicit`), emit both paths
    - _Requirements: 12.4_

  - [ ]* 7.5 Write property tests: __CreateDefault emission gating (Property 10)
    - **Property 10: __CreateDefault emission is gated by defaultability**
    - Place in `Test/Mist.Tests/Backend/CreateDefaultEmissionPropertyTests.cs`
    - Compile types in-process, inspect emitted C# source for presence/absence of `__CreateDefault`
    - **Validates: Requirements 12.1, 12.2**

  - [ ]* 7.6 Write property tests: Constructor + default coexistence (Property 11)
    - **Property 11: Both constructor and default block coexistence**
    - Place in `Test/Mist.Tests/Backend/ConstructorDefaultCoexistencePropertyTests.cs`
    - Compile types with both constructors and default blocks, verify both `__CreateDefault` and constructor appear in output
    - **Validates: Requirements 4.1, 12.4**

- [ ] 8. Inheritance defaultability and remaining diagnostics
  - [ ] 8.1 Implement inheritance defaultability rules
    - Derived with no constructors + non-defaultable base → emit `MIST_DEFAULT_BASE_NOT_DEFAULTABLE`
    - Derived with constructors + non-defaultable base → allow without error (constructor handles init)
    - Derived with constructors + default block + non-defaultable base → emit `MIST_DEFAULT_BASE_NOT_DEFAULTABLE`
    - _Requirements: 10.1, 10.2, 10.3, 10.4, 10.5_

  - [ ]* 8.2 Write property tests: Inheritance defaultability (Property 12)
    - **Property 12: Inheritance defaultability rules**
    - Place in `Test/Mist.Tests/Semantics/InheritanceDefaultabilityPropertyTests.cs`
    - Generate base/derived type combinations with/without constructors and default blocks
    - **Validates: Requirements 10.3, 10.4**

- [ ] 9. Standard Library: Add `where TValue : default` constraint to Array
  - [ ] 9.1 Update `Source/Standard/Bootstrap/Array.mist` to add the `default` constraint on `TValue`
    - Change declaration to: `intrinsic public class Array<TValue, Length:ZeroOrPositiveInt> : IEnumerable<TValue> where TValue : default`
    - _Requirements: 9.1, 9.2, 9.3_

- [ ] 10. Checkpoint - Ensure all tests pass
  - Ensure all tests pass, ask the user if questions arise.

- [ ] 11. TextMate Grammar: Highlight `default` modifier on enum variants and in constraint position
  - [ ] 11.1 Update `Editor/mist-syntax-highlighting/syntaxes/mist.tmLanguage.json`
    - Add pattern for `default` as `storage.modifier.mist` when appearing before an enum variant name
    - Add pattern for `default` in constraint position (`where T : default`) with appropriate scope
    - Ensure existing `default` keyword highlighting in expression contexts is unchanged
    - _Requirements: 13.1, 13.2, 13.3_

- [ ] 12. Sample Application: OptionalDefaultability
  - [ ] 12.1 Create `samples/OptionalDefaultability/Program.mist` with the proof code from requirements
    - Include all proof code verbatim as specified in Requirement 14
    - The sample must compile and produce the exact expected output
    - _Requirements: 14.1, 14.2, 14.3, 14.5_

  - [ ] 12.2 Add `OptionalDefaultability.Program.RunDemo()` call to `samples/AllFeatures/Program.mist`
    - Insert the call in the appropriate position in the AllFeatures orchestrator
    - _Requirements: 14.4_

- [ ] 13. Book Documentation: Update default-keyword chapter
  - [ ] 13.1 Update `book/src/advanced/default-keyword.md` to cover optional defaultability
    - Cover: default (data) vs new() (behavior), defaultable vs non-defaultable types, constructor-less types are implicitly defaultable, types with constructors opt out unless `default {}` block supplied, `where T : default` generic constraint, enum default variant designation, union type defaultability, definite assignment guarantee
    - Explain the design rationale
    - _Requirements: 15.1, 15.2, 15.3_

  - [ ] 13.2 Create compiled book snippets in `book/snippets/advanced/default-keyword/`
    - Add new `.mist` snippet files demonstrating optional defaultability patterns
    - Generate corresponding `.cs` files via the SnippetEmitter
    - _Requirements: 15.4_

- [ ] 14. Final checkpoint - Ensure all tests pass and sample produces expected output
  - Ensure all tests pass, ask the user if questions arise.
  - Verify `samples/OptionalDefaultability/Program.mist` compiles and produces the exact expected output from Requirement 14.

## Notes

- Tasks marked with `*` are optional and can be skipped for faster MVP
- Each task references specific requirements for traceability
- Checkpoints ensure incremental validation
- Property tests validate universal correctness properties from the design document
- Unit tests validate specific examples and edge cases
- All property tests use in-process compilation via shared `CompilerDriver` following the Test Process Spawning Policy (no spawning processes per iteration)
- Constructor syntax in Mist is `public ClassName(params)`, NOT `public new(params)`
- The `default` modifier on enum variants appears BEFORE the variant name: `default North,`
- String is already intrinsically defaultable via `IntrinsicDefaultableNames`
- The existing `ComputeDefaultability` pass uses topological sort in `SemanticModelBuilder`
- Existing tests use FsCheck property-based testing with in-process compilation
- The sample app proof code is locked in the requirements — implementation must make that exact code compile and produce the specified output
- Zero Warnings Policy: build must have zero warnings after all changes

## Task Dependency Graph

```json
{
  "waves": [
    { "id": 0, "tasks": ["1.1", "1.2"] },
    { "id": 1, "tasks": ["1.3", "2.2", "2.3", "2.4"] },
    { "id": 2, "tasks": ["1.4", "2.1"] },
    { "id": 3, "tasks": ["2.5", "2.6", "2.7"] },
    { "id": 4, "tasks": ["4.1", "5.1", "5.2", "5.3"] },
    { "id": 5, "tasks": ["4.2", "5.4", "8.1"] },
    { "id": 6, "tasks": ["7.1", "7.2", "7.3", "7.4", "8.2"] },
    { "id": 7, "tasks": ["7.5", "7.6", "9.1"] },
    { "id": 8, "tasks": ["11.1", "12.1"] },
    { "id": 9, "tasks": ["12.2", "13.1"] },
    { "id": 10, "tasks": ["13.2"] }
  ]
}
```
