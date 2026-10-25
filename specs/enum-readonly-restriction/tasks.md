# Implementation Plan: Enum Readonly Restriction

## Overview

This plan implements a compile-time constraint requiring enum base types to be readonly. The work spans three layers: marking bootstrap types as readonly in the standard library, adding validation in `TypeCheckerAnalyzer`, and creating a sample application demonstrating the feature. All property-based tests use in-process compilation via a shared `CompilerDriver` with batched scenarios to avoid per-iteration process spawning.

## Tasks

- [x] 1. Mark bootstrap types as readonly
  - [x] 1.1 Add `readonly` modifier to all 14 bootstrap type definitions
    - Modify each `.mist` file in `Source/Standard/Bootstrap/` to add the `readonly` modifier to the type declaration
    - Files: `Int32.mist`, `Int16.mist`, `Int64.mist`, `UInt16.mist`, `UInt32.mist`, `UInt64.mist`, `Byte.mist`, `SByte.mist`, `Boolean.mist`, `String.mist`, `Char.mist`, `Single.mist`, `Double.mist`, `Decimal.mist`
    - Change pattern: `intrinsic public struct X` → `intrinsic public readonly struct X`
    - _Requirements: 1.1–1.14_

  - [x] 1.2 Update existing Enums sample to add `readonly` to Vec2, Label, and Animal
    - Add `readonly` modifier to `struct Vec2`, `class Label`, and `class Animal` declarations in `samples/Enums/Program.mist`
    - These types are used as enum base types and must be readonly to conform to the new language rule
    - _Requirements: 1.15, 1.16_

- [x] 2. Implement enum base type readonly validation
  - [x] 2.1 Add diagnostic ID constants for MIST_TC_0102 (historical) and MIST_TC_0103 (historical)
    - Add `EnumBaseTypeNotReadonly` and `EnumUnionConstituentNotReadonly` static readonly fields to `TypeCheckerAnalyzer.cs`
    - Follow the existing pattern for diagnostic ID declarations (e.g., `PatternIncompatibleRefinement`)
    - _Requirements: 4.5_

  - [x] 2.2 Implement `CheckEnumBaseTypeReadonly()` method in TypeCheckerAnalyzer
    - Add private static method that iterates all enum symbols in the semantic model
    - For non-union underlying types: check `IsTypeReadonly()` and emit `MIST_TC_0102 (historical)` if not readonly
    - For union underlying types: check each constituent via `IsTypeReadonly()` and emit `MIST_TC_0103 (historical)` per non-readonly constituent
    - Skip enums with null or `ErrorType` underlying types to avoid cascading errors
    - Diagnostic messages must include the enum name and the non-readonly type name
    - _Requirements: 2.1–2.4, 3.1–3.5, 4.1–4.4_

  - [x] 2.3 Wire `CheckEnumBaseTypeReadonly()` into the `Analyze()` method
    - Call the new method from the existing `Analyze()` method in `TypeCheckerAnalyzer`
    - Place the call after enum types have been fully resolved
    - _Requirements: 2.1–2.4, 3.1–3.5_

- [x] 3. Checkpoint — Verify compiler builds and existing tests pass
  - Ensure all tests pass, ask the user if questions arise.

- [x] 4. Create sample application
  - [x] 4.1 Create `samples/EnumReadonlyRestriction/mist.toml` project file
    - Declare project name as `EnumReadonlyRestriction`
    - _Requirements: 6.1_

  - [x] 4.2 Create `samples/EnumReadonlyRestriction/Program.mist`
    - Demonstrate all usage patterns: enum with primitive base type (int, string, byte), enum with readonly struct base type, enum with readonly class base type, name-only enum (inferred byte), enum with union of readonly types, member access on readonly-base enums, implicit conversion from readonly-base enum to underlying type, and pattern matching with readonly-base enums
    - Each pattern prints a section header followed by indented value lines
    - Include `public static void RunDemo()` entry point
    - _Requirements: 6.1, 6.2, 6.3, 6.5_

  - [x] 4.3 Integrate into AllFeatures orchestrator
    - Add `EnumReadonlyRestriction.Program.RunDemo()` call to `samples/AllFeatures/Program.mist`
    - Add `[[references]]` entry with `path = "../EnumReadonlyRestriction"` to `samples/AllFeatures/mist.toml`
    - _Requirements: 6.4_

- [x] 5. Checkpoint — Verify sample compiles and runs
  - Ensure all tests pass, ask the user if questions arise.

- [x] 6. Property-based tests
  - [x] 6.1 Write property test for readonly modifier propagation
    - **Property 1: Readonly modifier propagation**
    - **Validates: Requirements 1.15**
    - Generate random type declarations (struct, class, interface, trait) with and without `readonly` modifier
    - Build semantic model in-process using shared `CompilerDriver`
    - Verify `IsReadonly` matches the presence/absence of the modifier
    - Use batched approach: generate all scenarios, build one compilation unit, verify all results
    - Test class: `ReadonlyModifierPropagationPropertyTests` in `Test/Mist.Tests/Analysis/`

  - [x] 6.2 Write property test for readonly base type acceptance
    - **Property 2: Readonly base type acceptance**
    - **Validates: Requirements 2.1, 3.1, 5.1, 5.2, 5.3, 5.4**
    - Generate random enum declarations with readonly base types (all four type kinds)
    - Build semantic model + run type checker in-process
    - Verify zero `MIST_TC_0102 (historical)` diagnostics for each enum
    - Use batched approach with shared `CompilerDriver`
    - Test class: `EnumReadonlyAcceptancePropertyTests` in `Test/Mist.Tests/Analysis/`

  - [x] 6.3 Write property test for non-readonly base type rejection
    - **Property 3: Non-readonly base type rejection**
    - **Validates: Requirements 2.2, 3.2, 4.1, 4.2, 4.4, 4.5**
    - Generate random enum declarations with non-readonly base types
    - Build semantic model + run type checker in-process
    - Verify exactly one `MIST_TC_0102 (historical)` diagnostic per enum with correct message content and severity
    - Use batched approach with shared `CompilerDriver`
    - Test class: `EnumNonReadonlyRejectionPropertyTests` in `Test/Mist.Tests/Analysis/`

  - [x] 6.4 Write property test for all-readonly union acceptance
    - **Property 4: All-readonly union acceptance**
    - **Validates: Requirements 2.3, 3.3**
    - Generate random enum declarations with union base types where all constituents are readonly
    - Verify zero `MIST_TC_0103 (historical)` diagnostics
    - Use batched approach with shared `CompilerDriver`
    - Test class: `EnumReadonlyUnionAcceptancePropertyTests` in `Test/Mist.Tests/Analysis/`

  - [x] 6.5 Write property test for non-readonly union constituent rejection
    - **Property 5: Non-readonly union constituent rejection**
    - **Validates: Requirements 2.4, 3.4, 4.3, 4.4, 4.5**
    - Generate random enum declarations with union base types containing non-readonly constituents
    - Verify correct count and content of `MIST_TC_0103 (historical)` diagnostics per non-readonly constituent
    - Use batched approach with shared `CompilerDriver`
    - Test class: `EnumNonReadonlyUnionRejectionPropertyTests` in `Test/Mist.Tests/Analysis/`

- [x] 7. Unit and integration tests
  - [x] 7.1 Write unit tests for bootstrap type readonly verification
    - Verify all 14 bootstrap types have `IsReadonly = true` after semantic model construction
    - Verify diagnostic IDs `MIST_TC_0102 (historical)` and `MIST_TC_0103 (historical)` are unique (don't collide with existing IDs)
    - Test class: `EnumReadonlyRestrictionTests` in `Test/Mist.Tests/Analysis/`
    - _Requirements: 1.1–1.16, 4.5_

  - [x] 7.2 Write integration test for sample compilation and output
    - Verify `samples/EnumReadonlyRestriction/Program.mist` compiles and produces expected output
    - Verify `samples/Enums/Program.mist` still compiles after adding `readonly` to Vec2, Label, and Animal
    - Add to existing `SamplesCompileAndMatchBaselineTest` or create new test class in `Test/Mist.Tests/Integration/`
    - _Requirements: 5.1–5.9, 6.5_

- [x] 8. Final checkpoint — Ensure all tests pass
  - Ensure all tests pass, ask the user if questions arise.

## Notes

- Tasks marked with `*` are optional and can be skipped for faster MVP
- Each task references specific requirements for traceability
- Checkpoints ensure incremental validation
- Property tests validate universal correctness properties from the design document
- Unit tests validate specific examples and edge cases
- All property-based tests use in-process compilation with a shared `CompilerDriver` instance (no per-iteration process spawning per the test-process-spawning policy)
- The project uses C#, .NET, and FsCheck for property-based testing
- Zero-warnings policy applies: the build must produce no warnings after changes

## Task Dependency Graph

```json
{
  "waves": [
    { "id": 0, "tasks": ["1.1", "2.1"] },
    { "id": 1, "tasks": ["1.2", "2.2"] },
    { "id": 2, "tasks": ["2.3", "4.1"] },
    { "id": 3, "tasks": ["4.2", "4.3"] },
    { "id": 4, "tasks": ["6.1", "6.2", "6.3", "6.4", "6.5"] },
    { "id": 5, "tasks": ["7.1", "7.2"] }
  ]
}
```
