# Requirements Document

## Introduction

This feature migrates the `mist.toml` project reference format from TOML's array-of-tables syntax (`[[references]]`) to a Cargo-style named map syntax (`[references]`). Each reference is keyed by the referenced project's name, enabling the compiler to validate that the declared name matches the target project's actual `name` field. The `ProjectReference` record gains a `Name` field, the parser switches from `TomlTableArray` to `TomlTable` iteration, and the resolver performs name-mismatch validation.

## Glossary

- **Project_File_Parser**: The `ProjectFileParser` static class that reads and validates `mist.toml` files, producing a `ProjectConfiguration` or reporting diagnostics.
- **Project_Reference_Resolver**: The `ProjectReferenceResolver` static class that performs DFS-based cycle detection, deduplication, and topological ordering of the project reference graph.
- **Project_Reference**: The `ProjectReference` record representing a single declared reference in `mist.toml`, carrying a `Name` and a normalized absolute `Path`.
- **Project_Configuration**: The `ProjectConfiguration` record representing the fully parsed contents of a `mist.toml` file.
- **Reference_Key**: The TOML key under the `[references]` table that identifies a project reference by name (e.g., `Primitives` in `Primitives = { path = "../Primitives" }`).
- **Reference_Value**: The inline table or sub-table associated with a Reference_Key, containing at minimum a `path` field.
- **Diagnostic_Bag**: The `DiagnosticBag` accumulator used to collect all diagnostics during parsing and resolution.

## Requirements

### Requirement 1: Parse named reference format

**User Story:** As a Mist developer, I want to declare project references using named keys in `mist.toml`, so that the reference declarations are more readable and self-documenting.

#### Acceptance Criteria

1. WHEN a `[references]` table is present in `mist.toml`, THE Project_File_Parser SHALL iterate each key-value pair in the table and produce one Project_Reference per entry.
2. WHEN a Reference_Key is present, THE Project_File_Parser SHALL use the key string as the `Name` field of the resulting Project_Reference, subject to the same character validation rules as the project `name` field (alphanumeric, hyphens, underscores, and periods only) and a maximum length of 128 characters.
3. WHEN a Reference_Value contains a `path` field with a non-empty, non-whitespace string value, THE Project_File_Parser SHALL resolve the path relative to the directory containing the `mist.toml` file, normalize it to an absolute path, and store it in the Project_Reference `Path` field.
4. WHEN a Reference_Value is an inline table (e.g., `Primitives = { path = "../Primitives" }`), THE Project_File_Parser SHALL parse it identically to the expanded sub-table form (`[references.Primitives]`).
5. WHEN the `references` field is absent from `mist.toml`, THE Project_File_Parser SHALL produce an empty references array with no diagnostics.
6. IF a Reference_Value does not contain a `path` field, or the `path` field is empty or whitespace, THEN THE Project_File_Parser SHALL report a diagnostic error indicating the missing or empty path and halt parsing, returning no configuration.
7. IF a Reference_Key fails character validation or exceeds 128 characters, THEN THE Project_File_Parser SHALL report a diagnostic error indicating the invalid reference name and halt parsing, returning no configuration.
8. IF the `references` field is present but is not a TOML table, THEN THE Project_File_Parser SHALL report a diagnostic error indicating the incorrect type and halt parsing, returning no configuration.

### Requirement 2: Validate reference entry format

**User Story:** As a Mist developer, I want clear error messages when my reference entries are malformed, so that I can fix configuration mistakes quickly.

#### Acceptance Criteria

1. IF the `references` field is present but is not a TOML table, THEN THE Project_File_Parser SHALL report a diagnostic with identifier `MIST_PROJECT_REFERENCE_INVALID_FORMAT` and severity Error, and return null.
2. IF a Reference_Value is not a TOML table (inline or sub-table), THEN THE Project_File_Parser SHALL report a `ProjectReferenceInvalidFormat` diagnostic with severity Error and return null.
3. IF a Reference_Value does not contain a `path` key, or the `path` key is present but its value is not a string, THEN THE Project_File_Parser SHALL report a `ProjectReferenceMissingPath` diagnostic with severity Error and return null.
4. IF a Reference_Value contains a `path` field whose string value is empty or consists solely of whitespace characters, THEN THE Project_File_Parser SHALL report a `ProjectReferenceEmptyPath` diagnostic with severity Error and return null.
5. WHEN the Project_File_Parser returns null due to a reference validation error, THE Project_File_Parser SHALL have added exactly one Error-severity diagnostic to the diagnostic bag before returning.
6. WHEN multiple references exist and the first entry is valid but a subsequent entry fails validation, THE Project_File_Parser SHALL report the diagnostic for the failing entry and return null without producing a ProjectConfiguration.

### Requirement 3: Validate reference key name

**User Story:** As a Mist developer, I want the compiler to reject invalid reference names, so that my project file stays consistent and machine-parseable.

#### Acceptance Criteria

1. IF a Reference_Key is empty or whitespace, THEN THE Project_File_Parser SHALL report a `ProjectReferenceInvalidName` diagnostic with severity Error and return null. The diagnostic message SHALL include the offending key value and the file path.
2. IF a Reference_Key does not match the valid project name pattern (`^[a-zA-Z0-9\-_\.]+$`), THEN THE Project_File_Parser SHALL report a `ProjectReferenceInvalidName` diagnostic with severity Error and return null. The diagnostic message SHALL include the offending key value and the file path.
3. IF a Reference_Key exceeds 128 characters, THEN THE Project_File_Parser SHALL report a `ProjectReferenceInvalidName` diagnostic with severity Error and return null. The diagnostic message SHALL include the key length and the maximum allowed length.
4. THE Project_File_Parser SHALL apply validation checks in the order listed (empty/whitespace, character pattern, length) and short-circuit on the first failure.

### Requirement 4: Validate reference name matches target project

**User Story:** As a Mist developer, I want the compiler to verify that my reference key matches the actual project name, so that typos and stale renames are caught at build time.

#### Acceptance Criteria

1. WHEN the Project_Reference_Resolver successfully parses a referenced project's `mist.toml`, THE Project_Reference_Resolver SHALL compare the Reference_Key (Project_Reference `Name`) against the parsed project's `name` field using ordinal (case-sensitive) string equality.
2. IF the Reference_Key does not equal the referenced project's `name` field (case-sensitive), THEN THE Project_Reference_Resolver SHALL report a `ProjectReferenceNameMismatch` diagnostic with severity Error.
3. WHEN a `ProjectReferenceNameMismatch` diagnostic is reported, THE Project_Reference_Resolver SHALL include both the declared Reference_Key and the actual project name in the diagnostic message.
4. WHEN a `ProjectReferenceNameMismatch` diagnostic is reported, THE Project_Reference_Resolver SHALL exclude the mismatched reference from the resolved output, skip traversal of its transitive dependencies, and continue resolving the remaining sibling references.

### Requirement 5: Update ProjectReference record

**User Story:** As a compiler maintainer, I want the `ProjectReference` record to carry the declared name, so that downstream validation can compare it against the target project.

#### Acceptance Criteria

1. THE Project_Reference record SHALL contain a `Name` field of type `string` representing the Reference_Key declared in the `[references]` table of `mist.toml`, constrained to a maximum length of 128 characters and matching the pattern `^[a-zA-Z0-9\-_\.]+$`.
2. THE Project_Reference record SHALL contain a `Path` field of type `string` representing the normalized absolute path to the referenced project's `mist.toml`.
3. THE Project_File_Parser SHALL construct each Project_Reference with both the `Name` and `Path` fields populated from the corresponding `[references]` entry.

### Requirement 6: Path normalization

**User Story:** As a Mist developer, I want relative paths in references to be resolved relative to the declaring project's directory, so that references work regardless of the working directory.

#### Acceptance Criteria

1. WHEN a reference `path` value is a relative path (does not start with a path root), THE Project_File_Parser SHALL resolve it relative to the directory containing the declaring `mist.toml`.
2. WHEN a reference `path` value is an absolute path, THE Project_File_Parser SHALL normalize it using `Path.GetFullPath`, resolving any intermediate `.` or `..` segments.
3. WHEN the normalized path does not end with `mist.toml` (case-sensitive comparison), THE Project_File_Parser SHALL append `mist.toml` to the path.

### Requirement 7: Round-trip serialization

**User Story:** As a compiler maintainer, I want the TOML serializer in the round-trip property test to produce the new named format, so that the property test validates the new parsing logic.

#### Acceptance Criteria

1. WHEN references are present in a ProjectConfiguration, THE serializer SHALL produce a `[references]` table section containing one key-value pair per reference.
2. FOR EACH ProjectReference, THE serializer SHALL emit a key-value pair where the key is the reference's `Name` field and the value is a TOML inline table containing exactly one field `path` whose value is the reference's `Path` field.
3. WHEN a ProjectConfiguration has zero references, THE serializer SHALL omit the `[references]` table section entirely.
4. FOR ALL valid ProjectConfiguration values containing between 1 and 10 references (inclusive), parsing the serialized TOML with ProjectFileParser SHALL produce a ProjectConfiguration where each reference has the same `Name` and `Path` field values as the corresponding original reference (round-trip property).

### Requirement 8: Migrate existing sample project file

**User Story:** As a Mist developer, I want the `samples/AllFeatures/mist.toml` file updated to the new format, so that the sample project continues to build correctly.

#### Acceptance Criteria

1. THE `samples/AllFeatures/mist.toml` file SHALL use the `[references]` table syntax where each key is a reference name and each value is an inline table containing the relative path to the referenced project directory.
2. EACH reference key in `samples/AllFeatures/mist.toml` SHALL match the `name` field of the referenced project's `mist.toml`.
3. THE migrated `samples/AllFeatures/mist.toml` file SHALL contain the same number of references as the original file, preserving all existing project references without additions or removals.
4. WHEN the compiler builds the AllFeatures sample after migration, THE compiler SHALL produce zero diagnostics of severity Error.

### Requirement 9: Update book documentation

**User Story:** As a Mist developer reading the language book, I want the Project Configuration chapter to document the new named reference format, so that I can learn the correct syntax for declaring dependencies.

#### Acceptance Criteria

1. THE `book/src/tooling/project-configuration.md` chapter SHALL replace all documentation of the `[[references]]` array-of-tables syntax with the new `[references]` named table syntax.
2. THE chapter SHALL document both the inline table form (`Primitives = { path = "../Primitives" }`) and the expanded sub-table form (`[references.Primitives]`) as equivalent alternatives.
3. THE chapter SHALL explain that the reference key must match the referenced project's `name` field exactly (case-sensitive).
4. THE chapter SHALL document the `ProjectReferenceNameMismatch` error that occurs when the key does not match the target project's name.
5. THE chapter SHALL update the fields summary table to show `[references]` as a table (not an array of tables), with a description reflecting the named key format.
6. THE chapter SHALL update all example configurations (multi-project, large project, project with global usings) to use the new `[references]` table syntax.
7. THE chapter SHALL update the error behavior table to reflect the new diagnostic IDs (`MIST_PROJECT_REFERENCE_INVALID_FORMAT`, `MIST_PROJECT_REFERENCE_INVALID_NAME`, `MIST_PROJECT_REFERENCE_NAME_MISMATCH`) and remove the old `[[references]]`-specific error descriptions.
8. THE chapter SHALL preserve all existing documentation about path resolution rules, circular reference detection, and missing reference errors — only the syntax and format descriptions change.

### Requirement 10: Register new diagnostic IDs

**User Story:** As a compiler maintainer, I want dedicated diagnostic IDs for the new validation rules, so that errors are distinguishable and tooling can filter by ID.

#### Acceptance Criteria

1. THE DiagnosticId type SHALL define a `public static readonly` field named `ProjectReferenceInvalidFormat` with value `"MIST_PROJECT_REFERENCE_INVALID_FORMAT"`.
2. THE DiagnosticId type SHALL define a `public static readonly` field named `ProjectReferenceInvalidName` with value `"MIST_PROJECT_REFERENCE_INVALID_NAME"`.
3. THE DiagnosticId type SHALL define a `public static readonly` field named `ProjectReferenceNameMismatch` with value `"MIST_PROJECT_REFERENCE_NAME_MISMATCH"`.
4. THE DiagnosticId type SHALL ensure that the values `"MIST_PROJECT_REFERENCE_INVALID_FORMAT"`, `"MIST_PROJECT_REFERENCE_INVALID_NAME"`, and `"MIST_PROJECT_REFERENCE_NAME_MISMATCH"` are unique across all defined diagnostic ID fields.
