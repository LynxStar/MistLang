# Requirements Document

## Introduction

A hybrid C# orchestrator application that drives an incremental code quality audit of the Mist compiler's source files. The orchestrator handles all mechanical work (file enumeration, metrics computation, state management, document generation) and shells out to Kiro CLI in headless mode for qualitative judgment calls (assessing code against audit categories, performing remediation). The result is a living Markdown document tracking audit status, findings, and remediation progress across the entire codebase.

## Glossary

- **Orchestrator**: The C# console application that drives the audit workflow — enumerating files, computing metrics, managing state, generating the Audit_Document, and invoking the AI_Agent for judgment calls.
- **AI_Agent**: Kiro CLI invoked in headless mode (`--no-interactive`) to perform qualitative assessment of source files or execute remediation changes.
- **Audit_Document**: The Markdown file produced and maintained by the Orchestrator that contains the full audit state (file list, statuses, findings, remediation progress).
- **Audit_State**: The persistent JSON data store used by the Orchestrator to track file statuses, findings, and remediation progress between runs.
- **Source_Directory**: The `Source/` directory at the repository root, containing all compiler source projects (`Mist/`, `Mist.CLI/`, `Storm.SourceGenerator/`).
- **In_Scope_File**: A C# file (`.cs` extension) within the Source_Directory that is not excluded by scope rules.
- **Excluded_File**: A file that matches one of the exclusion criteria: generated files under `Source/Mist/Generated/`, stdlib Mist source files (`.mist` files under `Source/Standard/`), non-C# files, or build artifact directories (`bin/`, `obj/`).
- **Audit_Status**: One of five states for a file: `unstarted` (not yet reviewed), `in_review` (AI_Agent dispatched), `completed` (review finished with findings recorded), `remediated` (all findings resolved), or `failed` (audit or remediation blocked).
- **Finding**: A categorized observation about a file's code quality, belonging to one or more Finding_Categories, with an associated note and Finding_Status.
- **Finding_Status**: One of three states for an individual finding: `open` (identified but not addressed), `in_progress` (remediation underway), or `resolved` (remediation complete).
- **Remediation**: The process of addressing a Finding by invoking the AI_Agent to modify the source code and resolve the identified issue.
- **Finding_Category**: One of the predefined audit categories: Maintainable, Split, SRP, Size, Methods, Naming, Coupling, Dead_Code, Misleading_Docs, or Layout.
- **Maintainable**: Finding category indicating a file is well-structured and needs no changes.
- **Split**: Finding category indicating a file contains multiple types that should be separated into their own files.
- **SRP**: Finding category indicating a class has a Single Responsibility Principle violation — it is doing too many unrelated things.
- **Size**: Finding category indicating a file exceeds acceptable length (roughly 500-1000+ lines).
- **Methods**: Finding category indicating individual methods that are too large (bigger than a screen is a smell; many screens indicates a violation).
- **Naming**: Finding category indicating files or classes whose names no longer reflect their actual behavior, or inconsistent naming patterns that hinder navigation.
- **Coupling**: Finding category indicating excessive dependencies or tight coupling that distracts from readability or makes refactoring significantly harder. High bar — only the worst cases.
- **Dead_Code**: Finding category indicating unused methods, unreachable branches, or commented-out code that was never cleaned up.
- **Misleading_Docs**: Finding category indicating XML doc comments that are incorrect or misleading (not merely missing).
- **Layout**: Finding category indicating poor internal organization of a file. Expected layout: fields/properties, then constructors, then methods. Private methods should be grouped immediately after the public methods that use them.
- **File_Metrics**: Computed statistics for a file including line count, number of type declarations, and number of methods.

## Requirements

### Requirement 1: File Enumeration

**User Story:** As a developer, I want the orchestrator to discover all auditable C# files in the Source directory, so that I have a complete inventory of files to review.

#### Acceptance Criteria

1. WHEN invoked, THE Orchestrator SHALL recursively scan the Source_Directory and collect all files with a `.cs` extension, returning each file as a path relative to the repository root.
2. THE Orchestrator SHALL exclude all files under the `Source/Mist/Generated/` directory.
3. THE Orchestrator SHALL exclude all files under `bin/` and `obj/` directories at any depth within the Source_Directory.
4. THE Orchestrator SHALL exclude all non-C# files (files without a `.cs` extension).
5. THE Orchestrator SHALL include C# files from `Source/Mist/`, `Source/Mist.CLI/`, and `Source/Storm.SourceGenerator/` and their subdirectories.
6. WHEN enumeration completes, THE Orchestrator SHALL write the total count of In_Scope_Files discovered to standard output.
7. IF the Source_Directory does not exist or is not accessible, THEN THE Orchestrator SHALL exit with a non-zero exit code and write an error message indicating the directory could not be found or read.
8. IF enumeration completes and zero `.cs` files match the inclusion criteria, THEN THE Orchestrator SHALL report a count of 0 and produce an empty file list without treating it as an error.

### Requirement 2: File Metrics Computation

**User Story:** As a developer, I want the orchestrator to compute basic metrics for each file, so that the AI agent has context for its assessment and the audit document shows file sizes.

#### Acceptance Criteria

1. THE Orchestrator SHALL compute the line count for each In_Scope_File by counting the number of newline-delimited lines in the file (an empty file yields a line count of 0).
2. THE Orchestrator SHALL count the number of type declarations (classes, structs, records, enums, interfaces) at all nesting levels in each In_Scope_File.
3. THE Orchestrator SHALL count the number of method and constructor declarations in each In_Scope_File, excluding property accessors, event accessors, and operator overloads.
4. THE Orchestrator SHALL store File_Metrics in the Audit_State alongside each file entry.
5. THE Orchestrator SHALL include File_Metrics in the context provided to the AI_Agent during audit dispatch.
6. IF an In_Scope_File cannot be read during metrics computation, THEN THE Orchestrator SHALL record a line count of 0, a type count of 0, and a method count of 0 for that file, log a warning indicating the file path and failure reason, and continue processing the remaining files.

### Requirement 3: Audit State Persistence

**User Story:** As a developer, I want audit progress persisted to disk so that the audit can be resumed across multiple sessions without losing work.

#### Acceptance Criteria

1. THE Orchestrator SHALL persist the Audit_State as a JSON file named `audit-state.json` located in the repository root directory.
2. THE Audit_State SHALL record for each file: the file path relative to the repository root, Audit_Status, File_Metrics, and a list of Findings.
3. WHEN the Orchestrator starts and a valid `audit-state.json` file exists on disk, THE Orchestrator SHALL load the existing Audit_State from that file.
4. IF the `audit-state.json` file exists but contains invalid JSON or cannot be parsed into a valid Audit_State, THEN THE Orchestrator SHALL report an error message indicating the file is corrupt and exit without overwriting the file.
5. WHEN the Orchestrator modifies the Audit_State, THE Orchestrator SHALL write the updated state to a temporary file and rename it to `audit-state.json` to prevent corruption from partial writes.
6. IF a write to the Audit_State file fails, THEN THE Orchestrator SHALL report an error message indicating the write failure and exit without losing the previously persisted state.
7. WHEN a new In_Scope_File is discovered that does not exist in the Audit_State, THE Orchestrator SHALL add it with status `unstarted`.
8. WHEN a file in the Audit_State no longer exists on disk, THE Orchestrator SHALL mark it as `removed` rather than deleting its entry.

### Requirement 4: Audit Document Generation

**User Story:** As a developer, I want the orchestrator to produce a living Markdown document from the audit state, so that I can read progress and share findings with the team.

#### Acceptance Criteria

1. WHEN the Orchestrator generates the Audit_Document, THE Orchestrator SHALL write a Markdown file named `AUDIT.md` in the repository root directory.
2. THE Orchestrator SHALL include an entry for every In_Scope_File from the Audit_State, excluding files with status `removed`.
3. THE Orchestrator SHALL record for each file entry: the relative file path, line count, Audit_Status, and any recorded Findings with their Finding_Status.
4. THE Orchestrator SHALL group file entries by their parent project directory (`Mist/`, `Mist.CLI/`, `Storm.SourceGenerator/`), with files sorted alphabetically within each group.
5. THE Orchestrator SHALL include a summary section showing counts of files in each Audit_Status.
6. THE Orchestrator SHALL include a remediation summary showing counts of findings in each Finding_Status.
7. THE Orchestrator SHALL regenerate the Audit_Document from the Audit_State on every command invocation.

### Requirement 5: AI Agent Audit Dispatch

**User Story:** As a developer, I want the orchestrator to invoke Kiro CLI in headless mode for each file that needs auditing, so that qualitative assessment is automated.

#### Acceptance Criteria

1. THE Orchestrator SHALL invoke the AI_Agent via `kiro-cli chat --no-interactive --trust-all-tools` with a structured prompt for each file in `unstarted` status.
2. WHEN dispatching an audit, THE Orchestrator SHALL provide in the prompt: the file path, the file content, the File_Metrics, the list of Finding_Categories with descriptions, and the split guidance criteria.
3. THE Orchestrator SHALL instruct the AI_Agent to return findings as a JSON array where each element contains: a `category` field with the Finding_Category name, a `note` field describing the issue, and an optional `targets` array listing the specific methods or types involved.
4. WHEN the AI_Agent returns a response, THE Orchestrator SHALL extract the JSON array from the response by searching for the first `[` and last `]` characters, parse it, and update the Audit_State.
5. WHEN audit findings are successfully parsed, THE Orchestrator SHALL transition the file's Audit_Status from `in_review` to `completed`.
6. IF the AI_Agent invocation fails, times out, or returns output from which no valid JSON array can be extracted, THEN THE Orchestrator SHALL mark the file as `failed` with an error reason indicating the failure mode and continue with the next file.
7. THE Orchestrator SHALL transition the file's status to `in_review` before invoking the AI_Agent.
8. THE Orchestrator SHALL apply a configurable timeout (default 120 seconds) to each AI_Agent invocation, killing the process and treating it as a failure if the timeout is exceeded.

### Requirement 6: Finding Categories

**User Story:** As a developer, I want the orchestrator to enforce structured finding categories, so that audit results are consistent and actionable.

#### Acceptance Criteria

1. THE Orchestrator SHALL support the following Finding_Categories: Maintainable, Split, SRP, Size, Methods, Naming, Coupling, Dead_Code, Misleading_Docs, and Layout.
2. THE Orchestrator SHALL allow one or more Finding_Categories to be assigned to a single file.
3. WHEN the Maintainable category is assigned, THE Orchestrator SHALL treat it as indicating the file needs no changes, and SHALL NOT allow any other Finding_Category to be assigned to the same file alongside Maintainable.
4. WHEN the Split category is assigned, THE Orchestrator SHALL require a non-empty note describing which types should be extracted into separate files.
5. WHEN the SRP category is assigned, THE Orchestrator SHALL require a non-empty note describing the unrelated responsibilities present in the class.
6. WHEN the Size category is assigned, THE Orchestrator SHALL record the file's line count alongside the finding and require a non-empty note.
7. WHEN the Methods category is assigned, THE Orchestrator SHALL require a non-empty note identifying the specific methods that are too large.
8. WHEN the Naming category is assigned, THE Orchestrator SHALL require a non-empty note describing which names are misleading or inconsistent.
9. WHEN the Coupling category is assigned, THE Orchestrator SHALL require a non-empty note describing the problematic dependencies and why they hinder readability or refactoring.
10. WHEN the Dead_Code category is assigned, THE Orchestrator SHALL require a non-empty note identifying the unused methods, unreachable branches, or commented-out code.
11. WHEN the Misleading_Docs category is assigned, THE Orchestrator SHALL require a non-empty note identifying which doc comments are incorrect and what they should say instead.
12. WHEN the Layout category is assigned, THE Orchestrator SHALL require a non-empty note describing what should be reordered.
13. IF the AI_Agent returns a finding with an unrecognized category name or a missing/empty note for a category that requires one, THEN THE Orchestrator SHALL discard that individual finding, log a warning with the invalid data, and continue processing the remaining findings for the file.

### Requirement 7: Split Category Guidance

**User Story:** As a developer, I want clear guidance on when to apply the Split finding, so that the AI agent applies it consistently.

#### Acceptance Criteria

1. THE Orchestrator SHALL include in the AI_Agent prompt that types which are only referenced within the same file by the file's primary class (the class whose name matches the file name or is the main public type) do not require splitting.
2. THE Orchestrator SHALL include in the AI_Agent prompt that splitting is warranted when secondary types in a file exceed approximately 50 lines individually and distract from the primary class's readability.
3. THE Orchestrator SHALL include in the AI_Agent prompt that splitting is warranted when a file contains more than one `public` or `internal` class that is referenced from outside the file.
4. THE Orchestrator SHALL include in the AI_Agent prompt that splitting is warranted when model or data structures used outside the file are defined alongside their consumer (for example, `NodeOrigin` containing `ParsedOrigin`, `SyntheticOrigin`, and `TestOrigin` should be four separate files).

### Requirement 8: Remediation Tracking

**User Story:** As a developer, I want the orchestrator to track remediation progress for each finding, so that I can fix issues incrementally and know what work remains.

#### Acceptance Criteria

1. THE Orchestrator SHALL track a Finding_Status for each individual Finding recorded against a file.
2. WHEN a finding is first recorded, THE Orchestrator SHALL set its Finding_Status to `open`.
3. WHEN the Orchestrator dispatches the AI_Agent to remediate a finding, THE Orchestrator SHALL transition its Finding_Status to `in_progress`.
4. WHEN the AI_Agent reports successful remediation for a finding, THE Orchestrator SHALL transition its Finding_Status to `resolved`.
5. WHEN all findings for a file are `resolved`, THE Orchestrator SHALL transition the file's Audit_Status to `remediated`.
6. IF remediation of a finding is attempted but the AI_Agent cannot complete it, THEN THE Orchestrator SHALL transition the finding's Finding_Status back to `open`, transition the file's Audit_Status to `failed`, and record a non-empty reason explaining the blocker.
7. THE Orchestrator SHALL only permit Finding_Status transitions in the sequence `open` to `in_progress` to `resolved`, or from `in_progress` back to `open` on failure.

### Requirement 9: Remediation Dispatch

**User Story:** As a developer, I want to invoke the orchestrator to fix audit findings by dispatching Kiro CLI to make the actual code changes, so that remediation is automated.

#### Acceptance Criteria

1. WHEN invoked in remediation mode, THE Orchestrator SHALL invoke the AI_Agent for each file with `open` findings, processing files sequentially one at a time.
2. WHEN dispatching a remediation, THE Orchestrator SHALL provide in the prompt: the file path, the specific findings to address, the finding notes, and instructions to apply code changes that resolve the identified issues.
3. WHEN the AI_Agent process exits with code 0 for a finding, THE Orchestrator SHALL transition that finding's Finding_Status to `resolved`.
4. IF the AI_Agent process exits with a non-zero exit code for a finding, THEN THE Orchestrator SHALL record the blocker reason from the AI_Agent's stderr output and leave that finding's Finding_Status as `in_progress`.
5. THE Orchestrator SHALL allow remediation to be performed for a single specified file or for all files with `open` findings.
6. WHEN dispatching a remediation for a finding, THE Orchestrator SHALL transition that finding's Finding_Status to `in_progress` before invoking the AI_Agent.
7. IF a file has multiple findings and some are resolved while others fail, THEN THE Orchestrator SHALL transition only the successful findings to `resolved`, leave the failed findings as `in_progress`, and transition the file's Audit_Status to `failed` with a reason indicating which findings could not be resolved.

### Requirement 10: Command-Line Interface

**User Story:** As a developer, I want to invoke the orchestrator with different commands, so that I can run specific phases of the audit workflow.

#### Acceptance Criteria

1. THE Orchestrator SHALL support an `enumerate` command that discovers files, computes metrics, and updates the Audit_State.
2. THE Orchestrator SHALL support an `audit` command that dispatches the AI_Agent for all `unstarted` files.
3. THE Orchestrator SHALL support an `audit --file <path>` option that dispatches the AI_Agent for a specific file regardless of its current status.
4. THE Orchestrator SHALL support a `remediate` command that dispatches the AI_Agent to fix all `open` findings.
5. THE Orchestrator SHALL support a `remediate --file <path>` option that dispatches the AI_Agent to fix findings for a specific file.
6. THE Orchestrator SHALL support a `report` command that regenerates the Audit_Document from the current Audit_State.
7. THE Orchestrator SHALL support a `status` command that prints a summary of files in each Audit_Status and findings in each Finding_Status to stdout.
8. IF the user provides an unrecognized command or missing required arguments, THEN THE Orchestrator SHALL print a usage message listing all available commands and options, and exit with a non-zero exit code.
9. IF the `--file` option specifies a path that does not exist in the Audit_State, THEN THE Orchestrator SHALL print an error message indicating the file is not tracked and exit with a non-zero exit code.
