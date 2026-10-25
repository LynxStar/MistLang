# Requirements Document

## Introduction

The Mist CLI currently accepts a flat argument list with no explicit command verb. This feature introduces two top-level commands — `build` and `run` — to the CLI. The `build` command compiles a Mist project (matching the current default behavior). The `run` command compiles the project and, on success, executes the resulting DLL artifact via `dotnet <artifact-path>`. When no command verb is provided, the CLI defaults to `build` to preserve backward compatibility.

Syntax: `mist <build|run> [project-folder] [--backend <id>] [--output <dir>] [--bin <dir>] [--mode <compile|lex>]`

## Glossary

- **CLI**: The Mist command-line interface entry point (`mist` executable), implemented in `Source/Mist.CLI/Program.cs`.
- **Argument_Parser**: The component that tokenizes and validates raw command-line arguments, implemented in `CliArgumentParser`.
- **CLI_Arguments**: The parsed, validated record of command-line options passed to the compiler pipeline, implemented as `CliArguments`.
- **Command**: The top-level verb (`build` or `run`) that determines the CLI execution mode.
- **Build_Command**: The command that compiles a Mist project and produces output artifacts without executing them.
- **Run_Command**: The command that compiles a Mist project and, on success, executes the primary output artifact.
- **Primary_Output_Artifact**: The DLL file produced by a successful compilation, referenced by `CompilerResult.PrimaryOutputArtifactPath`.
- **Dotnet_Runner**: The external `dotnet` process invoked to execute the Primary_Output_Artifact.
- **Compiler_Result**: The structured result of a compilation run, containing success status, artifact path, and diagnostics.
- **Compiler_Mode**: The execution mode for the compiler pipeline (`Compile` or `Lex`).

## Requirements

### Requirement 1: Command Verb Parsing

**User Story:** As a developer, I want to specify `build` or `run` as the first argument to the Mist CLI, so that I can control whether the project is only compiled or compiled and executed.

#### Acceptance Criteria

1. WHEN the first positional argument is `build`, THE Argument_Parser SHALL parse the remaining arguments and set the Command to Build_Command.
2. WHEN the first positional argument is `run`, THE Argument_Parser SHALL parse the remaining arguments and set the Command to Run_Command.
3. WHEN no positional argument matches a known command verb, THE Argument_Parser SHALL default the Command to Build_Command and treat all positional arguments as the project folder.

### Requirement 2: Backward-Compatible Default Command

**User Story:** As a developer, I want the CLI to behave as `build` when I omit the command verb, so that existing scripts and workflows continue to work without modification.

#### Acceptance Criteria

1. WHEN the CLI is invoked with no arguments, THE Argument_Parser SHALL set the Command to Build_Command and set the project folder to the current working directory.
2. WHEN the CLI is invoked with only a project folder path and optional flags, THE Argument_Parser SHALL set the Command to Build_Command and parse the project folder and flags as before.
3. WHEN the CLI is invoked with only flags and no command verb or project folder, THE Argument_Parser SHALL set the Command to Build_Command and set the project folder to the current working directory.

### Requirement 3: Build Command Execution

**User Story:** As a developer, I want the `build` command to compile my Mist project and report diagnostics, so that I can verify my code compiles correctly.

#### Acceptance Criteria

1. WHEN the Command is Build_Command, THE CLI SHALL discover source files, clean output directories, compile the project, print diagnostics, and return exit code 0 on success.
2. WHEN the Command is Build_Command and compilation fails, THE CLI SHALL print diagnostics to stderr and return a non-zero exit code.

### Requirement 4: Run Command Compilation Phase

**User Story:** As a developer, I want the `run` command to compile my project first, so that I always execute up-to-date artifacts.

#### Acceptance Criteria

1. WHEN the Command is Run_Command, THE CLI SHALL perform the full build pipeline (discover sources, clean directories, compile) before attempting execution.
2. WHEN the Command is Run_Command and compilation fails, THE CLI SHALL print diagnostics to stderr, return a non-zero exit code, and skip artifact execution.

### Requirement 5: Run Command Execution Phase

**User Story:** As a developer, I want the `run` command to execute the compiled DLL after a successful build, so that I can compile and test my program in a single step.

#### Acceptance Criteria

1. WHEN the Command is Run_Command and compilation succeeds, THE CLI SHALL invoke `dotnet <Primary_Output_Artifact>` as a child process.
2. WHEN the Command is Run_Command and compilation succeeds, THE CLI SHALL forward the exit code of the Dotnet_Runner process as the CLI exit code.
3. WHEN the Command is Run_Command and compilation succeeds, THE CLI SHALL forward the standard output of the Dotnet_Runner process to the CLI standard output.
4. WHEN the Command is Run_Command and compilation succeeds, THE CLI SHALL forward the standard error of the Dotnet_Runner process to the CLI standard error.

### Requirement 6: Run Command Error Handling

**User Story:** As a developer, I want clear error messages when the run command fails to execute the artifact, so that I can diagnose issues quickly.

#### Acceptance Criteria

1. IF the Dotnet_Runner process fails to start, THEN THE CLI SHALL print an error message to stderr indicating the `dotnet` process could not be launched.
2. IF the Dotnet_Runner process fails to start, THEN THE CLI SHALL return a non-zero exit code.
3. IF the Compiler_Result has a null Primary_Output_Artifact after a successful compilation, THEN THE CLI SHALL print an error message to stderr indicating no executable artifact was produced.
4. IF the Compiler_Result has a null Primary_Output_Artifact after a successful compilation, THEN THE CLI SHALL return a non-zero exit code.

### Requirement 7: Run Command and Lex Mode Interaction

**User Story:** As a developer, I want the CLI to reject the combination of `run` with `--mode lex`, so that I am not confused by a run attempt on non-executable lex output.

#### Acceptance Criteria

1. WHEN the Command is Run_Command and the Compiler_Mode is Lex, THE Argument_Parser SHALL produce a parse error stating that `run` is not compatible with `--mode lex`.
2. WHEN the Command is Run_Command and the Compiler_Mode is Lex, THE CLI SHALL print the error to stderr and return a non-zero exit code.

### Requirement 8: Updated Usage Message

**User Story:** As a developer, I want the CLI usage message to reflect the new command syntax, so that I can discover the `build` and `run` commands from the help output.

#### Acceptance Criteria

1. WHEN the Argument_Parser produces parse errors, THE CLI SHALL print a usage message showing the syntax `mist <build|run> [project-folder] [--backend <id>] [--output <dir>] [--bin <dir>] [--mode <compile|lex>]`.

### Requirement 9: CLI_Arguments Record Extension

**User Story:** As a developer, I want the parsed CLI arguments to include the resolved command, so that downstream code can branch on the command without re-parsing.

#### Acceptance Criteria

1. THE CLI_Arguments record SHALL include a field representing the resolved Command (Build_Command or Run_Command).
2. THE Argument_Parser SHALL populate the Command field in CLI_Arguments for every successful parse.
