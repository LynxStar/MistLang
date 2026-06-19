---
inclusion: manual
---

<!-- MIST-MANAGED-STEERING: installed by `mist steering`. Do not edit — this file is overwritten on every reinstall. Do not add this marker to your own steering files; any file containing it is treated as Mist-managed and may be deleted. -->

# Mist .gitignore

Reference for what a Mist project generates and what belongs in `.gitignore`. Include this when setting up version control for a Mist project.

## What the Compiler Generates

For each project, the `mist` CLI writes two artifact directories under the project root (the directory containing `mist.toml`):

| Directory | Flag | Contents | Commit? |
|-----------|------|----------|---------|
| `out/` | `--output` | Transpiled C# source (`.cs`) and a generated `.csproj` | No — regenerated on every build |
| `bin/` | `--bin` | Compiled .NET binaries (`.dll`, `.runtimeconfig.json`) | No — build product |

Both are derived entirely from your `.mist` sources, so neither should be committed. The directory names are defaults; if a build overrides `--output` or `--bin`, ignore those paths instead.

## Recommended `.gitignore`

A minimal Mist project `.gitignore`:

```gitignore
# Mist build artifacts (transpiled C# and compiled binaries)
out/
bin/

# macOS
.DS_Store
```

### Multi-project workspaces

Because every referenced project gets its own `out/` and `bin/` under its own directory, match them at any depth rather than only at the root:

```gitignore
# Mist build artifacts at any depth
**/out/
**/bin/
```

## What to Commit

Commit your sources and configuration:

- `mist.toml` (and any referenced projects' `mist.toml`)
- All `.mist` source files
- `.kiro/steering/` — including the Mist-managed steering files, so collaborators and Kiro get language awareness on clone

## Notes

- There is no per-user or machine-specific Mist state to ignore — artifacts are fully reproducible from sources.
- If you also open the generated `out/` project in a C#/.NET IDE, the standard .NET `obj/` and `bin/` ignore rules apply there, but those live under `out/` and are already covered by ignoring `out/`.
