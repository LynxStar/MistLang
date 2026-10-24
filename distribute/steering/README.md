# Mist Agent References

These files teach coding agents how to understand and write Mist code. `mist steering` installs them into a Mist project's `.mist/agents/` directory and points the project's `AGENTS.md` at them.

## Design Philosophy: Document Only the Differences

Mist compiles to C# and is intentionally C#-like. The language model already knows C# deeply. **These files assume "Mist behaves exactly like C#" as the baseline and spend context budget only on what actually differs.**

When editing these files, the test for whether something belongs is: *"Would a C# developer get this wrong if we didn't say it?"* If a feature works identically to C# (e.g. `var`, interpolated strings, `?.`/`??`, lambdas, properties, basic generics, `is` patterns, switch expressions, tuples, control flow), it does **not** belong here — describing it is wasted context. Only genuine divergences earn their space.

## Installation

The Mist CLI ships these files and installs them for you:

```bash
mist steering            # install into the current project
mist steering ./my-app   # target a specific project folder
```

The command is idempotent and user-safe:

- Each reference file carries a `MIST-MANAGED-STEERING` marker comment. On every run, the installer deletes only the files in `.mist/agents/` that carry that marker (the set it installed previously, even if they were renamed or dropped between versions), then drops the current bundled set in fresh. Your own files there — anything without the marker — are never touched.
- `AGENTS.md` gets a block fenced by `<!-- BEGIN MIST-MANAGED ... -->` and `<!-- END MIST-MANAGED -->`. If the file is missing, it is created holding only the block; if it has no block, the block is appended; if it has one, only the text between the markers is replaced. Everything outside the markers is left exactly as it was.
- `CLAUDE.md` gets the same block, but only when it already exists and does not import `@AGENTS.md`. The installer never creates a `CLAUDE.md`.
- A lone, repeated or reversed marker in either file stops the run before anything is written. Fix or remove the markers and run again.
- Managed files left in `.kiro/steering/` by earlier versions are deleted; your own files there are kept.

Re-run `mist steering` any time you upgrade the compiler to refresh the files.

## Files

| File | Purpose |
|------|---------|
| `agents-block.md` | The text of the block written into `AGENTS.md` and `CLAUDE.md`. Not copied into `.mist/agents/`. |
| `mist-language.md` | Core language reference — syntax, types, project structure, patterns. The block tells agents to read it before writing Mist. |
| `mist-ownership.md` | Ownership, move semantics, lending, drop, substructural types. |
| `mist-advanced-features.md` | Abstract types, default implementations, `with` blocks (retroactive conformance), structured context, refinement types, dependent types, extensions, async. |
| `mist-interop.md` | Consuming .NET — NuGet packages via `[packages]`, SDK types, and the CLR-to-Mist projection rules. |
| `mist-testing.md` | Test projects, `[Fact]`/`[Theory]`, the `mist test` workflow. |
| `mist-gitignore.md` | What Mist generates and what to ignore. |

The block lists every reference file and when to read it, so a new file needs a row there.

## Keeping Up to Date

These files should be regenerated when:
- New language features are added
- Existing syntax or semantics change
- The project configuration format (`mist.toml`) is updated

The source of truth is the Mist Book (`book/src/`) and the sample programs (`samples/`).

## Adding or Removing Distributed Files

The CLI bundles every `mist-*.md` file in this directory, plus `agents-block.md` (the `Mist.CLI.csproj` `Content` include), and installs them via `mist steering`.

When adding a new reference file:

1. Name it `mist-<topic>.md` so the bundler picks it up.
2. Embed the managed marker at the top so the installer can manage it across versions:

   ```html
   <!-- MIST-MANAGED-STEERING: installed by `mist steering`. Do not edit — this file is overwritten on every reinstall. -->
   ```

   A file **without** the marker is treated as a user file and will never be removed by the CLI.

3. Add its row to the table in `agents-block.md`.
4. Rebuild the CLI so the `Content` glob copies it into the binary output.

Removing or renaming a file needs only its row in `agents-block.md` changed: the next `mist steering` run deletes any previously-installed marked file that is no longer shipped, because deletion keys off the marker, not the filename.
