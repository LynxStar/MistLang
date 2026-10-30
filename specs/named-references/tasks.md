# Implementation Plan: Named References

## Overview

Migrate the `mist.toml` project reference format from `[[references]]` (TOML array-of-tables) to `[references]` (Cargo-style named table). The implementation modifies the data model, parser, resolver, tests, sample files, and book documentation in incremental steps.

## Tasks

- [x] 1. Update data model and diagnostic constants
  - [x] 1.1 Add `Name` parameter to `ProjectReference` record
    - Modify `Source/Mist/Driver/Contracts/ProjectReference.cs` to change the record signature from `ProjectReference(string Path)` to `ProjectReference(string Name, string Path)`
    - Update the XML doc comment to describe the `Name` parameter (reference key, validated by parser, compared by resolver)
    - _Requirements: 5.1, 5.2, 5.3_

  - [x] 1.2 Add 3 new `DiagnosticId` constants
    - Add `ProjectReferenceInvalidFormat` with value `"MIST0065"` to `Source/Mist/Common/Diagnostics/DiagnosticId.cs`
    - Add `ProjectReferenceInvalidName` with value `"MIST0067"`
    - Add `ProjectReferenceNameMismatch` with value `"MIST0070"`
    - Place them in the existing "Project reference diagnostics" region
    - _Requirements: 10.1, 10.2, 10.3, 10.4_

  - [x] 1.3 Fix all compilation errors from the `ProjectReference` signature change
    - Update all call sites that construct `ProjectReference` to pass a `Name` argument (first positional parameter)
    - Files affected: `ProjectFileParser.cs`, `ProjectReferenceResolverTests.cs`, `ProjectReferencePropertyTests.cs`, `ProjectConfigurationRoundTripPropertyTests.cs`
    - This is a mechanical fix — use the directory name or TOML key as the name value at each call site
    - _Requirements: 5.3_

- [x] 2. Rewrite ProjectFileParser step 9
  - [x] 2.1 Replace `TomlTableArray` iteration with `TomlTable` iteration
    - In `Source/Mist/Driver/ProjectFileParser.cs`, replace the entire step 9 block
    - Check `refsObj is not TomlTable` instead of `TomlTableArray`; report `ProjectReferenceInvalidFormat` on type mismatch
    - Iterate `foreach (var (refName, refValue) in refsTable)` instead of `foreach (var entry in refsArray)`
    - Validate reference key: empty/whitespace → pattern → length (short-circuit on first failure), report `ProjectReferenceInvalidName`
    - Validate reference value is a `TomlTable`; report `ProjectReferenceInvalidFormat` if not
    - Extract and validate `path` field (reuse existing `ProjectReferenceMissingPath` / `ProjectReferenceEmptyPath` diagnostics)
    - Normalize path (unchanged logic)
    - Construct `new ProjectReference(refName, normalizedPath)`
    - _Requirements: 1.1, 1.2, 1.3, 1.4, 1.5, 1.6, 1.7, 1.8, 2.1, 2.2, 2.3, 2.4, 2.5, 2.6, 3.1, 3.2, 3.3, 3.4, 6.1, 6.2, 6.3_

  - [x] 2.2 Write property test for round-trip (updated)
    - **Property 1: Round-trip with named references**
    - **Validates: Requirements 1.1, 1.2, 1.3, 5.1, 5.2, 7.1, 7.2, 7.3, 7.4**

- [x] 3. Add name-mismatch validation to ProjectReferenceResolver
  - [x] 3.1 Update `Dfs` method signature to accept `referenceName` parameter
    - Change private `Dfs` signature to include `string referenceName` as the first parameter
    - Update the `Resolve` method to pass `reference.Name` when calling `Dfs` for each direct reference
    - Update the recursive call inside `Dfs` to pass `transRef.Name` for transitive references
    - _Requirements: 4.1_

  - [x] 3.2 Add name-mismatch check after successful parse of target project
    - After `ProjectFileParser.Parse` succeeds and before recursing into transitive references, compare `config.Name` against `referenceName` using ordinal equality
    - If mismatch: report `ProjectReferenceNameMismatch` diagnostic with both names in the message, remove from `inProgress`, pop from `chain`, and return (skip transitive deps)
    - _Requirements: 4.1, 4.2, 4.3, 4.4_

  - [x] 3.3 Write unit test for name-mismatch detection
    - Add a test to `ProjectReferenceResolverTests.cs` that creates a reference with key "WrongName" pointing to a project with `name = "CorrectName"` and asserts `MIST0070` diagnostic is reported
    - Add a test verifying transitive deps of a mismatched reference are not traversed
    - _Requirements: 4.2, 4.3, 4.4_

  - [x] 3.4 Write property test for name-mismatch detection
    - **Property 2: Name-mismatch detection**
    - **Validates: Requirements 4.1, 4.2, 4.3, 4.4**

- [x] 4. Checkpoint - Ensure all tests pass
  - Ensure all tests pass, ask the user if questions arise.

- [x] 5. Update ProjectConfigurationRoundTripPropertyTests
  - [x] 5.1 Update generator to produce `ProjectReference(Name, Path)` with distinct names
    - Modify `GenProjectConfiguration` to generate distinct reference names using `GenProjectName()`
    - Ensure generated names are unique within a single configuration (deduplicate)
    - _Requirements: 7.4_

  - [x] 5.2 Update serializer to emit `[references]` table format
    - Replace the `[[references]]` serialization loop with `[references]` section + inline table entries (`Name = { path = "..." }`)
    - _Requirements: 7.1, 7.2, 7.3_

  - [x] 5.3 Update round-trip assertion to compare `Name` field
    - Add comparison of `parsed.References[i].Name` against `original.References[i].Name` in the property assertion
    - _Requirements: 7.4_

- [x] 6. Update ProjectReferenceResolverTests
  - [x] 6.1 Update `RefTo` helper to accept a name parameter
    - Change `RefTo(string projectDir)` to `RefTo(string name, string projectDir)` returning `new ProjectReference(name, path)`
    - Update all existing test call sites to pass the correct project name
    - _Requirements: 5.3_

  - [x] 6.2 Update all TOML strings in tests to use `[references]` format
    - Replace all `[[references]]\npath = "..."` patterns with `[references]\nName = { path = "..." }` format
    - Ensure each reference key matches the target project's `name` field
    - _Requirements: 1.1, 1.2_

- [x] 7. Update ProjectReferencePropertyTests
  - [x] 7.1 Update `RefTo` helper and TOML generation in P4 (cycle detection)
    - Change `RefTo` helper to accept a name parameter
    - Update TOML generation to emit `[references]` table format with named keys
    - _Requirements: 1.1, 1.2_

  - [x] 7.2 Update TOML generation in P5 (deduplication)
    - Convert all `[[references]]` TOML strings to `[references]` named table format
    - Ensure reference keys match target project names
    - _Requirements: 1.1, 1.2_

  - [x] 7.3 Update TOML generation in P6 (referenced unit metadata)
    - Convert TOML strings to new format
    - _Requirements: 1.1, 1.2_

  - [x] 7.4 Update TOML generation in P7 (topological ordering)
    - Convert TOML strings to new format
    - _Requirements: 1.1, 1.2_

  - [x] 7.5 Update TOML generation in P8 (transitive visibility)
    - Convert TOML strings to new format
    - _Requirements: 1.1, 1.2_

  - [x] 7.6 Update TOML generation in P9 (independent errors)
    - Convert TOML strings to new format (note: P9 uses non-existent paths, so reference names can be arbitrary valid names)
    - _Requirements: 1.1, 1.2_

- [x] 8. Checkpoint - Ensure all tests pass
  - Ensure all tests pass, ask the user if questions arise.

- [x] 9. Migrate sample and documentation
  - [x] 9.1 Migrate `samples/AllFeatures/mist.toml` to named reference format
    - Replace all `[[references]]` entries with a single `[references]` table
    - Each key must match the `name` field of the target project's `mist.toml` (which matches the directory name for all existing samples)
    - Preserve all existing references without additions or removals
    - _Requirements: 8.1, 8.2, 8.3, 8.4_

  - [x] 9.2 Update `book/src/tooling/project-configuration.md`
    - Replace all `[[references]]` documentation with `[references]` named table syntax
    - Document both inline table form and expanded sub-table form as equivalent
    - Explain that reference key must match target project's `name` field (case-sensitive)
    - Document `ProjectReferenceNameMismatch` error
    - Update fields summary table to show `[references]` as a table
    - Update all example configurations to use new syntax
    - Update error behavior table with new diagnostic IDs
    - Preserve path resolution, circular reference, and missing reference documentation
    - _Requirements: 9.1, 9.2, 9.3, 9.4, 9.5, 9.6, 9.7, 9.8_

- [x] 10. Final checkpoint - Ensure all tests pass
  - Ensure all tests pass, ask the user if questions arise.

## Notes

- Tasks marked with `*` are optional and can be skipped for faster MVP
- Each task references specific requirements for traceability
- Checkpoints ensure incremental validation
- Property tests validate universal correctness properties from the design document
- The zero-warnings policy applies: all code must build with zero warnings
- Tests must NOT spawn external processes per iteration (test-process-spawning policy)
- Sample app integrity: only `samples/AllFeatures/mist.toml` is modified; no other samples are touched

## Task Dependency Graph

```json
{
  "waves": [
    { "id": 0, "tasks": ["1.1", "1.2"] },
    { "id": 1, "tasks": ["1.3"] },
    { "id": 2, "tasks": ["2.1"] },
    { "id": 3, "tasks": ["2.2", "3.1"] },
    { "id": 4, "tasks": ["3.2"] },
    { "id": 5, "tasks": ["3.3", "3.4", "5.1"] },
    { "id": 6, "tasks": ["5.2", "5.3", "6.1"] },
    { "id": 7, "tasks": ["6.2", "7.1", "7.2", "7.3", "7.4", "7.5", "7.6"] },
    { "id": 8, "tasks": ["9.1", "9.2"] }
  ]
}
```
