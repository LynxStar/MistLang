# CLI Usage

The Mist CLI (`mist`) is the command-line interface for compiling, running and testing Mist projects and for checking their dependencies. It accepts a command verb, an optional project folder path, and flags that control compilation behavior.

## General Syntax

```
mist <command> [project-folder] [flags]
mist inspect <TypeName> [project-folder] [flags]
```

If no project folder is specified, the current working directory is used. Individual file paths are not accepted — you must provide a directory containing a `mist.toml` project file.

Running `mist` with no arguments prints the usage help. A project folder with no command builds it: `mist ./my-project` is `mist build ./my-project`.

## Commands

| Command | Compiles | Afterwards | Description |
|---------|----------|------------|-------------|
| `build` | Yes | — | Compiles the project and produces output artifacts. |
| `run` | Yes | Runs the artifact with `dotnet` | Compiles, then executes the resulting binary. Executable projects only: a project with `output_type = "library"` is refused before compiling. |
| `test` | Yes | Runs `dotnet test` on the emitted `.csproj` | Compiles, then runs the project's tests. Warns with `MIST0102` when it finds test-attributed methods but no test framework package. |
| `restore` | No | — | Resolves `[packages]` and `[framework_references]` only, and always prints the resolver's detail and summary. It never lexes, type-checks or emits. |
| `inspect <TypeName>` | No | — | Resolves the same assemblies a build would and prints a CLR type's projected and hidden members. It prints the same dependency diagnostics a build does to standard error, once each, and exits 1 without a report when one is an error. |
| `steering` | No | — | Installs the bundled Mist agent references into the project's `.mist/agents/` directory and adds a Mist block pointing at them to `AGENTS.md`, and to an existing `CLAUDE.md` that does not import `AGENTS.md`. |
| `version` | No | — | Prints the compiler version. |

`build`, `run` and `test` discover all `.mist` source files recursively within the project folder, clean the output and binary directories (Stage 0), and run the full compilation pipeline. If compilation fails, diagnostics are printed, the command exits with code 1, and `run` and `test` launch nothing. Otherwise `run` exits with the program's exit code and `test` with `dotnet test`'s.

`restore` and `inspect` require a `mist.toml` in the project folder and exit non-zero without one. Like a build, both print every diagnostic the `mist.toml` raises, warnings included, to standard error once each, and exit 1 before resolving anything when one is an error. `restore` and `inspect` exit non-zero when any dependency fails to resolve, and both print the dependency diagnostics; see [.NET Interop](../advanced/dotnet-interop.md) for `restore`'s output and the diagnostics.

## Flags

| Flag | Short | Value | Default | Description |
|------|-------|-------|---------|-------------|
| `--backend` | — | `<id>` | `csharp` | Selects the code generation backend. Currently only `csharp` is available; another value fails compilation with `MIST0099`. |
| `--output` | — | `<dir>` | `<project>/out` | Sets the directory where transpiled source files are written. |
| `--bin` | — | `<dir>` | `<project>/bin` | Sets the directory where compiled binary artifacts are placed. |
| `--mode` | — | `compile` \| `lex` | `compile` | Selects the compiler mode for `build`. `compile` runs the full pipeline; `lex` dumps token streams only. |
| `--pipeline` | — | `standard` | `standard` | Selects the compilation pipeline. `standard` is the only pipeline, so the flag can be omitted; any other value is a parse error. |
| `--platform` | — | `windows` \| `linux` \| `macos` | this machine | Sets the platform the program is compiled for, which decides the platform-specific startup code the compiler emits and the operating-system part of the target runtime identifier. |
| `--rid` | — | `<rid>` | this machine's | Sets the target runtime identifier, such as `linux-x64`, which decides the native and runtime-specific files copied into `bin/`. It takes precedence over `--platform`; an identifier the SDK does not know fails with `MIST0100`. See [Target runtime identifier](#target-runtime-identifier). |
| `--verbose` | `-v` | — | off | Shows the progress of every stage, including Stage 1.5 package-resolution detail. |
| `--quiet` | `-q` | — | off | Suppresses progress output during compilation, and the list of installed files for `steering`. |

### Notes on flags

- `--verbose` and `--quiet` are mutually exclusive. Supplying both produces an error.
- The `--mode lex` option is not compatible with the `run` or `test` commands. Attempting to combine them produces an error.
- The `--output` and `--bin` directories are cleaned (all contents deleted) at the start of every build.
- Unknown flags produce an error with a usage hint.

### Target runtime identifier

A build deploys for one runtime identifier (RID): the machine's own by default, such as `osx-arm64`. `--platform` changes the operating-system part and keeps the machine's processor architecture; `--rid <rid>` names the whole identifier and wins over `--platform`. Packages and referenced C# projects supply native and runtime-specific files under `runtimes/<rid>/...`, one folder per identifier. The compiler picks, for each package and each kind of file, the first identifier along the target's fallback chain that has any, from most to least specific as the SDK's runtime identifier graph defines it (for `osx-arm64`: `osx-arm64`, then `osx`, then `unix`, ending at `any`). It copies those files flat into `bin/`, beside the program, where the .NET host looks for them, so a program that uses SQLite or another native library runs with `mist run` and no hand-staged copy. The same selection applies to the `runtimes/<rid>/native/` files in a referenced C# project's output folder.

```bash
mist build my-project --rid linux-x64
```

## Output and Diagnostics

Progress goes to stdout. Diagnostics go to stderr, one per line, as `path(line,col): severity ID: message`; the path and position are left out when a diagnostic has none.

| Command | Default | `--verbose` | `--quiet` |
|---------|---------|-------------|-----------|
| `build` | Stages 0–6 and the summary; Stage 1.5 is silent unless something fails | Adds the Stage 1.5 detail | No progress and no summary |
| `run`, `test` | No progress, so stdout belongs to the program or the test runner | Stages 0–6, Stage 1.5 detail and the summary | Same as the default |

No flag hides a diagnostic. Most diagnostics are printed after the summary. A dependency failure is the exception: a package or framework reference that fails to resolve is printed as soon as Stage 1.5 ends, before Stage 2 begins, and is not repeated after the summary, though the summary counts it. Compilation still runs through analysis, so errors in your source are reported in the same run, and then fails before anything is emitted. [.NET Interop](../advanced/dotnet-interop.md#when-a-dependency-fails) shows an example.

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

For `inspect`, the type name comes first and the project folder, if any, second. The project folder must contain a `mist.toml` file, and `build`, `run` and `test` also need at least one `.mist` source file.

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

### Run a project's tests

```bash
mist test my-project
```

Compiles `my-project` and runs its tests through `dotnet test`, streaming the runner's output.

### Build with a custom output directory

```bash
mist build my-project --output ./generated --bin ./dist
```

Compiles `my-project`, placing transpiled C# files in `./generated` and the compiled binary in `./dist`.

### Show package resolution detail

```bash
mist build --verbose
```

Prints the `Stage 1.5 - Package Resolution` block, with one line per restored package and resolved framework reference. With `run` or `test`, `--verbose` also turns on the progress of every other stage.

### Check dependencies without building

```bash
mist restore
```

Resolves the declared packages and framework references, prints the detail and a summary, and exits non-zero if any of them failed.

### Inspect a CLR type

```bash
mist inspect System.Text.Encoding
```

Lists the type's members that Mist can use and the ones it hides, with the reason for each.

### Use lex mode for debugging

```bash
mist build my-project --mode lex
```

Runs only the lexer stage. For each `.mist` file, produces a `.tokens` file (showing each token as `[TokenKind: lexeme]`) and a reconstructed `.mist` file in the `out/lexer/` subdirectory. Useful for diagnosing tokenization issues.

Lex mode also writes a `.mist-generated` file into that `lexer/` directory. It tells every later build, of this project or of one that references it, not to read the reconstructed `.mist` files as sources, even when `--output` put them somewhere other than `out/`.

### Quiet build (suppress progress output)

```bash
mist build --quiet
```

Compiles the project without printing stage progress messages or the summary. Errors and diagnostics are still printed to stderr, a dependency failure still as soon as Stage 1.5 ends.
