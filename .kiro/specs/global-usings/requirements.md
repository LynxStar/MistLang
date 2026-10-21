# Requirements Document

## Introduction

This feature adds **global usings** to the Mist programming language. Unlike C# where global usings are declared in source files, Mist global usings are configured in the `mist.toml` project file under a dedicated `[global_usings]` section. A developer lists namespaces that should be implicitly available to all source files within that compilation unit without requiring explicit `using` declarations in each file.

Critically, global usings are scoped to the declaring project only. When project A references project B, project A does NOT inherit project B's global usings. Each project's global usings apply exclusively to its own source files, preventing implicit namespace leakage across project boundaries.

## Non-Goals

- **Source-file-based global usings**: Unlike C#, Mist does not support a `global using` statement in `.mist` source files. All global usings are declared in `mist.toml`.
- **Transitive global using inheritance**: Global usings from referenced projects are never inherited. This is by design to keep namespace visibility explicit at project boundaries.
- **Wildcard or pattern-based imports**: Only fully qualified namespace names are supported. Glob patterns or partial matches are not supported in this version.

## Glossary

- **Project_File_Parser**: The `ProjectFileParser` component that reads and validates `mist.toml` files.
- **Project_Configuration**: The `ProjectConfiguration` record representing the parsed `mist.toml` fields in memory.
- **Compiler_Driver**: The `CompilerDriver` component that orchestrates the full compilation pipeline.
- **Semantic_Model_Builder**: The `SemanticModelBuilder` component that constructs the semantic model from parsed ASTs, registering types into scopes and resolving symbols.
- **CSharp_Emitter**: The `CSharpEmitter` and `CSharpSourceWriter` components that produce C# source files from the semantic model.
- **Global_Using**: A namespace listed in the `[global_usings]` section of `mist.toml` that is implicitly imported into every source file within that project.
- **Compilation_Unit**: A single Mist source file passed through the compilation pipeline.
- **Using_Declaration**: A `using X.Y.Z;` statement in a source file that brings all types from namespace `X.Y.Z` into scope for that file.
- **Namespace**: A dot-separated identifier (e.g., `System`, `MyProject.Models`) that groups related types.
- **Referenced_Project**: A Mist project listed in the `[[references]]` array of the current project's `mist.toml`.

---

## Requirements

### Requirement 1: Declare Global Usings in mist.toml

**User Story:** As a Mist developer, I want to declare a list of namespaces in my `mist.toml` that are automatically imported into every source file in my project, so that I can avoid repetitive `using` declarations across files.

#### Acceptance Criteria

1. THE Project_File_Parser SHALL accept an optional `[global_usings]` section in `mist.toml`.
2. WHEN a `[global_usings]` section is present and contains a `namespaces` field, THE Project_File_Parser SHALL require that `namespaces` is an array of strings.
3. WHEN the `namespaces` array contains one or more entries, THE Project_File_Parser SHALL parse each entry as a fully qualified namespace name (a dot-separated sequence of identifiers as defined in the Glossary).
4. WHEN the `namespaces` array is empty, THE Project_File_Parser SHALL produce a Project_Configuration with an empty global usings list.
5. WHEN no `[global_usings]` section is present in `mist.toml`, THE Project_File_Parser SHALL produce a Project_Configuration with an empty global usings list.
6. THE Project_Configuration SHALL expose a `GlobalUsings` field containing the list of Global_Using namespace strings parsed from `mist.toml`, preserving the declaration order from the `namespaces` array.
7. IF a `[global_usings]` section is present but does not contain a `namespaces` field, THEN THE Project_File_Parser SHALL report a `MIST_GLOBAL_USING_MISSING_NAMESPACES` error diagnostic and return `null`.

---

### Requirement 2: Validate Global Using Entries

**User Story:** As a Mist developer, I want clear error messages when I misconfigure global usings, so that I can fix problems quickly.

#### Acceptance Criteria

1. WHEN a `namespaces` entry is empty or whitespace-only, THE Project_File_Parser SHALL report a `MIST_GLOBAL_USING_EMPTY_NAMESPACE` error diagnostic and return `null`.
2. WHEN a `namespaces` entry contains invalid characters (characters other than alphanumeric, underscores, and dots), THE Project_File_Parser SHALL report a `MIST_GLOBAL_USING_INVALID_NAMESPACE` error diagnostic listing the invalid characters and return `null`.
3. WHEN the `namespaces` field is not an array, THE Project_File_Parser SHALL report a `MIST_GLOBAL_USING_INVALID_FORMAT` error diagnostic and return `null`.
4. WHEN the `namespaces` array contains a non-string element, THE Project_File_Parser SHALL report a `MIST_GLOBAL_USING_INVALID_FORMAT` error diagnostic identifying the element index and return `null`.
5. WHEN the `namespaces` array contains duplicate entries (compared using case-sensitive ordinal equality), THE Project_File_Parser SHALL report a `MIST_GLOBAL_USING_DUPLICATE_NAMESPACE` warning diagnostic identifying the duplicate and continue parsing with the duplicate removed, regardless of whether other validation errors are also present.
6. WHEN reporting any global using diagnostic, THE Project_File_Parser SHALL include the path to the `mist.toml` file in the message.

---

### Requirement 3: Global Usings Flow Through Compilation Pipeline

**User Story:** As a compiler developer, I want the parsed global usings to be available throughout the compilation pipeline, so that the semantic model builder can apply them during name resolution.

#### Acceptance Criteria

1. THE Project_Configuration SHALL include a `GlobalUsings` field containing an immutable list of namespace strings parsed from the `[global_usings]` section.
2. IF the `[global_usings]` section is absent from the project file, THEN THE Project_Configuration SHALL set the `GlobalUsings` field to an empty list.
3. THE Compiler_Driver SHALL pass the `GlobalUsings` list to the Semantic_Model_Builder as a parameter during semantic model construction.
4. THE Compiler_Driver SHALL pass global usings only for the project's own Compilation_Units, not for Compilation_Units loaded from Referenced_Projects.

---

### Requirement 4: Semantic Resolution of Global Usings

**User Story:** As a Mist developer, I want types from globally-used namespaces to be resolvable by simple name in any source file, so that global usings behave like an implicit `using` at the top of every file.

#### Acceptance Criteria

1. WHEN resolving a type name in a Compilation_Unit, THE Semantic_Model_Builder SHALL treat each Global_Using namespace as if a Using_Declaration for that namespace appeared at the top of the file.
2. WHEN a source file contains an explicit Using_Declaration for a namespace that is also a Global_Using, THE Semantic_Model_Builder SHALL accept the redundant declaration without producing any diagnostic for the redundancy, while continuing to report unrelated diagnostics normally.
3. WHEN a Global_Using namespace does not exist in the semantic model (neither as a user-defined namespace nor as an ambient namespace), THE Semantic_Model_Builder SHALL report a `MIST_GLOBAL_USING_UNDEFINED_NAMESPACE` warning diagnostic.
4. WHEN multiple Global_Using namespaces contain types with the same simple name and that name is referenced without qualification, THE Semantic_Model_Builder SHALL report a `MIST_AMBIGUOUS_TYPE` error diagnostic consistent with the behavior for explicit Using_Declarations.
5. WHEN both a Global_Using and an explicit Using_Declaration in the same file provide a type with the same simple name and that name is referenced without qualification, THE Semantic_Model_Builder SHALL report a `MIST_AMBIGUOUS_TYPE` error diagnostic, treating Global_Usings with the same priority as explicit Using_Declarations.

---

### Requirement 5: Global Usings Are Scoped to the Declaring Project

**User Story:** As a Mist developer working with multi-project solutions, I want each project's global usings to apply only to its own source files, so that referenced projects do not pollute my namespace visibility.

#### Acceptance Criteria

1. WHEN project A references project B and project B declares Global_Usings, THE Compiler_Driver SHALL NOT apply project B's Global_Usings to project A's Compilation_Units.
2. WHEN project A references project B and project A declares Global_Usings, THE Compiler_Driver SHALL NOT apply project A's Global_Usings to project B's Compilation_Units.
3. WHEN compiling a Referenced_Project's source files, THE Compiler_Driver SHALL apply only that Referenced_Project's own Global_Usings (parsed from its own `mist.toml`) to its Compilation_Units.
4. WHEN project A transitively references project C through project B (A→B→C), THE Compiler_Driver SHALL NOT apply project C's Global_Usings to project A's or project B's Compilation_Units.
5. WHEN the Compiler_Driver passes Global_Usings to the Semantic_Model_Builder for a given project's Compilation_Units, THE Compiler_Driver SHALL pass only the Global_Usings parsed from that project's own `mist.toml`.

---

### Requirement 6: C# Emitter Output for Global Usings

**User Story:** As a Mist developer, I want the generated C# code to include the correct `using` directives for globally-used namespaces, so that the transpiled output compiles correctly.

Emitted using selection is separate from source using resolution. Requirement 5 governs how Mist resolves names through a Global_Using; nothing here changes that. The emitter writes only the directives C# can name. A namespace whose Mist declarations all erase (aliases, `with` declarations, intrinsics, a variadic family written as top-level monomorphs) is one a Mist file may import but no C# namespace corresponds to, so its directive is omitted. Every type the emitter writes is spelled in full, so the omission loses nothing. C# has a namespace when an emitted declaration or extension block sits in it, when a referenced CLR assembly declares it, or when it encloses either. The selection is decided over the whole program model before any file is written (`UsingSelection`).

#### Acceptance Criteria

1. WHEN a Compilation_Unit has Global_Usings applied, THE CSharp_Emitter SHALL emit a `using` directive in the generated C# file for each Global_Using namespace that C# has, and SHALL omit one naming a namespace whose declarations all erase.
2. THE CSharp_Emitter SHALL emit the selected `using` directives (from Global_Usings and explicit Using_Declarations) as a single merged block, sorted in ordinal alphabetical order. It SHALL NOT add a directive for its own sake.
3. WHEN a source file contains an explicit Using_Declaration for a namespace that is also a Global_Using, THE CSharp_Emitter SHALL emit the `using` directive only once (deduplicated by case-sensitive string equality).
4. WHEN a Global_Using names a namespace that has no C# counterpart, THE CSharp_Emitter SHALL omit its directive without reporting a diagnostic, and the Global_Using SHALL still take part in source name resolution as Requirement 5 states.

---

### Requirement 7: mist.toml Format — Round-Trip Fidelity

**User Story:** As a tooling author, I want the `mist.toml` format for global usings to be well-defined and parseable, so that I can build tools that read and write project files reliably.

#### Acceptance Criteria

1. THE Project_File_Parser SHALL parse a `mist.toml` containing a `[global_usings]` section with a `namespaces` array into a Project_Configuration with a Global_Using list that preserves the order and values of the entries in the `namespaces` array.
2. WHEN a valid Project_Configuration containing global usings is serialized to TOML (producing a `[global_usings]` section with a `namespaces` array of strings) and then parsed back, THE Project_File_Parser SHALL produce a Project_Configuration where the Name, NamespaceRoot, References, and GlobalUsings fields are structurally equal to the original (order-sensitive for GlobalUsings).
3. THE Project_File_Parser SHALL silently discard unknown fields within the `[global_usings]` section without reporting a diagnostic, producing a valid Project_Configuration from the recognized fields only.
4. THE Project_Configuration SHALL support serialization to TOML by writing the GlobalUsings list as a `namespaces` array of strings under a `[global_usings]` section, preserving the order of entries. Serialization SHALL only be performed on valid Project_Configuration objects (those that were successfully parsed or constructed without errors).

---

### Requirement 8: Book Documentation

**User Story:** As a Mist developer reading the language book, I want documentation explaining global usings so that I can learn how to configure and use them.

#### Acceptance Criteria

1. THE `book/src/tooling/project-configuration.md` chapter SHALL include a `## The [global_usings] Section` heading documenting the TOML format, the `namespaces` field, valid entry syntax, and error behavior.
2. THE project configuration chapter's Fields table SHALL include a row for `[global_usings]` with its type and description.
3. THE `book/src/basics/namespaces.md` chapter SHALL include a `## Global Usings` section explaining how global usings work from a language usage perspective.
4. THE namespaces chapter's Global Usings section SHALL explain that global usings are declared in `mist.toml` (not in source files), that they behave identically to explicit `using` declarations, and that they are scoped to the declaring project only.
5. THE namespaces chapter's Global Usings section SHALL include a code example showing a `mist.toml` with `[global_usings]` and a `.mist` file using types from a globally-used namespace without an explicit `using` declaration.
6. THE `book/src/SUMMARY.md` SHALL NOT require changes since global usings are documented within existing chapters rather than as a new chapter.

---

### Requirement 9: Sample Application

**User Story:** As a Mist developer, I want a comprehensive sample application demonstrating global usings, so that I can see how they work in practice and verify correctness.

#### Acceptance Criteria

1. A sample project SHALL exist at `samples/GlobalUsings/Program.mist`.
2. The sample project's `mist.toml` SHALL declare a `[global_usings]` section with at least two namespaces.
3. The sample SHALL include source files that define types in at least two distinct namespaces so the global usings feature can be exercised.
4. The sample SHALL demonstrate using types from each globally-used namespace without explicit `using` declarations in the main source file.
5. The sample SHALL demonstrate that an explicit `using` declaration for a globally-used namespace is accepted without error (redundant using).
6. The sample SHALL demonstrate types from multiple globally-used namespaces being used in the same file.
7. Each demonstrated permutation SHALL print labeled output to stdout so correctness is observable.
8. `samples/AllFeatures/Program.mist` SHALL call `GlobalUsings.Program.RunDemo()` to include the feature in the full-suite sample run.
9. The sample SHALL compile and run without errors using the Mist compiler.
