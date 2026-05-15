# CLI Usage

The Mist CLI (`mist`) is the command-line interface for compiling and running Mist projects. It accepts a command verb, an optional project folder path, and flags that control compilation behavior.

## General Syntax

```
mist <command> [project-folder] [flags]
```

If no project folder is specified, the current working directory is used. Individual file paths are not accepted — you must provide a directory containing a `mist.toml` project file.

## Commands

The CLI supports two commands:

| Command | Description |
|---------|-------------|
| `build` | Compiles the project and produces output artifacts. This is the default if no command is specified. |
| `run` | Compiles the project and then executes the resulting binary artifact. |

Both commands discover all `.mist` source files recursively within the project folder, clean the output and binary directories, and run the full compilation pipeline.

The `run` command performs the same compilation as `build`, then launches the compiled artifact using `dotnet`. If compilation fails, diagnostics are printed and no execution occurs.

## Flags

| Flag | Short | Value | Default | Description |
|------|-------|-------|---------|-------------|
| `--backend` | — | `<id>` | `csharp` | Selects the code generation backend. Currently only `csharp` is available. |
| `--output` | — | `<dir>` | `<project>/out` | Sets the directory where transpiled source files are written. |
| `--bin` | — | `<dir>` | `<project>/bin` | Sets the directory where compiled binary artifacts are placed. |
| `--mode` | — | `compile` \| `lex` | `compile` | Selects the compiler mode. `compile` runs the full pipeline; `lex` dumps token streams only. |
| `--quiet` | `-q` | — | off | Suppresses progress output during compilation. |

### Notes on flags

- The `--mode lex` option is not compatible with the `run` command. Attempting to combine them produces an error.
- The `--output` and `--bin` directories are cleaned (all contents deleted) at the start of every build.
- Unknown flags produce an error with a usage hint.

## Positional Argument

The optional positional argument specifies the project folder to compile:

```
mist build path/to/my-project
```

| Behavior | Description |
|----------|-------------|
| Omitted | Defaults to the current working directory. |
| Directory path | Uses the specified directory as the project root. |
| File path | Rejected with an error — individual file paths are not accepted. |
| Non-existent path | Rejected with an error indicating the directory does not exist. |
| Multiple paths | Rejected — only a single project folder is accepted. |

The project folder must contain a `mist.toml` file and at least one `.mist` source file.

## Usage Examples

### Build a project in the current directory

```bash
mist build
```

Compiles all `.mist` files in the current directory, writing transpiled C# to `./out` and the compiled binary to `./bin`.

### Run a project from a specified folder

```bash
mist run samples/HelloMist
```

Compiles the project at `samples/HelloMist` and immediately executes the resulting artifact. Console output from the program is printed to stdout.

### Build with a custom output directory

```bash
mist build my-project --output ./generated --bin ./dist
```

Compiles `my-project`, placing transpiled C# files in `./generated` and the compiled binary in `./dist`.

### Use lex mode for debugging

```bash
mist build my-project --mode lex
```

Runs only the lexer stage. For each `.mist` file, produces a `.tokens` file (showing each token as `[TokenKind: lexeme]`) and a reconstructed `.mist` file in the `out/lexer/` subdirectory. Useful for diagnosing tokenization issues.

### Quiet build (suppress progress output)

```bash
mist build --quiet
```

Compiles the project without printing stage progress messages. Errors and diagnostics are still printed to stderr.
