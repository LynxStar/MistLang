# Implementation Plan: Compiler Progress Output

## Overview

This plan implements structured per-stage progress output for the Mist compiler CLI. The work is organized into: defining the reporter interface and null implementation, extending the driver to call the reporter at each stage, extending supporting types (`AnalyzerRunResult`, `BackendResult`, `SyntaxTreeMetrics`), implementing the console reporter in the CLI, and wiring everything together in `Program.cs`. Each task builds incrementally so the project compiles at every step.

## Tasks

- [x] 1. Define ICompilationProgressReporter interface and NullProgressReporter
  - [x] 1.1 Create `ICompilationProgressReporter` interface in `Source/Mist/Driver/Contracts/ICompilationProgressReporter.cs`
    - Define `ReportStageStart(int stageNumber, string title)`
    - Define `ReportStageComplete(int stageNumber, TimeSpan elapsed, IReadOnlyList<string> details)`
    - Define `ReportBuildSummary(bool succeeded, int errorCount, int warningCount, TimeSpan totalElapsed)`
    - _Requirements: 1.1, 1.2, 1.3_
  - [x] 1.2 Create `NullProgressReporter` in `Source/Mist/Driver/NullProgressReporter.cs`
    - Implement all interface methods as no-ops
    - Expose `public static readonly NullProgressReporter Instance` singleton
    - Private constructor to prevent external instantiation
    - Place in `Mist.Driver` namespace (same as `CompilerDriver`)
    - _Requirements: 1.4, 1.5, 11.2, 11.3_

- [x] 2. Extend supporting types for progress data
  - [x] 2.1 Add `AnalyzerTimingEntry` and `AnalyzerRunResult` records in `Source/Mist/Analysis/AnalyzerRunner.cs`
    - Add `public sealed record AnalyzerTimingEntry(string AnalyzerId, TimeSpan Elapsed)`
    - Add `public sealed record AnalyzerRunResult(ImmutableArray<Diagnostic> Diagnostics, ImmutableArray<AnalyzerTimingEntry> Timings)`
    - Change `AnalyzerRunner.RunAll` return type from `ImmutableArray<Diagnostic>` to `AnalyzerRunResult`
    - Wrap each `analyzer.Analyze` call in a `Stopwatch` to capture per-analyzer timing
    - Update all call sites of `RunAll` in `CompilerDriver.Compile` to use the new return type
    - _Requirements: 7.1, 7.2_
  - [x] 2.2 Extend `BackendResult` with `EmittedFileCount` property
    - Add `int EmittedFileCount = 0` parameter with default value to `BackendResult` record in `Source/Mist/Backends/Contracts/BackendResult.cs`
    - Update `CSharpTranspilerBackend` (and any other backend) to pass the actual emitted `.cs` file count
    - _Requirements: 8.1_
  - [x] 2.3 Create `SyntaxTreeMetrics` utility in `Source/Mist/Driver/SyntaxTreeMetrics.cs`
    - Implement `internal static int CountNodes(Storm.Runtime.SyntaxNode root)` that recursively counts all nodes including the root
    - _Requirements: 5.2_

- [x] 3. Checkpoint
  - Ensure all tests pass, ask the user if questions arise.

- [x] 4. Integrate progress reporting into CompilerDriver
  - [x] 4.1 Add optional `ICompilationProgressReporter?` parameter to `CompilerDriver` constructor
    - Add `private readonly ICompilationProgressReporter progressReporter` field
    - Default `null` parameter resolved to `NullProgressReporter.Instance`
    - Existing call sites (tests, CLI) continue to compile without changes
    - _Requirements: 1.6_
  - [x] 4.2 Add Stage 1 (Loading) reporting to `CompilerDriver.Compile`
    - Wrap the loading section (Steps 1–1d) in a `Stopwatch`
    - Call `ReportStageStart(1, "Loading")` before loading begins
    - After combining units, compute stdlib count, referenced-project count, and user file count
    - Build detail strings: `"{N} stdlib files"`, `"{N} referenced project files"`, `"{N} user files"`
    - Call `ReportStageComplete(1, elapsed, details)`
    - _Requirements: 3.1, 3.2, 3.3_
  - [x] 4.3 Add Stage 2 (Lexing) reporting to `CompilerDriver.Compile`
    - Wrap the lexing loop in a `Stopwatch`
    - Call `ReportStageStart(2, "Lexing")` before lexing begins
    - After lexing, sum `DefaultChannelTokens.Length` across all successfully-lexed token streams
    - Build detail string: `"{N} tokens"`
    - Call `ReportStageComplete(2, elapsed, details)`
    - _Requirements: 4.1, 4.2, 4.3_
  - [x] 4.4 Add Stage 3 (Parsing) reporting to `CompilerDriver.Compile`
    - Wrap the parsing loop in a `Stopwatch`
    - Call `ReportStageStart(3, "Parsing")` before parsing begins
    - After parsing, use `SyntaxTreeMetrics.CountNodes` on each successfully-parsed root and sum
    - Build detail string: `"{N} AST nodes"`
    - Call `ReportStageComplete(3, elapsed, details)`
    - _Requirements: 5.1, 5.2, 5.3, 5.4_
  - [x] 4.5 Add Stage 4 (Semantic Model) reporting to `CompilerDriver.Compile`
    - Wrap the semantic model construction in a `Stopwatch`
    - Call `ReportStageStart(4, "Semantic Model")` before building
    - After building, filter `semanticModel.AllSymbols` to `Origin == UserCode` and `Kind` in {Class, Struct, Interface, Trait}
    - Build detail string: `"{N} top-level constructs"`
    - Call `ReportStageComplete(4, elapsed, details)`
    - _Requirements: 6.1, 6.2, 6.3, 6.4, 6.5_
  - [x] 4.6 Add Stage 5 (Analysis) reporting to `CompilerDriver.Compile`
    - Wrap the analyzer run in a `Stopwatch`
    - Call `ReportStageStart(5, "Analysis")` before running analyzers
    - After `RunAll` returns `AnalyzerRunResult`, build detail strings from `Timings`: `"{AnalyzerId}  {elapsed.TotalSeconds:F2}s"` per entry
    - Call `ReportStageComplete(5, elapsed, details)`
    - _Requirements: 7.1, 7.2, 7.3, 7.4_
  - [x] 4.7 Add Stage 6 (Emit) reporting to `CompilerDriver.Compile`
    - Wrap the backend emission in a `Stopwatch`
    - Call `ReportStageStart(6, "Emit")` before emitting
    - After emission, build detail strings: `"{EmittedFileCount} .cs files written"` and `"{PrimaryArtifactPath}"` (if non-null)
    - Call `ReportStageComplete(6, elapsed, details)`
    - _Requirements: 8.1, 8.2, 8.3, 8.4_

- [x] 5. Checkpoint
  - Ensure all tests pass, ask the user if questions arise.

- [x] 6. Implement ConsoleProgressReporter
  - [x] 6.1 Create `ConsoleProgressReporter` in `Source/Mist.CLI/Output/ConsoleProgressReporter.cs`
    - Accept a `TextWriter` in the constructor for testability
    - Implement `ReportStageStart`: write `"Stage {N} - {Title}"` line
    - Implement `ReportStageComplete`: write each detail line prefixed with four spaces, then `"    Took {elapsed.TotalSeconds:F2}s"`, then a blank line
    - Implement `ReportBuildSummary`: write `"{status}  {E} error(s), {W} warning(s)  Total: {T.TT}s"`
    - _Requirements: 2.1, 2.2, 2.3, 10.1, 10.3, 10.4, 10.5, 10.6, 9.2, 9.3_

- [x] 7. Wire progress reporting into CLI Program.cs
  - [x] 7.1 Update `RunBuildPipeline` in `Source/Mist.CLI/Program.cs` to use progress reporting
    - Create a `ConsoleProgressReporter(Console.Out)` instance
    - Create a top-level `Stopwatch` for total build time
    - Report Stage 0 (Cleaning): call `ReportStageStart(0, "Cleaning")` before cleaning, then `ReportStageComplete(0, elapsed, details)` after with detail listing cleaned directories
    - Pass the reporter to `CompilerDriver` constructor
    - After `Compile` returns, call `ReportBuildSummary` with succeeded flag, error/warning counts from diagnostics, and total elapsed time
    - _Requirements: 2.1, 2.2, 2.3, 9.1, 9.4_
  - [x] 7.2 Update `RunRunCommand` in `Source/Mist.CLI/Program.cs` to use progress reporting
    - Same pattern as `RunBuildPipeline`: create reporter, report Stage 0, pass to driver, report summary
    - _Requirements: 2.1, 9.1_

- [x] 8. Checkpoint
  - Ensure all tests pass, ask the user if questions arise.

- [x] 9. Write unit tests for NullProgressReporter and ConsoleProgressReporter
  - [x] 9.1 Write unit tests for `NullProgressReporter` in `Test/Mist.Tests/`
    - Test that all methods are callable without exception
    - Test that `Instance` returns the same reference (singleton)
    - _Requirements: 1.4, 1.5, 11.2_
  - [x] 9.2 Write unit tests for `ConsoleProgressReporter` in `Test/Mist.CLI.Tests/`
    - Use `StringWriter` to capture output
    - Test `ReportStageStart` produces `"Stage N - Title\n"`
    - Test `ReportStageComplete` produces indented details, elapsed time line, and blank separator
    - Test `ReportBuildSummary` produces correct format for both succeeded and failed
    - _Requirements: 10.3, 10.4, 10.5, 10.6, 9.2, 9.3_

- [x] 10. Write property-based tests
  - [x] 10.1 Write property test for AST node count recursion
    - **Property 1: AST node count is recursive sum**
    - Generate arbitrary tree structures and verify `CountNodes(root) == 1 + sum(CountNodes(child))`
    - **Validates: Requirements 5.2**
  - [x] 10.2 Write property test for top-level construct count filter
    - **Property 2: Top-level construct count matches symbol filter**
    - Generate lists of `(SymbolKind, SymbolOrigin)` pairs, verify filter count matches manual count
    - **Validates: Requirements 6.1, 6.2**
  - [x] 10.3 Write property test for ConsoleProgressReporter stage formatting
    - **Property 3: ConsoleProgressReporter stage formatting**
    - Generate random stage numbers (0–6), title strings, detail lists, and TimeSpans
    - Verify output matches expected format: header, indented details, elapsed line, blank line
    - **Validates: Requirements 10.3, 10.4, 10.5, 10.6**
  - [x] 10.4 Write property test for ConsoleProgressReporter summary formatting
    - **Property 4: ConsoleProgressReporter summary formatting**
    - Generate random booleans, non-negative ints, random TimeSpans
    - Verify output matches `"{status}  {E} error(s), {W} warning(s)  Total: {T.TT}s"`
    - **Validates: Requirements 9.2, 9.3**

- [x] 11. Write driver integration tests with spy reporter
  - [x] 11.1 Write tests verifying driver calls reporter with correct stage data
    - Create a spy/recording `ICompilationProgressReporter` implementation
    - Test Stage 1 file counts, Stage 2 token count, Stage 3 node count, Stage 4 construct count
    - Test Stage 5 analyzer timings (one entry per analyzer), Stage 6 emit metrics
    - Test summary is called with correct succeeded flag and diagnostic counts
    - Test that skipped stages (e.g., lex failure) are not reported
    - _Requirements: 3.1, 4.1, 5.1, 6.1, 7.1, 8.1, 9.1, 11.1_

- [x] 12. Final checkpoint
  - Ensure all tests pass, ask the user if questions arise.

- [x] 13. Deliver dependency diagnostics at Stage 1.5, once, apart from progress
  - [x] 13.1 Add `IResolutionDiagnosticListener` to `Mist.Driver.Contracts` and an optional listener argument to `CompilerDriver`; make `ResolutionDiagnostics` deliver Stage 1.5's diagnostics after both resolvers return and before Stage 2, and keep joining the same instances into the result.
    - _Requirements: 12.1, 12.2, 12.3, 12.4, 12.5, 12.6_
  - [x] 13.2 Add `Mist.CLI/Output/CompileFeedback.cs` and route `build`, `run` and `test` through it, replacing their three loops over the result; write delivered diagnostics on arrival and skip those instances after the summary; launch only on success.
    - _Requirements: 10.2, 12.7, 12.8, 12.9, 12.10, 12.11_
  - [x] 13.3 Tests at the owning tiers: `ResolutionDiagnosticsTests` (Tier 1), `CompileFeedbackTests` (Mist.CLI.Tests, Tier 2, supplied outcomes), and the driver seams whose invariant is order against stage starts, `PackageResolutionStageTests` and `CompilerDriverProgressReportingTests` (Tier 4).
    - _Requirements: 12.2, 12.3, 12.4, 12.6, 12.8, 12.9, 12.10, 12.11_
  - Evidence (2026-10-03): the probe `[packages] BadPkg = "not-valid"` printed `MIST0036` after `Build failed` in `build`, `build --verbose`, `run --verbose` and `test --verbose` before this task; it now prints between `Stage 1 - Loading` and `Stage 2 - Lexing` in all of them (inside the Stage 1.5 block under `--verbose`), and once, as it already did, with `--quiet` and in default `run` and `test`, with the same exit code 1. Two invalid packages, an unknown framework and an unresolved source type print the three dependency errors before Stage 2 and the source error after `Build failed  4 error(s)`. Mutants matching written diagnostics by value, and delivering nothing, each fail the new tests. `ConsoleProgressReporter` and `NullProgressReporter` are unchanged; the quiet case is proven where the CLI combines `NullProgressReporter` with `CompileFeedback`.

## Notes

- Tasks marked with `*` are optional and can be skipped for faster MVP
- Each task references specific requirements for traceability
- Checkpoints ensure incremental validation
- The design uses C# throughout — no language selection needed
- Existing partially-created files (`ICompilationProgressReporter.cs`, `NullProgressReporter.cs`, `ConsoleProgressReporter.cs`) need to be updated to match the design's method signatures (`ReportStageStart`/`ReportStageComplete`/`ReportBuildSummary` with `IReadOnlyList<string> details`)
- Property tests use FsCheck.Xunit (already in test project dependencies)
- Integration tests with the spy reporter run in-process (no process spawning needed)
