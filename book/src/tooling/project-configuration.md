# Project Configuration

Every Mist project is defined by a `mist.toml` file at the project root. This file tells the compiler the project's name, how to derive namespaces, and which other projects to include as dependencies.

## Fields

The table below summarizes all available fields:

| Field | Required | Type | Description |
|-------|----------|------|-------------|
| `name` | Yes | String | The project name. Used as the default namespace root and output assembly name. |
| `namespace_root` | No | String | Overrides the root segment for inferred namespaces. Defaults to `name` when omitted. |
| `[[references]]` | No | Array of tables | Declares dependencies on other Mist projects. Each entry requires a `path` field. |

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
| `references` is not an array of tables | `The 'references' field must be an array of tables ([[references]]).` |
| Reference entry missing `path` | `Reference entry is missing the required 'path' field.` |
| Reference entry with empty `path` | `Reference entry has an empty or whitespace 'path' field.` |
| Referenced project not found | Reports the missing path |
| Circular reference | Reports the full cycle chain |
