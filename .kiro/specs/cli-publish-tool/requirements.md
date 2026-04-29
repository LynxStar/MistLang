# Requirements Document

## Introduction

This document specifies the requirements for creating a publishable CLI tool for the Mist compiler. Currently, running the Mist CLI requires `dotnet run --project Source/Mist.CLI/Mist.CLI.csproj`, which is cumbersome for regular use. This feature packages the CLI as a self-contained tool named `mist` that is installed to a well-known location on the user's PATH, enabling direct invocation via `mist build`, `mist run`, etc. The publish mechanism uses a shell script that leverages `dotnet publish` to produce a self-contained executable and installs it to `~/.mist/bin`.

## Glossary

- **CLI_Project**: The Mist.CLI .NET project located at `Source/Mist.CLI/Mist.CLI.csproj` that serves as the command-line entry point for the Mist compiler.
- **Publish_Script**: A shell script (`publish.sh`) located at the repository root that automates building, publishing, and installing the Mist CLI tool.
- **Install_Directory**: The directory `~/.mist/bin` where the published `mist` executable is placed.
- **Self_Contained_Publish**: A `dotnet publish` mode that bundles the .NET runtime with the application, producing a single executable that does not require a separate .NET SDK installation on the target machine.
- **Shell_Profile**: The user's shell configuration file (e.g., `~/.bashrc`, `~/.zshrc`, `~/.bash_profile`) that is sourced on shell startup and defines environment variables including PATH.
- **PATH**: The operating system environment variable that lists directories searched for executable commands.
- **RID**: Runtime Identifier — a .NET concept that specifies the target platform for publishing (e.g., `linux-x64`, `osx-arm64`, `win-x64`).

## Requirements

### Requirement 1: Project Packaging Configuration

**User Story:** As a Mist developer, I want the CLI project configured for single-file self-contained publishing, so that the published output is a single portable executable.

#### Acceptance Criteria

1. THE CLI_Project SHALL define the `AssemblyName` property as `mist` so that the published executable is named `mist`.
2. THE CLI_Project SHALL define the `PublishSingleFile` property as `true` so that `dotnet publish` produces a single executable file.
3. THE CLI_Project SHALL define the `SelfContained` property as `true` so that the published executable includes the .NET runtime and does not require a separate .NET installation.
4. THE CLI_Project SHALL define the `PublishTrimmed` property as `true` so that unused framework code is removed and the published executable size is reduced.

### Requirement 2: Publish Script

**User Story:** As a Mist developer, I want a publish script that automates building and installing the CLI tool, so that I can install or update the `mist` command with a single command.

#### Acceptance Criteria

1. THE Publish_Script SHALL be located at the repository root and named `publish.sh`.
2. THE Publish_Script SHALL be executable (have the executable permission bit set).
3. WHEN the Publish_Script is executed, THE Publish_Script SHALL run `dotnet publish` on the CLI_Project in Release configuration with the current platform's RID.
4. WHEN the Publish_Script is executed, THE Publish_Script SHALL detect the current platform's RID automatically (e.g., `osx-arm64`, `linux-x64`, `win-x64`) without requiring the user to specify it manually.
5. WHEN the Publish_Script is executed, THE Publish_Script SHALL create the Install_Directory (`~/.mist/bin`) if the directory does not already exist.
6. WHEN `dotnet publish` completes successfully, THE Publish_Script SHALL copy the published `mist` executable to the Install_Directory.
7. IF `dotnet publish` fails, THEN THE Publish_Script SHALL print an error message to stderr and exit with a non-zero exit code.

### Requirement 3: PATH Configuration

**User Story:** As a Mist developer, I want the publish script to add the install directory to my PATH, so that I can invoke `mist` directly from any terminal session without manual configuration.

#### Acceptance Criteria

1. WHEN the Install_Directory is not already present in the user's PATH, THE Publish_Script SHALL append an `export PATH="$HOME/.mist/bin:$PATH"` line to the user's Shell_Profile.
2. WHEN the Install_Directory is already present in the user's PATH, THE Publish_Script SHALL skip modifying the Shell_Profile and print a message indicating the PATH is already configured.
3. THE Publish_Script SHALL detect the user's current shell (bash or zsh) and modify the appropriate Shell_Profile (`~/.bashrc` for bash, `~/.zshrc` for zsh).
4. WHEN the Shell_Profile is modified, THE Publish_Script SHALL print a message instructing the user to restart their terminal or source the Shell_Profile for the change to take effect.

### Requirement 4: Published Executable Behavior

**User Story:** As a Mist user, I want the published `mist` executable to behave identically to running the CLI via `dotnet run`, so that publishing does not change any compiler functionality.

#### Acceptance Criteria

1. WHEN the published `mist` executable is invoked with `build` and a project folder, THE published executable SHALL produce the same compilation output as `dotnet run --project Source/Mist.CLI/Mist.CLI.csproj -- build <project-folder>`.
2. WHEN the published `mist` executable is invoked with `run` and a project folder, THE published executable SHALL compile and execute the project identically to the `dotnet run` equivalent.
3. WHEN the published `mist` executable is invoked with invalid arguments, THE published executable SHALL print the same usage message and exit with exit code 1.
4. THE published `mist` executable SHALL return the same exit codes as the current CLI for all success and failure scenarios.

### Requirement 5: Idempotent Installation

**User Story:** As a Mist developer, I want to run the publish script multiple times safely, so that I can update my installed version without side effects or duplicate PATH entries.

#### Acceptance Criteria

1. WHEN the Publish_Script is executed and a previous `mist` executable exists in the Install_Directory, THE Publish_Script SHALL overwrite the existing executable with the newly published version.
2. WHEN the Publish_Script is executed multiple times, THE Publish_Script SHALL not add duplicate `export PATH` entries to the Shell_Profile.
3. WHEN the Publish_Script completes successfully, THE Publish_Script SHALL print the installed version path and a confirmation message.

### Requirement 6: Script Feedback and Error Handling

**User Story:** As a Mist developer, I want clear feedback from the publish script at each step, so that I can understand what happened and troubleshoot any issues.

#### Acceptance Criteria

1. THE Publish_Script SHALL print a status message before each major step (detecting platform, publishing, copying executable, configuring PATH).
2. IF the `dotnet` command is not found on the system PATH, THEN THE Publish_Script SHALL print an error message indicating that the .NET SDK is required and exit with a non-zero exit code.
3. IF the copy of the published executable to the Install_Directory fails, THEN THE Publish_Script SHALL print an error message to stderr and exit with a non-zero exit code.
4. WHEN the Publish_Script completes successfully, THE Publish_Script SHALL print a summary showing the install path and instructions for using the `mist` command.
