# Requirements Document

## Introduction

Top-level statements allow Mist developers to write executable code at file scope without wrapping it in a class and `static void Main` method. This feature reduces boilerplate for simple programs, scripts, and learning scenarios while maintaining strict entry point disambiguation rules that go beyond C#'s approach. Unlike C#, Mist treats any entry point ambiguity as a compile error unless the developer explicitly resolves it via the `entry_point` field in `mist.toml`.

The `entry_point` field serves as a universal disambiguation mechanism for two cases:
- **Case A (TLS + Main):** A project has both a top-level statement file and one or more `static void Main` methods. The developer must set `entry_point = "top-level"` — you cannot have a TLS that isn't the entry point.
- **Case B (Multiple Mains):** A project has multiple classes with `static void Main` and no TLS. The developer must set `entry_point` to the fully qualified class name of the desired entry point.

## Glossary

- **Top_Level_Statement_File**: A `.mist` source file that contains executable statements (expressions, variable declarations, control flow, etc.) at file scope — outside of any class, struct, or method declaration.
- **Entry_Point**: The method or top-level statement file that the runtime invokes when the compiled program starts.
- **Compilation_Unit**: The set of all source files compiled together as a single project, as defined by a `mist.toml` project file and its recursive file discovery.
- **Compiler**: The Mist compiler pipeline that processes source files from lexing through code generation.
- **Project_Configuration**: The `mist.toml` file that defines project settings including name, namespace root, references, and entry point disambiguation.
- **Entry_Point_Specification**: An optional field (`entry_point`) in `mist.toml` that explicitly designates which entry point to use when ambiguity exists.
- **Static_Main_Method**: A method with the signature `static void Main(string[] args)` or `static void Main()` declared within a class, serving as a traditional entry point.
- **Pretty_Printer**: The Mist pretty printer that formats syntax trees back into source text.
- **Sample_Application**: A self-contained Mist project under the `samples/` directory that demonstrates a language feature end-to-end.
- **Book**: The Mist language reference book (mdBook) that documents language features with examples.

## Requirements

### Requirement 1: Top-Level Statement Parsing

**User Story:** As a Mist developer, I want to write executable statements directly at file scope, so that I can create simple programs without class/method boilerplate.

#### Acceptance Criteria

1. WHEN a `.mist` file contains statements at file scope (outside any type declaration), THE Compiler SHALL parse those statements as top-level statements belonging to that file.
2. THE Compiler SHALL allow the following constructs as top-level statements: variable declarations, expression statements, if statements, while statements, for statements, foreach statements, return statements, break statements, and local function declarations (see `.kiro/specs/local-functions`, which added them on 2026-09-11).
3. WHEN a top-level statement file contains `using` declarations, THE Compiler SHALL process them before the top-level statements, following the same rules as `using` declarations in non-top-level files.
4. WHEN a top-level statement file contains type declarations (classes, structs, enums, interfaces, traits) after the top-level statements, THE Compiler SHALL parse those type declarations as normal declarations belonging to the file's namespace (inferred or explicit). They are not file-local — they are visible to other files in the same namespace.
5. WHEN a top-level statement file contains a file-scoped namespace declaration (e.g., `namespace Foo;`), THE Compiler SHALL apply that namespace to the type declarations in the file. The top-level statements themselves are not placed in a namespace (they become the entry point).
6. WHEN a top-level statement file contains a block namespace declaration (e.g., `namespace Foo { ... }`), THE Compiler SHALL report a compile error because block namespace declarations cannot coexist with top-level statements.

### Requirement 2: Single Top-Level Statement File Constraint

**User Story:** As a Mist developer, I want the compiler to enforce that only one file in my project contains top-level statements, so that the entry point is unambiguous.

#### Acceptance Criteria

1. WHEN a Compilation_Unit contains more than one Top_Level_Statement_File, THE Compiler SHALL report a compile error identifying all files that contain top-level statements.
2. THE Compiler SHALL report the error with a diagnostic message that lists the paths of all conflicting top-level statement files.

### Requirement 3: Entry Point Conflict Detection

**User Story:** As a Mist developer, I want the compiler to reject programs that have ambiguous entry points, so that there is never uncertainty about which code runs at startup.

#### Acceptance Criteria

1. WHEN a Compilation_Unit contains both a Top_Level_Statement_File and one or more Static_Main_Methods, AND no Entry_Point_Specification exists in the Project_Configuration, THE Compiler SHALL report a compile error.
2. WHEN a Compilation_Unit contains multiple classes with Static_Main_Methods (and no Top_Level_Statement_File), AND no Entry_Point_Specification exists in the Project_Configuration, THE Compiler SHALL report a compile error.
3. THE Compiler SHALL include in the error diagnostic the paths/names of all conflicting entry point candidates (top-level statement file path and/or fully qualified class names containing Static_Main_Methods).
4. THE Compiler SHALL suggest adding an `entry_point` field to `mist.toml` to resolve the conflict.

### Requirement 4: Entry Point Configuration in mist.toml

**User Story:** As a Mist developer, I want to specify which entry point to use in `mist.toml`, so that I can resolve entry point ambiguity when it exists.

#### Acceptance Criteria

1. WHEN the Project_Configuration contains an `entry_point` field with a value of `"top-level"`, THE Compiler SHALL use the Top_Level_Statement_File as the program entry point. All Static_Main_Methods in the project are compiled as normal static methods (callable by other code) but are not treated as entry points.
2. WHEN the Project_Configuration contains an `entry_point` field with a fully qualified class name (e.g., `"MyNamespace.Program"`), THE Compiler SHALL use the Static_Main_Method in that class as the program entry point. If a Top_Level_Statement_File exists, this is a compile error — a TLS must always be the entry point when present (see criterion 7).
3. IF the `entry_point` field specifies `"top-level"` but no Top_Level_Statement_File exists in the Compilation_Unit, THEN THE Compiler SHALL report a compile error stating that no top-level statement file was found.
4. IF the `entry_point` field specifies a class name but no matching class with a Static_Main_Method exists in the Compilation_Unit, THEN THE Compiler SHALL report a compile error stating that the specified class does not contain a valid entry point method.
5. WHEN only a Top_Level_Statement_File exists (no Static_Main_Method anywhere), THE Compiler SHALL use it as the entry point without requiring an `entry_point` field in the Project_Configuration.
6. WHEN only a single Static_Main_Method exists (no Top_Level_Statement_File, no other Main methods), THE Compiler SHALL use it as the entry point without requiring an `entry_point` field in the Project_Configuration.
7. WHEN a Top_Level_Statement_File exists in the project, the `entry_point` field SHALL only accept the value `"top-level"`. Specifying a class name when a TLS exists is a compile error — a top-level statement file cannot be present in a project without being the entry point.

### Requirement 5: Code Generation for Top-Level Statements

**User Story:** As a Mist developer, I want top-level statements to be transpiled into valid C# that the .NET runtime can execute, so that my programs work correctly.

#### Acceptance Criteria

1. WHEN the Compiler emits C# for a Top_Level_Statement_File, THE Compiler SHALL emit the statements as C# top-level statements (without wrapping in a class or Main method).
2. WHEN the Top_Level_Statement_File contains type declarations after the top-level statements, THE Compiler SHALL emit those types in the same generated C# file, after the top-level statements, within their appropriate namespace.
3. THE Compiler SHALL emit any `using` declarations from the Top_Level_Statement_File at the top of the generated C# file.
4. THE Compiler SHALL NOT emit a namespace declaration wrapping the top-level statements themselves (C# top-level statements cannot be inside a namespace). Type declarations in the file receive their inferred or explicit namespace as a separate declaration block after the statements.
5. WHEN a Static_Main_Method exists but is not the entry point (because `entry_point = "top-level"`), THE Compiler SHALL emit it as a normal static method in its containing class — it is compiled and callable but not the program's start point.

### Requirement 6: Top-Level Statement File with args Access

**User Story:** As a Mist developer, I want to access command-line arguments from top-level statements, so that I can write command-line tools without boilerplate.

#### Acceptance Criteria

1. THE Compiler SHALL make a variable named `args` of type `string[]` implicitly available within a Top_Level_Statement_File, representing the command-line arguments passed to the program.
2. WHEN a top-level statement references `args`, THE Compiler SHALL resolve it to the implicit command-line arguments parameter without requiring an explicit declaration.

### Requirement 7: Return Statements in Top-Level Code

**User Story:** As a Mist developer, I want to use return statements in top-level code to set the process exit code, so that I can write scripts that communicate status to the calling process.

#### Acceptance Criteria

1. WHEN a Top_Level_Statement_File contains a `return` statement with an integer expression, THE Compiler SHALL treat it as setting the process exit code.
2. WHEN a Top_Level_Statement_File contains a `return` statement without a value, THE Compiler SHALL treat it as an early exit with exit code 0.
3. IF a Top_Level_Statement_File contains a `return` statement with a non-integer expression, THEN THE Compiler SHALL report a type error.

### Requirement 8: Top-Level Statement Pretty Printing

**User Story:** As a Mist developer, I want the pretty printer to correctly round-trip top-level statement files, so that tooling preserves my source code structure.

#### Acceptance Criteria

1. THE Pretty_Printer SHALL format Top_Level_Statement_Files by printing statements at file scope without wrapping them in a class or method.
2. FOR ALL valid Top_Level_Statement_Files, parsing then pretty-printing then parsing SHALL produce an equivalent syntax tree (round-trip property).

### Requirement 9: Sample Application

**User Story:** As a Mist developer exploring the language, I want a working sample application that demonstrates top-level statements, so that I can see the feature in action and use it as a reference.

#### Acceptance Criteria

1. A sample project SHALL exist at `samples/TopLevelStatements/Program.mist` containing a Top_Level_Statement_File as its entry point.
2. The sample SHALL demonstrate the following permutation axes:
   - Variable declarations at file scope (explicit type and `var` inference)
   - Expression statements (method calls, string interpolation)
   - Control flow at file scope (`if`, `for`, `while`, `foreach`)
   - The implicit `args` variable (reading command-line arguments)
   - A `return` statement with an integer exit code
   - A `return` statement without a value (early exit)
   - Type declarations (class, struct) coexisting after top-level statements
   - A file-scoped namespace declaration coexisting with top-level statements
   - Using declarations before top-level statements
3. Each demonstrated permutation SHALL print labeled output to stdout so correctness is observable.
4. The sample SHALL also contain a class with a `public static void RunDemo()` method and a `static void Main(string[] args)` method, with `entry_point = "top-level"` in its `mist.toml`, demonstrating the disambiguation mechanism itself.
5. `samples/AllFeatures/Program.mist` SHALL call `TopLevelStatements.Program.RunDemo()` to include the feature in the full-suite sample run.
6. The sample SHALL compile and run without errors using the Mist compiler.
7. WHEN compiled and run, THE Sample_Application SHALL produce output that proves the top-level statements executed correctly (output would differ or compilation would fail if the feature were removed).

### Requirement 10: Book Documentation

**User Story:** As a Mist developer learning the language, I want documentation in the language book that explains top-level statements, so that I understand the syntax, rules, and entry point disambiguation behavior.

#### Acceptance Criteria

1. THE Book SHALL contain a chapter on top-level statements in the "Getting Started" section, explaining how to write programs without class/Main boilerplate.
2. THE Book SHALL include a Mist source example and its emitted C# equivalent showing a top-level statement program.
3. THE Book SHALL document the single top-level statement file constraint (only one per project).
4. THE Book SHALL document the entry point conflict rules: having both a top-level statement file and a static void Main without `mist.toml` disambiguation is a compile error, and having multiple Main methods without disambiguation is also a compile error.
5. THE Book SHALL document the `entry_point` field in `mist.toml` with examples of both `"top-level"` and class name values, and explain that a TLS must always be the entry point when present.
6. THE Book SHALL document the implicit `args` variable and `return` statement behavior for exit codes.
7. THE Book SHALL document that file-scoped namespace declarations are allowed in TLS files (applying to type declarations but not the statements themselves).
