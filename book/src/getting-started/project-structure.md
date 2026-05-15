# Project Structure

Every Mist project is defined by a `mist.toml` configuration file. This chapter explains what goes in that file, how the compiler discovers your source files, and how to organize multi-file and multi-project builds.

## The `mist.toml` File

The `mist.toml` file lives at the root of your project directory. It uses [TOML](https://toml.io) syntax and supports the following fields:

| Field | Required | Description |
|-------|----------|-------------|
| `name` | Yes | The project name. Used as the default namespace root and the output assembly name. Must contain only alphanumeric characters, hyphens, underscores, and periods. |
| `namespace_root` | No | Overrides the root namespace for the project. When omitted, defaults to the value of `name`. |
| `[[references]]` | No | An array of tables, each with a `path` field pointing to another Mist project directory. Used for multi-project builds. |

A minimal `mist.toml` only needs the `name` field:

```toml
name = "MyApp"
```

A project with a custom namespace root and a reference to a shared library looks like this:

```toml
name = "MyApp"
namespace_root = "MyCompany.MyApp"

[[references]]
path = "../SharedLib"
```

## Directory Layout

A Mist project has a simple layout. The minimum valid project is a directory containing a `mist.toml` file and at least one `.mist` source file:

```
my-project/
├── mist.toml
└── Program.mist
```

As your project grows, you can organize source files into subdirectories. The compiler discovers all files with the `.mist` extension recursively within the project directory — you don't need to list them anywhere.

```
my-project/
├── mist.toml
├── Program.mist
├── AppConfig.mist
└── Models/
    ├── Player.mist
    ├── Item.mist
    └── Game/
        └── GameState.mist
```

Every `.mist` file in the tree is automatically included in compilation. The directory structure also determines namespace inference: a file in `Models/Game/` receives the inferred namespace `{NamespaceRoot}.Models.Game`. We'll cover namespace inference in detail in the [Namespaces](../basics/namespaces.md) chapter.

## Multi-File Projects

Within a single project, all `.mist` files can reference each other's types freely — there's no need for import declarations between files in the same namespace. Files in different subdirectories (and therefore different inferred namespaces) use `using` declarations to access each other's types.

For example, given the directory layout above with `namespace_root` defaulting to the project name `"Namespaces"`:

- `Program.mist` at the project root is in namespace `Namespaces`
- `Models/Player.mist` is in namespace `Namespaces.Models`
- `Models/Game/GameState.mist` is in namespace `Namespaces.Models.Game`

To use `Player` from `Program.mist`, you add a `using` declaration:

```mist
using Namespaces.Models;
```

## Multi-Project Builds with `[[references]]`

Larger codebases can be split into multiple projects, each with its own `mist.toml`. The `[[references]]` array lets one project depend on another by specifying a `path` to the referenced project's directory.

Each `path` value can be relative (resolved from the referencing project's directory) or absolute. The referenced directory must contain its own `mist.toml` file.

```toml
name = "AllFeatures"

[[references]]
path = "../Primitives"

[[references]]
path = "../Classes"

[[references]]
path = "../Namespaces"
```

When you build a project with references, the compiler:

1. Parses each referenced project's `mist.toml`
2. Recursively resolves any transitive references (references of references)
3. Discovers all `.mist` files in each referenced project
4. Compiles everything together, with referenced types available via `using` declarations

References are resolved in dependency order (deepest dependencies first), so a referenced project's types are always available to projects that depend on it.

### Circular Reference Detection

The compiler detects circular references and reports an error. If project A references project B, and project B references project A (directly or transitively), compilation halts with a diagnostic identifying the cycle.

## Error Behavior

The compiler reports diagnostic errors and halts compilation in the following cases:

- **Missing `mist.toml`**: If the project directory does not contain a `mist.toml` file, the compiler reports `MIST_PROJECT_FILE_NOT_FOUND`.
- **Invalid TOML syntax**: If `mist.toml` contains malformed TOML, the compiler reports `MIST_PROJECT_TOML_ERROR` with details about the parse failure.
- **Missing `name` field**: If the required `name` field is absent, the compiler reports `MIST_PROJECT_MISSING_NAME`.
- **Invalid `name` value**: If the name contains disallowed characters or exceeds 128 characters, the compiler reports `MIST_PROJECT_INVALID_NAME` or `MIST_PROJECT_NAME_TOO_LONG`.
- **Invalid reference path**: If a `[[references]]` entry is missing the `path` field or the path points to a directory without a `mist.toml`, the compiler reports a reference resolution error.
- **Circular references**: If the reference graph contains a cycle, the compiler reports `MIST_PROJECT_CIRCULAR_REFERENCE`.

In all error cases, the compiler prints the diagnostic message and stops — no output is produced.
