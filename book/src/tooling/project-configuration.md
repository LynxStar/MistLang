# Project Configuration

Every Mist project is defined by a `mist.toml` file at the project root. This file tells the compiler the project's name, how to derive namespaces, and which other projects to include as dependencies.

## Fields

The table below summarizes all available fields:

| Field | Required | Type | Description |
|-------|----------|------|-------------|
| `name` | Yes | String | The project name. Used as the default namespace root and output assembly name. |
| `namespace_root` | No | String | Overrides the root segment for inferred namespaces. Defaults to `name` when omitted. |
| `entry_point` | No | String | Disambiguates the program entry point when multiple candidates exist. |
| `[[references]]` | No | Array of tables | Declares dependencies on other Mist projects. Each entry requires a `path` field. |
| `[global_usings]` | No | Table | Declares namespaces implicitly imported into every source file in the project. |

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

## The `[[references]]` Array

The `[[references]]` array declares dependencies on other Mist projects. Each entry is a TOML table with a single required field:

| Field | Required | Type | Description |
|-------|----------|------|-------------|
| `path` | Yes | String | Path to the referenced project's directory or `mist.toml` file. |

```toml
[[references]]
path = "../SharedLib"
```

When the compiler processes references, it parses each referenced project's `mist.toml` and includes that project's source files in the compilation. Referenced projects can themselves declare references, forming a dependency graph.

### Path Resolution

The `path` field accepts both relative and absolute paths:

- **Relative paths** are resolved from the directory containing the current `mist.toml`
- **Absolute paths** are used as-is
- If the path points to a **directory**, the compiler automatically appends `mist.toml` (i.e., `"../SharedLib"` resolves to `../SharedLib/mist.toml`)
- If the path already ends with `mist.toml`, it is used directly

### Circular Reference Detection

The compiler performs depth-first traversal of the reference graph and detects cycles. If project A references project B, and project B references project A (directly or transitively), the compiler reports an error:

```
Circular project reference detected: /path/to/A/mist.toml → /path/to/B/mist.toml → /path/to/A/mist.toml.
```

Self-references (a project referencing itself) are also detected and reported as circular references.

### Missing References

If a referenced path does not exist, the compiler reports a diagnostic:

- If the directory exists but contains no `mist.toml`: `Referenced directory '/path/to/dir' does not contain a 'mist.toml' file.`
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

Valid examples: `System`, `MyProject.Models`, `_Internal.Utils`

### Error Behavior

| Condition | Diagnostic | Severity |
|-----------|-----------|----------|
| Section present but missing `namespaces` field | `MIST_GLOBAL_USING_MISSING_NAMESPACES` | Error |
| Empty or whitespace-only entry | `MIST_GLOBAL_USING_EMPTY_NAMESPACE` | Error |
| Entry contains invalid characters | `MIST_GLOBAL_USING_INVALID_NAMESPACE` | Error |
| `namespaces` is not an array | `MIST_GLOBAL_USING_INVALID_FORMAT` | Error |
| Non-string element in `namespaces` | `MIST_GLOBAL_USING_INVALID_FORMAT` | Error |
| Duplicate entry (case-sensitive) | `MIST_GLOBAL_USING_DUPLICATE_NAMESPACE` | Warning |

All error diagnostics halt compilation. The duplicate warning is non-fatal — the duplicate is removed and parsing continues.

All diagnostics include the path to the `mist.toml` file for orientation.

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

[[references]]
path = "../SharedLib"

[[references]]
path = "../DataModels"
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

For projects with many dependencies, each reference gets its own `[[references]]` entry:

```toml
name = "AllFeatures"

[[references]]
path = "../Primitives"

[[references]]
path = "../Classes"

[[references]]
path = "../Traits"
```

### Project with Global Usings

A project that uses global usings to avoid repetitive imports:

```toml
name = "MyApp"
namespace_root = "MyApp"

[global_usings]
namespaces = ["System", "MyApp.Models", "MyApp.Services"]

[[references]]
path = "../SharedLib"
```

With this configuration, every `.mist` file in the project can reference types from `System`, `MyApp.Models`, and `MyApp.Services` without explicit `using` declarations. The referenced `SharedLib` project does not inherit these global usings.

## Error Behavior

The compiler validates `mist.toml` strictly and halts compilation on any error:

| Condition | Error |
|-----------|-------|
| `mist.toml` not found | `Project file 'path/mist.toml' not found.` |
| Invalid TOML syntax | `Failed to parse project file: {details}` |
| Missing `name` field | `Project file is missing the required 'name' field.` |
| Empty or whitespace `name` | `Project name must not be empty or whitespace.` |
| Invalid characters in `name` | Lists the invalid characters found |
| `name` exceeds 128 characters | `Project name exceeds the maximum length of 128 characters.` |
| Empty `namespace_root` | `Namespace root must not be empty or whitespace when specified.` |
| Invalid characters in `namespace_root` | Lists the invalid characters found |
| Empty `entry_point` | `entry_point must not be empty or whitespace when specified.` |
| `entry_point = "top-level"` but no TLS file | `entry_point = "top-level" specified in mist.toml but no file contains top-level statements.` |
| `entry_point` specifies unknown class | `entry_point specifies '{class}' but no class with that name contains a static void Main method.` |
| `entry_point` specifies class but TLS exists | `Cannot specify entry_point = "{class}" when a top-level statement file exists.` |
| `references` is not an array of tables | `The 'references' field must be an array of tables ([[references]]).` |
| Reference entry missing `path` | `Reference entry is missing the required 'path' field.` |
| Reference entry with empty `path` | `Reference entry has an empty or whitespace 'path' field.` |
| Referenced project not found | Reports the missing path |
| Circular reference | Reports the full cycle chain |
| `[global_usings]` missing `namespaces` field | `MIST_GLOBAL_USING_MISSING_NAMESPACES` |
| Empty or whitespace namespace entry | `MIST_GLOBAL_USING_EMPTY_NAMESPACE` |
| Invalid characters in namespace entry | `MIST_GLOBAL_USING_INVALID_NAMESPACE` |
| `namespaces` is not an array or contains non-strings | `MIST_GLOBAL_USING_INVALID_FORMAT` |
| Duplicate namespace entry | `MIST_GLOBAL_USING_DUPLICATE_NAMESPACE` (warning, parsing continues) |
