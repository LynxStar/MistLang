# MistLang
The Mist programming language

## CLI Usage

```
mist <build|run|test|restore|steering|version> [project-folder] [options]
mist inspect <TypeName> [project-folder] [options]
```

### Commands

- `build` — Compile a Mist project and report diagnostics. A project folder given with no command is built; `mist` alone prints help.
- `run` — Compile the project and execute the resulting artifact via `dotnet`. Executable projects only.
- `test` — Compile the project and run its tests via `dotnet test` on the emitted `.csproj`.
- `restore` — Resolve `[packages]` and `[framework_references]` and print the resolver's detail, without compiling.
- `inspect <TypeName>` — Show which members of a CLR type Mist projects and which it hides, and why. Resolves dependencies like a build but does not report their failures; use `restore` for those.
- `steering` — Install the bundled Mist steering files into the project's `.kiro/steering/` directory so Kiro understands the language. Idempotent and safe to re-run; it never touches your own steering files. See [Steering Files](distribute/steering/README.md).
- `version` — Print the compiler version.

`build`, `run` and `test` compile; `run` and `test` launch nothing when compilation fails. `restore` and `inspect` only resolve dependencies.

### Options

| Flag | Default | Description |
|---|---|---|
| `project-folder` | Current directory | Root directory of the Mist project. |
| `--backend <id>` | `csharp` | Backend to use for code generation. |
| `--output <dir>` | `<project>/out` | Directory for transpiled output. |
| `--bin <dir>` | `<project>/bin` | Directory for compiled binaries. |
| `--mode <compile\|lex>` | `compile` | `compile` runs the full pipeline; `lex` dumps token streams. |
| `--pipeline standard` | `standard` | The compilation pipeline. `standard` is the only one, so omit the flag. |
| `--platform <windows\|linux\|macos>` | This machine | The platform the program is compiled for. |
| `--verbose`, `-v` | Off | Show every stage's progress, including Stage 1.5 package-resolution detail. |
| `--quiet`, `-q` | Off | Suppress progress output. |

> `--mode lex` is not compatible with the `run` or `test` commands, and `--verbose` and `--quiet` cannot be combined.

### Output

Progress goes to stdout and diagnostics to stderr. `build` shows stage progress by default; `run` and
`test` show it only with `--verbose`, leaving stdout to the program. No option hides a diagnostic. A
package or framework reference that fails to resolve is reported as Stage 1.5 ends, before lexing, and
is not repeated with the diagnostics printed after the summary; compilation still runs through analysis
and then fails. See [CLI Usage](book/src/tooling/cli-usage.md) in the book for details.

### Examples

```bash
# Build the project in the current directory
mist build

# Build a specific project folder
mist build ./my-project

# Compile and run
mist run ./my-project

# Compile and run the tests
mist test ./my-project

# Check that declared packages and framework references resolve
mist restore ./my-project

# Show package resolution detail during a build
mist build --verbose

# Install Mist steering files for Kiro into the current project
mist steering

# Omitting the command defaults to build
mist ./my-project

# Dump lexer tokens
mist build --mode lex
```

### Running from source

Clone with the private Storm submodule available at its source generator path:

```bash
git clone --recurse-submodules https://github.com/LynxStar/MistLang.git
```

For an existing checkout, run `git submodule update --init --recursive` before building.

To build and install the CLI to `~/.mist/bin`, run `./publish.sh` on macOS/Linux or
`./publish.ps1` on Windows. Both scripts can be invoked from any directory and initialize
the Storm submodule when it is missing. This requires Git access to the private Storm repository.

`git worktree add` does not populate submodules. Enable the tracked hooks once per clone so
worktrees created through Git initialize the submodule, borrowing objects from the main checkout:

```bash
git config core.hooksPath .githooks
```

Codex disables Git hooks when creating managed worktrees. Configure
`git submodule update --init --recursive` as the project's Codex local-environment setup script
to initialize Storm before ordinary builds and tests in those worktrees.

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
