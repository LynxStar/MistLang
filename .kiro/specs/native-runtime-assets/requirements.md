# Requirements Document

## Introduction

The compiler copies every managed assembly a program's packages supply into `bin/` and writes a
`runtimeconfig.json`, so `dotnet bin/<app>.dll` finds its managed dependencies. It copies nothing from a
package's `runtimes/<rid>/native/` folders and nothing runtime-identifier-specific from a referenced C#
project's output, so a program that uses SQLite, Skia or any other package with a native library dies at
first use with `DllNotFoundException`. `samples/EfCoreMigrations` stages `libe_sqlite3.dylib` by hand in a
shell script for exactly this reason, and the Virtue spike of 2026-10-21 recorded the gap as its second
backlog item.

The restore the compiler already runs produces everything needed. `project.assets.json` lists each
package's `runtimeTargets`: a path inside the package, an `assetType` of `native` or `runtime`, and the
`rid` it belongs to. The same file names the SDK's runtime-identifier graph, which defines the fallback
order from a specific identifier such as `osx-arm64` through `osx` and `unix` to `any`. This feature picks
a target runtime identifier, selects each package's assets along that fallback order, and copies them flat
into `bin/` beside the primary output, where the host's default probing finds them. The same selection
applies to the `runtimes/` folder a referenced C# project's build leaves in its output.

Flat copy is chosen over a `.deps.json`: the compiler already runs programs without one, the host probes
the application base for native libraries, and the hand-staged copy in the EF sample proved the layout.
Out of scope: a `.deps.json`, self-contained or trimmed output, and a runtime identifier declared in
`mist.toml` rather than on the command line.

## Glossary

- **Runtime_Identifier**: a .NET RID such as `osx-arm64`, `linux-x64` or `win-x64`.
- **Target_Rid**: the Runtime_Identifier a compilation deploys for.
- **Host_Rid**: `RuntimeInformation.RuntimeIdentifier` of the process running the compiler.
- **Rid_Graph**: the SDK's `RuntimeIdentifierGraph.json`, whose path the assets file records under the
  project framework's `runtimeIdentifierGraphPath`; each entry's `#import` list names the identifiers it
  falls back to.
- **Fallback_Order**: the Target_Rid followed by the identifiers reachable from it through `#import`,
  breadth first, each once.
- **Runtime_Asset**: one `runtimeTargets` entry of a package in the assets file: its absolute path, its
  `rid`, and whether it is `native` or `runtime` (a managed assembly specific to a rid).
- **Closure_Asset**: a file under `runtimes/<rid>/native/` or `runtimes/<rid>/lib/<tfm>/` in a referenced
  C# project's output directory.
- **Package_Resolver**: `PackageResolver`, which restores and parses `project.assets.json`.
- **Backend**: `CSharpCompilationDriver`, which fills `bin/`.
- **Output_Directory**: `bin/`.

## Requirements

### Requirement 1: The target runtime identifier

**User Story:** As a Mist developer, I want the compiler to deploy for the machine it runs on by default
and for a named runtime when I ask, so that native libraries match the process that loads them.

#### Acceptance Criteria

1. WHEN neither `--platform` nor `--rid` is given, THE compiler SHALL use the Host_Rid as the Target_Rid.
2. WHEN `--platform` names an operating system, THE compiler SHALL form the Target_Rid from that system's
   token (`win`, `linux`, `osx`) and the host's processor architecture token (`x64`, `arm64`, `x86`,
   `arm`).
3. WHEN `--rid <rid>` is given, THE compiler SHALL use it as the Target_Rid, taking precedence over
   `--platform`.
4. IF the Target_Rid is not a key of the Rid_Graph, THEN THE compiler SHALL report `MIST_RID_UNKNOWN` naming
   it and SHALL stop before Stage 2. The graph comes from the restore's assets file, and the restore runs
   after Stage 1 because referenced manifests add packages during loading, so the check cannot run earlier.
   Without packages there is no restore and no graph, and any identifier is accepted with the built-in
   fallback order.
5. THE Fallback_Order SHALL be computed from the Rid_Graph by breadth-first traversal of `#import`, each
   identifier once, starting with the Target_Rid itself.
6. IF the assets file names no Rid_Graph or the file is missing, THEN THE compiler SHALL use a built-in
   fallback table covering `win`, `linux`, `osx` with `x64` and `arm64`, and their parents `unix` and
   `any`, so that the common identifiers resolve without the SDK file.
7. THE Target_Rid and the Fallback_Order SHALL be carried in the pipeline input so that every later
   component reads the same decision.

### Requirement 2: Selecting package runtime assets

**User Story:** As a Mist developer, I want the native libraries of the packages I declare to arrive in
`bin/`, so that `mist run` works without a script.

#### Acceptance Criteria

1. THE Package_Resolver SHALL read every package's `runtimeTargets` from the assets file's target for the
   project's framework and produce a Runtime_Asset for each entry, with the absolute path under the global
   packages folder, the entry's `rid` and its `assetType`.
2. WHEN a package has Runtime_Assets for several identifiers, THE Package_Resolver SHALL keep only those
   whose `rid` is the first identifier in the Fallback_Order for which that package has any asset of that
   asset type, so that `osx-arm64` wins over `osx`, and `osx` is used when no `osx-arm64` asset exists.
3. THE Package_Resolver SHALL keep `native` and `runtime` assets independently, so that a package with
   only a native library for the Target_Rid and a managed assembly for `any` keeps both.
4. THE Package_Resolver SHALL ignore entries whose `rid` is absent from the Fallback_Order, so that a
   `browser-wasm` static library is never selected for a desktop target.
5. THE Package_Resolver SHALL carry the selected assets in `PackageResolutionResult` beside the assembly
   paths, and the selection SHALL be a pure function over the parsed entries and the Fallback_Order.

### Requirement 3: Deploying runtime assets

**User Story:** As a Mist developer, I want every selected asset copied where the runtime looks for it,
so that the program loads it without configuration.

#### Acceptance Criteria

1. THE Backend SHALL copy every selected `native` Runtime_Asset into the Output_Directory by its file name,
   flat, beside the primary output.
2. THE Backend SHALL copy every selected `runtime` Runtime_Asset into the Output_Directory by its file name,
   and WHEN a managed assembly of the same file name was also copied from a package's `lib/` folder, THE
   rid-specific copy SHALL replace it, as the SDK does for a rid-specific build.
3. WHEN two selected assets share a file name, THE Backend SHALL copy the first in package order and skip
   the rest, so that deployment is deterministic.
4. THE Backend SHALL refresh a copy already in the Output_Directory when its source is newer. A rid-specific
   managed replacement under criterion 2 is copied regardless of timestamps, since the copy it replaces
   may well be the newer file.
5. THE Backend SHALL NOT add any Runtime_Asset to Roslyn's references.

### Requirement 4: Closure assets of referenced C# projects

**User Story:** As a Mist developer, I want a referenced C# project's native dependencies to arrive in
`bin/` too, so that the EF sample runs with one command.

#### Acceptance Criteria

1. WHEN a referenced C# project's output directory holds a `runtimes/` folder, THE Backend SHALL select,
   for each asset type, the first identifier in the Fallback_Order that has a `runtimes/<rid>/native/` or
   `runtimes/<rid>/lib/<tfm>/` folder with files, and copy those files flat into the Output_Directory under
   the rules of Requirement 3. A closure `lib/<tfm>` assembly replaces a same-named file from that project's output root.
   Only `CSharp_Project` references contribute closure assets; a bare `.dll` reference has no output
   folder of its own. Files nested below `native/` are not assets.
2. THE Backend SHALL NOT copy the `runtimes/` tree itself or any identifier's folder other than the
   selected one.
3. The Closure_Asset selection SHALL be a pure function over a listing of relative paths and the
   Fallback_Order, so that it is proven at Tier 1.

### Requirement 5: Reporting

**User Story:** As a Mist developer, I want to see what was deployed, so that a missing library is
diagnosable from the build output.

#### Acceptance Criteria

1. THE Stage 1 progress detail SHALL state the Target_Rid whenever the build has a possible source of
   runtime assets: a declared package, in the root or a referenced manifest, or an external assembly.
   Selection happens after Stage 1 completes, so the detail cannot wait for it; every build that selects an
   asset prints the line, and some builds that select nothing print it too.
2. THE Backend's progress detail SHALL state how many runtime assets were deployed when the number is not
   zero.
3. THE compiler SHALL define `MIST_RID_UNKNOWN`, severity Error, Driver stage, in `DriverDiagnostics`.

### Requirement 6: Verification

**User Story:** As a compiler maintainer, I want the feature proven at the tiers the testing philosophy
assigns, with the EF sample as the demonstration.

#### Acceptance Criteria

1. The Target_Rid decision (platform plus architecture, `--rid` precedence) SHALL be proven at Tier 1.
2. The Fallback_Order over an in-memory Rid_Graph and the built-in table SHALL be proven at Tier 1,
   including an identifier absent from the graph.
3. The Runtime_Asset selection over parsed entries SHALL be proven at Tier 1: specific over generic, the
   two asset types independent, an absent identifier ignored, and a package with no assets for any
   identifier in the order yielding nothing.
4. The Closure_Asset selection over a listing SHALL be proven at Tier 1.
5. The deployment rules of Requirement 3 (replacement, first wins, refresh) SHALL be proven at Tier 1 over
   a stub file source, as the reference planner's rules are.
6. `samples/CSharpReferences` SHALL gain a fake native file per common identifier in `Bridge`'s output
   under `runtimes/<rid>/native/`, and its Tier 5 test SHALL assert that the host identifier's file, and
   only that one, lands in `App/bin`.
7. `samples/EfCoreMigrations` SHALL lose `run.sh`; its README SHALL document `mist run App` as the whole
   demonstration. It stays a manual demonstration because it needs NuGet and the `dotnet-ef` tool.
8. The assets-file parsing SHALL be proven at Tier 1 over a synthetic document in the shape recorded in
   the design.

### Requirement 7: Non-goals

#### Acceptance Criteria

1. THE feature SHALL NOT emit a `.deps.json`.
2. THE feature SHALL NOT read a runtime identifier from `mist.toml`.
3. THE feature SHALL NOT produce self-contained or trimmed output.
