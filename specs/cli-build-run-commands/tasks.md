# Implementation Plan: CLI Build & Run Commands

## Overview

Add `build` and `run` command verbs to the Mist CLI. The implementation modifies three files in `Source/Mist.CLI`: the `CliArguments` record, the `CliArgumentParser`, and `Program.cs`. Tests go in the existing `Test/Mist.CLI.Tests` project. The order is: data model first, then parser logic, then Program.cs branching and the `RunArtifact` helper, and finally wiring and tests.

## Tasks

- [x] 1. Add `CliCommand` enum and extend `CliArguments` record
  - [x] 1.1 Define `CliCommand` enum and add `Command` field to `CliArguments`
    - Add `CliCommand` enum (`Build`, `Run`) to `Source/Mist.CLI/Args/CliArguments.cs`
    - Add `Command` parameter (default `CliCommand.Build`) as the second positional parameter in the `CliArguments` record, after `ProjectFolder`
    - _Requirements: 9.1_

  - [ ]* 1.2 Write unit tests for `CliCommand` enum and `CliArguments` defaults
    - Verify `CliArguments` defaults `Command` to `CliCommand.Build` when not specified
    - Add tests in `Test/Mist.CLI.Tests/Args/CliArgumentParserTests.cs`
    - _Requirements: 9.1_

- [x] 2. Implement command verb parsing in `CliArgumentParser`
  - [x] 2.1 Add command verb detection to `CliArgumentParser.Parse`
    - After the main loop collects positional args, inspect `positionalArgs[0]`: if `"build"` set command to `Build` and remove from list; if `"run"` set command to `Run` and remove from list; otherwise leave list untouched (implicit `Build`)
    - Pass the resolved `command` into the `CliArguments` constructor
    - _Requirements: 1.1, 1.2, 1.3, 2.1, 2.2, 2.3, 9.2_

  - [x] 2.2 Add `run` + `--mode lex` validation
    - After all parsing, if `command == CliCommand.Run && mode == CompilerMode.Lex`, add `CliParseError("The 'run' command is not compatible with '--mode lex'.")`
    - _Requirements: 7.1_

  - [x] 2.3 Update the usage string
    - Change the usage string in `Program.cs` from `"Usage: mist [project-folder] ..."` to `"Usage: mist <build|run> [project-folder] [--backend <id>] [--output <dir>] [--bin <dir>] [--mode <compile|lex>]"`
    - _Requirements: 8.1_

  - [ ]* 2.4 Write unit tests for command verb parsing
    - `Parse([])` → `Command == Build`, `ProjectFolder == cwd` (Req 2.1)
    - `Parse(["build"])` → `Command == Build` (Req 1.1)
    - `Parse(["run"])` → `Command == Run` (Req 1.2)
    - `Parse(["<valid-dir>"])` → `Command == Build`, `ProjectFolder == dir` (Req 1.3, 2.2)
    - `Parse(["--backend", "csharp"])` → `Command == Build`, `ProjectFolder == cwd` (Req 2.3)
    - `Parse(["run", "--mode", "lex"])` → error containing "not compatible" (Req 7.1)
    - `Parse(["build", "--mode", "lex"])` → no error, `Mode == Lex` (Req 1.1)
    - Verify parse error output includes updated usage string with `<build|run>` (Req 8.1)
    - Add tests in `Test/Mist.CLI.Tests/Args/CliArgumentParserTests.cs`
    - _Requirements: 1.1, 1.2, 1.3, 2.1, 2.2, 2.3, 7.1, 8.1_

  - [ ]* 2.5 Write property test: Non-"run" arguments default to Build command
    - **Property 1: Non-"run" arguments default to Build command**
    - Generate random valid argument arrays whose first positional token is not `"run"` (including `"build"`, a valid directory path, flag-only arrays, and empty arrays). Assert `Command == CliCommand.Build` with no errors.
    - Use FsCheck.Xunit with a custom generator; use `Directory.GetCurrentDirectory()` or a temp directory as the project folder value.
    - Add test in `Test/Mist.CLI.Tests/Args/CliArgumentParserTests.cs`
    - **Validates: Requirements 1.1, 1.3, 2.1, 2.2, 2.3, 9.2**

  - [ ]* 2.6 Write property test: "run" verb sets Run command
    - **Property 2: "run" verb sets Run command**
    - Generate random valid argument arrays with `"run"` as the first positional token, excluding `--mode lex` from flags. Assert `Command == CliCommand.Run` with no errors.
    - Use FsCheck.Xunit with a custom generator.
    - Add test in `Test/Mist.CLI.Tests/Args/CliArgumentParserTests.cs`
    - **Validates: Requirements 1.2, 9.2**

  - [ ]* 2.7 Write property test: "run" with lex mode is rejected
    - **Property 3: "run" with lex mode is rejected**
    - Generate random argument arrays with `"run"` as the first positional token and `--mode lex` always included. Assert at least one error whose message indicates `run` is not compatible with `--mode lex`.
    - Use FsCheck.Xunit with a custom generator.
    - Add test in `Test/Mist.CLI.Tests/Args/CliArgumentParserTests.cs`
    - **Validates: Requirements 7.1**

- [x] 3. Checkpoint
  - Ensure all tests pass, ask the user if questions arise.

- [x] 4. Implement command branching and `RunArtifact` in `Program.cs`
  - [x] 4.1 Add `Build` command branching in `Program.Main`
    - After successful parse, branch on `cliArgs.Command`: for `CliCommand.Build`, execute the existing build pipeline (lex mode or compile mode) unchanged
    - _Requirements: 3.1, 3.2_

  - [x] 4.2 Add `Run` command branching in `Program.Main`
    - For `CliCommand.Run`, execute the build pipeline (compile mode). On build failure, print diagnostics and return non-zero exit code. On success, check `result.PrimaryOutputArtifactPath`: if null, print `"error: Build succeeded but no executable artifact was produced."` to stderr and return exit code 1
    - _Requirements: 4.1, 4.2, 6.3, 6.4_

  - [x] 4.3 Implement `RunArtifact` private helper method
    - Create `private static int RunArtifact(string artifactPath)` in `Program`
    - Create `ProcessStartInfo` targeting `"dotnet"` with the artifact path as argument
    - Set `UseShellExecute = false`, do not redirect stdout/stderr (inherit from parent)
    - Start the process; if `Process.Start` returns null, print `"error: Failed to start 'dotnet' process."` to stderr and return 1
    - Wrap in try/catch; on exception print `"error: Failed to launch 'dotnet': {ex.Message}"` to stderr and return 1
    - Call `process.WaitForExit()` and return `process.ExitCode`
    - _Requirements: 5.1, 5.2, 5.3, 5.4, 6.1, 6.2_

  - [ ]* 4.4 Write unit tests for `Run` command error paths
    - Test that a null `PrimaryOutputArtifactPath` after successful compilation produces the expected error message and exit code 1
    - Test that a process start failure produces the expected error message and exit code 1
    - Add tests in `Test/Mist.CLI.Tests/`
    - _Requirements: 6.1, 6.2, 6.3, 6.4_

- [x] 5. Final checkpoint
  - Ensure all tests pass, ask the user if questions arise.

## Notes

- Tasks marked with `*` are optional and can be skipped for faster MVP
- Each task references specific requirements for traceability
- The existing `Test/Mist.CLI.Tests` project already has `FsCheck.Xunit 3.1.0` as a dependency — no package additions needed for property tests
- No changes are required to the compiler library (`Source/Mist`) — all modifications are in `Source/Mist.CLI`
- Property tests validate the argument-parsing layer only; process-launching logic is integration-level
