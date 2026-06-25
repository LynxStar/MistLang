# Mist Steering Files

These steering files teach Kiro how to understand and write Mist code. Copy them into any Mist project's `.kiro/steering/` directory to get language-aware assistance.

## Design Philosophy: Document Only the Differences

Mist compiles to C# and is intentionally C#-like. The language model already knows C# deeply. **These files assume "Mist behaves exactly like C#" as the baseline and spend context budget only on what actually differs.**

When editing these files, the test for whether something belongs is: *"Would a C# developer get this wrong if we didn't say it?"* If a feature works identically to C# (e.g. `var`, interpolated strings, `?.`/`??`, lambdas, properties, basic generics, `is` patterns, switch expressions, tuples, control flow), it does **not** belong here — describing it is wasted context. Only genuine divergences earn their space.

## Installation

The Mist CLI ships these files and installs them for you:

```bash
mist steering            # install into ./.kiro/steering
mist steering ./my-app   # target a specific project folder
```

The command is idempotent and user-safe:

- Each distributed file carries a `MIST-MANAGED-STEERING` marker comment.
- On every run, the installer deletes only the files that carry that marker (the set it installed previously, even if they were renamed or dropped between versions), then drops the current bundled set in fresh.
- Your own steering files — anything without the marker — are never touched.

Re-run `mist steering` any time you upgrade the compiler to refresh the files.

### Manual installation

If you'd rather not use the CLI, copy the files directly:

```bash
mkdir -p .kiro/steering
cp path/to/distribute/steering/mist-*.md .kiro/steering/
```

## Files

| File | Inclusion | Purpose |
|------|-----------|---------|
| `mist-language.md` | Always | Core language reference — syntax, types, project structure, patterns. Loaded into every conversation. |
| `mist-ownership.md` | Manual (`#`) | Deep dive on ownership, move semantics, lending, drop, substructural types. Include when working with owned code. |
| `mist-advanced-features.md` | Manual (`#`) | Abstract types, default implementations, `with` blocks (retroactive conformance), structured context, refinement types, dependent types, extensions, async. Include when using advanced features. |
| `mist-interop.md` | Manual (`#`) | Consuming .NET — NuGet packages via `[packages]`, SDK types, and the CLR-to-Mist projection rules. Include when adding dependencies or calling C#. |
| `mist-gitignore.md` | Manual (`#`) | What Mist generates and what to ignore. Include when setting up version control. |

## How Inclusion Works

- **Always** files are automatically included in every Kiro conversation. Keep these concise to minimize context budget usage.
- **Manual** files are included only when referenced with `#` in chat (e.g., `#mist-ownership`). Use these for deep dives without paying the always-on context cost. The always-included `mist-language.md` lists the manual files and when to reach for each, so they remain discoverable.

## Keeping Up to Date

These files should be regenerated when:
- New language features are added
- Existing syntax or semantics change
- The project configuration format (`mist.toml`) is updated

The source of truth is the Mist Book (`book/src/`) and the sample programs (`samples/`).

## Adding or Removing Distributed Files

The CLI bundles every `mist-*.md` file in this directory (the `Mist.CLI.csproj` `Content` include) and installs them via `mist steering`.

When adding a new distributed steering file:

1. Name it `mist-<topic>.md` so the bundler picks it up.
2. Embed the managed marker so the installer can manage it across versions. Place this comment at the top (after any YAML front matter):

   ```html
   <!-- MIST-MANAGED-STEERING: installed by `mist steering`. Do not edit — this file is overwritten on every reinstall. -->
   ```

   A file **without** the marker is treated as a user file and will never be installed or removed by the CLI.

3. Rebuild the CLI so the `Content` glob copies it into the binary output.

Removing or renaming a file requires no special handling: the next `mist steering` run deletes any previously-installed marked file that is no longer shipped, because deletion keys off the marker, not the filename.
