# Design Document

## Overview

Three small pieces. The driver decides a target runtime identifier and its fallback order. The package
resolver, which already parses `project.assets.json`, also reads each package's `runtimeTargets` and keeps
the entries the fallback order selects. The backend copies the selected assets, and the matching folders
of a referenced C# project's output, flat into `bin/`. No `.deps.json`: the compiler already runs programs
from `bin/` with only a `runtimeconfig.json`, and the host probes the application base for native
libraries, which the EF sample's hand-staged copy proved.

## Architecture

```
CLI (--platform, --rid) ──► TargetRuntime.Select(platform, host rid, host arch, --rid) ─► Target_Rid
                                        │
assets file ─► RidGraph.FallbackOrder(graph, Target_Rid) ─► [osx-arm64, osx, unix, any]
                                        │
PackageResolver.ParseAssets ─► runtimeTargets ─► RuntimeAssetSelection.Select(entries, order)
                                        │                       (pure, Tier 1)
                                        ▼
PipelineInput(RuntimeIdentifiers, RuntimeAssets) ─► emitter ─► CSharpCompilationDriver
                                                                  │  ClosureAssetSelection(listing, order)
                                                                  ▼
                                                   bin/: primary, references, natives, facades
```

## The assets file, as restored on 2026-10-24

```json
"SQLitePCLRaw.lib.e_sqlite3/2.1.12": {
  "runtimeTargets": {
    "runtimes/linux-arm/native/libe_sqlite3.so":           { "assetType": "native", "rid": "linux-arm" },
    "runtimes/osx-arm64/native/libe_sqlite3.dylib":        { "assetType": "native", "rid": "osx-arm64" },
    "runtimes/browser-wasm/nativeassets/net9.0/e_sqlite3.a": { "assetType": "native", "rid": "browser-wasm" }
  }
}
```

The target is `targets["net10.0"]`, the one the compiler already reads for `compile` and `runtime`. The
package folder is `packageFolders` joined with the lower-cased `id/version`. The graph path is
`project.frameworks["net10.0"].runtimeIdentifierGraphPath`, a file under the SDK whose `runtimes` object
maps each identifier to `{ "#import": [ ... ] }`.

## Components

### New

| Component | Location | Role |
| --- | --- | --- |
| `TargetRuntime` | `Source/Mist/Driver/TargetRuntime.cs` | Pure: `Select(TargetPlatform? platform, string hostRid, Architecture hostArch, string? rid)`. |
| `RidGraph` | `Source/Mist/Driver/RidGraph.cs` | Parse the graph JSON; `FallbackOrder(graph, rid)`; the built-in table when the file is absent. |
| `RuntimeAssetSelection` | `Source/Mist/Driver/RuntimeAssetSelection.cs` | Pure: keep, per package and per asset type, the entries of the first identifier in the order that has any. |
| `RuntimeAsset`, `RuntimeAssetKind` | `Source/Mist/Driver/Contracts/` | `(Path, Rid, Kind)`; `Native`, `Managed`. |
| `ClosureAssetSelection` | `Source/Mist/Backends/CSharpTranspiler/ClosureAssetSelection.cs` | Pure over a listing of relative paths under a C# output directory. |

### Modified

| Component | Change |
| --- | --- |
| `CliArgumentParser`, `CliArguments` | `--rid <rid>`. |
| `CompilerOptions` | `string? RuntimeIdentifier` from `--rid`. |
| `PackageResolver` | Parses `runtimeTargets`; reads the graph path; `PackageResolutionResult` gains `RuntimeAssets`. |
| `CompilerDriver` | Computes the identifiers before resolution, reports `MIST_RID_UNKNOWN`, fills `PipelineInput.RuntimeIdentifiers` and `RuntimeAssets`, adds the Stage 1 detail. |
| `PipelineInput` | `ImmutableArray<string> RuntimeIdentifiers` (target first), `ImmutableArray<RuntimeAsset> RuntimeAssets`. |
| `CSharpEmitterV2`, `CSharpCompilationDriver` | Carry both into the deployment step; closure selection; the copy rules; the deployed count. |
| `DriverDiagnostics` | `MIST_RID_UNKNOWN`. |

## Detailed Design

### 1. Target identifier

`TargetRuntime.Select` returns `rid` when given. Otherwise, when `platform` is null or equals the host's,
it returns `hostRid`. Otherwise it joins the platform token (`win`, `linux`, `osx`) with the architecture
token from `RuntimeInformation.OSArchitecture` (`X64` to `x64`, `Arm64` to `arm64`, `X86` to `x86`,
`Arm` to `arm`). The driver validates the result against the graph and reports `MIST_RID_UNKNOWN` when
absent.

### 2. Fallback order

`RidGraph.FallbackOrder` is a breadth-first walk over `#import` from the target, each identifier once, the
target first. For `osx-arm64` the SDK graph yields `osx-arm64, osx, unix-arm64, unix, any, base` in its
own order; the exact sequence is whatever the graph says, which is why the file is read rather than
hard-coded. The built-in table used when the graph is unavailable maps `{win,linux,osx}-{x64,arm64}` to
their OS, the OS to `unix` (not for `win`), and everything to `any`.

### 3. Selection

`RuntimeAssetSelection.Select(entries, order)` groups a package's entries by asset type; for each type it
finds the first identifier in `order` that has entries and keeps exactly those. An entry whose identifier
is not in the order is dropped. The result is appended to the package's assembly paths in
`PackageResolutionResult.RuntimeAssets`, in package order.

### 4. Deployment

After the primary output emits, the backend copies in this order: managed references as today; selected
`runtime` assets, replacing a same-named `lib/` copy; selected `native` assets; closure assets from each
referenced C# project's `runtimes/` folder, chosen by `ClosureAssetSelection` over a relative-path listing
of that folder; facades. Within a step, the first file of a name wins and later ones are skipped. A copy is
refreshed when its source is newer. Nothing from this step enters Roslyn's references.

### 5. Reporting

Stage 1 detail: `runtime identifier osx-arm64` when any asset was selected. Backend detail: `N runtime
assets deployed` when `N > 0`.

## Error Handling

| Situation | Where | Diagnostic |
| --- | --- | --- |
| `--rid foo-bar` | driver | `MIST_RID_UNKNOWN` |
| `--platform windows` on an arm64 Mac | driver | Target_Rid `win-arm64`; packages lacking it fall back along the graph |
| package with assets only for other identifiers | resolver | nothing selected, no diagnostic; the runtime reports the missing library as today |

## Testing Strategy

- **Tier 1.** `TargetRuntimeTests` (6.1), `RidGraphTests` over an in-memory graph and the built-in table
  (6.2), `RuntimeAssetSelectionTests` (6.3), `ClosureAssetSelectionTests` (6.4), `RuntimeAssetDeploymentTests`
  over a stub file source (6.5), `PackageAssetsParsingTests` over a synthetic assets document (6.8).
- **Tier 5.** `CSharpReferences_RunsFromACleanTree` extended: `Bridge` ships a fake native file per common
  identifier as content under `runtimes/<rid>/native/`, and the test asserts the host identifier's file is
  in `App/bin` and the others are not (6.6).
- **Demonstration.** `samples/EfCoreMigrations` with `run.sh` removed: `mist run App` builds the C# host,
  emits the facade, deploys `libe_sqlite3.dylib`, applies the migrations and prints the row (6.7).

## Alternatives Considered

- **Emit a `.deps.json` and keep the `runtimes/` layout.** The host's documented layout, and what the SDK
  does for a rid-agnostic build. It needs a correct dependency graph with every library's version and
  asset list, which is a second resolver. Flat copy is what the SDK itself produces for a rid-specific
  build, and it is what the compiler already does for managed assemblies.
- **Read the identifier from `mist.toml`.** Reasonable later; left out so that the manifest stays
  machine-independent and the choice stays with the command that builds.
