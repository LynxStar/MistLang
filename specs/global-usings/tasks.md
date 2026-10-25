# Implementation Plan: Global Usings

## Overview

This plan implements the `[global_usings]` feature for the Mist compiler. Global usings are declared in `mist.toml` and implicitly imported into every source file within the declaring project. The implementation modifies the parsing, pipeline wiring, semantic resolution, and code generation stages, followed by property-based tests, a sample app, and book documentation.

## Tasks

- [x] 1. Extend data models and core interfaces
  - [x] 1.1 Add `GlobalUsings` field to `ProjectConfiguration` record
    - Add `ImmutableArray<string> GlobalUsings` as a new parameter to the `ProjectConfiguration` record in `Source/Mist/Driver/Contracts/ProjectConfiguration.cs`
    - Default to `ImmutableArray<string>.Empty` when not provided
    - Update XML doc comments to document the new field
    - Fix all existing construction sites that now need the additional parameter
    - _Requirements: 1.6, 3.1, 3.2_

  - [x] 1.2 Add `GlobalUsings` property to `CompilationUnit` record
    - Add `public ImmutableArray<string> GlobalUsings { get; init; } = ImmutableArray<string>.Empty;` to `CompilationUnit` in `Source/Mist/Driver/Contracts/CompilationUnit.cs`
    - Add XML doc comment explaining the property's purpose
    - _Requirements: 3.3, 5.5_

  - [x] 1.3 Add `GlobalUsings` field to `CompilerOptions` record
    - Add `ImmutableArray<string> GlobalUsings = default` as an optional parameter to `CompilerOptions` in `Source/Mist/Driver/Contracts/CompilerOptions.cs`
    - This enables in-process test compilation with global usings without requiring a `mist.toml` on disk
    - _Requirements: 3.1_

- [x] 2. Implement ProjectFileParser global usings parsing and validation
  - [x] 2.1 Add `ValidNamespacePattern` regex and parse `[global_usings]` section
    - Add `[GeneratedRegex(@"^[a-zA-Z_][a-zA-Z0-9_]*(\.[a-zA-Z_][a-zA-Z0-9_]*)*$")]` partial method to `ProjectFileParser`
    - Implement parsing of the optional `[global_usings]` section after the `[[references]]` section
    - Handle: section absent (empty list), section present with valid `namespaces` array, section present without `namespaces` field (error)
    - Pass the parsed `GlobalUsings` to the `ProjectConfiguration` constructor
    - _Requirements: 1.1, 1.2, 1.3, 1.4, 1.5, 1.7_

  - [x] 2.2 Implement validation rules for namespace entries
    - Validate each entry: reject empty/whitespace-only strings (`MIST_GLOBAL_USING_EMPTY_NAMESPACE`)
    - Validate character set using `ValidNamespacePattern` regex (`MIST_GLOBAL_USING_INVALID_NAMESPACE`)
    - Validate `namespaces` field is an array (`MIST_GLOBAL_USING_INVALID_FORMAT`)
    - Validate each element is a string (`MIST_GLOBAL_USING_INVALID_FORMAT` with index)
    - Detect and warn on duplicates (`MIST_GLOBAL_USING_DUPLICATE_NAMESPACE`), removing duplicates but continuing
    - Include `mist.toml` file path in all diagnostic messages
    - _Requirements: 2.1, 2.2, 2.3, 2.4, 2.5, 2.6_

  - [x] 2.3 Write unit tests for ProjectFileParser global usings parsing
    - Test valid `[global_usings]` with multiple namespaces
    - Test absent `[global_usings]` section produces empty list
    - Test empty `namespaces` array produces empty list
    - Test missing `namespaces` field produces `MIST_GLOBAL_USING_MISSING_NAMESPACES` error
    - Test empty/whitespace entry produces `MIST_GLOBAL_USING_EMPTY_NAMESPACE` error
    - Test invalid characters produce `MIST_GLOBAL_USING_INVALID_NAMESPACE` error
    - Test non-array `namespaces` produces `MIST_GLOBAL_USING_INVALID_FORMAT` error
    - Test non-string element produces `MIST_GLOBAL_USING_INVALID_FORMAT` error
    - Test duplicate entries produce warning and are removed
    - Test unknown fields in `[global_usings]` are silently discarded
    - Test all diagnostics include the file path
    - _Requirements: 1.1–1.7, 2.1–2.6, 7.1, 7.3_

- [x] 3. Checkpoint - Ensure all tests pass
  - Ensure all tests pass, ask the user if questions arise.

- [x] 4. Wire global usings through CompilerDriver
  - [x] 4.1 Set `GlobalUsings` on `CompilationUnit` instances in `CompilerDriver`
    - When loading user source files, set `GlobalUsings` from the root project's `ProjectConfiguration.GlobalUsings`
    - When loading referenced project source files, set `GlobalUsings` from that project's `ResolvedProjectReference.Configuration.GlobalUsings`
    - Stdlib units keep the default empty `GlobalUsings`
    - When using `CompilerOptions.GlobalUsings` (test path), apply those to user units
    - _Requirements: 3.3, 3.4, 5.1, 5.2, 5.3, 5.4, 5.5_

  - [x] 4.2 Write unit tests for CompilerDriver global usings wiring
    - Test that user compilation units receive the root project's global usings
    - Test that referenced project units receive their own project's global usings
    - Test that stdlib units have empty global usings
    - Test that `CompilerOptions.GlobalUsings` is applied to user units
    - _Requirements: 3.3, 3.4, 5.1–5.5_

- [x] 5. Implement semantic resolution of global usings
  - [x] 5.1 Apply global usings to file scopes in `SemanticModelBuilder`
    - In the file scope creation pass, read `unit.GlobalUsings` for each compilation unit
    - For each global using namespace, check if it exists (user-defined or ambient)
    - If it exists, call `fileScope.AddImportedNamespace(globalNs)`
    - If it does not exist, report `MIST_GLOBAL_USING_UNDEFINED_NAMESPACE` warning diagnostic
    - Redundant explicit usings are silently accepted (HashSet deduplicates)
    - Ambiguity detection works via existing `MIST_AMBIGUOUS_TYPE` mechanism
    - _Requirements: 4.1, 4.2, 4.3, 4.4, 4.5_

  - [x] 5.2 Write unit tests for SemanticModelBuilder global usings resolution
    - Test that types from global using namespaces resolve by simple name
    - Test that redundant explicit using produces no diagnostic
    - Test that undefined global using namespace produces `MIST_GLOBAL_USING_UNDEFINED_NAMESPACE` warning
    - Test that ambiguous types from multiple global usings produce `MIST_AMBIGUOUS_TYPE` error
    - Test that ambiguity between global using and explicit using produces `MIST_AMBIGUOUS_TYPE` error
    - _Requirements: 4.1–4.5_

- [x] 6. Implement CSharpSourceWriter global usings emission
  - [x] 6.1 Merge global usings into emitted `using` directive block in `CSharpEmitter.cs`
    - In `CSharpSourceWriter.EmitCompilationUnit`, after collecting explicit usings and backend namespaces, add each `unit.GlobalUsings` entry to the `usings` HashSet
    - The existing `HashSet<string>` deduplicates and `OrderBy` sorts the output
    - Access the compilation unit's `GlobalUsings` via the semantic model's unit list
    - _Requirements: 6.1, 6.2, 6.3, 6.4_

  - [x] 6.2 Write unit tests for CSharpSourceWriter global usings emission
    - Test that global usings appear in emitted `using` directives
    - Test that all usings (global, explicit, backend) are merged, sorted, and deduplicated
    - Test that a namespace present in both global usings and explicit usings appears only once
    - Test that a namespace present in both global usings and backend namespaces appears only once
    - _Requirements: 6.1–6.4_

- [x] 7. Checkpoint - Ensure all tests pass
  - Ensure all tests pass, ask the user if questions arise.

- [x] 8. Property-based tests for parsing
  - [x] 8.1 Write property test for ProjectConfiguration round-trip
    - **Property 1: ProjectConfiguration round-trip**
    - Generate random `ProjectConfiguration` objects with random `GlobalUsings` lists (valid namespace strings)
    - Serialize to TOML, parse back, assert structural equality (order-sensitive for `GlobalUsings`)
    - Minimum 100 iterations, in-process only (no process spawning)
    - **Validates: Requirements 1.1, 1.2, 1.3, 1.6, 3.1, 7.1, 7.2, 7.4**

  - [x] 8.2 Write property test for invalid namespace rejection
    - **Property 2: Invalid namespace rejection**
    - Generate random strings that are empty, whitespace-only, or contain invalid characters
    - Assert parsing produces `MIST_GLOBAL_USING_EMPTY_NAMESPACE` or `MIST_GLOBAL_USING_INVALID_NAMESPACE` error and returns `null`
    - Minimum 100 iterations, in-process only
    - **Validates: Requirements 2.1, 2.2**

  - [x] 8.3 Write property test for duplicate detection and removal
    - **Property 3: Duplicate detection and removal**
    - Generate random namespace arrays with injected duplicates
    - Assert `MIST_GLOBAL_USING_DUPLICATE_NAMESPACE` warning is produced
    - Assert resulting `GlobalUsings` contains each namespace at most once, preserving first-occurrence order
    - Minimum 100 iterations, in-process only
    - **Validates: Requirements 2.5**

  - [x] 8.4 Write property test for diagnostic file path inclusion
    - **Property 4: Diagnostic file path inclusion**
    - Generate random invalid `[global_usings]` configurations
    - Assert all produced diagnostics contain the `mist.toml` file path
    - Minimum 100 iterations, in-process only
    - **Validates: Requirements 2.6**

- [x] 9. Property-based tests for semantic resolution and emission
  - [x] 9.1 Write property test for global using resolution equivalence
    - **Property 5: Global using resolution equivalence**
    - Generate random namespace/type pairs, compile with global usings using in-process `CompilerDriver.Compile()` with `EmitToDisk = false`
    - Assert type resolves by simple name (equivalent to explicit `using`)
    - Minimum 100 iterations, in-process compilation only (no process spawning)
    - **Validates: Requirements 4.1**

  - [x] 9.2 Write property test for redundant explicit using tolerance
    - **Property 6: Redundant explicit using tolerance**
    - Generate random namespaces present in both global and explicit usings
    - Compile in-process, assert no redundancy diagnostic is produced
    - Minimum 100 iterations, in-process only
    - **Validates: Requirements 4.2**

  - [x] 9.3 Write property test for undefined namespace warning
    - **Property 7: Undefined namespace warning**
    - Generate random non-existent namespace names as global usings
    - Compile in-process, assert `MIST_GLOBAL_USING_UNDEFINED_NAMESPACE` warning is produced
    - Minimum 100 iterations, in-process only
    - **Validates: Requirements 4.3**

  - [x] 9.4 Write property test for emitter deduplication and sorting
    - **Property 8: Emitter deduplication and sorting**
    - Generate random namespace sets from global usings, explicit usings, and backend namespaces
    - Assert emitted C# file contains each namespace as a `using` directive exactly once, sorted in ordinal alphabetical order
    - Minimum 100 iterations, in-process only
    - **Validates: Requirements 6.1, 6.2, 6.3, 6.4**

- [x] 10. Checkpoint - Ensure all tests pass
  - Ensure all tests pass, ask the user if questions arise.

- [x] 11. Sample application
  - [x] 11.1 Create `samples/GlobalUsings/` project with `mist.toml` and source files
    - Create `samples/GlobalUsings/mist.toml` with a `[global_usings]` section declaring at least two namespaces
    - Create source files that define types in at least two distinct namespaces (e.g., `Models/` and `Services/` subdirectories)
    - Create `samples/GlobalUsings/Program.mist` that uses types from globally-used namespaces without explicit `using` declarations
    - Demonstrate redundant explicit `using` for a globally-used namespace (accepted without error)
    - Demonstrate types from multiple globally-used namespaces in the same file
    - Print labeled output for each demonstrated permutation
    - _Requirements: 9.1, 9.2, 9.3, 9.4, 9.5, 9.6, 9.7, 9.9_

  - [x] 11.2 Integrate into `samples/AllFeatures/`
    - Add `GlobalUsings.Program.RunDemo()` call to `samples/AllFeatures/Program.mist`
    - Add project reference to `samples/AllFeatures/mist.toml`
    - _Requirements: 9.8_

- [x] 12. Book documentation
  - [x] 12.1 Document global usings in `book/src/tooling/project-configuration.md`
    - Add a `## The [global_usings] Section` heading
    - Document the TOML format, the `namespaces` field, valid entry syntax, and error behavior
    - Add a row for `[global_usings]` in the Fields table with type and description
    - _Requirements: 8.1, 8.2_

  - [x] 12.2 Document global usings in `book/src/basics/namespaces.md`
    - Add a `## Global Usings` section explaining how global usings work from a usage perspective
    - Explain that global usings are declared in `mist.toml` (not in source files)
    - Explain that they behave identically to explicit `using` declarations
    - Explain that they are scoped to the declaring project only
    - Include a code example showing a `mist.toml` with `[global_usings]` and a `.mist` file using types without explicit `using`
    - _Requirements: 8.3, 8.4, 8.5, 8.6_

- [x] 13. Final checkpoint - Ensure all tests pass
  - Ensure all tests pass, ask the user if questions arise.

## Notes

- Tasks marked with `*` are optional and can be skipped for faster MVP
- Each task references specific requirements for traceability
- Checkpoints ensure incremental validation
- Property tests validate universal correctness properties from the design document
- Unit tests validate specific examples and edge cases
- All property-based tests are pure in-process (no process spawning) per the test-process-spawning policy
- Property 5 uses in-process `CompilerDriver.Compile()` with `EmitToDisk = false` — no external process is spawned per iteration
- The `CSharpSourceWriter` class is nested inside `CSharpEmitter.cs` (not a separate file)
- Global usings do not introduce new keywords or syntax, so no TextMate grammar update is needed

## Task Dependency Graph

```json
{
  "waves": [
    { "id": 0, "tasks": ["1.1", "1.2", "1.3"] },
    { "id": 1, "tasks": ["2.1", "2.2"] },
    { "id": 2, "tasks": ["2.3", "4.1"] },
    { "id": 3, "tasks": ["4.2", "5.1"] },
    { "id": 4, "tasks": ["5.2", "6.1"] },
    { "id": 5, "tasks": ["6.2", "8.1", "8.2", "8.3", "8.4"] },
    { "id": 6, "tasks": ["9.1", "9.2", "9.3", "9.4"] },
    { "id": 7, "tasks": ["11.1", "12.1", "12.2"] },
    { "id": 8, "tasks": ["11.2"] }
  ]
}
```
