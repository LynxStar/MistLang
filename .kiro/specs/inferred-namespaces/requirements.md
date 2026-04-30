# Requirements Document

## Introduction

The Mist programming language currently supports a single optional namespace declaration per file, using either file-scoped (`namespace X;`) or block (`namespace X { }`) syntax. Files without a namespace declaration place their types in the global scope. This feature introduces **inferred namespaces**, where every file automatically belongs to a namespace derived from its folder path relative to the project root, following C#-style dot-separated naming. It also extends the grammar to support **multiple namespace declarations per file**, adds a configurable `namespace_root` field to `mist.toml`, enables **C#-style same-namespace scoping** (types in the same namespace can reference each other without `using`), and ensures `using` statements correctly resolve types from other namespaces. A new sample project demonstrates all namespace styles and interactions.

## Glossary

- **Compiler_Driver**: The `CompilerDriver` component that orchestrates the full compilation pipeline (lex, parse, semantic model, analyzers, backend emit).
- **Semantic_Model_Builder**: The `SemanticModelBuilder` component that constructs the semantic model from parsed ASTs, registering types into scopes and resolving symbols.
- **CSharp_Emitter**: The `CSharpEmitter` and `CSharpSourceWriter` components that walk the semantic model and ASTs to produce C# source files.
- **Project_File_Parser**: The `ProjectFileParser` component that reads and validates `mist.toml`.
- **Project_Configuration**: The `ProjectConfiguration` record representing parsed `mist.toml` fields.
- **Compiler_Options**: The `CompilerOptions` record carrying all options for a compilation run.
- **Namespace_Root**: The root segment of all inferred namespaces for a project. Configured via the `namespace_root` field in `mist.toml`, defaulting to the project `name` field when not specified.
- **Inferred_Namespace**: A namespace automatically assigned to types in a file that has no explicit `namespace` declaration. Computed as `{Namespace_Root}` for files at the project root, or `{Namespace_Root}.{FolderSegments}` for files in subdirectories, where folder path separators are replaced with dots.
- **Explicit_Namespace**: A namespace declared in source code using the `namespace` keyword, in either file-scoped or block notation.
- **File_Scoped_Namespace**: A namespace declared with `namespace X;` syntax, where all subsequent type declarations (until another namespace declaration or end of file) belong to namespace X.
- **Block_Namespace**: A namespace declared with `namespace X { }` syntax, where only type declarations inside the braces belong to namespace X.
- **Using_Declaration**: A `using X.Y.Z;` statement that brings all types from namespace `X.Y.Z` into scope for the current file.
- **Same_Namespace_Scoping**: The rule that types within the same namespace can reference each other by simple name without requiring a `using` declaration.
- **Grammar**: The `Mist.storm` parser grammar defining the syntactic structure of Mist source files.
- **Compilation_Unit**: A single Mist source file processed through the compilation pipeline, represented by the `CompilationUnit` record.
- **Qualified_Identifier**: A dot-separated name such as `MyProject.Entities.Player`, used for namespace names and type references.

## Requirements

### Requirement 1: NamespaceRoot Configuration in mist.toml

**User Story:** As a developer, I want to configure the root namespace for my project in `mist.toml`, so that all inferred namespaces use a consistent project-level prefix.

#### Acceptance Criteria

1. THE Project_File_Parser SHALL accept an optional `namespace_root` field of type string in the `mist.toml` file.
2. WHEN the `namespace_root` field is present and non-empty, THE Project_File_Parser SHALL use the `namespace_root` value as the Namespace_Root.
3. WHEN the `namespace_root` field is absent from `mist.toml`, THE Project_File_Parser SHALL default the Namespace_Root to the value of the `name` field.
4. WHEN the `namespace_root` field is present but empty or whitespace-only, THE Project_File_Parser SHALL report an error indicating that `namespace_root` must be non-empty when specified.
5. THE Project_File_Parser SHALL validate that the `namespace_root` value contains only alphanumeric characters, underscores, hyphens, periods, and no path separators.
6. IF the `namespace_root` value contains invalid characters, THEN THE Project_File_Parser SHALL report an error listing the invalid characters found.
7. THE Project_Configuration record SHALL include a `NamespaceRoot` field of type `string` populated from the resolved Namespace_Root value.

### Requirement 2: NamespaceRoot Flows Through Compilation Pipeline

**User Story:** As a compiler developer, I want the resolved Namespace_Root to be available throughout the compilation pipeline, so that the semantic model builder and emitter can compute inferred namespaces.

#### Acceptance Criteria

1. THE Compiler_Options record SHALL include a `NamespaceRoot` field of type `string` populated from the Project_Configuration.
2. THE Compiler_Driver SHALL pass the `NamespaceRoot` value to the Semantic_Model_Builder during semantic model construction.
3. THE Compiler_Driver SHALL pass the `ProjectFolder` path to the Semantic_Model_Builder during semantic model construction.
4. THE Semantic_Model_Builder `Build` method SHALL accept the `NamespaceRoot` and `ProjectFolder` as parameters (or through an extended options object).

### Requirement 3: Inferred Namespace Computation

**User Story:** As a developer, I want files without an explicit namespace declaration to automatically belong to a namespace derived from their folder path, so that my project has a consistent namespace hierarchy without boilerplate.

#### Acceptance Criteria

1. WHEN a source file contains no explicit namespace declaration, THE Semantic_Model_Builder SHALL compute an Inferred_Namespace for the file.
2. WHEN the source file resides in the same directory as `mist.toml`, THE Semantic_Model_Builder SHALL set the Inferred_Namespace to the Namespace_Root value (e.g., `MyProject`).
3. WHEN the source file resides in a subdirectory relative to the project folder, THE Semantic_Model_Builder SHALL set the Inferred_Namespace to `{Namespace_Root}.{FolderSegments}` where each folder name becomes a dot-separated segment (e.g., a file in `Entities/` yields `MyProject.Entities`).
4. WHEN the source file resides in a nested subdirectory, THE Semantic_Model_Builder SHALL include all intermediate folder names as dot-separated segments (e.g., a file in `Models/Game/` yields `MyProject.Models.Game`).
5. THE Semantic_Model_Builder SHALL normalize path separators to dots regardless of the operating system path separator character.
6. THE Semantic_Model_Builder SHALL register all types from a file with no explicit namespace under the computed Inferred_Namespace scope.
7. WHEN multiple files in the same directory have no explicit namespace, THE Semantic_Model_Builder SHALL place all their types in the same Inferred_Namespace scope.

### Requirement 4: Grammar Support for Multiple Namespaces Per File

**User Story:** As a developer, I want to declare multiple namespaces in a single file, so that I can organize related types from different namespaces together when it makes sense.

#### Acceptance Criteria

1. THE Grammar SHALL allow zero or more namespace declarations in a single Compilation_Unit (changing from the current limit of zero or one).
2. THE Grammar SHALL support interleaving namespace declarations with top-level type declarations in a Compilation_Unit.
3. WHEN a File_Scoped_Namespace declaration appears, THE Grammar SHALL associate all subsequent type declarations with that namespace until another namespace declaration or end of file.
4. WHEN a Block_Namespace declaration appears, THE Grammar SHALL associate only the type declarations within the braces with that namespace.
5. THE Grammar SHALL allow a mix of File_Scoped_Namespace and Block_Namespace declarations in the same file.
6. THE Grammar SHALL continue to support files with no namespace declarations.

### Requirement 5: Semantic Handling of Multiple Namespaces Per File

**User Story:** As a compiler developer, I want the semantic model to correctly register types from multiple namespace declarations in a single file, so that each type is placed in its declared namespace scope.

#### Acceptance Criteria

1. WHEN a file contains multiple namespace declarations, THE Semantic_Model_Builder SHALL create or reuse a separate namespace scope for each declared namespace.
2. WHEN a File_Scoped_Namespace declaration shadows a previous namespace, THE Semantic_Model_Builder SHALL register subsequent types under the new namespace until another namespace declaration or end of file.
3. WHEN a Block_Namespace declaration appears, THE Semantic_Model_Builder SHALL register only the types within the block under that namespace.
4. WHEN a file contains both explicit namespace declarations and top-level type declarations outside any namespace, THE Semantic_Model_Builder SHALL register the top-level types under the Inferred_Namespace for that file.
5. WHEN two files declare the same namespace name, THE Semantic_Model_Builder SHALL merge their types into the same namespace scope.

### Requirement 6: Same-Namespace Scoping (Implicit Visibility)

**User Story:** As a developer, I want types in the same namespace to reference each other without `using` statements, so that I get the same convenience as C# namespace scoping.

#### Acceptance Criteria

1. WHEN resolving a type name, THE Semantic_Model_Builder SHALL first search the current namespace scope before searching the global scope.
2. WHEN a type references another type by simple name and both types are in the same namespace, THE Semantic_Model_Builder SHALL resolve the reference without requiring a Using_Declaration.
3. WHEN a type references another type by simple name and the type exists in a different namespace, THE Semantic_Model_Builder SHALL require a Using_Declaration for that namespace to resolve the reference.
4. THE Semantic_Model_Builder SHALL apply same-namespace scoping to both explicitly declared namespaces and inferred namespaces.

### Requirement 7: Using Declaration Namespace Resolution

**User Story:** As a developer, I want `using` statements to bring types from other namespaces into scope, so that I can reference types across namespace boundaries.

#### Acceptance Criteria

1. WHEN a Using_Declaration specifies a namespace name, THE Semantic_Model_Builder SHALL make all types from that namespace available for unqualified reference in the current file.
2. WHEN a Using_Declaration specifies a namespace that does not exist, THE Semantic_Model_Builder SHALL report a diagnostic warning indicating the namespace was not found.
3. WHEN multiple Using_Declarations import namespaces containing types with the same simple name, THE Semantic_Model_Builder SHALL report an ambiguity error when the ambiguous name is referenced without qualification.
4. THE Semantic_Model_Builder SHALL process Using_Declarations before resolving type references in the file.

### Requirement 8: C# Emitter Namespace Output

**User Story:** As a developer, I want the C# emitter to produce correct namespace declarations in the generated C# code, so that the transpiled output compiles and runs correctly.

#### Acceptance Criteria

1. WHEN a type belongs to an Inferred_Namespace, THE CSharp_Emitter SHALL emit a file-scoped namespace declaration (`namespace X.Y.Z;`) in the generated C# file.
2. WHEN a type belongs to an Explicit_Namespace declared with file-scoped syntax, THE CSharp_Emitter SHALL emit a file-scoped namespace declaration in the generated C# file.
3. WHEN a type belongs to an Explicit_Namespace declared with block syntax, THE CSharp_Emitter SHALL emit a block namespace declaration in the generated C# file.
4. WHEN a file contains multiple namespace declarations, THE CSharp_Emitter SHALL emit each namespace with its associated types in the generated C# file.
5. WHEN a file contains types in both an explicit namespace and the inferred namespace, THE CSharp_Emitter SHALL emit both namespaces in the generated C# file.
6. THE CSharp_Emitter SHALL emit `using` directives in the generated C# file for each Using_Declaration in the Mist source.

### Requirement 9: Inferred Namespace for Files with Explicit Namespaces

**User Story:** As a developer, I want explicit namespace declarations to override the inferred namespace for the types they contain, so that I have full control when I need it.

#### Acceptance Criteria

1. WHEN a file contains an explicit namespace declaration, THE Semantic_Model_Builder SHALL place the types within that declaration under the explicit namespace, not the Inferred_Namespace.
2. WHEN a file contains both explicit namespace declarations and top-level types outside any namespace, THE Semantic_Model_Builder SHALL place the top-level types under the Inferred_Namespace.
3. WHEN a file contains only explicit namespace declarations covering all types, THE Semantic_Model_Builder SHALL not create an Inferred_Namespace scope for that file.

### Requirement 10: Sample Project Demonstrating Namespaces

**User Story:** As a developer, I want a sample project that demonstrates all namespace features, so that I can learn how inferred namespaces, explicit namespaces, and `using` statements work together.

#### Acceptance Criteria

1. THE sample project SHALL be located in the `samples/Namespaces` directory with a `mist.toml` file.
2. THE sample project SHALL include at least one file at the project root demonstrating the Inferred_Namespace matching the Namespace_Root.
3. THE sample project SHALL include at least one subdirectory with files demonstrating Inferred_Namespaces derived from folder paths.
4. THE sample project SHALL include at least one file demonstrating a File_Scoped_Namespace declaration.
5. THE sample project SHALL include at least one file demonstrating a Block_Namespace declaration.
6. THE sample project SHALL include at least one file demonstrating multiple namespace declarations (mixing file-scoped and block styles).
7. THE sample project SHALL include at least one file demonstrating Using_Declarations to reference types from other namespaces.
8. THE sample project SHALL include at least one file demonstrating Same_Namespace_Scoping (referencing a type in the same namespace without a `using` statement).
9. THE sample project SHALL compile and run successfully using the Mist compiler.

### Requirement 11: Pretty Printer Namespace Support

**User Story:** As a compiler developer, I want the pretty printer to correctly format files with multiple namespace declarations, so that round-trip formatting preserves the source structure.

#### Acceptance Criteria

1. THE Pretty_Printer SHALL format files containing multiple namespace declarations, preserving each declaration and its associated types.
2. THE Pretty_Printer SHALL format File_Scoped_Namespace declarations as `namespace X.Y.Z;` followed by their type declarations.
3. THE Pretty_Printer SHALL format Block_Namespace declarations as `namespace X.Y.Z { ... }` with their type declarations indented inside the braces.
4. FOR ALL valid Mist source files containing namespace declarations, parsing then pretty-printing then parsing SHALL produce an equivalent AST (round-trip property).

### Requirement 12: Backward Compatibility

**User Story:** As a developer with existing Mist projects, I want the namespace feature to be backward compatible, so that my existing code continues to compile without changes.

#### Acceptance Criteria

1. WHEN an existing project has no `namespace_root` field in `mist.toml`, THE Project_File_Parser SHALL default the Namespace_Root to the project `name` without requiring any changes to the project file.
2. WHEN existing source files contain a single explicit namespace declaration, THE Semantic_Model_Builder SHALL continue to handle the files identically to the current behavior.
3. WHEN existing source files contain no namespace declaration and all files are at the project root, THE Semantic_Model_Builder SHALL place their types under the Inferred_Namespace equal to the Namespace_Root (the project name), which changes the current behavior of placing types in the global scope.
4. IF backward compatibility with global-scope placement is needed, THEN the developer SHALL add explicit namespace declarations or adjust the Namespace_Root configuration.
