# Requirements Document

## Introduction

The Mist compiler generates `.csproj` files for the C# transpilation backend. Currently, the generated `<PropertyGroup>` contains only a fixed set of properties (`OutputType`, `TargetFramework`, `ImplicitUsings`, `Nullable`, `Deterministic`, `EnableNETAnalyzers`). Users have no way to declaratively inject arbitrary MSBuild properties into the generated csproj.

This feature adds a `[csproj_properties]` table to `mist.toml` that passes arbitrary key-value pairs through to the csproj's `<PropertyGroup>`. The name is deliberately C#-backend-specific so that it stands out as irrelevant in any future non-C# backend context. This enables scenarios like ASP.NET User Secrets (`UserSecretsId`), custom assembly names (`AssemblyName`), documentation generation (`GenerateDocumentationFile`), source generator configuration, and any other MSBuild property a downstream tool might require.

## Glossary

- **Project_File_Parser**: The component (`ProjectFileParser.cs`) that reads and validates `mist.toml`, producing a `ProjectConfiguration`.
- **Project_Configuration**: The immutable record (`ProjectConfiguration.cs`) that holds all parsed project settings.
- **CSharp_Emitter**: The component (`CSharpEmitter.cs`) that generates the `.csproj` file with its hardcoded template.
- **Compiler_Driver**: The orchestrating component (`CompilerDriver.cs`) that coordinates compilation and patches the csproj with package/framework references.
- **Csproj_Properties_Table**: The `[csproj_properties]` TOML table in `mist.toml` where users declare arbitrary MSBuild property key-value pairs for the generated csproj.
- **Property_Group**: The `<PropertyGroup>` XML element in the generated `.csproj` file where MSBuild properties reside.
- **MSBuild_Property**: A key-value pair emitted as an XML element inside `<PropertyGroup>` (e.g., `<UserSecretsId>abc-123</UserSecretsId>`).

## Requirements

### Requirement 1: Parse the Csproj Properties Table

**User Story:** As a Mist developer, I want to declare arbitrary MSBuild properties in `mist.toml`, so that the generated csproj includes them without manual editing.

#### Acceptance Criteria

1. WHEN a `[csproj_properties]` table is present in `mist.toml`, THE Project_File_Parser SHALL parse each entry as a string key-value pair and include the entries in the Project_Configuration as an ordered collection preserving declaration order.
2. WHEN the `[csproj_properties]` table is absent from `mist.toml`, THE Project_File_Parser SHALL produce a Project_Configuration with an empty csproj properties collection.
3. IF a `csproj_properties` field is present in `mist.toml` but is not a TOML table, THEN THE Project_File_Parser SHALL emit an error diagnostic and skip csproj properties parsing.
4. IF a property key in the `[csproj_properties]` table is empty or contains only whitespace after trimming, THEN THE Project_File_Parser SHALL emit an error diagnostic and skip that entry.
5. IF a property value in the `[csproj_properties]` table is not a TOML string, THEN THE Project_File_Parser SHALL emit an error diagnostic and skip that entry.
6. IF a property value in the `[csproj_properties]` table is empty or contains only whitespace after trimming, THEN THE Project_File_Parser SHALL emit an error diagnostic and skip that entry.

### Requirement 2: Validate Property Keys

**User Story:** As a Mist developer, I want the compiler to reject invalid property keys early, so that I get clear feedback instead of a broken csproj.

#### Acceptance Criteria

1. THE Project_File_Parser SHALL accept property keys that are at least 1 character long and contain only ASCII letters, ASCII digits, hyphens, underscores, and periods.
2. WHEN a property key contains characters outside the allowed set, THE Project_File_Parser SHALL emit an error diagnostic listing each distinct invalid character found in the key, skip that entry, and continue processing remaining entries.
3. WHEN a property key exceeds 128 characters in length, THE Project_File_Parser SHALL emit an error diagnostic reporting the actual length of the key, skip that entry, and continue processing remaining entries.
4. WHEN duplicate property keys appear in the `[csproj_properties]` table, THE Project_File_Parser SHALL use TOML's standard last-wins behavior without emitting a diagnostic.
5. WHEN a property key fails validation, THE Project_File_Parser SHALL NOT abort parsing of the entire `[csproj_properties]` table; subsequent valid entries SHALL still be included in the Project_Configuration.

### Requirement 3: Emit Properties into the Generated Csproj

**User Story:** As a Mist developer, I want declared properties emitted into the csproj's PropertyGroup, so that MSBuild and downstream tools pick them up automatically.

#### Acceptance Criteria

1. WHEN the Project_Configuration contains one or more csproj properties, THE Compiler_Driver SHALL emit each property as an XML element inside a `<PropertyGroup>` in the generated `.csproj` file.
2. THE Compiler_Driver SHALL emit each property in the format `<Key>Value</Key>` where Key is the property key and Value is the XML-escaped property value.
3. WHEN the Project_Configuration contains no csproj properties, THE Compiler_Driver SHALL produce a `.csproj` file with no additional PropertyGroup for custom properties.
4. THE Compiler_Driver SHALL emit custom properties in a separate `<PropertyGroup>` element placed after the existing hardcoded PropertyGroup and before any ItemGroup elements.
5. THE Compiler_Driver SHALL emit properties in the same order they appear in the Project_Configuration (i.e., declaration order from `mist.toml`) for deterministic output.
6. WHEN a property value contains XML-special characters (`<`, `>`, `&`, `"`, `'`), THE Compiler_Driver SHALL XML-escape the value so the generated csproj remains valid XML.

### Requirement 4: Prevent Override of Compiler-Managed Properties

**User Story:** As a Mist developer, I want the compiler to warn me if I accidentally override a property it already manages, so that I avoid subtle build breakage.

#### Acceptance Criteria

1. WHEN a property key in the `[csproj_properties]` table matches a compiler-managed property name (`OutputType`, `TargetFramework`, `ImplicitUsings`, `Nullable`, `Deterministic`, `EnableNETAnalyzers`) using case-insensitive comparison, THE Project_File_Parser SHALL emit a warning diagnostic that identifies the property key and indicates it is managed by the compiler.
2. WHEN a warning has been emitted for a compiler-managed property override, THE Project_File_Parser SHALL still include the property in the Project_Configuration so that the user's intent is honored.
3. WHEN multiple property keys in the `[csproj_properties]` table each match a compiler-managed property name, THE Project_File_Parser SHALL emit a separate warning diagnostic for each matching key.
