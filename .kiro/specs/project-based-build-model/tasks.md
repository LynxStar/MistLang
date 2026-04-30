# Implementation Plan: Project File (mist.toml)

## Overview

This plan implements Requirements 10–14: adding a TOML project file (`mist.toml`) that provides an explicit project name, replacing the current folder-name derivation. The implementation adds the Tomlyn NuGet dependency, creates the `ProjectConfiguration` record and `ProjectFileParser` static class, threads `ProjectName` through `CompilerOptions` and the compilation pipeline, updates the CLI entry point, and adds `mist.toml` files to all sample projects. Property-based tests validate the core parsing/validation logic; unit tests cover specific error paths.

## Tasks

- [x] 1. Add Tomlyn dependency and create ProjectConfiguration record
  - [x] 1.1 Add Tomlyn NuGet package to Mist.csproj
    - Add `<PackageReference Include="Tomlyn" Version="0.17.0" />` to the `<ItemGroup>` in `Source/Mist/Mist.csproj`
    - _Requirements: 10.1, 12.1_

  - [x] 1.2 Create the ProjectConfiguration record
    - Create `Source/Mist/Driver/Contracts/ProjectConfiguration.cs`
    - Define `public sealed record ProjectConfiguration(string Name)` in namespace `Mist.Driver.Contracts`
    - Include XML doc comment noting forward-compatible extension
    - _Requirements: 12.2, 12.4_

- [x] 2. Implement ProjectFileParser with validation
  - [x] 2.1 Create ProjectFileParser static class
    - Create `Source/Mist/Driver/ProjectFileParser.cs`
    - Define `public static class ProjectFileParser` in namespace `Mist.Driver`
    - Add constants: `FileName = "mist.toml"`, `MaxNameLength = 128`
    - Implement `public static ProjectConfiguration? Parse(string projectFolder, DiagnosticBag diagnostics)` with the following validation order:
      1. File existence check → `MIST_PROJECT_FILE_NOT_FOUND` diagnostic
      2. File read with I/O error handling → `MIST_PROJECT_FILE_READ_ERROR` diagnostic
      3. TOML parsing via `Tomlyn.Toml.ToModel()`, catching `TomlException` → `MIST_PROJECT_TOML_ERROR` diagnostic
      4. `name` field presence check → `MIST_PROJECT_MISSING_NAME` diagnostic
      5. `name` non-empty/non-whitespace check → `MIST_PROJECT_EMPTY_NAME` diagnostic
      6. `name` character validation via regex `^[a-zA-Z0-9\-_\.]+$` → `MIST_PROJECT_INVALID_NAME` diagnostic (message lists invalid chars)
      7. `name` length check ≤ 128 → `MIST_PROJECT_NAME_TOO_LONG` diagnostic
    - All diagnostics use `PipelineStage.Driver`, `DiagnosticSeverity.Error`, `Location: null`, and set `CompilationUnitPath` to the `mist.toml` file path
    - Return `null` on any error, short-circuiting on first failure
    - _Requirements: 10.1, 10.4, 10.5, 10.6, 11.1, 11.2, 11.3, 11.4, 12.1, 12.2, 12.3_

  - [x] 2.2 Write property test: TOML round-trip (Property 1)
    - **Property 1: TOML parsing round-trip**
    - Create test in `Test/Mist.Tests/Backend/ProjectFileParserPropertyTests.cs`
    - Generate random strings from `[a-zA-Z0-9\-_\.]` of length 1–128
    - Write `name = "{generated}"` to a temp `mist.toml`, call `ProjectFileParser.Parse`, assert `Name` equals the original string
    - **Validates: Requirements 10.2, 12.2**

  - [x] 2.3 Write property test: Whitespace-only names rejected (Property 2)
    - **Property 2: Whitespace-only names are rejected**
    - Add test to `Test/Mist.Tests/Backend/ProjectFileParserPropertyTests.cs`
    - Generate random strings composed entirely of whitespace characters (`\x20`, `\t`, `\n`, `\r`)
    - Parse and assert null result with `MIST_PROJECT_EMPTY_NAME` diagnostic
    - **Validates: Requirements 10.6**

  - [x] 2.4 Write property test: Name character validation (Property 3)
    - **Property 3: Name character validation**
    - Add test to `Test/Mist.Tests/Backend/ProjectFileParserPropertyTests.cs`
    - Two sub-generators: (a) valid names from `[a-zA-Z0-9\-_\.]` → assert accepted; (b) strings with at least one char outside that set → assert rejected with `MIST_PROJECT_INVALID_NAME` diagnostic listing invalid chars
    - **Validates: Requirements 11.1, 11.2**

  - [x] 2.5 Write unit tests for ProjectFileParser error paths
    - Add tests to `Test/Mist.Tests/Backend/ProjectFileParserTests.cs`
    - Test: missing `mist.toml` → `MIST_PROJECT_FILE_NOT_FOUND`
    - Test: TOML without `name` key → `MIST_PROJECT_MISSING_NAME`
    - Test: malformed TOML content → `MIST_PROJECT_TOML_ERROR`
    - Test: name exactly 128 chars → accepted (boundary)
    - Test: name of 129 chars → `MIST_PROJECT_NAME_TOO_LONG` (boundary)
    - _Requirements: 10.4, 10.5, 11.3, 11.4, 12.3_

- [x] 3. Checkpoint
  - Ensure all tests pass, ask the user if questions arise.

- [x] 4. Thread ProjectName through the compilation pipeline
  - [x] 4.1 Add ProjectName field to CompilerOptions
    - Modify `Source/Mist/Driver/Contracts/CompilerOptions.cs`
    - Add `string ProjectName` parameter between `BinDirectory` and `EnabledAnalyzerIds`
    - _Requirements: 13.2_

  - [x] 4.2 Update CompilerDriver to use options.ProjectName
    - Modify `Source/Mist/Driver/CompilerDriver.cs`
    - Replace the line `var projectName = Path.GetFileName(options.ProjectFolder.TrimEnd(...))` with `var projectName = options.ProjectName;`
    - _Requirements: 10.3, 13.3_

  - [x] 4.3 Update CLI Program.cs to parse project file and wire ProjectName
    - Modify `Source/Mist.CLI/Program.cs`
    - In `CompileProject` method: after resolving `cliArgs`, call `ProjectFileParser.Parse(cliArgs.ProjectFolder, diagnostics)`. If null, format diagnostics and return exit code 1. Otherwise, pass `config.Name` as `ProjectName` to `CompilerOptions`.
    - Apply the same pattern in `RunRunCommand` before calling `CompileProject`
    - Add necessary `using Mist.Driver;` and `using Mist.Common.Diagnostics;` imports
    - _Requirements: 10.4, 13.1_

  - [x] 4.4 Fix all CompilerOptions construction sites
    - Update any other call sites that construct `CompilerOptions` (e.g., in test helpers) to include the new `ProjectName` parameter
    - Search for `new CompilerOptions(` across the codebase and update each occurrence
    - _Requirements: 13.2_

  - [x] 4.5 Write property test: Project name propagation (Property 4)
    - **Property 4: Project name propagation**
    - Add test to `Test/Mist.Tests/Backend/ProjectFileParserPropertyTests.cs`
    - Generate valid project names. Construct `CompilerOptions` with the name. Use a mock/spy `IBackend` to capture the `projectName` argument passed to `Emit`. Assert it matches the `ProjectName` from options — never a folder-derived name.
    - **Validates: Requirements 10.3, 13.3**

  - [x] 4.6 Write property test: Emitter .csproj naming (Property 5)
    - **Property 5: Emitter uses project name for .csproj filename**
    - Add test to `Test/Mist.Tests/Backend/ProjectFileParserPropertyTests.cs`
    - Generate valid project names. Call `CSharpEmitter.Emit` with a mock semantic model into a temp directory. Assert a file named `{name}.csproj` exists in the output directory.
    - **Validates: Requirements 13.4**

- [x] 5. Checkpoint
  - Ensure all tests pass, ask the user if questions arise.

- [x] 6. Add mist.toml to sample projects
  - [x] 6.1 Create mist.toml for HelloMist sample
    - Create `samples/HelloMist/mist.toml` with content: `name = "HelloMist"`
    - _Requirements: 14.2_

  - [x] 6.2 Create mist.toml for AllFeatures sample
    - Create `samples/AllFeatures/mist.toml` with content: `name = "AllFeatures"`
    - _Requirements: 14.3_

  - [x] 6.3 Create mist.toml for Simpliest sample
    - Create `samples/Simpliest/mist.toml` with content: `name = "Simpliest"`
    - _Requirements: 14.4_

  - [x] 6.4 Create mist.toml for remaining sample projects
    - Create `mist.toml` for `Inheritance`, `Interfaces`, `Properties`, and `TypeLiterals` samples with `name` matching each folder name
    - _Requirements: 14.1_

  - [x] 6.5 Write unit tests verifying sample project mist.toml files
    - Add tests to `Test/Mist.CLI.Tests/` that verify each sample project's `mist.toml` exists and parses to the expected project name
    - _Requirements: 14.1, 14.2, 14.3, 14.4_

- [x] 7. Final checkpoint
  - Ensure all tests pass, ask the user if questions arise.

## Notes

- Tasks marked with `*` are optional and can be skipped for faster MVP
- Each task references specific requirements for traceability
- Checkpoints ensure incremental validation
- Property tests validate universal correctness properties from the design document (5 properties, 100+ iterations each via FsCheck)
- Unit tests cover specific error-path examples and boundary conditions
- The design uses C# throughout — all code examples target C# with xUnit and FsCheck.Xunit
- Requirements 1–9 are already implemented; this plan covers only Requirements 10–14
