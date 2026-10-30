# Project Configuration

Every Mist project is defined by a `mist.toml` file at the project root. This file tells the compiler the project's name, how to derive namespaces, and which other projects and assemblies to include as dependencies.

## Fields

The table below summarizes all available fields:

| Field | Required | Type | Description |
|-------|----------|------|-------------|
| `name` | Yes | String | The project name. Used as the default namespace root and output assembly name. |
| `namespace_root` | No | String | Overrides the root segment for inferred namespaces. Defaults to `name` when omitted. |
| `entry_point` | No | String | Disambiguates the program entry point when multiple candidates exist. |
| `[references]` | No | Table | Declares dependencies on other Mist projects, C# projects and compiled assemblies. Each key is the referenced assembly's name mapping to a table with a `path` field. |
| `[global_usings]` | No | Table | Declares namespaces implicitly imported into every source file in the project. |
| `[diagnostics]` | No | Table | Declares diagnostic policy: which warnings become errors and which optional rules run. |

## The `name` Field

The `name` field is the only required field. It identifies the project and serves as the default namespace root when `namespace_root` is not specified.

**Constraints:**

- Must not be empty or whitespace-only
- Maximum length: 128 characters
- Allowed characters: letters (`a-z`, `A-Z`), digits (`0-9`), hyphens (`-`), underscores (`_`), and periods (`.`)

```toml
name = "MyProject"
```

If the name contains invalid characters, the compiler reports an error listing the offending characters:

```
Project name contains invalid characters: ' ', '@'. Only alphanumeric characters,
hyphens, underscores, and periods are allowed.
```

## The `namespace_root` Field

The optional `namespace_root` field overrides the root segment used when the compiler infers namespaces from directory structure. When omitted, the compiler uses the `name` field as the namespace root.

The same character constraints apply as for `name`: alphanumeric characters, hyphens, underscores, and periods only, with a maximum length of 128 characters.

```toml
name = "my-game-engine"
namespace_root = "GameEngine"
```

In this example, a file at `Models/Player.mist` without an explicit namespace declaration receives the inferred namespace `GameEngine.Models.Player` rather than `my-game-engine.Models.Player`.

## The `entry_point` Field

The optional `entry_point` field disambiguates the program entry point when a project has multiple candidates. You only need this field when there is ambiguity — for example, a project with both a top-level statement file and a `static void Main` method, or multiple classes containing `static void Main`.

**Valid values:**

| Value | Meaning |
|-------|---------|
| `"top-level"` | Use the top-level statement file as the entry point. |
| A fully qualified class name (e.g., `"MyApp.Program"`) | Use the `static void Main` in that class as the entry point. |

**When you don't need it:**

- If your project has exactly one top-level statement file and no `static void Main` methods, the compiler uses the TLS file automatically.
- If your project has exactly one class with `static void Main` and no top-level statements, the compiler uses that class automatically.

**Constraints:**

- Must not be empty or whitespace-only when present
- When a top-level statement file exists, the only valid value is `"top-level"` — a TLS file must always be the entry point when present

```toml
# Use the top-level statement file as the entry point
entry_point = "top-level"
```

```toml
# Use a specific class's Main method as the entry point
entry_point = "MyApp.Program"
```

If the specified entry point cannot be found, the compiler reports an error:

- `entry_point = "top-level"` but no file contains top-level statements: `entry_point = "top-level" specified in mist.toml but no file contains top-level statements.`
- `entry_point = "MyApp.Program"` but no matching class has a `static void Main`: `entry_point specifies 'MyApp.Program' but no class with that name contains a static void Main method.`

For full details on top-level statements and entry point rules, see [Top-Level Statements](../getting-started/top-level-statements.md).

## The `[references]` Table

The `[references]` table declares dependencies on other Mist projects, C# projects and compiled assemblies. Each entry is a key-value pair where the key is the referenced assembly's name and the value is a table containing a `path` field:

| Field | Required | Type | Description |
|-------|----------|------|-------------|
| `path` | Yes | String | Path to the referenced project's directory, or to its `mist.toml`, `.csproj` or `.dll` file. |

There are two equivalent ways to declare references:

**Inline table form:**

```toml
[references]
SharedLib = { path = "../SharedLib" }
DataModels = { path = "../DataModels" }
```

**Expanded sub-table form:**

```toml
[references.SharedLib]
path = "../SharedLib"

[references.DataModels]
path = "../DataModels"
```

Both forms produce identical results. The inline form is more compact for simple references; the expanded form may be preferred when additional fields are added in the future.

### Reference Kinds

An entry may name a Mist project, a C# project or a compiled assembly. The compiler decides which from `path` alone and does not read the file:

| `path` names | Kind | What the compiler does |
|--------------|------|------------------------|
| a `mist.toml` | Mist project | Composes the project's source into this compilation. |
| a `.csproj` | C# project | Builds it with `dotnet build` before compiling any Mist, references its output and copies the DLLs from its output folder into `bin/`. |
| a `.dll` | Assembly | References the file and copies it into `bin/`. Nothing beside it is inferred, so a dependency of the assembly that the program needs is declared as its own reference or package. |

```toml
[references]
"Lib" = { path = "../Lib/mist.toml" }
"Bridge" = { path = "../Bridge/Bridge.csproj" }
"Vendor.Tools" = { path = "../vendor/Vendor.Tools.dll" }
```

A path to a file with any other extension is `MIST0071`, and a `.csproj` or `.dll` that does not exist is `MIST0072`.

**A bare directory.** A `path` that names a directory is valid when that directory's own files hold exactly one `mist.toml`, `.csproj` or `.dll`; that file decides the kind. Subdirectories are never searched, so a project's `bin/`, `obj/` and `out/` never count, and extensions compare without regard to case. A directory with none of the three is `MIST0063`. A directory with two or more, of one kind or several, is `MIST0073`: the message lists every candidate, and the fix is to name the intended file in `path`.

```toml
[references]
"Lib" = { path = "../Lib" }
"Bridge" = { path = "../Bridge" }
```

**The key names the produced assembly.** For every kind the key must equal the assembly the target produces, so a typo or a rename fails at build time. That is the manifest's `name` for a Mist project, the file name without its extension for a `.dll`, and, for a C# project, the file name of its built output, which is MSBuild's `AssemblyName` and need not equal the `.csproj` file's name. A mismatch is `MIST0070`; its message names the kind and the name the target produces.

**Facades.** A C# project that depends on a Mist library was compiled against the library's own assembly, and the runtime resolves a type by assembly and name. The program, however, composes the library's source into its own DLL, so the same type lives in a different assembly and the C# project's methods would not accept it. When a referenced C# assembly was compiled against a Mist library that the program composes, the compiler therefore writes a small assembly named after that library into `bin/`, beside the program's DLL, holding nothing but type forwards to the program. The C# project and the program then agree on what each type is, and a signature that crosses the boundary works. No setting asks for this. The C# project reaches the Mist library through a hand-written MSBuild target that runs `mist build` on it; `samples/CSharpReferences` in the repository shows the whole arrangement.

### Reference Key Naming

The reference key (e.g., `SharedLib` in the examples above) **must match the `name` field of the referenced Mist project's `mist.toml` exactly** (case-sensitive); the other kinds follow the key rule under Reference Kinds. This ensures that dependency declarations are unambiguous and that typos or stale renames are caught at build time.

For example, if `../SharedLib/mist.toml` contains `name = "SharedLib"`, then the reference key must be `SharedLib` — not `sharedlib`, `Shared-Lib`, or any other variation.

The reference key follows the same naming constraints as the `name` field:

- Must not be empty or whitespace-only
- Maximum length: 128 characters
- Allowed characters: letters (`a-z`, `A-Z`), digits (`0-9`), hyphens (`-`), underscores (`_`), and periods (`.`)

If the reference key does not match the target project's `name` field, the compiler reports a `ProjectReferenceNameMismatch` error:

```
Reference key 'WrongName' does not match 'SharedLib', the name the Mist project '/path/to/SharedLib/mist.toml' produces.
```

When a name mismatch is detected, the mismatched reference is excluded from the build and its transitive dependencies are not traversed. The compiler continues resolving the remaining sibling references.

### Path Resolution

The `path` field accepts both relative and absolute paths:

- **Relative paths** are resolved from the directory containing the current `mist.toml`
- **Absolute paths** are used as-is
- If the path points to a **directory**, the compiler uses the one `mist.toml`, `.csproj` or `.dll` that directory holds (i.e., `"../SharedLib"` resolves to `../SharedLib/mist.toml` when that is the only candidate)
- If the path points to a **file**, its extension decides the kind

### Circular Reference Detection

The compiler performs depth-first traversal of the reference graph and detects cycles. If project A references project B, and project B references project A (directly or transitively), the compiler reports an error:

```
Circular project reference detected: /path/to/A/mist.toml → /path/to/B/mist.toml → /path/to/A/mist.toml.
```

Self-references (a project referencing itself) are also detected and reported as circular references.

### Missing References

If a referenced path does not exist, the compiler reports a diagnostic:

- If the directory exists but holds no `mist.toml`, `.csproj` or `.dll`: `MIST0063`
- If the path does not exist at all: `Referenced project path '/path/to/file' does not exist.`

## The `[global_usings]` Section

The optional `[global_usings]` section declares namespaces that are implicitly imported into every source file within the project. This eliminates the need for repetitive `using` declarations across files.

Global usings are scoped to the declaring project only. When project A references project B, project A does **not** inherit project B's global usings. Each project's global usings apply exclusively to its own source files.

| Field | Required | Type | Description |
|-------|----------|------|-------------|
| `namespaces` | Yes (when section present) | Array of strings | The list of namespaces to implicitly import. |

```toml
[global_usings]
namespaces = ["System", "System.Collections.Generic", "MyProject.Models"]
```

### Valid Namespace Syntax

Each entry in the `namespaces` array must be a fully qualified namespace name — a dot-separated sequence of identifiers matching the pattern:

```
^[a-zA-Z_][a-zA-Z0-9_]*(\.[a-zA-Z_][a-zA-Z0-9_]*)*$
```

In plain terms: each segment starts with a letter or underscore, followed by zero or more letters, digits, or underscores. Segments are separated by dots.

Valid examples: `Standard`, `MyProject.Models`, `_Internal.Utils`

### Error Behavior

| Condition | Diagnostic | Severity |
|-----------|-----------|----------|
| Section present but missing `namespaces` field | `MIST0079` | Error |
| Empty or whitespace-only entry | `MIST0080` | Error |
| Entry contains invalid characters | `MIST0081` | Error |
| `namespaces` is not an array | `MIST0077` | Error |
| Non-string element in `namespaces` | `MIST0078` | Error |
| Duplicate entry (case-sensitive) | `MIST0082` | Warning |

All error diagnostics halt compilation. The duplicate warning is non-fatal — the duplicate is removed and parsing continues.

All diagnostics include the path to the `mist.toml` file for orientation.

## The `[diagnostics]` Table

The optional `[diagnostics]` table sets the project's diagnostic policy. Policy can only raise a severity or switch an optional rule off and on; nothing in it can lower a severity or hide a diagnostic, so an invalid program never compiles because of it. The policy applies to the project being compiled; a referenced project's `[diagnostics]` table does not reach the program that references it.

| Key | Type | Meaning |
|-----|------|---------|
| `warnings_as_errors` | Boolean or array of strings | `true` raises every promotable diagnostic, warning or note, to an error. An array raises only the listed diagnostic numbers, and may name notes as well as warnings. |
| `disable` | Array of strings | Optional rules that do not run for this project. |
| `enable` | Array of strings | Optional rules that are off by default and run for this project. |

Every entry is a diagnostic number written as a string, for example `"MIST0148"`. Each diagnostic has one of three policy classes: a *fixed* diagnostic reports a violated contract and no policy touches it; a *promotable* diagnostic is a warning or note that `warnings_as_errors` may raise; an *optional* diagnostic is a rule that `disable` and `enable` may switch and that `warnings_as_errors` may also raise. When a rule is disabled it does not run at all.

```toml
[diagnostics]
warnings_as_errors = true
disable = ["MIST0148"]
```

```toml
[diagnostics]
warnings_as_errors = ["MIST0082"]
```

A promoted warning or note fails the build exactly where an error would, and the build summary counts it as an error.

### Error Behavior

| Condition | Diagnostic | Severity |
|-----------|-----------|----------|
| `diagnostics` is not a table | `MIST0103` | Error |
| A key other than `warnings_as_errors`, `disable` or `enable` | `MIST0104` | Error |
| A value of the wrong shape | `MIST0105` | Error |
| An entry naming a number the compiler does not know | `MIST0106` | Error |
| A `disable` or `enable` entry naming a fixed or promotable diagnostic | `MIST0107` | Error |

Every entry is checked against the compiler's registry before compilation starts, and any of these errors fails the command.

## Example Configurations

### Minimal Configuration

A single-project setup requires only the `name` field:

```toml
name = "HelloMist"
```

This is sufficient for a project with one or more `.mist` files in the same directory. The compiler discovers all `.mist` files recursively and uses `HelloMist` as the namespace root.

### Multi-Project Configuration

A project that depends on a shared library and specifies a custom namespace root:

```toml
name = "my-web-app"
namespace_root = "WebApp"

[references]
SharedLib = { path = "../SharedLib" }
DataModels = { path = "../DataModels" }
```

The corresponding directory layout:

```
workspace/
├── my-web-app/
│   ├── mist.toml          # references SharedLib and DataModels
│   ├── Program.mist
│   └── Controllers/
│       └── HomeController.mist
├── SharedLib/
│   ├── mist.toml          # name = "SharedLib"
│   └── Utils.mist
└── DataModels/
    ├── mist.toml          # name = "DataModels"
    └── Models/
        └── User.mist
```

In this setup:

- `my-web-app` can use types from both `SharedLib` and `DataModels`
- Files in `my-web-app/Controllers/` receive the inferred namespace `WebApp.Controllers`
- Each referenced project is compiled independently and its types become available to the referencing project

### Large Project with Many References

For projects with many dependencies, each reference is a named key in the `[references]` table:

```toml
name = "AllFeatures"

[references]
Primitives = { path = "../Primitives" }
Classes = { path = "../Classes" }
AbstractTypes = { path = "../AbstractTypes" }
```

### Project with Global Usings

A project that uses global usings to avoid repetitive imports:

```toml
name = "MyApp"
namespace_root = "MyApp"

[global_usings]
namespaces = ["Standard", "MyApp.Models", "MyApp.Services"]

[references]
SharedLib = { path = "../SharedLib" }
```

With this configuration, every `.mist` file in the project can reference types from `Standard`, `MyApp.Models`, and `MyApp.Services` without explicit `using` declarations. The referenced `SharedLib` project does not inherit these global usings.

## Error Behavior

The compiler validates `mist.toml` strictly and halts compilation on any error:

| Condition | Diagnostic | Severity |
|-----------|-----------|----------|
| `mist.toml` not found | `Project file 'path/mist.toml' not found.` | Error |
| Invalid TOML syntax | `Failed to parse project file: {details}` | Error |
| Missing `name` field | `Project file is missing the required 'name' field.` | Error |
| Empty or whitespace `name` | `Project name must not be empty or whitespace.` | Error |
| Invalid characters in `name` | Lists the invalid characters found | Error |
| `name` exceeds 128 characters | `Project name exceeds the maximum length of 128 characters.` | Error |
| Empty `namespace_root` | `Namespace root must not be empty or whitespace when specified.` | Error |
| Invalid characters in `namespace_root` | Lists the invalid characters found | Error |
| Empty `entry_point` | `entry_point must not be empty or whitespace when specified.` | Error |
| `entry_point = "top-level"` but no TLS file | `entry_point = "top-level" specified in mist.toml but no file contains top-level statements.` | Error |
| `entry_point` specifies unknown class | `entry_point specifies '{class}' but no class with that name contains a static void Main method.` | Error |
| `entry_point` specifies class but TLS exists | `Cannot specify entry_point = "{class}" when a top-level statement file exists.` | Error |
| `references` is not a table | `MIST0066` | Error |
| Reference value is not a table | `MIST0065` | Error |
| Reference key is empty, has invalid characters, or exceeds 128 chars | `MIST0068`, `MIST0067` or `MIST0069` | Error |
| Reference key does not match target project's `name` | `MIST0070` | Error |
| Reference entry missing `path` | `Reference entry is missing the required 'path' field.` | Error |
| Reference entry with empty `path` | `Reference entry has an empty or whitespace 'path' field.` | Error |
| Referenced project not found | Reports the missing path | Error |
| Reference `path` names a file other than `mist.toml`, `.csproj` or `.dll` | `MIST0071` | Error |
| Referenced `.csproj` or `.dll` does not exist | `MIST0072` | Error |
| Referenced directory holds more than one `mist.toml`, `.csproj` or `.dll` | `MIST0073` | Error |
| Referenced C# project fails to build | `MIST0074` | Error |
| Circular reference | Reports the full cycle chain | Error |
| `[global_usings]` missing `namespaces` field | `MIST0079` | Error |
| Empty or whitespace namespace entry | `MIST0080` | Error |
| Invalid characters in namespace entry | `MIST0081` | Error |
| `namespaces` is not an array or contains non-strings | `MIST0077` or `MIST0078` | Error |
| Duplicate namespace entry | `MIST0082` | Warning |
| `diagnostics` is not a table, or a `[diagnostics]` key or value is invalid | `MIST0103`, `MIST0104`, `MIST0105` | Error |
| A `[diagnostics]` entry names an unknown number, or switches a fixed or promotable diagnostic | `MIST0106`, `MIST0107` | Error |
