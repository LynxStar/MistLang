# Requirements Document

## Introduction

This feature adds the ability for a Mist project to declare references to other Mist projects. A referenced project's source files are recompiled as part of the referencing project's compilation, and the referenced project's public types and namespaces become available to the referencing project during semantic analysis, enabling multi-project solutions where code can be shared across project boundaries.

References are declared in the `mist.toml` project file using a `[[references]]` array. Each entry's `path` field points to either a `mist.toml` file directly or a directory that contains a `mist.toml` file. The compiler resolves each referenced project, recompiles its sources from scratch, and makes its public symbols available to the referencing project's semantic model.

## Non-Goals

- **Pre-compiled artifact linking**: Referenced projects are always recompiled from source. Loading pre-built `.dll` or other binary artifacts is not supported in this version.
- **Package manager / external dependencies**: Packages distributed outside the local file system will use a separate `[[dependencies]]` section (not `[[references]]`), keeping the two concerns distinct. This is reserved for a future spec.

## Glossary

- **Project_File_Parser**: The `ProjectFileParser` component that reads and validates `mist.toml` files.
- **Compiler_Driver**: The `CompilerDriver` component that orchestrates the full compilation pipeline.
- **Project_Configuration**: The structured in-memory representation of a parsed `mist.toml` file (`ProjectConfiguration`).
- **Project_Reference**: A declared dependency on another Mist project, identified by a `path` that resolves to a `mist.toml` file.
- **Referenced_Project**: A Mist project that is listed as a dependency of the current project.
- **Referencing_Project**: The Mist project that declares one or more `[[references]]` entries.
- **Reference_Path**: The file-system path (relative or absolute) written in the `path` field of a `[[references]]` entry. May point to a `mist.toml` file directly or to a directory containing one.
- **Resolved_Path**: The absolute, canonical file-system path to the Referenced_Project's `mist.toml` file, obtained by resolving a `Reference_Path`.
- **Compilation_Unit**: A single Mist source file passed through the compilation pipeline, carrying its source text, namespace root, and project folder.
- **Circular_Reference**: A situation where project A references project B and project B (directly or transitively) references project A.
- **Direct_Reference**: A project that is explicitly listed in the Referencing_Project's `[[references]]` array.
- **Transitive_Reference**: A project that is a dependency of a Direct_Reference (or of another Transitive_Reference), but is not itself listed in the Referencing_Project's `[[references]]` array.

---

## Requirements

### Requirement 1: Declare Project References in mist.toml

**User Story:** As a Mist developer, I want to declare references to other projects in my `mist.toml` file, so that I can share code across project boundaries.

#### Acceptance Criteria

1. THE `Project_File_Parser` SHALL accept an optional `[[references]]` array in `mist.toml`.
2. WHEN a `[[references]]` entry is present, THE `Project_File_Parser` SHALL require each entry to contain a `path` field of type string.
3. WHEN a `[[references]]` entry is missing the `path` field, THE `Project_File_Parser` SHALL report a `MIST_PROJECT_REFERENCE_MISSING_PATH` error diagnostic and return `null`.
4. WHEN a `[[references]]` entry contains a `path` field that is empty or whitespace, THE `Project_File_Parser` SHALL report a `MIST_PROJECT_REFERENCE_EMPTY_PATH` error diagnostic and return `null`.
5. WHEN no `[[references]]` array is present, THE `Project_File_Parser` SHALL produce a `Project_Configuration` with an empty references list.
6. THE `Project_Configuration` SHALL expose the list of declared `Project_Reference` entries parsed from `mist.toml`.

---

### Requirement 2: Resolve Reference Paths

**User Story:** As a Mist developer, I want to point a reference at either a project folder or a `mist.toml` file directly, and have relative paths resolved against my project's folder, so that I can use portable paths in `mist.toml` without worrying about the working directory.

#### Acceptance Criteria

1. WHEN a `Reference_Path` is a relative path, THE `Project_File_Parser` SHALL resolve it against the Referencing_Project's folder to produce an intermediate absolute path.
2. WHEN a `Reference_Path` is an absolute path, THE `Project_File_Parser` SHALL use it directly as the intermediate absolute path.
3. THE `Project_File_Parser` SHALL normalize the intermediate absolute path to a canonical form (no `.` or `..` segments).
4. WHEN the normalized path points directly to a file named `mist.toml`, THE `Project_File_Parser` SHALL use that file as the `Resolved_Path`.
5. WHEN the normalized path points to a directory, THE `Project_File_Parser` SHALL append `mist.toml` to that directory path to produce the `Resolved_Path`.
6. WHEN the normalized path points to a directory that does not contain a `mist.toml` file, THE `Compiler_Driver` SHALL report a `MIST_PROJECT_REFERENCE_NO_PROJECT_FILE` error diagnostic and abort compilation.
7. WHEN the normalized path does not point to an existing file or directory, THE `Compiler_Driver` SHALL report a `MIST_PROJECT_REFERENCE_NOT_FOUND` error diagnostic and abort compilation.
8. WHEN the normalized path points to an existing file that is not named `mist.toml`, THE `Compiler_Driver` SHALL report a `MIST_PROJECT_REFERENCE_NOT_FOUND` error diagnostic and abort compilation.

---

### Requirement 3: Parse Referenced Project Files

**User Story:** As a Mist developer, I want the compiler to automatically read and validate each referenced project's `mist.toml`, so that I get clear errors when a referenced project is misconfigured.

#### Acceptance Criteria

1. WHEN resolving a `Project_Reference`, THE `Compiler_Driver` SHALL parse the Referenced_Project's `mist.toml` using the `Project_File_Parser`.
2. WHEN the Referenced_Project's `mist.toml` is invalid, THE `Compiler_Driver` SHALL propagate the resulting error diagnostics and abort compilation.
3. THE `Compiler_Driver` SHALL parse each Referenced_Project's `mist.toml` exactly once per compilation, even if the same project is referenced multiple times.

---

### Requirement 4: Detect Circular References

**User Story:** As a Mist developer, I want the compiler to detect circular project references, so that I get a clear error instead of an infinite loop or stack overflow.

#### Acceptance Criteria

1. WHEN a `Circular_Reference` is detected during reference resolution, THE `Compiler_Driver` SHALL report a `MIST_PROJECT_CIRCULAR_REFERENCE` error diagnostic that names the cycle.
2. WHEN a `Circular_Reference` is detected, THE `Compiler_Driver` SHALL abort compilation without processing any further references in the cycle.
3. THE `Compiler_Driver` SHALL detect both direct circular references (A → A) and transitive circular references (A → B → A).

---

### Requirement 5: Compile Referenced Project Sources

**User Story:** As a Mist developer, I want the types and namespaces from referenced projects to be available in my project, so that I can use them without copying source files.

#### Acceptance Criteria

1. WHEN a Referenced_Project is successfully resolved, THE `Compiler_Driver` SHALL load all `.mist` source files from the Referenced_Project's folder as `Compilation_Unit`s and recompile them from source.
2. THE `Compiler_Driver` SHALL set each Referenced_Project `Compilation_Unit`'s `NamespaceRoot` to the Referenced_Project's configured namespace root.
3. THE `Compiler_Driver` SHALL set each Referenced_Project `Compilation_Unit`'s `ProjectFolder` to the directory containing the Referenced_Project's `Resolved_Path`.
4. THE `Compiler_Driver` SHALL include Referenced_Project `Compilation_Unit`s in the compilation before the Referencing_Project's own source units.
5. WHEN multiple levels of references exist (A references B, B references C), THE `Compiler_Driver` SHALL load all transitively referenced projects' sources in dependency order (deepest dependency first).
6. THE `Compiler_Driver` SHALL NOT load, under criterion 1, any file inside the Referenced_Project's default output directory (`out`) or default bin directory (`bin`) directly beneath its own folder. Containment SHALL be decided on normalized full paths and whole directory segments against that folder: a folder named `out` or `bin` deeper in the project, and a sibling such as `out-other`, still hold sources. The Referencing_Project's `--output` and `--bin` do not apply to a Referenced_Project.

> **Amendment note (2026-09-27).** Criterion 1 said "all `.mist` source files", and the resolver
> implemented it literally. Lex mode writes a reconstructed `.mist` beside each token dump in
> `<folder>/out/lexer/`, and building a project that references the lexed one does not clean the
> referenced project's output, so every type compiled twice (`MIST_PM_DUPLICATE_TYPE`). Criterion 6
> is a clarification added with that fix; it was not part of the original design. It differs on
> purpose from Source_Discovery for the project being built (`project-based-build-model`
> Requirement 2.4–2.5), which excludes the current invocation's effective output and bin directories.
> A reference records no output location, so only the defaults of `project-based-build-model`
> Requirement 3 are excluded: `.mist` files that an invocation of the referenced project wrote under a
> custom `--output` or `--bin` inside its folder are still loaded.

7. THE `Compiler_Driver` SHALL NOT load, under criterion 1, any file inside a directory of the Referenced_Project's folder that holds a file named `.mist-generated` (`project-based-build-model` Requirement 8.6). Containment SHALL be decided as in criterion 6.

> **Amendment note (2026-09-28).** Criterion 7 narrows the limit criterion 6's note records. Lex mode
> now marks the directory it writes reconstructed sources into, so lex output under a custom `--output`
> inside the Referenced_Project's folder is no longer loaded, whichever invocation wrote it. Lex output
> written before the marker existed carries none, and is still loaded unless it lies in the default
> `out` or `bin`.

---

### Requirement 6: Transitive Reference Visibility

**User Story:** As a Mist developer, I want transitive references to be compiled but not automatically visible to my project, so that my project's dependencies are explicit and I am not surprised by types appearing from projects I did not directly reference.

#### Acceptance Criteria

1. WHEN project A references project B and project B references project C, THE `Compiler_Driver` SHALL compile C's sources so that B can use C's types.
2. WHEN project A references project B and project B references project C, THE `Compiler_Driver` SHALL NOT make C's types directly available to A unless A also declares an explicit `[[references]]` entry for C.
3. WHEN project B exposes a type from C in its public API and project A references only B, THE `Compiler_Driver` SHALL report a diagnostic requiring A to add an explicit reference to C in order to use that type.
4. THE `Compiler_Driver` SHALL apply the same transitive visibility rules at all depths of the reference graph.

---

### Requirement 7: Diagnostic Quality for Reference Errors

**User Story:** As a Mist developer, I want clear, actionable error messages when project references are misconfigured, so that I can fix problems quickly.

#### Acceptance Criteria

1. WHEN reporting a `MIST_PROJECT_REFERENCE_NOT_FOUND` diagnostic, THE `Compiler_Driver` SHALL include the normalized path in the message.
2. WHEN reporting a `MIST_PROJECT_CIRCULAR_REFERENCE` diagnostic, THE `Compiler_Driver` SHALL include the full reference chain (e.g. `A → B → A`) in the message.
3. WHEN reporting a `MIST_PROJECT_REFERENCE_MISSING_PATH` or `MIST_PROJECT_REFERENCE_EMPTY_PATH` diagnostic, THE `Project_File_Parser` SHALL include the path to the `mist.toml` file in the message.
4. THE `Compiler_Driver` SHALL report all resolvable reference errors before aborting, rather than stopping at the first error.

---

### Requirement 8: mist.toml Format — Round-Trip Fidelity

**User Story:** As a tooling author, I want the `mist.toml` format for references to be well-defined and parseable, so that I can build tools that read and write project files reliably.

#### Acceptance Criteria

1. THE `Project_File_Parser` SHALL parse a `mist.toml` containing a `[[references]]` array with one or more `path` entries into a `Project_Configuration` with a matching list of `Project_Reference` entries.
2. FOR ALL valid `Project_Configuration` objects containing references, serializing to TOML and then parsing SHALL produce an equivalent `Project_Configuration` (round-trip property).
3. THE `Project_File_Parser` SHALL treat unknown fields within a `[[references]]` entry as ignored (forward-compatible parsing).
