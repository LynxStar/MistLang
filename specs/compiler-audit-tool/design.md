# Design Document

## Overview

The Compiler Audit Tool is a standalone .NET console application (`Audit`) that drives an incremental code quality audit of the Mist compiler source. It enumerates C# files, computes structural metrics via Roslyn, persists audit state as JSON, dispatches Kiro CLI in headless mode for qualitative assessment and remediation, and generates a living Markdown report.

The tool follows the same project conventions as the existing `Mist.CLI`: hand-rolled argument parsing, `net10.0` target, file-scoped namespaces, and the coding style defined in `.editorconfig`.

## Architecture

```
┌─────────────────────────────────────────────────────────┐
│                    Audit Tool (CLI)                       │
├─────────────────────────────────────────────────────────┤
│  Program.cs                                              │
│  ┌───────────┐  ┌───────────┐  ┌───────────────────┐   │
│  │CommandParser│  │CommandRouter│  │ ConsoleReporter  │   │
│  └───────────┘  └───────────┘  └───────────────────┘   │
├─────────────────────────────────────────────────────────┤
│                    Core Services                         │
│  ┌──────────────┐  ┌──────────────┐  ┌─────────────┐  │
│  │FileEnumerator│  │MetricsComputer│  │  StateStore │  │
│  └──────────────┘  └──────────────┘  └─────────────┘  │
│  ┌──────────────┐  ┌──────────────┐  ┌─────────────┐  │
│  │AgentDispatcher│  │ReportGenerator│  │PromptBuilder│  │
│  └──────────────┘  └──────────────┘  └─────────────┘  │
├─────────────────────────────────────────────────────────┤
│                    Data Models                           │
│  AuditState, FileEntry, Finding, FileMetrics,           │
│  FindingCategory, AuditStatus, FindingStatus            │
└─────────────────────────────────────────────────────────┘
         │                    │
         ▼                    ▼
   ┌───────────┐      ┌────────────┐
   │ File System│      │  kiro-cli  │
   │ (Source/)  │      │ (headless) │
   └───────────┘      └────────────┘
```

## Components and Interfaces

### 1. CommandParser

Hand-rolled argument parser following the `Mist.CLI` pattern.

```csharp
// CommandParser.cs
namespace Audit;

public enum Command
{
    Enumerate,
    Audit,
    Remediate,
    Report,
    Status,
}

public sealed record ParsedCommand(
    Command Command,
    string? FilePath,
    int TimeoutSeconds);

public static class CommandParser
{
    /// <summary>
    /// Parses CLI arguments into a structured command. Writes errors to errorOutput and returns null on failure.
    /// </summary>
    public static ParsedCommand? Parse(string[] args, TextWriter errorOutput) { ... }
}
```

Supported syntax:
```
mist-audit enumerate
mist-audit audit [--file <path>] [--timeout <seconds>]
mist-audit remediate [--file <path>] [--timeout <seconds>]
mist-audit report
mist-audit status
```

### 2. StateStore

Handles loading and saving `AuditState` to `audit-state.json` at the repository root. Uses atomic write (write to temp file, then rename) to prevent corruption.

```csharp
// StateStore.cs
namespace Audit;

public sealed class StateStore
{
    private readonly string statePath;
    private readonly JsonSerializerOptions jsonOptions;

    public StateStore(string repositoryRoot) { ... }

    /// <summary>
    /// Loads existing state from disk, or returns an empty state if the file does not exist.
    /// Throws InvalidOperationException if the file exists but is corrupt/unparseable.
    /// </summary>
    public AuditState Load() { ... }

    /// <summary>
    /// Persists state atomically: writes to a .tmp file then renames over the target.
    /// Throws IOException if the write fails (does not corrupt the existing file).
    /// </summary>
    public void Save(AuditState state) { ... }
}
```

JSON serialization uses `System.Text.Json` with `JsonStringEnumConverter` for human-readable enum values.

### 3. FileEnumerator

Discovers all in-scope C# files under `Source/`.

```csharp
// FileEnumerator.cs
namespace Audit;

public static class FileEnumerator
{
    /// <summary>
    /// Returns relative paths (from repo root) of all in-scope .cs files.
    /// Excludes: Source/Mist/Generated/, bin/, obj/, non-.cs files.
    /// Includes: Source/Mist/, Source/Mist.CLI/, Source/Storm.SourceGenerator/.
    /// </summary>
    public static IReadOnlyList<string> Enumerate(string repositoryRoot) { ... }
}
```

Implementation uses `Directory.EnumerateFiles` with post-filtering. Paths are normalized to forward slashes and stored relative to the repository root.

### 4. MetricsComputer

Uses the Roslyn `Microsoft.CodeAnalysis.CSharp` API to parse each file and walk the syntax tree counting type and method declarations.

```csharp
// MetricsComputer.cs
namespace Audit;

public static class MetricsComputer
{
    /// <summary>
    /// Computes metrics for a single file: line count, type declaration count, method/constructor count.
    /// Returns zero metrics on read failure (logs warning to stderr).
    /// </summary>
    public static FileMetrics Compute(string absolutePath) { ... }
}
```

Counts:
- **Lines**: Number of newline-delimited lines (`File.ReadAllLines(path).Length`)
- **Types**: All `TypeDeclarationSyntax` nodes (class, struct, record, interface) + `EnumDeclarationSyntax` at any nesting depth
- **Methods**: All `MethodDeclarationSyntax` + `ConstructorDeclarationSyntax` nodes, excluding property/event accessors and operator declarations

### 5. PromptBuilder

Constructs the structured prompts sent to the AI agent. Separates prompt construction from process management for testability.

```csharp
// PromptBuilder.cs
namespace Audit;

public static class PromptBuilder
{
    /// <summary>
    /// Builds the audit prompt including file content, metrics, category definitions, and split guidance.
    /// </summary>
    public static string BuildAuditPrompt(string filePath, string fileContent, FileMetrics metrics) { ... }

    /// <summary>
    /// Builds the remediation prompt including file path, findings, and instructions.
    /// </summary>
    public static string BuildRemediationPrompt(string filePath, IReadOnlyList<Finding> findings) { ... }
}
```

The audit prompt includes:
- File path and content
- Metrics summary (lines, types, methods)
- All 10 finding category definitions with descriptions
- Split guidance criteria (Requirement 7)
- Instructions to return JSON array with `category`, `note`, and `targets` fields
- Example JSON output format

### 6. AgentDispatcher

Spawns `kiro-cli chat --no-interactive --trust-all-tools` as a child process, pipes a prompt via stdin, reads structured JSON from stdout.

```csharp
// AgentDispatcher.cs
namespace Audit;

public sealed class AgentDispatcher
{
    private readonly string workingDirectory;
    private readonly TimeSpan timeout;

    public AgentDispatcher(string workingDirectory, TimeSpan? timeout = null) { ... }

    /// <summary>
    /// Dispatches an audit prompt and returns parsed findings, or an error message on failure.
    /// </summary>
    public AgentResult DispatchAudit(string filePath, string fileContent, FileMetrics metrics) { ... }

    /// <summary>
    /// Dispatches a remediation prompt for specific findings on a file.
    /// </summary>
    public AgentResult DispatchRemediation(string filePath, IReadOnlyList<Finding> findings) { ... }
}

public sealed record AgentResult(bool Succeeded, IReadOnlyList<Finding>? Findings, string? ErrorMessage);
```

Process management:
- Starts `kiro-cli` with `RedirectStandardInput = true`, `RedirectStandardOutput = true`, `RedirectStandardError = true`
- Writes the prompt to stdin, then closes stdin
- Reads stdout with a timeout (default 120s, configurable via `--timeout` CLI option)
- Kills the process on timeout
- Extracts JSON by finding the first `[` and last `]` in the output

### 7. ReportGenerator

Renders `AuditState` into `AUDIT.md` Markdown.

```csharp
// ReportGenerator.cs
namespace Audit;

public static class ReportGenerator
{
    /// <summary>
    /// Generates AUDIT.md content from the current audit state.
    /// Groups files by project, sorts alphabetically, includes summary tables.
    /// Excludes files with Removed status.
    /// </summary>
    public static string Generate(AuditState state) { ... }
}
```

Output structure:
```markdown
# Compiler Audit Report

## Summary
| Status | Count |
|--------|-------|
| Unstarted | 42 |
| Completed | 15 |
...

## Findings Summary
| Status | Count |
|--------|-------|
| Open | 23 |
| Resolved | 7 |
...

## Mist/

| File | Lines | Status | Findings |
|------|-------|--------|----------|
| Analysis/AnalyzerRunner.cs | 120 | Completed | Maintainable |
...

## Mist.CLI/
...

## Storm.SourceGenerator/
...
```

### 8. Program (Command Router)

Top-level entry point that parses arguments and dispatches to the appropriate workflow.

```csharp
// Program.cs
namespace Audit;

internal class Program
{
    private static int Main(string[] args)
    {
        var command = CommandParser.Parse(args, Console.Error);
        if (command is null)
        {
            return 1;
        }

        var repoRoot = FindRepositoryRoot();
        var store = new StateStore(repoRoot);

        return command.Command switch
        {
            Command.Enumerate => RunEnumerate(store, repoRoot),
            Command.Audit => RunAudit(store, repoRoot, command),
            Command.Remediate => RunRemediate(store, repoRoot, command),
            Command.Report => RunReport(store, repoRoot),
            Command.Status => RunStatus(store),
            _ => 1,
        };
    }

    private static string FindRepositoryRoot() { ... }
    private static int RunEnumerate(StateStore store, string repoRoot) { ... }
    private static int RunAudit(StateStore store, string repoRoot, ParsedCommand command) { ... }
    private static int RunRemediate(StateStore store, string repoRoot, ParsedCommand command) { ... }
    private static int RunReport(StateStore store, string repoRoot) { ... }
    private static int RunStatus(StateStore store) { ... }
}
```

## Data Models

### AuditState

```csharp
namespace Audit.Models;

public sealed record AuditState(IReadOnlyList<FileEntry> Files);
```

### FileEntry

```csharp
namespace Audit.Models;

public sealed record FileEntry(
    string RelativePath,
    AuditStatus Status,
    FileMetrics Metrics,
    IReadOnlyList<Finding> Findings,
    string? FailureReason);
```

### FileMetrics

```csharp
namespace Audit.Models;

public sealed record FileMetrics(int LineCount, int TypeCount, int MethodCount);
```

### Finding

```csharp
namespace Audit.Models;

public sealed record Finding(
    FindingCategory Category,
    string Note,
    IReadOnlyList<string> Targets,
    FindingStatus Status);
```

### Enumerations

```csharp
namespace Audit.Models;

public enum AuditStatus
{
    Unstarted,
    InReview,
    Completed,
    Remediated,
    Failed,
    Removed,
}

public enum FindingStatus
{
    Open,
    InProgress,
    Resolved,
}

public enum FindingCategory
{
    Maintainable,
    Split,
    Srp,
    Size,
    Methods,
    Naming,
    Coupling,
    DeadCode,
    MisleadingDocs,
    Layout,
}
```

### JSON Schema

The `audit-state.json` file:

```json
{
  "files": [
    {
      "relativePath": "Source/Mist/Semantics/Model/SemanticModelBuilder.cs",
      "status": "completed",
      "metrics": {
        "lineCount": 2847,
        "typeCount": 1,
        "methodCount": 42
      },
      "findings": [
        {
          "category": "size",
          "note": "File exceeds 2800 lines. Should be decomposed into focused builders per semantic domain.",
          "targets": [],
          "status": "open"
        },
        {
          "category": "methods",
          "note": "BuildMethodDeclaration (180 lines) and BuildTypeDeclaration (220 lines) handle too many cases inline.",
          "targets": ["BuildMethodDeclaration", "BuildTypeDeclaration"],
          "status": "open"
        }
      ],
      "failureReason": null
    }
  ]
}
```

## Data Flow

### Enumerate Command

```
FileEnumerator.Enumerate(repoRoot)
    → list of relative paths
    → for each: MetricsComputer.Compute(absolutePath)
    → merge into existing AuditState (add new as Unstarted, mark missing as Removed)
    → StateStore.Save(updatedState)
    → print summary to stdout
```

### Audit Command

```
StateStore.Load()
    → filter files: Unstarted (or specific --file)
    → for each file:
        → set status to InReview, save
        → AgentDispatcher.DispatchAudit(path, content, metrics)
        → if success: parse findings, set status to Completed, save
        → if failure: set status to Failed with reason, save
    → ReportGenerator.Generate(state) → write AUDIT.md
```

### Remediate Command

```
StateStore.Load()
    → filter files: those with Open findings (or specific --file)
    → for each file:
        → set finding statuses to InProgress, save
        → AgentDispatcher.DispatchRemediation(path, findings)
        → if success: set findings to Resolved, save
        → if partial: resolved findings → Resolved, failed → back to Open, file → Failed, save
    → ReportGenerator.Generate(state) → write AUDIT.md
```

### Report Command

```
StateStore.Load()
    → ReportGenerator.Generate(state)
    → write AUDIT.md to repo root
```

### Status Command

```
StateStore.Load()
    → aggregate counts by AuditStatus and FindingStatus
    → print summary table to stdout
```

## Correctness Properties

### Property 1: Atomic Persistence

State is always written to a temp file first, then renamed. A crash mid-write never corrupts `audit-state.json`. If the rename fails, the previous state file remains intact.

**Validates: Requirements 3.5, 3.6**

### Property 2: Status Transition Invariants

`AuditStatus` transitions follow a directed graph: `Unstarted → InReview → Completed → Remediated` or `→ Failed` from any active state. `Removed` is terminal. No backward transitions except through explicit re-audit via `--file`.

**Validates: Requirements 5.5, 5.7, 8.5, 8.6**

### Property 3: Finding Status Invariants

`FindingStatus` transitions: `Open → InProgress → Resolved` or `InProgress → Open` on failure. No other transitions are valid. The code rejects invalid transitions at the point of state mutation.

**Validates: Requirements 8.2, 8.3, 8.4, 8.7**

### Property 4: Maintainable Exclusivity

If a file has a `Maintainable` finding, no other finding categories may coexist. The orchestrator enforces this during agent response parsing by discarding Maintainable when other categories are present.

**Validates: Requirements 6.3**

### Property 5: Idempotent Enumeration

Running `enumerate` multiple times produces the same state (new files added as Unstarted, deleted files marked Removed, existing entries preserved with their current status and findings unchanged).

**Validates: Requirements 3.7, 3.8**

### Property 6: No Data Loss on Failure

If any command fails partway through (agent crash, disk full), the state file reflects progress up to the last successful save. Each file's audit result is saved immediately after completion.

**Validates: Requirements 3.5, 3.6, 5.6**

### Property 7: Stable Relative Paths

All paths in state are relative to the repo root using forward slashes, making the state file portable across machines and consistent regardless of OS path separators.

**Validates: Requirements 1.1, 3.2**

## Testing Strategy

1. **FileEnumerator**: Unit test with a temp directory containing a mix of `.cs` files, generated files, bin/obj, and non-C# files. Assert correct inclusion/exclusion.
2. **MetricsComputer**: Unit test with known C# source strings. Assert expected line, type, and method counts for various patterns (nested classes, records, constructors, property accessors excluded).
3. **StateStore**: Unit test round-trip serialization. Test atomic write behavior (simulate crash by checking temp file cleanup). Test corrupt file detection.
4. **CommandParser**: Unit test all valid command forms and error cases. Assert error messages for invalid input.
5. **PromptBuilder**: Unit test that generated prompts contain expected sections (categories, guidance, format instructions).
6. **ReportGenerator**: Unit test with known state, assert Markdown output matches expected format (grouping, sorting, summary tables).
7. **AgentDispatcher**: Integration test only (requires `kiro-cli`). For unit testing, the dispatcher's JSON extraction logic is tested separately with sample outputs containing embedded JSON.

## Project Structure

```
Tools/Audit/
├── Audit.csproj
├── Program.cs
├── CommandParser.cs
├── StateStore.cs
├── FileEnumerator.cs
├── MetricsComputer.cs
├── AgentDispatcher.cs
├── PromptBuilder.cs
├── ReportGenerator.cs
└── Models/
    ├── AuditState.cs
    ├── AuditStatus.cs
    ├── FileEntry.cs
    ├── FileMetrics.cs
    ├── Finding.cs
    ├── FindingCategory.cs
    └── FindingStatus.cs
```

## Project File

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <OutputType>Exe</OutputType>
    <TargetFramework>net10.0</TargetFramework>
    <ImplicitUsings>enable</ImplicitUsings>
    <Nullable>enable</Nullable>
  </PropertyGroup>

  <ItemGroup>
    <PackageReference Include="Microsoft.CodeAnalysis.CSharp" Version="4.12.0" />
  </ItemGroup>
</Project>
```

The project references `Microsoft.CodeAnalysis.CSharp` directly (same version as `Mist.Core`) for Roslyn syntax tree parsing in `MetricsComputer`. No project reference to `Mist.Core` is needed since the audit tool operates on source files independently.

## Repository Root Detection

The tool locates the repository root by walking up from `Environment.CurrentDirectory` looking for the `Mist.slnx` file. This anchors all relative paths consistently regardless of where the tool is invoked from within the repo.

## Error Handling

| Scenario | Behavior |
|----------|----------|
| Corrupt state file | Print error message, exit code 1, do not overwrite |
| Missing Source/ directory | Print error, exit code 1 |
| Unreadable source file during metrics | Log warning, record zero metrics, continue |
| Agent timeout/crash | Mark file as Failed with reason, continue to next file |
| Unparseable agent output | Mark file as Failed with "unparseable response" reason, continue |
| Invalid finding category from agent | Discard that finding, log warning, keep valid findings |
| Maintainable + other categories | Discard the Maintainable finding, log warning |
| Unknown CLI command | Print usage, exit code 1 |
| --file path not in state | Print error, exit code 1 |
