# Implementation Plan: Project-Based Build Model

## Overview

Replace the Mist compiler's file-list CLI model with a project-folder-based build model. The implementation follows the design's linear data flow: update data models first, then parser/validation, then backend interface, then orchestration wiring, and finally tests. Each task builds incrementally on the previous one so the codebase stays compilable at each step.

## Tasks

- [x] 1. Update data models (CliArguments and CompilerOptions)
  - [x] 1.1 Update `CliArguments` record to replace `SourceFiles` with `ProjectFolder`
    - In `Source/Mist.CLI/Args/CliArguments.cs`, replace the `SourceFiles` field (`IReadOnlyList<string>`) with `ProjectFolder` (`string`)
    - Add `BinDirectory` field (`string`, default `""`)
    - Change `OutputDirectory` default from `"./out"` to `""`
    - Keep `BackendId` and `Mode` unchanged
    - _Requirements: 7.1, 7.2, 7.3_

  - [x] 1.2 Update `CompilerOptions` record to add `ProjectFolder` and `BinDirectory`
    - In `Source/Mist/Driver/Contracts/CompilerOptions.cs`, add `ProjectFolder` (`string`) as the first parameter
    - Add `BinDirectory` (`string`) after `OutputDirectory`
    - Keep `SourcePaths`, `Backend`, `OutputDirectory`, and `EnabledAnalyzerIds` unchanged
    - _Requirements: 6.1, 6.2, 6.3, 6.4_

- [x] 2. Update `CliArgumentParser` for project-folder validation
  - [x] 2.1 Rewrite `CliArgumentParser.Parse()` to accept a single project folder
    - Track a single `string? projectFolder` instead of `List<string> sourceFiles`
    - Add `--bin` flag handling (same pattern as `--output`)
    - After the parsing loop, validate: more than one positional arg → error; positional arg is a file → error; positional arg is a non-existent directory → error; zero positional args → use `Directory.GetCurrentDirectory()`
    - Resolve `outputDirectory` to `Path.Combine(projectFolder, "out")` when empty
    - Resolve `binDirectory` to `Path.Combine(projectFolder, "bin")` when empty
    - Construct and return the new `CliArguments` record
    - _Requirements: 1.1, 1.2, 1.3, 1.4, 1.5, 3.1, 3.2, 3.3, 3.4_

  - [ ]* 2.2 Write property tests for `CliArgumentParser` (Properties 1–3, 5–6)
    - **Property 1: Single positional argument sets ProjectFolder**
    - **Validates: Requirement 1.1**

  - [ ]* 2.3 Write property test: multiple positional arguments produce a parse error
    - **Property 2: Multiple positional arguments produce a parse error**
    - **Validates: Requirement 1.3**

  - [ ]* 2.4 Write property test: non-directory positional argument produces a parse error
    - **Property 3: Non-directory positional argument produces a parse error**
    - **Validates: Requirements 1.4, 1.5**

  - [ ]* 2.5 Write property test: default directories derived from ProjectFolder
    - **Property 5: Default directories are derived from ProjectFolder**
    - **Validates: Requirements 3.1, 3.3**

  - [ ]* 2.6 Write property test: explicit flag values override directory defaults
    - **Property 6: Explicit flag values override directory defaults**
    - **Validates: Requirements 3.2, 3.4**

  - [ ]* 2.7 Update existing `CliArgumentParserTests` unit tests
    - Update tests in `Test/Mist.CLI.Tests/Args/CliArgumentParserTests.cs` to match the new `CliArguments` shape (replace `SourceFiles` assertions with `ProjectFolder` assertions)
    - Add tests for `--bin` flag parsing, missing `--bin` value error, file-path rejection, multi-arg rejection, and default directory resolution
    - _Requirements: 1.1–1.5, 3.1–3.4, 5.1, 5.2_

- [x] 3. Checkpoint
  - Ensure all tests pass, ask the user if questions arise.

- [x] 4. Update backend interface and implementations
  - [x] 4.1 Add `binDirectory` parameter to `IBackend.Emit`
    - In `Source/Mist/Backends/Contracts/IBackend.cs`, change `Emit` signature to include `string binDirectory` after `outputDirectory`
    - _Requirements: 9.4_

  - [x] 4.2 Update `CSharpTranspilerBackend.Emit` to pass `binDirectory` through
    - In `Source/Mist/Backends/CSharpTranspiler/CSharpTranspilerBackend.cs`, update `Emit` to accept `binDirectory` and pass it to `CSharpCompilationDriver.Compile`
    - `CSharpEmitter.Emit` call remains unchanged (still uses `outputDirectory`)
    - _Requirements: 9.1, 9.2, 9.4_

  - [x] 4.3 Update `CSharpCompilationDriver.Compile` to accept and use `binDirectory`
    - In `Source/Mist/Backends/CSharpTranspiler/CSharpCompilationDriver.cs`, add `string binDirectory` parameter
    - Change `dotnet build` arguments to `build --output "{binDirectory}"`
    - Update artifact path from `Path.Combine(projectDirectory, "bin", "Debug", "net10.0", "MistOutput.dll")` to `Path.Combine(binDirectory, "MistOutput.dll")`
    - _Requirements: 9.2, 9.3_

- [x] 5. Wire `CompilerDriver` to pass `binDirectory` to backend
  - In `Source/Mist/Driver/CompilerDriver.cs`, update the `backend.Emit(...)` call to pass `options.BinDirectory` as the new parameter
  - _Requirements: 3.5, 3.6_

- [x] 6. Checkpoint
  - Ensure all tests pass, ask the user if questions arise.

- [x] 7. Update `Program.cs` orchestration
  - [x] 7.1 Rewrite source discovery and `CompilerOptions` construction
    - Replace the multi-file/directory expansion logic with a single `Directory.EnumerateFiles(cliArgs.ProjectFolder, "*.mist", SearchOption.AllDirectories)` call, sorted ordinally
    - Construct `CompilerOptions` with the new `ProjectFolder` and `BinDirectory` fields
    - Update the usage string to `mist [project-folder] [--backend <id>] [--output <dir>] [--bin <dir>] [--mode <compile|lex>]`
    - _Requirements: 2.1, 2.2, 2.3, 5.1, 5.2, 6.1–6.4_

  - [x] 7.2 Add clean logic for `out/` and `bin/` directories before build
    - Add a `CleanDirectory(string path)` helper that deletes all files and subdirectories within a directory (no-op if directory doesn't exist)
    - Call `CleanDirectory` for both `OutputDirectory` and `BinDirectory` before invoking the compiler driver or lex mode
    - Wrap in try/catch for `IOException`, print error and return exit code 1 on failure
    - _Requirements: 4.1, 4.2, 4.3, 4.4, 4.5_

  - [x] 7.3 Update `RunLexMode` for project-folder model
    - Update `RunLexMode` to accept the project folder and use source discovery from it
    - Lex output path (`Path.Combine(outputDirectory, "lexer")`) remains unchanged
    - _Requirements: 8.1, 8.2, 8.3, 8.4_

  - [ ]* 7.4 Write property test: source discovery returns exactly the sorted .mist files
    - **Property 4: Source discovery returns exactly the sorted .mist files**
    - **Validates: Requirements 2.1, 2.2**

  - [ ]* 7.5 Write property test: clean removes all directory contents
    - **Property 7: Clean removes all directory contents**
    - **Validates: Requirements 4.1, 4.2**

- [x] 8. Update existing tests that reference old `CliArguments` or `CompilerOptions` shapes
  - Search `Test/Mist.CLI.Tests/` and `Test/Mist.Tests/` for any tests constructing `CliArguments` or `CompilerOptions` and update them to use the new record shapes
  - Update `Test/Mist.CLI.Tests/MistFileDiscoveryTests.cs` if it references the old source-file list model
  - _Requirements: 7.1–7.3, 6.1–6.4_

- [x] 9. Final checkpoint
  - Ensure all tests pass, ask the user if questions arise.

## Notes

- Tasks marked with `*` are optional and can be skipped for faster MVP
- Each task references specific requirements for traceability
- Property tests use FsCheck (already referenced in both test `.csproj` files)
- The `CSharpEmitter` requires no changes — it already writes to `outputDirectory`
- Checkpoints ensure incremental validation after data model changes and after backend changes
