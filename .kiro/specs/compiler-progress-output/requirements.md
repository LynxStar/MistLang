# Requirements Document

## Introduction

The Mist compiler CLI is currently silent during successful builds — it produces no output unless there are errors. This feature adds structured, per-stage progress output to the CLI so developers can see what the compiler is doing, how long each stage takes, and a final build summary. The output covers seven stages: cleaning, loading, lexing, parsing, semantic model construction, analysis, and emission. The compiler driver is extended with a progress-reporter interface so the feature remains testable (tests inject a no-op reporter; the CLI injects a console reporter).

## Glossary

- **CLI**: The `Mist.CLI` executable (`Program.cs`) that parses arguments and drives compilation.
- **CompilerDriver**: The `Mist.Driver.CompilerDriver` class that orchestrates the full compilation pipeline.
- **ICompilationProgressReporter**: The new interface through which the driver emits progress events.
- **ConsoleProgressReporter**: The CLI-side implementation of `ICompilationProgressReporter` that writes formatted output to stdout.
- **NullProgressReporter**: A no-op singleton implementation of `ICompilationProgressReporter` used in tests and any context where progress output is unwanted.
- **Stage**: A numbered, named phase of the compilation pipeline (0–6).
- **CompilationUnit**: A single Mist source file passed through the pipeline, carrying an `IsStdlib` flag and an optional `OriginProjectPath`.
- **TokenStream**: The lexer output for one compilation unit; exposes `DefaultChannelTokens` (parser-visible) and `AllTokens`.
- **ParseResult**: The parser output for one compilation unit; exposes a `Root` `SyntaxNode`.
- **ISemanticModel**: The semantic model built from all ASTs; exposes `AllSymbols`.
- **SymbolKind**: Enum of semantic symbol kinds (`Class`, `Struct`, `Interface`, `Trait`, `Namespace`, etc.).
- **SymbolOrigin**: Enum distinguishing `UserCode` from `Backend` symbols.
- **IAnalyzer**: A pluggable analysis pass with an `AnalyzerId` string property.
- **BackendResult**: The emission result; carries `Succeeded` and `PrimaryArtifactPath`.
- **UserCode top-level construct**: A symbol whose `Origin == UserCode` and whose `Kind` is one of `Class`, `Struct`, `Interface`, or `Trait`. `Namespace` symbols and stdlib symbols are excluded.

---

## Requirements

### Requirement 1: Progress Reporter Interface

**User Story:** As a compiler library consumer, I want the driver to accept a progress reporter through a well-defined interface, so that I can inject a no-op reporter in tests and a real reporter in the CLI without coupling the driver to console I/O.

#### Acceptance Criteria

1. THE `ICompilationProgressReporter` SHALL define a method for reporting the start of a named stage identified by a stage number and a stage title.
2. THE `ICompilationProgressReporter` SHALL define a method for reporting the completion of a stage, accepting the elapsed time and a string detail line.
3. THE `ICompilationProgressReporter` SHALL define a method for reporting the final build summary, accepting a succeeded flag, error count, warning count, and total elapsed time.
4. THE `NullProgressReporter` SHALL implement `ICompilationProgressReporter` and perform no I/O for any method call.
5. THE `NullProgressReporter` SHALL expose a static `Instance` singleton property to avoid repeated allocations.
6. WHEN `CompilerDriver` is constructed without an explicit `ICompilationProgressReporter`, THE `CompilerDriver` SHALL use `NullProgressReporter.Instance` as the default.

---

### Requirement 2: Stage 0 — Cleaning

**User Story:** As a developer, I want to see when the output and bin directories are being cleaned, so that I know the build environment is being reset before compilation starts.

#### Acceptance Criteria

1. WHEN the CLI cleans the output and bin directories before invoking the driver, THE `ConsoleProgressReporter` SHALL emit a Stage 0 header line in the format `Stage 0 - Cleaning`.
2. WHEN Stage 0 completes, THE `ConsoleProgressReporter` SHALL emit a detail line indented by four spaces describing which directories were cleaned.
3. WHEN Stage 0 completes, THE `ConsoleProgressReporter` SHALL emit the elapsed time for the cleaning step in the format `Took X.XXs`.

---

### Requirement 3: Stage 1 — Loading

**User Story:** As a developer, I want to see how many files were loaded in each group (stdlib, referenced projects, user files), so that I can verify the compiler is picking up the right sources.

#### Acceptance Criteria

1. WHEN the driver finishes loading all compilation units, THE `ICompilationProgressReporter` SHALL be notified of Stage 1 completion with a detail string listing each group and its file count.
2. THE detail string for Stage 1 SHALL include the stdlib file count, the referenced-project file count, and the user file count as separate labelled entries, each on its own indented line.
3. WHEN Stage 1 completes, THE `ConsoleProgressReporter` SHALL emit the elapsed time for the loading step in the format `Took X.XXs`.

---

### Requirement 4: Stage 2 — Lexing

**User Story:** As a developer, I want to see the total number of tokens produced by the lexer, so that I have a rough sense of the source size being compiled.

#### Acceptance Criteria

1. WHEN the driver finishes lexing all compilation units, THE `ICompilationProgressReporter` SHALL be notified of Stage 2 completion with the total count of default-channel tokens across all successfully-lexed units.
2. THE detail string for Stage 2 SHALL report the token count in the format `    {N} tokens`.
3. WHEN Stage 2 completes, THE `ConsoleProgressReporter` SHALL emit the elapsed time for the lexing step in the format `Took X.XXs`.

---

### Requirement 5: Stage 3 — Parsing

**User Story:** As a developer, I want to see the total number of AST nodes produced by the parser, so that I can gauge the structural complexity of the compiled program.

#### Acceptance Criteria

1. WHEN the driver finishes parsing all compilation units, THE `ICompilationProgressReporter` SHALL be notified of Stage 3 completion with the total AST node count across all successfully-parsed files.
2. THE AST node count SHALL be computed by walking each `ParseResult.Root` `SyntaxNode` tree and counting every node (including the root).
3. THE detail string for Stage 3 SHALL report the node count in the format `    {N} AST nodes`.
4. WHEN Stage 3 completes, THE `ConsoleProgressReporter` SHALL emit the elapsed time for the parsing step in the format `Took X.XXs`.

---

### Requirement 6: Stage 4 — Semantic Model

**User Story:** As a developer, I want to see how many top-level user-code constructs were resolved by the semantic model, so that I can confirm the compiler found all my types.

#### Acceptance Criteria

1. WHEN the driver finishes building the semantic model, THE `ICompilationProgressReporter` SHALL be notified of Stage 4 completion with the count of top-level user-code constructs.
2. THE top-level user-code construct count SHALL be computed by filtering `ISemanticModel.AllSymbols` to symbols where `Origin == SymbolOrigin.UserCode` and `Kind` is one of `SymbolKind.Class`, `SymbolKind.Struct`, `SymbolKind.Interface`, or `SymbolKind.Trait`.
3. `WithDeclarationNode` constructs SHALL NOT be counted because they do not register symbols in the semantic model.
4. THE detail string for Stage 4 SHALL report the count in the format `    {N} top-level constructs`.
5. WHEN Stage 4 completes, THE `ConsoleProgressReporter` SHALL emit the elapsed time for the semantic model step in the format `Took X.XXs`.

---

### Requirement 7: Stage 5 — Analysis

**User Story:** As a developer, I want to see each analyzer that ran and how long it took, so that I can identify slow analysis passes.

#### Acceptance Criteria

1. WHEN the driver finishes running all analyzers, THE `ICompilationProgressReporter` SHALL be notified of Stage 5 completion with per-analyzer timing information.
2. THE detail for Stage 5 SHALL include one entry per analyzer that ran, identified by its `IAnalyzer.AnalyzerId` string and the elapsed time for that analyzer.
3. THE detail string for each analyzer entry SHALL follow the format `    {AnalyzerId}  {X.XXs}`.
4. WHEN Stage 5 completes, THE `ConsoleProgressReporter` SHALL emit the elapsed time for the total analysis step in the format `Took X.XXs`.

---

### Requirement 8: Stage 6 — Emit

**User Story:** As a developer, I want to see what was emitted and where the output artifact was written, so that I know where to find the compiled output.

#### Acceptance Criteria

1. WHEN the driver finishes the backend emission step, THE `ICompilationProgressReporter` SHALL be notified of Stage 6 completion with the number of `.cs` files written and the primary artifact path.
2. THE detail string for Stage 6 SHALL include the `.cs` file count in the format `    {N} .cs files written`.
3. THE detail string for Stage 6 SHALL include the output artifact path in the format `    {PrimaryArtifactPath}`.
4. WHEN Stage 6 completes, THE `ConsoleProgressReporter` SHALL emit the elapsed time for the emission step in the format `Took X.XXs`.

---

### Requirement 9: Final Build Summary

**User Story:** As a developer, I want a single summary line at the end of every build, so that I can immediately see whether the build succeeded or failed and how long it took in total.

#### Acceptance Criteria

1. WHEN compilation finishes (whether succeeded or failed), THE `ICompilationProgressReporter` SHALL be notified with the final build outcome, total error count, total warning count, and total elapsed time.
2. THE `ConsoleProgressReporter` SHALL emit the summary in the format `Build succeeded  {E} error(s), {W} warning(s)  Total: {X.XXs}` when the build succeeded.
3. THE `ConsoleProgressReporter` SHALL emit the summary in the format `Build failed  {E} error(s), {W} warning(s)  Total: {X.XXs}` when the build failed.
4. THE total elapsed time in the summary SHALL cover the entire build from the start of Stage 0 (cleaning) to the end of the last completed stage.

---

### Requirement 10: Output Formatting

**User Story:** As a developer, I want consistent, readable formatting for all progress output, so that the build log is easy to scan.

#### Acceptance Criteria

1. THE `ConsoleProgressReporter` SHALL write all progress output to stdout.
2. THE `ConsoleProgressReporter` SHALL write all diagnostic error and warning messages to stderr (no change from current behaviour).
3. WHEN a stage header is emitted, THE `ConsoleProgressReporter` SHALL format it as `Stage {N} - {Title}` on its own line.
4. WHEN stage detail lines are emitted, THE `ConsoleProgressReporter` SHALL indent each detail line with exactly four spaces.
5. WHEN a stage elapsed-time line is emitted, THE `ConsoleProgressReporter` SHALL format it as `Took {X.XX}s` where the time is formatted to two decimal places.
6. THE `ConsoleProgressReporter` SHALL emit a blank line after the elapsed-time line of each stage to visually separate stages.

---

### Requirement 11: Testability — NullProgressReporter in Tests

**User Story:** As a test author, I want to pass a no-op reporter to the compiler driver, so that existing and new tests produce no console output and are not affected by the progress-reporting feature.

#### Acceptance Criteria

1. WHEN `CompilerDriver.Compile` is called with `NullProgressReporter.Instance`, THE `CompilerDriver` SHALL produce identical `CompilerResult` output as it did before this feature was introduced.
2. THE `NullProgressReporter` SHALL NOT throw, allocate, or perform I/O for any method call.
3. THE `ICompilationProgressReporter` interface SHALL be defined in the `Mist.Driver.Contracts` namespace so that test projects can reference it without depending on the CLI project.
