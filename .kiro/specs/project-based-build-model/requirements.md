# Requirements Document

## Introduction

The Mist compiler currently accepts individual source files and directories as positional arguments, expanding directories to discover `.mist` files recursively. This feature replaces that model with a project-folder-based build model inspired by .NET's `bin`/`obj` convention. Instead of passing individual files or folders, the user passes a single project folder. Build output is split into two directories: an intermediate output directory (`out`) for generated source code (emitted C# files, `.csproj`), and a binary directory (`bin`) for the final compiled artifact (e.g., the compiled DLL/executable). Both directories live inside the project folder by default, and every build cleans both directories before emitting new artifacts. The user may still override either directory location.

## Glossary

- **CLI**: The Mist command-line interface entry point (`Mist.CLI`).
- **Project_Folder**: A directory that serves as the root of a Mist project. It contains `.mist` source files (possibly in subdirectories) and hosts the default output directory.
- **Output_Directory**: The directory where the compiler writes intermediate generated source code (emitted C# files, `.csproj`). This is temporary/intermediate output produced by the Backend emitter. Defaults to `<Project_Folder>/out`.
- **Bin_Directory**: The directory where the compiler writes the final compiled artifact (e.g., the compiled DLL/executable). This is the final output produced by the compilation driver. Defaults to `<Project_Folder>/bin`.
- **Source_Discovery**: The process of recursively finding all `.mist` files within the Project_Folder.
- **Argument_Parser**: The `CliArgumentParser` component that parses raw command-line arguments into structured `CliArguments`.
- **Compiler_Driver**: The `CompilerDriver` component that orchestrates the full compilation pipeline.
- **Backend**: A pluggable code-generation component (e.g., `CSharpTranspilerBackend`) that emits intermediate source code into the Output_Directory and places the final compiled artifact into the Bin_Directory.
- **Clean**: The act of deleting all contents of both the Output_Directory and the Bin_Directory before a new build begins.

## Requirements

### Requirement 1: Accept a Single Project Folder

**User Story:** As a developer, I want to pass a single project folder to the Mist compiler, so that I do not need to enumerate individual source files or multiple directories.

#### Acceptance Criteria

1. WHEN the user provides a single positional argument that is an existing directory, THE Argument_Parser SHALL interpret the argument as the Project_Folder.
2. WHEN the user provides no positional arguments, THE Argument_Parser SHALL use the current working directory as the Project_Folder.
3. IF the user provides more than one positional argument, THEN THE Argument_Parser SHALL return a parse error indicating that only a single project folder is accepted.
4. IF the user provides a positional argument that is a file path, THEN THE Argument_Parser SHALL return a parse error indicating that individual file paths are no longer accepted.
5. IF the positional argument does not refer to an existing directory, THEN THE Argument_Parser SHALL return a parse error indicating the directory does not exist.

### Requirement 2: Discover Source Files from the Project Folder

**User Story:** As a developer, I want the compiler to automatically find all `.mist` source files within my project folder, so that I do not need to list them manually.

#### Acceptance Criteria

1. WHEN a valid Project_Folder is resolved, THE Source_Discovery SHALL recursively enumerate all files with the `.mist` extension within the Project_Folder.
2. WHEN Source_Discovery completes, THE Source_Discovery SHALL return the discovered files sorted in ordinal string order.
3. IF Source_Discovery finds zero `.mist` files in the Project_Folder, THEN THE CLI SHALL report an error stating that no `.mist` source files were found in the project folder.

### Requirement 3: Default Output and Bin Directories Inside the Project Folder

**User Story:** As a developer, I want the intermediate and final build output to live inside my project folder by default, so that each project is self-contained like .NET's `bin`/`obj` convention.

#### Acceptance Criteria

1. WHEN the user does not provide the `--output` flag, THE CLI SHALL set the Output_Directory to `<Project_Folder>/out`.
2. WHEN the user provides the `--output` flag with a value, THE CLI SHALL use the provided value as the Output_Directory.
3. WHEN the user does not provide the `--bin` flag, THE CLI SHALL set the Bin_Directory to `<Project_Folder>/bin`.
4. WHEN the user provides the `--bin` flag with a value, THE CLI SHALL use the provided value as the Bin_Directory.
5. THE Compiler_Driver SHALL pass the resolved Output_Directory to the Backend for intermediate source emission.
6. THE Compiler_Driver SHALL pass the resolved Bin_Directory to the Backend for final artifact placement.

### Requirement 4: Clean Both Output Directories Before Each Build

**User Story:** As a developer, I want both the intermediate output directory and the binary directory to be cleaned before every build, so that stale artifacts from previous builds do not interfere with the current build.

#### Acceptance Criteria

1. WHEN a build begins and the Output_Directory exists, THE CLI SHALL delete all contents of the Output_Directory before invoking the Compiler_Driver.
2. WHEN a build begins and the Bin_Directory exists, THE CLI SHALL delete all contents of the Bin_Directory before invoking the Compiler_Driver.
3. WHEN a build begins and the Output_Directory does not exist, THE CLI SHALL proceed without attempting to clean the Output_Directory.
4. WHEN a build begins and the Bin_Directory does not exist, THE CLI SHALL proceed without attempting to clean the Bin_Directory.
5. IF the Clean operation fails on either directory due to a file system error, THEN THE CLI SHALL report an error describing the failure and abort the build.

### Requirement 5: Update CLI Usage and Help Text

**User Story:** As a developer, I want the CLI usage message to reflect the new project-folder-based interface, so that I understand how to invoke the compiler correctly.

#### Acceptance Criteria

1. THE CLI SHALL display the usage pattern as `mist [project-folder] [--backend <id>] [--output <dir>] [--bin <dir>] [--mode <compile|lex>]`.
2. WHEN the Argument_Parser encounters parse errors, THE CLI SHALL print the updated usage pattern to standard error.

### Requirement 6: Update CompilerOptions to Reflect Project Model

**User Story:** As a developer of the Mist compiler, I want `CompilerOptions` to carry the project folder path alongside source paths and both output directories, so that downstream pipeline stages have access to the project root and know where to place intermediate and final artifacts.

#### Acceptance Criteria

1. THE CompilerOptions record SHALL include a `ProjectFolder` field of type `string` representing the resolved absolute path of the Project_Folder.
2. THE CompilerOptions record SHALL continue to include a `SourcePaths` field populated by Source_Discovery from the Project_Folder.
3. THE CompilerOptions record SHALL include an `OutputDirectory` field representing the resolved Output_Directory for intermediate generated source.
4. THE CompilerOptions record SHALL include a `BinDirectory` field of type `string` representing the resolved Bin_Directory for the final compiled artifact.

### Requirement 7: Update CliArguments to Reflect Project Model

**User Story:** As a developer of the Mist compiler, I want `CliArguments` to carry a single project folder instead of a list of source files, so that the CLI data model matches the new interface.

#### Acceptance Criteria

1. THE CliArguments record SHALL replace the `SourceFiles` field with a `ProjectFolder` field of type `string`.
2. THE CliArguments record SHALL retain the `BackendId`, `OutputDirectory`, and `Mode` fields.
3. THE CliArguments record SHALL include a `BinDirectory` field of type `string`.
4. WHEN the user does not provide a project folder argument, THE CliArguments `ProjectFolder` field SHALL default to the current working directory.

### Requirement 8: Lex Mode Compatibility

**User Story:** As a developer, I want lex mode to work with the project-folder-based model, so that I can still dump token streams for all source files in my project.

#### Acceptance Criteria

1. WHEN the compiler runs in lex mode, THE CLI SHALL discover source files from the Project_Folder using Source_Discovery.
2. WHEN the compiler runs in lex mode, THE CLI SHALL write lex output into a `lexer` subdirectory of the Output_Directory.
3. WHEN the compiler runs in lex mode, THE CLI SHALL clean the Output_Directory before writing lex output.
4. WHEN the compiler runs in lex mode, THE CLI SHALL clean the Bin_Directory before writing lex output.

### Requirement 9: Backend Emits Intermediate Source to Output Directory and Final Artifact to Bin Directory

**User Story:** As a developer, I want the backend to emit generated source code to the intermediate output directory and place the final compiled artifact in the binary directory, so that intermediate and final outputs are clearly separated.

#### Acceptance Criteria

1. WHEN the Backend emits intermediate source code, THE Backend SHALL write generated C# files and the `.csproj` file to the Output_Directory.
2. WHEN the Backend compiles the emitted source code, THE Backend SHALL place the final compiled artifact (DLL or executable) in the Bin_Directory.
3. THE Backend SHALL pass the Bin_Directory as the output path to the `dotnet build` invocation so that the compiled binary is placed in the Bin_Directory.
4. THE Backend SHALL receive both the Output_Directory and the Bin_Directory from the Compiler_Driver.
