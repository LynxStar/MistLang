# Implementation Plan: Compiler Audit Tool

## Overview

Build the `Audit` console application that drives incremental code quality auditing of the Mist compiler source. Tasks are ordered bottom-up: data models first, then services, then command workflows, then integration.

## Tasks

- [x] 1. Create project scaffolding
  - Create `Tools/Audit/Audit.csproj` with OutputType=Exe, TargetFramework=net10.0, ImplicitUsings=enable, Nullable=enable, and PackageReference to Microsoft.CodeAnalysis.CSharp 4.12.0
  - Create `Tools/Audit/Models/` directory
  - Create a minimal `Program.cs` that returns 0 (placeholder entry point)
  - Verify the project builds with `dotnet build` from the `Tools/Audit` directory with zero warnings
  - **Requirements:** 10.1 (partial)
  - **Design:** Project File

- [x] 2. Implement data models
  - Create `Models/AuditStatus.cs` with enum values: Unstarted, InReview, Completed, Remediated, Failed, Removed
  - Create `Models/FindingStatus.cs` with enum values: Open, InProgress, Resolved
  - Create `Models/FindingCategory.cs` with enum values: Maintainable, Split, Srp, Size, Methods, Naming, Coupling, DeadCode, MisleadingDocs, Layout
  - Create `Models/FileMetrics.cs` as sealed record with LineCount, TypeCount, MethodCount
  - Create `Models/Finding.cs` as sealed record with Category, Note, Targets (IReadOnlyList<string>), Status
  - Create `Models/FileEntry.cs` as sealed record with RelativePath, Status, Metrics, Findings (IReadOnlyList<Finding>), FailureReason (nullable)
  - Create `Models/AuditState.cs` as sealed record with Files (IReadOnlyList<FileEntry>)
  - Verify project builds with zero warnings
  - **Requirements:** 3.2, 6.1, 8.1
  - **Design:** Data Models

- [x] 3. Implement command parser
  - Create `CommandParser.cs` with Command enum (Enumerate, Audit, Remediate, Report, Status)
  - Create ParsedCommand record with Command, FilePath (nullable), TimeoutSeconds (default 120)
  - Implement `Parse(string[] args, TextWriter errorOutput)` returning `ParsedCommand?`
  - Handle positional command verb as first argument
  - Handle `--file <path>` option for audit and remediate commands
  - Handle `--timeout <seconds>` option for audit and remediate commands
  - Write usage message to errorOutput and return null for unrecognized commands or missing arguments
  - Write error for `--file` or `--timeout` used with commands that don't support them
  - Verify project builds with zero warnings
  - **Requirements:** 10.1, 10.2, 10.3, 10.4, 10.5, 10.6, 10.7, 10.8
  - **Design:** Components and Interfaces §1 (CommandParser)

- [x] 4. Implement state store with atomic persistence
  - Create `StateStore.cs` with constructor accepting repositoryRoot path
  - Configure JsonSerializerOptions with JsonStringEnumConverter (camelCase), camelCase property naming, and WriteIndented=true
  - Implement `Load()`: return empty AuditState if file doesn't exist; deserialize if valid; throw InvalidOperationException with message if corrupt
  - Implement `Save(AuditState)`: serialize to temp file, then File.Move with overwrite to `audit-state.json`
  - Verify round-trip: save then load produces equivalent state
  - Verify project builds with zero warnings
  - **Requirements:** 3.1, 3.2, 3.3, 3.4, 3.5, 3.6
  - **Design:** Components and Interfaces §2 (StateStore), Correctness Properties §1

- [x] 5. Implement file enumerator
  - Create `FileEnumerator.cs` with static `Enumerate(string repositoryRoot)` method
  - Recursively scan `Source/` for `.cs` files using `Directory.EnumerateFiles`
  - Exclude files under `Source/Mist/Generated/`
  - Exclude files under any `bin/` or `obj/` directory at any depth
  - Include only files from `Source/Mist/`, `Source/Mist.CLI/`, `Source/Storm.SourceGenerator/` subtrees
  - Return paths relative to repository root with forward slashes
  - Throw if Source directory does not exist
  - Return empty list (not error) if zero files match
  - Verify project builds with zero warnings
  - **Requirements:** 1.1, 1.2, 1.3, 1.4, 1.5, 1.7, 1.8
  - **Design:** Components and Interfaces §3 (FileEnumerator), Correctness Properties §7

- [x] 6. Implement metrics computer
  - Create `MetricsComputer.cs` with static `Compute(string absolutePath)` method
  - Compute line count via `File.ReadAllLines(path).Length`
  - Parse file with `CSharpSyntaxTree.ParseText` (Roslyn)
  - Count type declarations: walk all `TypeDeclarationSyntax` (class, struct, record, interface) and `EnumDeclarationSyntax` at any depth
  - Count method declarations: walk all `MethodDeclarationSyntax` and `ConstructorDeclarationSyntax`, skip `OperatorDeclarationSyntax`, `ConversionOperatorDeclarationSyntax`, and accessor declarations
  - On read failure: log warning to stderr, return FileMetrics(0, 0, 0)
  - Verify project builds with zero warnings
  - **Requirements:** 2.1, 2.2, 2.3, 2.6
  - **Design:** Components and Interfaces §4 (MetricsComputer)

- [x] 7. Implement report generator
  - Create `ReportGenerator.cs` with static `Generate(AuditState state)` returning Markdown string
  - Generate a summary table with counts per AuditStatus (exclude Removed)
  - Generate a findings summary table with counts per FindingStatus
  - Group files by project directory: extract project from path (Mist/, Mist.CLI/, Storm.SourceGenerator/)
  - Within each group, sort files alphabetically by relative path
  - For each file: render path, line count, status, and findings (category + note + status)
  - Exclude files with Removed status from the report
  - Verify project builds with zero warnings
  - **Requirements:** 4.1, 4.2, 4.3, 4.4, 4.5, 4.6
  - **Design:** Components and Interfaces §7 (ReportGenerator)

- [x] 8. Implement prompt builder
  - Create `PromptBuilder.cs` with static `BuildAuditPrompt` and `BuildRemediationPrompt` methods
  - In audit prompt: include file path, file content, and metrics summary
  - In audit prompt: include all 10 finding category definitions with descriptions
  - In audit prompt: include split guidance (helper types OK if private/exclusive; split when >50 lines, multiple public classes, or types leak out of file)
  - In audit prompt: instruct agent to return JSON array with `category`, `note`, and `targets` fields
  - In audit prompt: include example JSON output format
  - In remediation prompt: include file path, list of findings with notes, and instructions to make code changes
  - Verify project builds with zero warnings
  - **Requirements:** 5.2, 5.3, 7.1, 7.2, 7.3, 7.4, 9.2
  - **Design:** Components and Interfaces §5 (PromptBuilder)

- [x] 9. Implement agent dispatcher
  - Create `AgentDispatcher.cs` with constructor accepting workingDirectory and optional timeout
  - Create `AgentResult` record with Succeeded, Findings (nullable), ErrorMessage (nullable)
  - Implement `DispatchAudit`: spawn `kiro-cli chat --no-interactive --trust-all-tools`, write prompt to stdin, close stdin, read stdout with timeout
  - Implement JSON extraction: find first `[` and last `]` in stdout, parse as JSON array
  - Validate each finding: check category is recognized, note is non-empty for non-Maintainable categories; discard invalid findings with warning
  - Enforce Maintainable exclusivity: if Maintainable + other categories present, discard Maintainable
  - On timeout: kill process, return AgentResult with failure message
  - On non-zero exit or no JSON found: return AgentResult with failure message from stderr
  - Implement `DispatchRemediation`: spawn kiro-cli with remediation prompt, check exit code for success
  - Verify project builds with zero warnings
  - **Requirements:** 5.1, 5.4, 5.6, 5.8, 6.3, 6.13, 9.1, 9.3, 9.4
  - **Design:** Components and Interfaces §6 (AgentDispatcher)

- [x] 10. Implement enumerate command workflow
  - Implement `RunEnumerate` in Program.cs: call FileEnumerator, compute metrics for each file
  - Merge results with existing state: add new files as Unstarted, mark missing files as Removed, preserve existing entries
  - Save updated state via StateStore
  - Print file count summary to stdout
  - Handle Source directory not found: print error, return exit code 1
  - Regenerate AUDIT.md after enumeration
  - Verify project builds with zero warnings
  - **Requirements:** 1.1, 1.6, 1.7, 1.8, 2.4, 3.7, 3.8, 4.7, 10.1
  - **Design:** Data Flow §Enumerate Command, Correctness Properties §5

- [x] 11. Implement audit command workflow
  - Implement `RunAudit` in Program.cs: load state, filter to Unstarted files (or specific --file)
  - For --file: validate path exists in state, return error if not
  - For each file: transition to InReview, save state, read file content, call AgentDispatcher.DispatchAudit
  - On success: store findings with status Open, transition file to Completed, save state
  - On failure: transition file to Failed with error reason, save state, continue to next file
  - After all files processed: regenerate AUDIT.md
  - Print progress to stdout (file being audited, result)
  - Verify project builds with zero warnings
  - **Requirements:** 2.5, 5.1, 5.4, 5.5, 5.6, 5.7, 8.2, 10.2, 10.3, 10.9
  - **Design:** Data Flow §Audit Command, Correctness Properties §2, §6

- [x] 12. Implement remediate command workflow
  - Implement `RunRemediate` in Program.cs: load state, filter to files with Open findings (or specific --file)
  - For --file: validate path exists in state, return error if not
  - For each file: transition Open findings to InProgress, save state, call AgentDispatcher.DispatchRemediation
  - On full success (exit code 0): transition all findings to Resolved, check if all file findings resolved and transition to Remediated, save state
  - On failure (non-zero exit): transition failed findings back to Open, transition file to Failed with reason, save state
  - On partial success: resolve successful findings, revert failed findings to Open, mark file Failed, save state
  - After all files processed: regenerate AUDIT.md
  - Print progress to stdout
  - Verify project builds with zero warnings
  - **Requirements:** 8.3, 8.4, 8.5, 8.6, 8.7, 9.1, 9.3, 9.4, 9.5, 9.6, 9.7, 10.4, 10.5, 10.9
  - **Design:** Data Flow §Remediate Command, Correctness Properties §3

- [x] 13. Implement report and status commands
  - Implement `RunReport` in Program.cs: load state, call ReportGenerator.Generate, write to AUDIT.md at repo root
  - Implement `RunStatus` in Program.cs: load state, aggregate counts by AuditStatus and FindingStatus, print to stdout
  - Format status output as a readable table
  - Verify project builds with zero warnings
  - **Requirements:** 4.1, 4.7, 10.6, 10.7
  - **Design:** Data Flow §Report Command, §Status Command

- [x] 14. Wire up Program entry point and repository root detection
  - Implement `FindRepositoryRoot`: walk up from CurrentDirectory looking for `Mist.slnx`
  - Print clear error and exit 1 if not found
  - Wire up Main: parse command, find repo root, construct StateStore, dispatch to appropriate Run method
  - Return appropriate exit codes (0 success, 1 failure)
  - Verify project builds with zero warnings
  - Run `dotnet format --verify-no-changes` on the Audit project and fix any violations
  - **Requirements:** 10.8, 10.9
  - **Design:** Components and Interfaces §8 (Program), Repository Root Detection

- [x] 15. End-to-end smoke test with enumerate command
  - From the repository root, run `dotnet run --project Tools/Audit -- enumerate`
  - Verify it discovers a non-zero count of .cs files
  - Verify `audit-state.json` is created at repo root with valid JSON
  - Verify `AUDIT.md` is created at repo root with file entries grouped by project
  - Verify all files in state have status Unstarted with non-zero metrics
  - Verify no files from Source/Mist/Generated/ appear in the state
  - Clean up generated files (delete audit-state.json and AUDIT.md)
  - **Requirements:** 1.1, 1.2, 1.3, 1.5, 2.1, 2.2, 2.3, 3.1, 4.1
  - **Design:** Data Flow §Enumerate Command

## Task Dependency Graph

```json
{
  "waves": [
    {"tasks": [1]},
    {"tasks": [2]},
    {"tasks": [3, 4, 5, 6, 7, 8]},
    {"tasks": [9]},
    {"tasks": [10, 11, 12, 13]},
    {"tasks": [14]},
    {"tasks": [15]}
  ]
}
```

## Notes

- Tasks 5-9 can be implemented in parallel once task 2 is complete.
- Tasks 10-13 can be partially parallelized but share the Program.cs file.
- Task 14 is the final wiring that connects all pieces.
- Task 15 is a manual verification step that exercises the full enumerate pipeline against the real codebase.
