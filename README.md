# MistLang
The Mist programming language

## CLI Usage

```
mist <build|run|steering> [project-folder] [--backend <id>] [--output <dir>] [--bin <dir>] [--mode <compile|lex>]
```

### Commands

- `build` — Compile a Mist project and report diagnostics. This is the default when no command is specified.
- `run` — Compile the project and execute the resulting artifact via `dotnet`.
- `steering` — Install the bundled Mist steering files into the project's `.kiro/steering/` directory so Kiro understands the language. Idempotent and safe to re-run; it never touches your own steering files. See [Steering Files](distribute/steering/README.md).

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

# Install Mist steering files for Kiro into the current project
mist steering

# Omitting the command defaults to build
mist ./my-project

# Dump lexer tokens
mist build --mode lex
```

### Running from source

```bash
dotnet run --project Source/Mist.CLI -- build ./my-project
```

## Editor Support

### Storm

The project includes a TextMate grammar extension that provides syntax highlighting for `.storm` files in Kiro and VS Code. It highlights keywords (`grammar`, `import`, `token`, `rule`, `fragment`, `hidden`, `as`), token and rule definition names, comments, string literals and escape sequences, character classes, operators, labels, and punctuation.

Install the extension by creating a symlink into the Kiro extensions directory:

```bash
ln -s "$(pwd)/Editor/storm-syntax-highlighting" ~/.kiro/extensions/storm-syntax-highlighting
```

Using a symlink means any changes you make to the grammar source are reflected immediately — no need to reinstall the extension.

After installing, reload or restart Kiro for the extension to take effect.

### Mist

A companion TextMate grammar extension provides syntax highlighting for `.mist` files in Kiro and VS Code. It highlights keywords, types, literals, interpolated strings, comments, operators, declaration names, and punctuation.

Install the extension by creating a symlink into the Kiro extensions directory:

```bash
ln -s "$(pwd)/Editor/mist-syntax-highlighting" ~/.kiro/extensions/mist-syntax-highlighting
```

Using a symlink means any changes you make to the grammar are reflected immediately — no need to reinstall the extension.

After installing, reload or restart Kiro for the extension to take effect.

## Documentation (The Mist Book)

The Mist Book lives in the `book/` directory and is built with [mdBook](https://rust-lang.github.io/mdBook/).

### Prerequisites

```bash
cargo install mdbook
cargo install mdbook-tabs
```

### Build and Preview

```bash
./book/build.sh
```

This builds the book and opens it in your default browser. The source files are in `book/src/`.
