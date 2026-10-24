# Design Document

## Overview

Three changes, in the order the pipeline meets them. The manifest and resolver learn two more reference
kinds and decide the kind from the path. The driver builds referenced C# projects with the .NET SDK before
loading source and feeds their outputs into the resolved-assembly list that packages already fill. The
backend reads each external assembly's reference table and, for every composed Mist project a C# assembly
was compiled against, emits a type-forwarding facade and drops the standalone copy.

The frontend needs no mapping component. Both probes on 2026-10-24 showed that the program model already
binds a CLR signature's `App.Data.StoreContext` to the declared type when `App.Data` is composed; every
identity error came from Roslyn, and the same emitted C# compiled and ran once a facade stood in for the
standalone `App.Data.dll`. Requirement 6.3 turns that observation into a contract and Requirement 6.4 adds
the one guard the provider lacks.

## Architecture

```
mist.toml ──► ProjectFileParser ──► ReferenceClassifier ──► ProjectReference(Name, Path, Kind)
                                                                    │
                     ┌──────────────────────────────────────────────┘
                     ▼
        ProjectReferenceResolver (graph, cycles, key check)
                     │  Mist_Project ─► composed units (as today)
                     │  CSharp_Project ─► CSharpProjectBuilder ─► dotnet build ─► TargetPath + closure dir
                     │  Assembly ─► file
                     ▼
        CompilerDriver.Compile: ResolvePackages(...) + external assemblies ─► resolvedAssemblyPaths
                     │
                     ▼
        AmbientTypeProviderV2Factory.Create(tfm, paths, closureDirs) ─► program model (declared wins)
                     │
                     ▼
        Stages 1..5 unchanged ─► Emit (unchanged)
                     │
                     ▼
        CSharpCompilationDriver: AssemblyRef scan ─► facades ─► Roslyn(refs - shadowed + facades) ─► bin/
```

Everything above the backend is the driver's. The facade is the backend's because only the backend knows the
emitted type names, the assembly version and the output directory.

## Components and Interfaces

### New

| Component | Location | Role |
| --- | --- | --- |
| `ReferenceKind` | `Source/Mist/Driver/Contracts/ReferenceKind.cs` | `MistProject`, `CSharpProject`, `Assembly`. |
| `ReferenceClassifier` | `Source/Mist/Driver/ReferenceClassifier.cs` | Pure: `Classify(string path, bool isDirectory, IReadOnlyList<string> directoryFileNames)` returns a kind and resolved path, or a classification failure (`None`, `Ambiguous(candidates)`, `Unsupported`). |
| `CSharpProjectBuilder` | `Source/Mist/Driver/CSharpProjectBuilder.cs` | Runs `dotnet build`, reads `TargetPath`, returns `ExternalAssembly`. Modelled on `DotnetRestoreProcess`. |
| `ExternalAssembly` | `Source/Mist/Driver/Contracts/ExternalAssembly.cs` | `(string Name, string Path, ImmutableArray<string> Closure, string? RuntimeConfigPath, ReferenceKind Kind)`. |
| `BuildChain` | `Source/Mist/Driver/BuildChain.cs` | Reads and extends `MIST_BUILD_CHAIN`; pure `Contains(chain, manifest)` decision. |
| `FacadeEmitter` | `Source/Mist/Backends/CSharpTranspiler/FacadeEmitter.cs` | Writes the facade with `MetadataBuilder` and `ManagedPEBuilder`: exported-type forwarder rows, one assembly reference to the primary output, deterministic MVID. |
| `FacadeTypeSelection` | `Source/Mist/Backends/CSharpTranspiler/FacadeTypeSelection.cs` | Pure: which declared types a facade forwards, from candidates attributed to their origin project. |
| `ExternalReferencePlanner` | `Source/Mist/Backends/CSharpTranspiler/ExternalReferencePlanner.cs` | Pure over a stubbable metadata source: shadowing, conflict, framework check, root cycle, which projects get a facade. |
| `AssemblyConflictRule`, `RuntimeFrameworkRule` | same folder | The version rule over (name, version) pairs; the framework names in a `runtimeconfig.json`. |
| `ProjectReferenceLoader`, `DotnetProcess` | `Source/Mist/Driver/` | Resolves the graph then builds C# references, shared by the driver and `inspect`; runs `dotnet` with extra environment and a timeout. |
| `AssemblyReferenceScan` | `Source/Mist/Backends/CSharpTranspiler/AssemblyReferenceScan.cs` | Pure over a `PEReader`: referenced simple names, assembly version. |

### Modified

| Component | Change |
| --- | --- |
| `ProjectReference` | Gains `ReferenceKind Kind`. The parser sets it; `Path` is the resolved file for every kind. |
| `ProjectFileParser` step 9 | Calls `ReferenceClassifier` for a file path; for a directory it defers to the resolver, which can list the directory, and records `Kind = MistProject` provisionally with a `IsDirectory` flag on the reference. |
| `ProjectReferenceResolver` | For directory entries, lists top-level files and classifies. Dispatches by kind: Mist projects load manifests as today; C# projects and assemblies become `ExternalAssembly` candidates with the key check deferred to after the build. Returns both resolved Mist references and external assemblies. |
| `CompilerOptions` | `AssemblyPaths` is populated from external assemblies; gains `ExternalAssemblies` for the backend's closure and runtime-config checks. It stays internal. |
| `CompilerDriver.Compile` | Runs `CSharpProjectBuilder` for every C# reference before `ResolvePackages`, merges external paths into `resolvedAssemblyPaths`, passes closure directories to the provider factory, and hands `ExternalAssemblies` to the backend through `PipelineInput`. |
| `AmbientTypeProviderV2Factory.Create` | Takes resolver-only directories in addition to registered assembly paths; skips registration for any assembly whose simple name is a composed project's name. |
| `CSharpCompilationDriver.Compile` | Before creating the Roslyn compilation: scan, exclude shadowed files, emit facades, add facade references. After emit: copy closures with the conflict check; write facades. |
| `DriverDiagnostics`, `BackendDiagnostics` | The seven descriptors of Requirement 10 and the three reworded messages. |
| `Program.cs` (CLI) | `restore` runs `dotnet restore` on C# references; `inspect` builds them; Stage 1 detail line. |

## Data Models

```csharp
public enum ReferenceKind { MistProject, CSharpProject, Assembly }

public sealed record ProjectReference(string Name, string Path, ReferenceKind Kind);

public sealed record ExternalAssembly(
    string Name,
    string Path,
    ImmutableArray<string> Closure,
    string? RuntimeConfigPath,
    ReferenceKind Kind);

public abstract record Classification;
public sealed record Classified(ReferenceKind Kind, string ResolvedPath) : Classification;
public sealed record NoCandidate : Classification;
public sealed record Ambiguous(ImmutableArray<string> Candidates) : Classification;
public sealed record UnsupportedExtension(string Extension) : Classification;
```

`ResolvedProjectReference` is unchanged; it describes Mist projects only. The resolver's result type gains
an `ImmutableArray<ExternalAssembly>` beside the Mist references, so callers that only want Mist projects
keep the array they have.

## Detailed Design

### 1. Classification

`ReferenceClassifier.Classify` is a function of three inputs: the normalized path, whether it is a
directory, and, when it is, the directory's own file names. It never touches the file system; the resolver
supplies the listing. Rules, in order:

1. Not a directory: `mist.toml` is `MistProject`; `.csproj` is `CSharpProject`; `.dll` is `Assembly`; any
   other extension is `UnsupportedExtension`. Extensions compare case-insensitively.
2. Directory: candidates are the file names equal to `mist.toml` or ending in `.csproj` or `.dll`. One
   candidate classifies as its kind with the resolved path `directory/candidate`. Zero is `NoCandidate`. Two
   or more is `Ambiguous` with the candidates in ordinal order.

The parser keeps its existing behavior for a path that already ends in `mist.toml` and for a path with no
recognized extension that is not a directory at parse time; existence is still checked by the resolver, as
`project-references` requires. The resolver performs the directory listing with `Directory.EnumerateFiles`
at depth one and calls the classifier.

### 2. Building a referenced C# project

`CSharpProjectBuilder.Build(csprojPath, targetFramework, chain)` runs:

```
dotnet build "<csproj>" -c Debug -f <tfm> --nologo -v:quiet
```

with `MIST_BUILD_CHAIN` set to the inherited chain plus the current manifest, then runs

```
dotnet msbuild "<csproj>" -getProperty:TargetPath -p:Configuration=Debug -p:TargetFramework=<tfm>
```

and reads the single line of output. The closure is every `.dll` in `TargetPath`'s directory other than
`TargetPath` itself, and `RuntimeConfigPath` is the sibling `<name>.runtimeconfig.json` when it exists. A
non-zero exit or a timeout of 600 seconds reports `MIST_CSHARP_PROJECT_BUILD_FAILED` with the build's error
lines, filtered to lines containing `error`.

Order: the driver walks the resolved Mist graph deepest first, as it already does for loading units, and
builds each manifest's C# references in declaration order, so a library's C# dependency is built before the
root's. Each C# project is built once per compilation even if two manifests name it.

The bridge inside a referenced C# project typically runs `mist build ../App.Data` for a Mist library it
depends on. That nested compilation inherits `MIST_BUILD_CHAIN`, sees that `App.Data`'s manifest is not in
the chain, and proceeds as an ordinary build of that library into its own `out/` and `bin/`, which the outer
compilation does not write. If the C# project instead bridges to the root project, the nested build finds
the root manifest in the chain and reports `MIST_REFERENCE_CYCLE`; MSBuild surfaces that as the C# build's
failure, and the outer build reports `MIST_CSHARP_PROJECT_BUILD_FAILED` carrying the inner message.

### 3. The frontend

`CompilerDriver.Compile` today computes `resolvedAssemblyPaths` as packages plus framework plus
`options.AssemblyPaths` (line 80). External assembly paths join that list, so Roslyn, the copy step and the
provider all see them without further change. `AmbientTypeProviderV2Factory.Create` gains a second list,
resolver-only directories, which it adds to the `PathAssemblyResolver` so that a C# project's own
dependencies resolve while reading its metadata, without registering their types.

Requirement 6.4 is a filter in the factory's registration loop: an assembly whose simple name equals a
composed project's name is loaded for resolution but never registered. Requirement 6.3 is the existing
behavior of `BuildAndRegister` and the binder, which look types up by full name against the program model
where declared types are already present; the Tier 2 test pins it so a later change to registration order
cannot silently break it.

### 4. Facades

In `CSharpCompilationDriver.Compile`, before `CSharpCompilation.Create`:

1. `AssemblyReferenceScan.ReferencedNames(path)` opens each external assembly with `PEReader` and returns
   the simple names in its `AssemblyReference` table. This is `System.Reflection.Metadata`; no Roslyn and no
   loading.
2. The composed names are the root project's name and every `Mist_Project` reference's name.
3. Any reference path, external or closure member, whose file stem is a composed name is removed from the
   reference list and marked not-for-deployment.
4. For each composed name that some external assembly references: if it is the root's name, report
   `MIST_REFERENCE_CYCLE`; otherwise `FacadeEmitter.Emit(name, programModel, primaryOutputIdentity)`.
5. Facade paths are appended to Roslyn's references.

`FacadeEmitter` receives its forwarded names from the compilation's declared types, attributed to projects
through the emitter's per-file origin map (one C# file per Mist unit; every other emitted file is a support
file), and adds a row for each nested type of a forwarded type under its encloser. The spec originally selected
from the program model: public top-level types whose declaring
compilation unit's `OriginProjectPath` is the composed project's manifest, plus public top-level types from
units with `IsStdlib` and from the generated support files, which the emitter already enumerates when it
writes `__*.g.cs`. It writes one source text of `[assembly: TypeForwardedTo(typeof(...))]` lines, compiles it
with Roslyn as a library named after the composed project, referencing the primary output being built. Roslyn
binds a forwarder's target to the compilation under construction when the identities match; the 2026-10-24
probe compiled a crossing signature this way with zero errors. The facade is emitted after the primary
output so its `typeof` operands resolve, and the primary compilation receives the facade's image as a
reference in a second pass only when a crossing signature exists. To avoid two passes, the facade is instead
generated first from the program model's type list with a reference to the previous build's primary output
when present and to a placeholder compilation otherwise; the task list leaves the pass structure to the
implementer with this constraint: the primary compilation must see the facade, and the facade's forwards
must name the primary output's identity.

Determinism: the source text is sorted by full type name, and the facade compilation uses the same
`Deterministic` setting as the primary output.

### 5. Deployment

The copy loop in `CSharpCompilationDriver` gains the external assemblies and their closures. Before copying
a closure member whose simple name the compiler's own resolution already supplies, the backend compares the
two files' assembly versions through `AssemblyReferenceScan.Version(path)`; equal versions skip the copy,
unequal versions report `MIST_REFERENCE_ASSEMBLY_CONFLICT`. Shadowed files are never copied. Facades are
written beside the primary output. When a closure's `runtimeconfig.json` lists a framework under
`runtimeOptions.frameworks` that the Mist manifest's `framework_references` does not declare, the backend
reports `MIST_REFERENCE_FRAMEWORK_MISSING`.

### 6. CLI

`restore` runs `dotnet restore` on each C# reference and prints one line per reference with its kind.
`inspect` runs the builder before creating its provider. `build` and `run` need no change beyond the driver.
The Stage 1 detail gains "N C# projects built, M external assemblies".

## Error Handling

| Situation | Where | Diagnostic |
| --- | --- | --- |
| `path = "libs/Foo.txt"` | parser | `MIST_REFERENCE_UNSUPPORTED_PATH` |
| `path = "../Lib/Lib.csproj"`, file absent | resolver | `MIST_REFERENCE_FILE_NOT_FOUND` |
| directory with `A.csproj` and `B.csproj` | resolver | `MIST_REFERENCE_AMBIGUOUS` listing both |
| directory with `mist.toml` and `A.csproj` | resolver | `MIST_REFERENCE_AMBIGUOUS` listing both |
| directory with no candidate | resolver | `MIST_PROJECT_REFERENCE_NO_PROJECT_FILE` (reworded) |
| key `Migrations`, output `App.Migrations.dll` | resolver, after build | `MIST_PROJECT_REFERENCE_NAME_MISMATCH` |
| `dotnet build` fails | builder | `MIST_CSHARP_PROJECT_BUILD_FAILED` with MSBuild errors |
| C# project bridges to the root | nested compiler | `MIST_REFERENCE_CYCLE`, surfaced through the build failure |
| external assembly references the root by name | backend | `MIST_REFERENCE_CYCLE` |
| closure has `Newtonsoft.Json` 13.0.1, Mist resolved 13.0.3 | backend | `MIST_REFERENCE_ASSEMBLY_CONFLICT` |
| C# project targets `Microsoft.AspNetCore.App`, manifest does not | backend | `MIST_REFERENCE_FRAMEWORK_MISSING` |

## Correctness Properties

1. **Classification is total and pure.** For every path and listing, `Classify` returns exactly one
   `Classification`, and the same inputs return the same result.
2. **A facade forwards exactly the public surface of its project.** The set of forwarded names equals the
   set of public top-level types whose origin is the project, plus the standard-library and support set, and
   nothing else.
3. **Shadowing is complete.** After the backend's exclusion step, no reference path has a file stem equal
   to a composed project's name.
4. **Deployment is closed.** Every assembly name referenced by an external assembly resolves to a file in
   `bin/`, to a facade, or to the shared framework, or a diagnostic was reported.

## Testing Strategy

The binding rules are the root `AGENTS.md` "Tests" section and `.claude/skills/testing-philosophy/SKILL.md`.

- **Tier 1.** `ReferenceClassifierTests`: listings built in memory, every case of Requirement 11.1.
  `FacadeTypeSelectionTests`: a program model built directly, Requirement 11.2. `AssemblyReferenceScanTests`:
  images from `MetadataBuilder`, two reference rows, one version. `BuildChainTests`: contains and extend over
  strings. `AssemblyConflictRuleTests`: the version comparison over `(name, version)` pairs.
- **Tier 2.** `ExternalAssemblyShadowingTests`: a stub `IAmbientTypeProvider` registering a type named like
  a declared one is skipped; a stub signature naming the declared type binds to it.
- **Tier 3.** `ProjectReferenceResolverTests` extended: a temp directory with one, none, and two candidates;
  the key check for an assembly file.
- **Tier 4.** `CompilerDriverExternalAssemblyTests`: `CompilerOptions` with `AssemblyPaths` pointing at a
  fixture assembly under `Test/Fixtures/`, no MSBuild; the driver binds a call into it and reaches emit.
- **Tier 5.** `CSharpReferences_RunsFromACleanTree`: the sample of Requirement 11.7 through the prebuilt
  CLI with `--no-build`, as the existing Tier 5 tests run it, asserting the output and the presence of the
  facade in `bin/`. It is the one test that runs MSBuild, and it is justified as the only tier that can.

No test compiles a program to watch it fail; refusals are proven beside the rules that report them.

## Alternatives Considered

- **Per-project emission.** One Roslyn compilation per Mist project with `Standard` as a shared assembly,
  so a C# project references a Mist project's csproj. Measured on 2026-10-24 over `AllFeaturesV2`: one
  compilation 0.39 to 0.41 s warm, 103 sequential compilations 1.58 to 1.68 s, parallel 0.53 to 0.60 s.
  The time is acceptable; the cost is elsewhere: `internal` support types stop being visible without
  `InternalsVisibleTo`, and per-instantiation specializations in `__Variadics.g.cs` are declared in a
  sample's namespace and had to move into that sample's compilation. It also makes `Standard` a shared,
  versioned assembly. Right long-term shape for packing and IDE loading; not needed for this feature.
- **Rewriting the external assembly's reference table** to name the primary output. Same runtime effect as
  a facade with fewer files, but it patches a binary the user built, breaks its debug directory, and makes
  the design-time copy and the deployed copy different assemblies.
- **A runtime `AssemblyLoadContext.Resolving` hook.** Puts the fix in the program's startup and the loader
  checks that the returned assembly's simple name matches the request.
- **An explicit `kind` field** or **separate tables** for the new kinds. Rejected on 2026-10-24: a manifest
  is never read apart from its checkout, so naming the kind in the manifest buys nothing the referenced
  directory does not already show.

## Open Items

- Build configuration for referenced C# projects is fixed at `Debug`. A `configuration` field on the entry
  is a small later addition if needed.
- A multi-targeting C# project is built for the Mist project's framework only; a project that does not
  target it fails the build with the SDK's own message.
- The facade pass structure in section 4 is left to the implementer within the stated constraint.
