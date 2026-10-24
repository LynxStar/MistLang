# Requirements Document

## Introduction

The Mist CLI reports progress through the standard compilation pipeline. It reports cleaning (Stage 0); `CompilerDriver` reports loading, optional package resolution, lexing, and parsing (Stages 1, 1.5, 2, and 3); and `StandardPipeline` reports program-model construction, analysis, and emission (Stages 4–6). The current `ICompilationProgressReporter` contract carries stage events and the final build summary. The CLI uses `ConsoleProgressReporter` when progress is enabled and `NullProgressReporter` when it is disabled. Diagnostics are not progress and no reporter carries one: the driver delivers Stage 1.5's diagnostics through a separate `IResolutionDiagnosticListener`, and the CLI writes every diagnostic to stderr whatever the verbosity (Requirement 12). Stage details below describe the current driver and pipeline behavior.

## Glossary

- **CLI**: The `Mist.CLI` executable (`Program.cs`), which reports Stage 0 cleaning, selects a progress reporter, and reports the final build summary.
- **CompilerDriver**: The driver for loading, optional package resolution, lexing, and parsing; it passes parsed inputs to the compilation pipeline for Stages 4–6.
- **StandardPipeline**: The sole compilation pipeline. It builds the program model (Stage 4), runs analyzers against it (Stage 5), and emits through the C# backend (Stage 6).
- **ICompilationProgressReporter**: The current driver contract for stage start and completion events and the final build summary.
- **ConsoleProgressReporter**: The CLI implementation of `ICompilationProgressReporter` that writes formatted progress to its `TextWriter`.
- **NullProgressReporter**: A no-op singleton implementation used when progress output is disabled or unwanted.
- **IResolutionDiagnosticListener**: The driver contract, in `Mist.Driver.Contracts`, that receives each diagnostic Stage 1.5 reports, during that stage. It is optional and separate from `ICompilationProgressReporter`.
- **CompileFeedback**: The CLI's listener for `build`, `run` and `test` (`Mist.CLI/Output/CompileFeedback.cs`). It writes diagnostics to stderr, reports the build summary through the progress reporter, and decides whether a command goes on to launch anything.
- **Stage**: A numbered, named unit of reported work: cleaning (0), loading (1), optional package resolution (1.5), lexing (2), parsing (3), program-model construction (4), analysis (5), and emission (6).
- **CompilationUnit**: A single Mist source file passed through the pipeline, carrying standard-library, project, namespace, and parsed-AST metadata.
- **TokenStream**: The lexer output for one compilation unit; exposes `DefaultChannelTokens` (parser-visible) and `AllTokens`.
- **ParseResult**: The parser result for one compilation unit; carries its source, token stream, nullable AST root, and diagnostics.
- **IProgramModel**: The fully resolved semantic representation passed to Stage 5 analyzers and the Stage 6 emitter.
- **IProgramAnalyzer**: An internal Stage 5 rule that analyzes the completed `IProgramModel` and reports diagnostics.
- **CompilerResult**: The driver result carrying `Succeeded`, `PrimaryOutputArtifactPath`, and diagnostics.
- **BackendResult**: The C# backend's emission result carrying success, primary artifact path, diagnostics, and emitted-file count.
- **Top-level construct**: A top-level class, struct, or abstract type in the Self or Reference assemblies. Standard-library types, nested types, enums, aliases, and newtype clones are excluded; the count comes from `Driver/TopLevelConstructs.cs`, not symbol-kind totals.

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

### Requirement 6: Stage 4 — Program Model

**User Story:** As a developer, I want to see how many top-level constructs of my program were bound into the program model, so that I can confirm the compiler found all my types.

#### Acceptance Criteria

1. WHEN Stages 1, 2 and 3 reported no error, THE `StandardPipeline` SHALL report Stage 4 under the title `Program Model`, Stage 1.5 (Package Resolution) errors notwithstanding (csharp-interop-v2 Requirements 2.3 and 2.6). THE stage SHALL cover the creation of the ambient type provider and `ProgramModelBuilder.Build`.
2. WHEN the pipeline finishes building the program model, THE `ICompilationProgressReporter` SHALL be notified of Stage 4 completion with the count of top-level constructs, computed by `Driver/TopLevelConstructs.cs`.
3. THE top-level construct count SHALL be the number of class, struct and abstract types in the `Types` of every compilation unit of the program model's Self and Reference assemblies. An abstract type is what `Interface` and `Trait` mean elsewhere in this document.
4. Types declared in a referenced project SHALL be counted. Types declared in Standard SHALL NOT be counted.
5. Nested types SHALL NOT be counted, because `Types` holds only top-level declarations. Monomorphs, union carriers and closures SHALL NOT be counted, because the emitter produces them and they never appear in `Types`.
6. Enums and type aliases SHALL NOT be counted. A newtype's clone, which replaces the newtype in its compilation unit as a class or struct, SHALL NOT be counted.
7. THE detail line for Stage 4 SHALL be built by `StandardStageDetails` in the format `{N} top-level constructs`, formatted in the invariant culture and passed to the reporter unindented; the indentation is the reporter's (Requirement 10).
8. WHEN model construction produces no model, THE `StandardPipeline` SHALL complete Stage 4 with a count of `0` and SHALL NOT start Stage 5 or Stage 6.
9. WHEN Stage 4 completes, THE `ConsoleProgressReporter` SHALL emit the elapsed time for the program model step in the format `Took X.XXs`.

---

### Requirement 7: Stage 5 — Analysis

**User Story:** As a developer, I want to see each analyzer that ran and how long it took, so that I can identify slow analysis passes.

#### Acceptance Criteria

1. WHEN Stage 4 produced a program model, THE `StandardPipeline` SHALL run Stage 5 under the title `Analysis`, including when Stage 4 reported errors.
2. `ProgramAnalysis/AnalysisTimings.cs` SHALL time each `IProgramAnalyzer.Analyze` call and add it to the entry of that analyzer instance, over every scope the runner analyzes: the program, each clone conversion that is judged (one with a `BindingFailure` is not), and each cloned type.
3. WHEN the pipeline finishes running all analyzers, THE `ICompilationProgressReporter` SHALL be notified of Stage 5 completion with detail lines built by `StandardStageDetails`, in this order: one scope line, one line per analyzer instance, one remainder line.
4. THE scope line SHALL follow the format `{A} analyzers over {S} scopes: {P} program, {C} clone conversions, {K} cloned types`.
5. THE detail SHALL include one line per analyzer instance, in registration order, in the format `{Id} ({TypeName})  {X.XXs}`, where `{Id}` is its `IProgramAnalyzer.Id` and `{TypeName}` is the name of its type. Entries SHALL NOT be summed under an `Id`, because several analyzers share one and the type name is what keeps them apart.
6. THE remainder line SHALL follow the format `outside analyzers  {X.XXs}`, reporting the stage's elapsed time minus the sum of the analyzer entries, clamped at zero, so that the lines account for the whole stage.
7. Seconds in Stage 5 detail lines SHALL be formatted `F2` in the invariant culture. Detail lines SHALL be passed to the reporter unindented; the indentation is the reporter's (Requirement 10).
8. WHEN Stage 5 completes, THE `ConsoleProgressReporter` SHALL emit the elapsed time for the total analysis step in the format `Took X.XXs`.
9. THE `StandardPipeline` SHALL judge the diagnostics of Stage 4 and Stage 5 together, at one gate after Stage 5: Stage 6 SHALL run only when Stages 1, 2, 3, 4 and 5 AND Stage 1.5 (Package Resolution) reported no error. An error from Stage 1.5 is preserved in the result, in stage order, and the build fails.
10. WHEN Stage 6 runs, THE `.cs` file count of Requirement 8 SHALL be `CSharpEmitterV2.EmittedFileCount`, the files this run wrote, on every return path; the output directory SHALL NOT be recounted. THE artifact path line SHALL follow the count only when there is an artifact. A failed emission SHALL still complete Stage 6.
11. A stage that reports its start SHALL report its completion on every handled path; an exception propagates with no completion. This holds outside the pipeline too: a load that fails SHALL complete Stage 1 with no detail lines, and a clean that fails SHALL complete the CLI's Stage 0 with no detail lines.
12. THE `StandardPipeline` SHALL NOT report a build summary. THE summary of Requirement 9 SHALL be reported once, by the CLI.

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
2. THE CLI SHALL write every diagnostic error and warning message to stderr, through `CompileFeedback` for `build`, `run` and `test` (Requirement 12). THE `ConsoleProgressReporter` SHALL write no diagnostic, so that the progress reporter the verbosity selects never decides whether a diagnostic appears.
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

---

### Requirement 12: Dependency Diagnostic Delivery

**User Story:** As a developer, I want a package or framework reference that fails to resolve reported while the build is still at Stage 1.5, once, whatever the verbosity, so that I see the cause before the rest of the build runs and do not read it twice.

#### Acceptance Criteria

1. THE `IResolutionDiagnosticListener` SHALL be defined in `Mist.Driver.Contracts` with one method that receives one `Diagnostic`. THE `CompilerDriver` SHALL accept one as an optional constructor argument, beside its progress reporter.
2. WHEN Stage 1.5 runs and the driver has a listener, THE `CompilerDriver` SHALL deliver every diagnostic the package and framework-reference resolvers reported, each once and in report order, after both resolvers return and before Stage 2 starts. WHEN `VerboseResolution` is set, delivery SHALL come after the Stage 1.5 start and before its completion.
3. Delivery SHALL be per diagnostic: two dependencies that fail with the same id SHALL be two deliveries, never merged (csharp-interop-v2 Requirements 3.6 and 5.7).
4. Every delivered diagnostic SHALL remain in `CompilerResult.Diagnostics` as the same instance, after the diagnostics Stage 1 reported and before those of Stage 2. Delivery SHALL NOT change which stages run, the error gate of Requirement 7.9, or the result's verdict and artifact.
5. WHEN the driver has no listener, THE `CompilerDriver` SHALL deliver nothing and return the same result. The driver SHALL perform no console I/O either way.
6. WHEN no package or framework reference is declared, or the build stops before Stage 1.5 (an unknown backend, a failed load), nothing SHALL be delivered.
7. THE CLI's `build`, `run` and `test` SHALL give the driver a `CompileFeedback` over stderr, independent of whichever progress reporter the verbosity selected, so that `--quiet` and the progress-free default of `run` and `test` hide no diagnostic.
8. `CompileFeedback` SHALL write each delivered diagnostic when it arrives; after compilation it SHALL report the summary, with error and warning counts taken from every diagnostic in the result, and then write the result's diagnostics in order, except the instances it has already written. It SHALL match by instance, never by id, message or value equality, so an equal diagnostic it did not receive is still written.
9. A result built with no delivery, such as a project-file failure before the driver runs or a failed load, SHALL be written in full after the summary.
10. `run` and `test` SHALL launch the artifact or the test runner only when the build succeeded; a failed build SHALL exit with code 1 and launch nothing.
11. Each compilation SHALL start with nothing recorded as written, so repeated runs each write their own diagnostics once.
12. `mist restore` runs no compilation and SHALL keep printing its diagnostics after its summary (csharp-interop-v2 Requirement 13).
