# Requirements Document

## Introduction

The Mist compiler currently accepts individual source files and directories as positional arguments, expanding directories to discover `.mist` files recursively. This feature replaces that model with a project-folder-based build model inspired by .NET's `bin`/`obj` convention. Instead of passing individual files or folders, the user passes a single project folder. Build output is split into two directories: an intermediate output directory (`out`) for generated source code (emitted C# files, `.csproj`), and a binary directory (`bin`) for the final compiled artifact (e.g., the compiled DLL/executable). Both directories live inside the project folder by default, and every build cleans both directories before emitting new artifacts. The user may still override either directory location.

Additionally, the Mist compiler introduces a **project file** that lives in the project folder root. This file provides project-level metadata — starting with a `name` field that controls the generated project name (used for the `.csproj` filename and the output DLL/executable name). This replaces the current behavior of deriving the project name from the folder name. The project file format must be chosen from among JSON, TOML, YAML, or XML, considering the Mist ecosystem context (a C# compiler that transpiles to C#).

## Format Decision: Project File

**Chosen format: TOML** (`mist.toml`)

TOML was selected because it is purpose-built for configuration files, supports comments, and is the modern standard for language project files (Rust's `Cargo.toml`, Python's `pyproject.toml`). The `Tomlyn` NuGet package provides TOML parsing for .NET.

Example `mist.toml`:
```toml
# Mist project configuration
name = "HelloMist"
```

## Glossary

- **CLI**: The Mist command-line interface entry point (`Mist.CLI`).
- **Project_Folder**: A directory that serves as the root of a Mist project. It contains `.mist` source files (possibly in subdirectories), the Project_File, and hosts the default output directories.
- **Project_File**: A TOML configuration file named `mist.toml` in the root of the Project_Folder that contains project-level metadata.
- **Project_Name**: The name of the project as specified in the `name` field of the Project_File. Used to name the generated `.csproj` file and the output DLL/executable.
- **Project_File_Parser**: The component responsible for reading and parsing the Project_File into a structured representation.
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

1. THE CLI SHALL display the usage pattern as `mist <build|run> [project-folder] [--backend <id>] [--output <dir>] [--bin <dir>] [--mode <compile|lex>]`.
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

### Requirement 10: Project File Provides Project Name

**User Story:** As a developer, I want to define my project's name in a project file, so that I have explicit control over the generated project name instead of relying on the folder name.

#### Acceptance Criteria

1. THE Project_File SHALL be a TOML file named `mist.toml` residing in the root of the Project_Folder.
2. THE Project_File SHALL contain a `name` field that specifies the Project_Name.
3. WHEN the Project_File exists and contains a valid `name` field, THE Compiler_Driver SHALL use the `name` value as the Project_Name for the generated `.csproj` filename and the output artifact name.
4. IF the `mist.toml` file does not exist in the Project_Folder, THEN THE CLI SHALL report an error indicating that no project file was found and the build cannot proceed.
5. IF the Project_File exists but does not contain a `name` field, THEN THE Project_File_Parser SHALL report an error indicating that the `name` field is required.
6. IF the Project_File exists but contains a `name` field with an empty or whitespace-only value, THEN THE Project_File_Parser SHALL report an error indicating that the project name must be non-empty.

### Requirement 11: Project Name Validation

**User Story:** As a developer, I want the compiler to validate my project name, so that the generated artifacts have valid filenames and the build does not fail due to invalid characters.

#### Acceptance Criteria

1. THE Project_File_Parser SHALL validate that the Project_Name contains only alphanumeric characters, hyphens, underscores, and periods.
2. IF the Project_Name contains invalid characters, THEN THE Project_File_Parser SHALL report an error listing the invalid characters found.
3. THE Project_File_Parser SHALL validate that the Project_Name is no longer than 128 characters.
4. IF the Project_Name exceeds 128 characters, THEN THE Project_File_Parser SHALL report an error indicating the maximum length.

### Requirement 12: Parse Project File

**User Story:** As a developer of the Mist compiler, I want a dedicated parser for the project file, so that the project file format is cleanly separated from the rest of the compiler.

#### Acceptance Criteria

1. THE Project_File_Parser SHALL read the `mist.toml` file from the Project_Folder root.
2. THE Project_File_Parser SHALL parse the TOML content into a structured Project_Configuration object containing the Project_Name.
3. IF the Project_File contains TOML syntax errors, THEN THE Project_File_Parser SHALL report a descriptive parse error including the file path and error details from the TOML parser.
4. THE Project_File_Parser SHALL produce a Project_Configuration object that can be extended with additional fields in the future without breaking changes.

### Requirement 13: Project Name Flows Through Compilation Pipeline

**User Story:** As a developer of the Mist compiler, I want the project name from the project file to replace the folder-name derivation, so that the entire pipeline uses the explicit project name.

#### Acceptance Criteria

1. THE CLI SHALL read the Project_File and extract the Project_Name before invoking the Compiler_Driver.
2. THE CompilerOptions record SHALL include a `ProjectName` field of type `string` populated from the Project_File.
3. THE Compiler_Driver SHALL pass the Project_Name to the Backend instead of deriving it from the folder name.
4. THE CSharpEmitter SHALL use the Project_Name to name the generated `.csproj` file (e.g., `{ProjectName}.csproj`).
5. THE CSharpCompilationDriver SHALL use the Project_Name to determine the output artifact filename (e.g., `{ProjectName}.dll`).

### Requirement 14: Update Sample Projects with Project Files

**User Story:** As a developer, I want the existing sample projects to include project files, so that they serve as examples of the new project model.

#### Acceptance Criteria

1. WHEN a sample project exists in the `samples/` directory, THE sample project SHALL contain a `mist.toml` file with a `name` field matching the sample's intended project name.
2. THE sample project `HelloMist` SHALL have a `mist.toml` with `name = "HelloMist"`.
3. THE sample project `AllFeatures` SHALL have a `mist.toml` with `name = "AllFeatures"`.
4. THE sample project `Simpliest` SHALL have a `mist.toml` with `name = "Simpliest"`.
