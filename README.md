# MistLang
The Mist programming language

## CLI Usage

```
mist <build|run> [project-folder] [--backend <id>] [--output <dir>] [--bin <dir>] [--mode <compile|lex>]
```

### Commands

- `build` — Compile a Mist project and report diagnostics. This is the default when no command is specified.
- `run` — Compile the project and execute the resulting artifact via `dotnet`.

### Options

| Flag | Default | Description |
|---|---|---|
| `project-folder` | Current directory | Root directory of the Mist project. |
| `--backend <id>` | `csharp` | Backend to use for code generation. |
| `--output <dir>` | `<project>/out` | Directory for transpiled output. |
| `--bin <dir>` | `<project>/bin` | Directory for compiled binaries. |
| `--mode <compile\|lex>` | `compile` | `compile` runs the full pipeline; `lex` dumps token streams. |

> `--mode lex` is not compatible with the `run` command.

### Examples

```bash
# Build the project in the current directory
mist build

# Build a specific project folder
mist build ./my-project

# Compile and run
mist run ./my-project

# Omitting the command defaults to build
mist ./my-project

# Dump lexer tokens
mist build --mode lex
```

### Running from source

```bash
dotnet run --project Source/Mist.CLI -- build ./my-project
```
