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
- `steering` — Install the bundled Mist agent references into the project's `.mist/agents/` directory and add a Mist block pointing at them to its `AGENTS.md` (and to `CLAUDE.md`, if one exists that does not import `AGENTS.md`), so coding agents understand the language. Idempotent and safe to re-run; it never touches your own files or the text outside the block. See [Agent References](distribute/steering/README.md).
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

Diagnostic controls: `--diagnostic-format text|json` (default `text`),
`--diagnostic-file <path>`, `--min-severity info|warning|error` (default `info`),
`--warnings-as-errors [Number,...]`, `--disable <Number,...>` and `--enable <Number,...>`.
Bare `--warnings-as-errors` promotes every Promotable diagnostic, including notes; a list
selects which non-Fixed diagnostics to promote. Disable and enable apply only to Optional
rules. CLI policy overrides the manifest. Unknown numbers and invalid values fail before execution.

All six controls apply to `build`, `run`, `test`, `inspect` and `restore`. Lex mode accepts
format, file and minimum severity only; `steering` and `version` accept none.
JSON is one buffered document on stderr, completed before a run or test child starts.
With `--diagnostic-file`, JSON goes to that UTF-8 file and stderr keeps text diagnostics.
Program and test-runner output remains separate. If the host fails after completing the document,
it reports one text ICE on stderr and exits 1; the completed document is not rewritten. JSON includes hidden diagnostics with
`visible: false`. Consumers must ignore unknown keys; additive schema changes raise the
minor version, and removals or changed meanings raise the major version.

> `--mode lex` is not compatible with the `run` or `test` commands, and `--verbose` and `--quiet` cannot be combined.

### Output

Progress goes to stdout and diagnostics to stderr. `build` shows stage progress by default; `run` and
`test` show it only with `--verbose`, leaving stdout to the program. No option hides an error. `--min-severity` can hide warnings and notes while the summary still counts them. A
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

# Install the Mist agent references into the current project
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

Read [The Mist Programming Language](https://mistlang.com/). The book lives in
the `book/` directory and is built with [mdBook](https://rust-lang.github.io/mdBook/).

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

### Weekly updates

[What's New](https://mistlang.com/updates/) collects dated updates, with regular editions
planned for Monday evenings in `America/New_York`. The kickoff edition is dated October 28,
2026. Scheduling and authorship are separate from Jenkins's existing book publication.

To prepare an edition, copy `book/updates-template.md` to `book/src/updates/YYYY-MM-DD.md`.
Use the actual publication date for the filename and heading. Review the changes on `main`
since the previous edition's ending revision, and record the exact start and end revisions
with a GitHub comparison link. Use merged implementation, documentation, and test evidence;
a spec, task status, or commit subject alone does not establish a delivered feature.

Group related commits under New Language Features, C# Interop, Improvements, Developer
Improvements, Breaking Changes, and Bug Fixes, in that order. Developer Improvements address
contributors working on the compiler. Omit empty sections and documentation or hosting
housekeeping. Add Performance or Standard Library when relevant; give performance numbers
only with a linked measurement and its limits. Each feature or improvement gets a short
paragraph and a small preview, such as code, configuration, a command, or a diagnostic.
Breaking changes explain the required adjustment. Bug fixes follow breaking changes and
stay brief, without previews. Describe changes to `main` without implying a versioned release
or completeness. Label GitHub commit links with their short hashes and comparison links with
the revision range, reserving descriptive link text for book chapters. Follow `book/AGENTS.md`
for examples, including compiling Mist snippets and generating their C# tabs.

Update the latest-edition title, link, and `{{#include YYYY-MM-DD.md:edition}}` in
`book/src/updates/index.md`. The include renders the full latest edition on What's New from
the dated page's `edition` anchors, so the content has one source. Keep the anchors when
editing an edition. Add its row to the newest-first archive on that page, then add the dated
page after the final separator in `book/src/SUMMARY.md`, newest first. Older editions keep
their dated URLs and archive rows as the latest-edition include moves forward.
The template stays outside `book/src/` and is not published. Build with
`bash book/publish.sh` and check the rendered index, edition, and links before pushing.
Jenkins publishes these pages through the same pipeline as the rest of the book.

### Jenkins publication

The root `Jenkinsfile` builds the book from `main` on the shared Linux agent and
publishes it to isengard. It polls `main` every five minutes for changes. Successful
builds archive `book/site.tar.gz`; deployment
switches the served release atomically and checks its `revision.txt` over HTTP.
The compiler and private Storm submodule are not needed for publication.

`bash book/publish.sh` runs the same noninteractive build locally. It installs
mdBook 0.5.2 and mdbook-tabs 0.3.4 in the ignored `book/bin/` directory and supports
Linux and macOS on x64 and ARM64.

The static server uses `book/ci/compose.yaml` and `book/ci/nginx.conf`, installed
in `~/mist-book` on isengard. Its read-only site mount contains `site/releases/`
and the `site/current` symlink. `book/ci/receive.py`, installed at
`~/mist-book/receive.py`, receives a tar archive over SSH and accepts only a full
commit SHA as the SSH command. It rejects path traversal, links, special files,
archives above 100 MiB and releases without an index. A rejected upload leaves
the current release in place. The latest release and nine previous releases
remain available for rollback.

Jenkins credential `mist-book-isengard-deploy` holds a dedicated SSH private key
for user `lynxstar`. Its public key in isengard's `~/.ssh/authorized_keys` uses:

```text
restrict,command="/usr/bin/python3 /home/lynxstar/mist-book/receive.py" ssh-ed25519 PUBLIC_KEY
```

The server host key is pinned in `book/ci/known_hosts`; a host-key change must be
verified before updating it. The deployment key can upload the book, without a
shell, port forwarding or Docker access.

Cloudflare keeps DNS and public TLS for `mistlang.com`. Tunnel
`mist-book-isengard` (`8d71617a-09ec-4583-8035-d3af521a1079`) routes that
hostname to `http://book:8080`; other hostnames receive 404. The apex DNS record
is a proxied CNAME to `8d71617a-09ec-4583-8035-d3af521a1079.cfargotunnel.com`.
The Compose `tunnel` profile runs the outbound connector; no inbound router
forwarding is needed for this site.
Install that tunnel's token as `~/mist-book/tunnel-token` on isengard, outside
Git and Jenkins, and restrict the parent directory to its owner. Set the token
file to mode `0600`; the connector runs as isengard user ID 1000 so it can read
that file without granting access to other users. Start the services with:

```bash
cd ~/mist-book
docker compose --profile tunnel up -d
```

The LAN-only listener `http://192.168.1.6:8090` supports deployment checks. Never
forward that port from the Internet. The tunnel route exposes only the static
book service. Existing inbound rules for other sites are separate.
