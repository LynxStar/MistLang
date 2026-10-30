# Design Document

## Overview

This is the design investigation for MIST-1045 (audit item SH-001). It answers one question for the owner's
Decision MIST-1046: which capability does Mist gain so that a Mist-authored Storm regenerates the Mist
frontend from the `.storm` inputs inside the integrated build, and what may the C# seed and pre-generated
sources do until then.

Four parts. The edge as it stands, anchored at MistLang `87f8f3f87698632b038cd3bd1edeb4df215e2032` with the
Storm submodule at `86dfd33895ba15ec7a3187e00340b236e666ab19`. The options, each with its invocation point,
inputs and outputs, staleness rule, residual seed and costs. The transition rule. A recommendation with the
questions the Decision must settle. One option was executed before this was written: the native source
generator spike of 2026-10-29 (`spike/native-source-generators`, commit
`26b4f30e6547670378567301cd9c72c95305ab0a` on base `d9fe430f`), reviewed, re-executed and ported onto main the
same day; its measurements and the eighteen verified defects are the finding on MIST-1045 dated 2026-10-30.
Where a figure below has no other source, it is that finding's.

## The edge as it stands

`Source/Mist` is a C# project. Its frontend is generated at C# compile time by Storm running as a Roslyn
incremental generator:

- `Source/Mist/Mist.csproj` line 30 references `..\Storm.SourceGenerator\Storm.SourceGenerator.csproj` with
  `OutputItemType="Analyzer"` and `ReferenceOutputAssembly="false"`; lines 40 to 44 list the five
  `AdditionalFiles` (`Grammar\Mist.storm`, `Grammar\tokens\tokens.storm`, `keywords.storm`, `operators.storm`,
  `literals.storm`); line 9 sets `StormNodeOriginType` to `Mist.Common.Source.NodeOrigin` and line 36 makes
  it a `CompilerVisibleProperty`; line 7 sets `EmitCompilerGeneratedFiles` so the output can be inspected
  under `obj/`.
- `Source/Storm.SourceGenerator/Storm.SourceGenerator.csproj` line 4 targets `netstandard2.0`, line 7 sets
  `IsRoslynComponent`, and lines 21 and 22 reference `Microsoft.CodeAnalysis.Analyzers` 3.3.4 and
  `Microsoft.CodeAnalysis.CSharp` 4.12.0.
- The adapter is `Source/Storm.SourceGenerator/Generator/StormIncrementalGenerator.cs`. Lines 28 and 29 select
  the additional texts whose path ends in `.storm`; lines 35 to 38 read `build_property.StormNodeOriginType`
  and fall back to `object`; lines 55 to 63 build an in-memory map of the selected files, and every later read
  goes through that map, so an import resolves only to a file that is also an `AdditionalFiles` entry and the
  disk is never read; lines 93 to 134 make the root the one file holding a `grammar` declaration
  (`STORM0010` for two, `STORM0009` for none); lines 140 and 141 resolve imports through
  `Resolve/StormImportResolver.cs` line 89, relative to the importing file, reporting `STORM0002` at the
  import site when a file is missing; lines 155 to 229 run grammar building, validation, trait validation,
  IR lowering, trait IR and memoization analysis, and any error ends the run with no output; lines 233, 237,
  241 and 245 add the four sources.
- Diagnostics are `STORM0001` to `STORM0006` and `STORM0008` to `STORM0025`, declared in
  `Diagnostics/StormDiagnosticIds.cs` lines 11 to 80, carried as a record of identifier, severity, message,
  file, line and column (`Diagnostics/StormDiagnostic.cs` lines 22 to 28) and surfaced as Roslyn diagnostics
  located at that file, line and column (lines 51 to 70).

What flows across the edge:

| Direction | Item | Size at this revision |
|---|---|---|
| in | `Grammar/Mist.storm` (imports `tokens/tokens`) | 796 lines |
| in | `Grammar/tokens/tokens.storm` (imports `keywords`, `operators`, `literals`) | 11 lines |
| in | `Grammar/tokens/keywords.storm`, `operators.storm`, `literals.storm` | 123, 46, 40 lines |
| in | `StormNodeOriginType` | one identifier |
| out | `Storm.Runtime.g.cs`: runtime support types, namespace `Storm.Runtime` | 176 lines |
| out | `Mist.Ast.g.cs`: 423 node classes and trait interfaces, namespace `Mist.Generated` | 5,294 lines |
| out | `Mist.Lexer.g.cs`: `TokenKind` and the table-driven `MistLexer` | 295 lines, 318 KB |
| out | `Mist.Parser.g.cs`: `MistParser`, one method per rule, packrat memoized | 24,430 lines |
| out | diagnostics | 24 identifiers |

`Source/Mist` consumes the output through the namespaces `Mist.Generated` (83 files) and `Storm.Runtime`
(167 files). `Source/Mist/Generated` holds eight handwritten extension classes, 641 lines, none of them
partials of generated types. The node-origin type is `Source/Mist/Common/Source/NodeOrigin.cs` line 6. The
generator has no mode that emits Mist: the only language it names is `LanguageNames.CSharp`. Its tests are
`Storm.SourceGenerator.Tests` (73 files, on synthetic grammars, including a `GeneratorDriver` run) and
`Test/Mist.StormIntegration.Tests` (46 tests, on the real grammar, without MSBuild).

So the edge is five files and one property in, four sources and diagnostics out, with imports resolved
among the declared files only. Any replacement must carry exactly that, and carry the diagnostics with their
locations.

## Architecture

The bootstrap sequence every option must serve:

```
Generation 0 (C#, MSBuild + Roslyn Storm)          <- the seed; built once, outside the Mist toolchain
   │ compiles
   ▼
Mist-authored Storm (a Mist executable project)    <- SC-001..004, SE-01..07
   │ run by Generation N over Grammar_Inputs
   ▼
Frontend_Artifacts (.mist)                          <- SE-06 contract
   │ composed into
   ▼
Mist-authored compiler (Consumer)                   <- SH-003..006
   │ compiled by Generation N
   ▼
Generation N+1                                      <- SH-007 packages, SH-008 proves N+1 == N+2 with no seed
```

Generation N compiles the generator, runs it and compiles the consumer: three steps, one compiler. There is
no earlier generation in the loop once Generation 1 exists. The Roslyn generator lives only in the box at the
top.

Where the host sits in the pipeline, for the options that keep it in the toolchain:

```
CLI build/run/test ──► Source_Discovery ──► CompilerDriver.Compile
                                                │ Stage 1: ReadManifest
                                                ├─ ProjectReferenceLoader.Load   (C# references: dotnet build)
                                                ├─ Generator_Host.Prepare        (generators: compile, run, publish)
                                                ▼
                                            compose projects ──► Stages 2..5 ──► backend
```

`ProjectReferenceLoader.Load` (`Source/Mist/Driver/ProjectReferenceLoader.cs` lines 9 to 26, called from
`CompilerDriver.cs` line 367) already runs `dotnet build` for referenced C# projects before any source is
read, with a 600-second limit, a cycle guard through `MIST_BUILD_CHAIN`, an injectable process runner and a
descriptor-reported failure (`CSharpProjectBuilder.cs` lines 13, 26 to 31, 76 to 118). The host is the same
shape with a Mist program in place of `dotnet build`.

## Options

### Shared mechanics

Options A and B differ only in where outputs land and under what identity. Both need the same five
mechanics, all of which the spike executed:

1. **Declare.** The consumer names the generator project, the inputs and the arguments in `mist.toml`.
2. **Compile the generator** with the current compiler into a content-keyed cache, so a warm build does not
   rebuild it. Measured: a generator rebuilt after deleting its cache entry was byte-identical, so the run
   that depended on it stayed valid.
3. **Run it** as a process with the declared inputs, capturing its output, under a time limit.
4. **Validate and publish** the output list atomically; a failure publishes nothing.
5. **Ingest** the outputs into the consumer's compilation past Source_Discovery.

### Option A: a pre-build tool step whose outputs become the consumer's own sources

- **Invocation point.** Stage 1 of the consumer's build, before source discovery completes. The host runs
  the generator and writes its outputs into a folder the host owns inside the consumer's project, for example
  `generated/<Generator>/`, clearing it first.
- **Inputs and outputs.** Declared inputs and arguments in; `.mist` files that Source_Discovery then picks up
  as the consumer's own sources, under the consumer's name, `namespace_root` and global usings.
- **Staleness.** Content keys as in Requirement 2; the owned folder is rewritten only on a miss.
- **Residual seed.** The owned folder is exactly where Pre_Generated_Sources would sit, and Source_Discovery
  cannot tell a generated file from a checked-in one. Proving a build seed-free means proving that folder was
  written by this build, which needs the Provenance_Record and a marker anyway.
- **Costs.** No language surface. Manifest and driver changes as B. Determinism: equal to B. The source tree
  carries generated files, which must be ignored by git and must never be committed; a removed generator
  must have its folder removed by the host or the stale sources compile. Diagnostics name stable paths for
  free. Generated types sit under the consumer's namespaces without declaring any.
- **Gates.** Nothing in Storm in Mist. SE-05 (MIST-1060, consumer wrappers) is simplest here. SH-007
  (MIST-1072) must package a source tree that contains build output. SH-008 (MIST-1073) must prove the
  folder's provenance, which A makes the weakest point of the design.

### Option B: a build-chain reference to a generated project

This is the spike's design and the one measured.

- **Invocation point.** Stage 1 of every project in the composition, in dependency order, beside C#
  reference builds. The spike placed it in the CLI's `CompileProject` instead, which left direct
  `CompilerDriver.Compile` callers and `inspect` without generators; the driver placement is recommended.
- **Inputs and outputs.** The consumer's `[source_generators]` table names the generator project, the
  inputs and the arguments. A Run's published outputs form a synthesized library project
  `<Consumer>.Generated.<Generator>` that enters the resolver's dependency order as an ordinary reference
  through an overlay, so the generated sources are composed like any referenced project's.
- **Staleness.** Requirement 2 in full. Measured on 2026-10-29: cold build 2.9 s, warm 1.6 s on the spike's
  base; cold 4.2 s, warm 2.1 s on main, against 2.0 s for a plain sample build with the same compiler; a
  touch alone kept the hit; a same-size edit missed; restoring an input returned to the old key.
- **Residual seed.** Generated sources never sit in a source tree, so Pre_Generated_Sources can only enter
  through the explicit declaration of Requirement 6.3. The no-fallback proof reduces to reading manifests
  and Provenance_Records.
- **Costs.** No language surface. Manifest: a project type and one table. Driver: a graph pass over
  generator edges, a compile-and-run host, an overlay into the resolver, a copy into `out/`. Determinism:
  the generator binary must be byte-deterministic for the run key to be stable, which the Mist backend's
  in-process Roslyn emit already is. Generated code must declare its namespaces, since the synthesized
  project has its own identity; the real edge already does (`Mist.Generated`, `Storm.Runtime`). The
  synthesized name must fit the 128-character limit.
- **Gates.** Nothing in Storm in Mist. SE-06 (MIST-1054) gains the generator protocol of Detailed Design 4
  as part of its output contract. SE-05 (MIST-1060) must keep explicit namespaces. SH-006 (MIST-1069) ports
  the host. SH-007 (MIST-1072) and SH-008 (MIST-1073) depend on it, as Carbonek already records.

### Option C: a language-level generator capability

- **Invocation point.** Inside the pipeline, after Stage 1 or Stage 4: the compiler loads generator code and
  runs it against the program model, as Roslyn runs source generators against a compilation.
- **Inputs and outputs.** The program model, additional files and options in; synthesized compilation units
  out, with no process boundary.
- **Staleness.** None needed, since the generator runs on every compile; the cost is paid every build.
- **Residual seed.** The compiler must be able to load and execute user code at compile time, which the C#
  compiler can do through Roslyn's assembly loading and the Mist-authored compiler cannot until it has an
  equivalent; the seed therefore persists until the whole compiler is in Mist and can host dynamic loading.
- **Costs.** New language surface (a declaration marking a generator, an API over the model), a new stage,
  dynamic assembly loading, determinism and sandboxing questions the audit explicitly declined to design,
  and a dependency of every Storm-in-Mist item on the compiler port. Gates SC-001 to SC-004, SE-01 to SE-07
  and SH-003 to SH-006 behind itself.
- **Gates.** Everything above it. Not designed here, by the audit's own scope.

### Option D: a frozen pre-generated seed

Keep the Frontend_Artifacts checked in and regenerate them by hand. The workstream rules this out as
completion. It is the natural form of Pre_Generated_Sources during the transition, and Requirement 6.3
gives it the one shape in which it may be used: declared, attributed and reported, never silent.

### Comparison

| | A: tool step | B: generated reference | C: language-level | D: frozen seed |
|---|---|---|---|---|
| New language surface | none | none | declaration + API | none |
| Manifest | generator table | type + generator table | none or attribute | provenance declaration |
| Driver | host + owned folder | host + overlay + copy | new stage + loading | marker + report |
| Staleness | content keys | content keys | none, runs every build | none |
| Determinism burden | generator | generator + binary | compiler + loader | none |
| Diagnostics path | stable | stable via `out/` copy | in-compilation | n/a |
| Seed-free proof | provenance of an in-tree folder | manifests + records | compiler can host loading | never |
| Executed evidence | shares B's mechanics | yes, 2026-10-29 | none | today's state |
| Gates in Storm in Mist | none | none (adds protocol to SE-06) | all | all |

## Transition rule

1. **Generation 0** is the C# compiler built by MSBuild with the Roslyn Storm generator (`Mist.csproj` line
   30). It compiles Generation 1's sources and nothing else; its Roslyn host, analyzer reference and
   `Microsoft.CodeAnalysis` never enter the Later_Stage_Graph.
2. **The Stage_Zero_Tool** is the C# Storm frontend run as a process that emits Mist under SE-06. The spike's
   bridge is its shape: it links the whole of `Storm.SourceGenerator` and `Microsoft.CodeAnalysis.dll` and
   performs lexing, parsing and import resolution. It is declared exactly where a Mist-authored generator
   is declared, so swapping the two is a one-line manifest change, and every Run it produces carries it in
   the Provenance_Record as a Seed. It may unblock SE-05 and the consumer side of SH-003 to SH-006 before
   SC-001 to SC-004 land. It is never completion.
3. **Pre_Generated_Sources** are a captured output set with its Provenance_Record, consumed only through the
   explicit declaration of Requirement 6.3 and reported by `GeneratorSeedUsed`. They may unblock a
   consumer before any generator runs. They are never completion.
4. **Removal criterion.** A build is closed when no manifest in it declares a Stage_Zero_Tool or
   Pre_Generated_Sources, every Frontend_Artifact was produced in that build by a generator compiled from
   Mist sources by Generation N, and every Provenance_Record names only Grammar_Inputs, that generator and
   Generation N's Toolchain_Identity. SH-008 proves it by building N+1 from N and N+2 from N+1 with an empty
   cache and comparing attributable hashes; the absence of the seed diagnostic and of any seed declaration
   is the no-fallback evidence.

## Components

For option B, the recommended design.

### New

| Component | Location | Role |
|---|---|---|
| `GeneratorReference` | `Source/Mist/Driver/Contracts/GeneratorReference.cs` | One `[source_generators]` entry: name, project path, inputs, arguments, time limit. |
| `MistProjectType` | `Source/Mist/Driver/Contracts/MistProjectType.cs` | `Regular` or `SourceGenerator`. |
| `GeneratorGraph` | `Source/Mist/Driver/Generators/GeneratorGraph.cs` | Pure: dependency order over generator edges only, cycle refusal naming the chain. Ordinary edges are the resolver's. |
| `GeneratorKeys` | `Source/Mist/Driver/Generators/GeneratorKeys.cs` | Pure: compile key, run key and Toolchain_Identity over precomputed hashes with length-delimited sections. |
| `OutputListValidation` | `Source/Mist/Driver/Generators/OutputListValidation.cs` | Pure: the refusals of Requirement 3.2 over the listed and the actual relative paths. |
| `GeneratorHost` | `Source/Mist/Driver/Generators/GeneratorHost.cs` | Compiles, runs, validates and publishes; takes an injectable runner like `CSharpProjectBuilder`. |
| `GeneratorCache` | `Source/Mist/Driver/Generators/GeneratorCache.cs` | Content-addressed store under the cache root; Provenance_Record read and write; atomic publish; waiting locks. |
| Generator descriptors | `Source/Mist/Common/Diagnostics/Descriptors/DriverDiagnostics.cs` | The ten Descriptors of Requirement 4.3, beside `CSharpProjectBuildFailed`. |

### Modified

| Component | Change |
|---|---|
| `ProjectFileParser` | Reads `project_type` and `[source_generators]`; refuses a library generator and an ordinary reference to a generator (5.1, 5.3). |
| `ProjectConfiguration` | Carries `ProjectType` and `Generators`. |
| `CompilerDriver.ReadManifest` | Calls `GeneratorHost.Prepare` beside `ProjectReferenceLoader.Load`; passes the overlay into resolution. |
| `ProjectReferenceResolver` | Accepts a per-manifest overlay of synthesized `ProjectConfiguration`s and visits them in order; judges nothing about them but order. |
| `CompilerOptions` | Carries the overlay for callers that prepare generators themselves. |
| `ProjectLayout` / CLI | `out/generated/<Generator>/` as the stable load path; `mist clean --generators`. |

## Data Models

```csharp
public enum MistProjectType { Regular, SourceGenerator }

public sealed record GeneratorReference(
    string Name,
    string ProjectPath,                 // canonical mist.toml path, same rules as a reference path
    ImmutableArray<string> Inputs,      // canonical paths, declared order
    ImmutableArray<string> Arguments,
    TimeSpan TimeLimit);                // default 600 s

public sealed record ProvenanceRecord(
    string ToolchainIdentity,
    string GeneratorIdentity,           // compile key of the generator used
    string ConsumerName,
    string ConsumerManifestPath,
    ImmutableArray<(string Path, string Sha256)> Inputs,
    ImmutableArray<string> Arguments,
    string RunKey,
    ImmutableArray<(string Path, string Sha256)> Outputs,
    bool SeedTool);                     // true when the generator is a declared Stage_Zero_Tool

public sealed record GeneratedProject(
    string Name,                        // <Consumer>.Generated.<Generator>, length-checked
    string LoadFolder,                  // <consumer>/out/generated/<Generator>
    ProvenanceRecord Provenance);
```

## Detailed Design

### 1. Declaration

A generator project:

```toml
name = "StormMist"
project_type = "source_generator"
entry_point = "StormMist.Program"

[references]
StormCore = { path = "../StormCore" }
```

A consumer:

```toml
name = "Mist"

[source_generators]
StormMist = { path = "../../Storm/StormMist",
              inputs = ["Grammar/Mist.storm", "Grammar/tokens/tokens.storm", "Grammar/tokens/keywords.storm",
                        "Grammar/tokens/operators.storm", "Grammar/tokens/literals.storm"],
              arguments = ["Mist.Common.Source.NodeOrigin"],
              timeout_seconds = 600 }
```

`project_type` defaults to `regular`. A generator is an executable; `output_type = "library"` is
`GeneratorProjectInvalid`. Paths resolve against the declaring manifest exactly as `[references]`
paths do (`named-references`). A `[references]` entry whose target is a generator is
`GeneratorReferenceInvalid`, as is a `[source_generators]` entry whose target is not one. The names
`project_type` and `source_generators` are proposals for the Decision; the spike used them.

A Stage_Zero_Tool is declared in the same table with `seed = true`, which marks every Run's
Provenance_Record. Pre_Generated_Sources are declared as `seed_sources = { path = "...", provenance = "..." }`
and reported by `GeneratorSeedUsed`.

### 2. The graph

`GeneratorGraph` loads generator edges only. The ordinary resolver supplies the already-resolved Mist
configurations for each consumer and generator; the graph visits those known edges to order transitive
consumers and detect mixed cycles before compiling or running anything. It never loads an ordinary
reference's manifest itself and never parses a C# project or assembly. Ordinary-only cycles, name
matching, missing manifests and external-reference classification remain `ProjectReferenceResolver`'s.
This shared graph resolves the generator-only traversal conflict under the operator's clarification in
run `6ac521f4-16cc-4f50-8a13-3ebf9c5d7843`.

### 3. Compiling the generator

The host compiles a generator with the same driver, into the cache under its compile key, for the host
runtime, with no `--rid` or `--platform` from the consumer. The emitted C# is compiled in process by
Roslyn, deterministic, so the DLL hash in the run key is stable across rebuilds: the spike measured a
byte-identical rebuild. A generator that fails to compile is `GeneratorBuildFailed` at the
generator's manifest. The generator's own diagnostics are reported as they are, at its sources, and never
copied into a Slot (`diagnostics` Requirement 7.8).

### 4. Running the generator: the protocol

The host creates a staging directory, copies every Declared_Input into `staging/inputs/` preserving its
path relative to the consumer's folder, and writes `staging/request.txt`: one line per input, the copied
absolute path, in declared order, followed by a blank line and the arguments. It then runs

```
dotnet <generator>.dll <staging>
```

with the staging directory as working directory and a clean environment plus `MIST_GENERATOR_STAGING`. The
generator reads `request.txt`, resolves imports among the listed files only, writes its `.mist` outputs
under `staging/`, lists them in `staging/outputs.txt`, one relative path per line, and writes
`staging/diagnostics.txt`, one line per diagnostic in the unified text form. Exit zero means the list is
complete. The host validates the list (Requirement 3.2), reports every diagnostic through the bag under the
wrappers of Requirement 4.2 and fails on any error among them, then publishes.

Copying the inputs is what makes the declared list complete by construction: a Storm import of a file that
was not declared resolves to a path that does not exist in `staging/inputs/` and fails inside Storm with
`STORM0002` at the import site, exactly as the Roslyn adapter fails today. The spike passed original paths
and got that behavior only from the bridge's own allow-list.

A Stage_Zero_Tool speaks the same protocol, which is why it can be swapped for the Mist-authored generator
without touching the consumer.

### 5. Ingesting the outputs

The published Run's outputs are copied into `<consumer>/out/generated/<Generator>/`, which the CLI cleans on
every build and Source_Discovery excludes, and a synthesized `ProjectConfiguration` named
`<Consumer>.Generated.<Generator>` with `output_type = "library"`, the consumer's `namespace_root` and global
usings, is handed to `ProjectReferenceResolver` through the overlay keyed by the consumer's canonical
manifest path. The resolver visits it after the consumer's ordinary references, so a generated source may
use any referenced type, and composition is flat, so the consumer and its other references see the
generated types. Diagnostics in generated code name the `out/generated/` path, stable across runs.

### 6. Cache and identity

The cache root is `MIST_CACHE_ROOT`, defaulting to the user's cache directory, under
`generators/<ToolchainIdentity>/compiled/<key>` and `.../runs/<key>`. The Toolchain_Identity hashes the
compiler's own assemblies, the bundled Standard sources, the resolved reference assembly paths and versions
from `SdkReferencePathCache`, and `Environment.Version`, with length-delimited sections. It spawns nothing
and reads no working directory. Because the compiler's assemblies embed the source revision, every compiler
commit starts a new identity and a cold cache; the spike observed this when a rebuild after its own commit
missed every entry. That is correct and cheap for users, and expensive only for compiler developers, who
may pin `MIST_CACHE_ROOT` per worktree.

Publication writes the Provenance_Record last and renames the staging directory into place; a second
publisher of the same key finds the rename refused and reuses the winner. A compile or run in progress
holds a lock file beside its destination; a second builder waits on it up to the time limit, then reads
the published result. Reuse never locks.

### 7. Diagnostics

Generator diagnostics arrive as data (Detailed Design 4). Each line becomes an Occurrence of
`GeneratorReportedError` or `GeneratorReportedWarning` at the line's file, line and column, with the
generator's code as its Provider_Code, the way `RoslynDiagnosticAdapter` carries a Roslyn error under
`MIST0003`. Host failures are the Descriptors of Requirement 4.3, declared in `DriverDiagnostics` beside
`CSharpProjectBuildFailed` with named Slots, built through `DiagnosticFactory` at the declaring manifest,
never from an exception message. Each new Descriptor takes the next Number under `diagnostics` Requirement
1.4, adds its row to the Tier 1 text-freeze table, and the change regenerates the catalog with
`tools/diagnostics/catalog.sh` so the staleness check passes.

### 8. CLI and placement

Nothing new in the CLI except `clean --generators` and the `out/generated/` layout. `build`, `run`, `test`
and `inspect` already reach `CompilerDriver.ReadManifest` or `ProjectReferenceLoader.Load`, which is where
`GeneratorHost.Prepare` runs. The host writes nothing to the console; cache events are progress-reporter
details, invisible under `--quiet`, and the generator's output is captured.

## Error Handling

| Situation | Where | Diagnostic |
|---|---|---|
| `project_type = "source_generator"` with `output_type = "library"` | parser | `GeneratorProjectInvalid` |
| `[source_generators]` entry without `path` or `inputs`, or an empty `inputs` | parser | `GeneratorReferenceInvalid` |
| `[references]` entry naming a generator project | host, at the declaring manifest | `GeneratorReferenceInvalid` |
| `[source_generators]` entry naming a regular project | host | `GeneratorReferenceInvalid` |
| a Declared_Input missing | host | `GeneratorInputNotFound` |
| consumer → generator → consumer, generator → itself | host, before any compile | `GeneratorCycle` |
| ordinary-only cycle | resolver | `MIST0064` |
| generator fails to compile | host, at the generator's manifest | `GeneratorBuildFailed` |
| nonzero exit or time limit | host | `GeneratorRunFailed` |
| output list empty, duplicate, escaping, missing, unlisted, under `out`/`bin`/marker | host | `GeneratorOutputInvalid` |
| a line in `diagnostics.txt` | host, at the line's location, the generator's code as Provider_Code | `GeneratorReportedError` or `GeneratorReportedWarning` |
| synthesized name over 128 characters | host | `GeneratorReferenceInvalid` |
| Pre_Generated_Sources consumed | host | `GeneratorSeedUsed` (warning) |
| concurrent builder holds the lock past the limit | host | `GeneratorRunFailed` |

## Correctness Properties

1. **No stale output compiles.** Every generated source the consumer compiles came from a Run whose
   Provenance_Record matches the current key over every declared input's content; a change to any input,
   argument, generator source, generator reference, consumer identity or toolchain misses.
2. **No undeclared input is read silently.** A generator that imports an undeclared file fails in the
   generator at the import site, because the file is absent from the staging copy.
3. **No partial publication.** A Run is visible only after validation, by one rename, with its record last.
4. **Seed use is visible.** Every build that used a Stage_Zero_Tool or Pre_Generated_Sources says so in its
   diagnostics and its records, and a closed build says nothing.
5. **Ordinary references are untouched.** A project with no generator edge builds exactly as before the
   capability, including C# project and assembly references.
6. **Warm builds are quiet and fast.** A hit reads records and hashes, takes no lock, modifies nothing and
   prints nothing under `--quiet`.

## Testing Strategy

The binding rules are the root `AGENTS.md` "Tests" section and `.claude/skills/testing-philosophy/SKILL.md`.

- **Tier 1.** `GeneratorKeysTests`: section boundaries, order, every key ingredient including the consumer's
  identity and argument boundaries (2.1, 2.2, 8.1). `OutputListValidationTests`: one case per refusal of
  3.2 (8.2). `ProjectFileParserGeneratorTests`: the refusals of Requirement 5 over in-memory tables (8.3).
  `GeneratorHostTests` with a stubbed runner: compile hit and miss, run hit and miss, timeout, nonzero exit,
  invalid list, diagnostics re-reported, nothing published on failure (8.5).
- **Tier 2.** `GeneratorGraphTests`: order, diamond, shared generator, nested generator, every cycle shape,
  and that an ordinary edge of any kind is never loaded (4.5, 5.5, 8.4). `GeneratedReferenceOverlayTests`:
  root and transitive overlay order with stub loaders (8.7).
- **Tier 3.** `GeneratorCacheTests` over a temporary directory: publish, verify, same-size same-mtime
  tampering, missing and extra files, atomic rename against a competing publisher (2.4, 7.4, 8.6).
- **Tier 6.** One sample with a pure-Mist generator referenced from `AllFeaturesV2`, whose output line is in
  the baseline (8.8).
- **Demonstration.** The real edge regenerated under the capability once SE-06 defines the Mist output, with
  the Stage_Zero_Tool first and the Mist-authored Storm after; recorded as findings on MIST-1072.

No test compiles a program to watch it fail; refusals are proven beside the rules that report them.

## Alternatives Considered

- **Running the host from the CLI only**, as the spike did. Rejected: `inspect`, direct driver callers and
  any future entry point would compile a consumer without its generated sources, and the C# reference
  build already lives in Stage 1.
- **A cache inside the project tree** (`.mist-source-generators/` beside the sources), as the spike did.
  Rejected: it needs an ignore rule in every consumer, grows without bound, fails for a read-only generator
  folder, and relies on a marker file to stay out of Source_Discovery.
- **Passing original input paths** to the generator and trusting its declarations. Rejected: an undeclared
  import then reads the source tree and the key never learns of it; copying 1,016 lines costs nothing.
- **Tracking file reads** with a sandbox or a file-system watcher to detect undeclared inputs. Rejected:
  not portable, and unnecessary once inputs are copied.
- **Diagnostics on stderr** parsed by pattern. Rejected in favor of a file: a generator's stderr also carries
  crashes and tool noise, and the file is the same unified format with no ambiguity.
- **Positional inputs on the command line**, as the spike did. Rejected: argument-list limits with large
  input sets and no section boundary between inputs and arguments; a request file has neither problem.
- **Writing outputs into the consumer's tree** (option A). Rejected for the recommendation, kept as an
  option: it is the one place a seed can hide.
- **A language-level capability** (option C). Not designed, by the audit's scope; the cost is the whole
  compiler port before the first generator runs.

## Recommendation

Option B, placed in the driver's Stage 1 beside C# reference builds, with the spike's seam as its measured
shape and the requirements above closing the spike's defects: generator-only graph walk, consumer identity
in the run key, waiting locks, copied inputs, a diagnostics file in the protocol, descriptor-declared host
diagnostics, a user cache, and a stable load path under `out/`. It adds no language surface, it was
executed end to end on 2026-10-29 on both the spike's base and main, and it makes the no-fallback proof a
matter of reading manifests and records rather than auditing a source tree. The transition is the
Stage_Zero_Tool of Detailed Design 1, swapped for the Mist-authored Storm by a manifest edit when SC-001 to
SC-004 and SE-01 to SE-07 land.

## Decision register

The owner settled the following on 2026-10-30 (MIST-1046). Each row names the requirements that carry it.

| Decision | Settled as | Requirements |
|---|---|---|
| D01 Capability | Option B: a build-chain reference. A generator is an executable Mist project; the consumer declares it with inputs and arguments; the toolchain compiles it, runs it, validates its output list and composes the outputs as a synthesized library reference. | 1, 3, 5 |
| D02 Invocation point | Stage 1 of every project in the composition, beside C# reference builds, so `build`, `run`, `test` and `inspect` all obtain generated sources. | 1.4, 1.5 |
| D03 Inputs, outputs, staleness | Declared inputs copied into the run's staging directory and passed by a request file; `.mist` outputs, an output list and a diagnostics file in the unified text form out; reuse only under the content key of Requirement 2, every cached file re-verified by hash, timestamps never in the key. | 2, 3, 4.1 |
| D04 Seed during the transition | Generation 0 stays the C# compiler built with the Roslyn generator and builds generation 1 only. The C# Storm frontend may run as a stage-zero tool declared with `seed = true`; pre-generated sources enter only through `seed_sources` with a provenance record, and the build warns when it used them. | 6.1, 6.2, 6.3 |
| D05 Removal criterion | A build is closed when no manifest declares a stage-zero tool or seed sources, every frontend artifact was produced in that build by a generator compiled from Mist sources by generation N, and every provenance record names only the grammar inputs, that generator and generation N's identity; SH-008 proves it with an empty cache across two generations. | 6.4 |
| D06 Manifest spellings | `project_type = "source_generator"`, a `[source_generators]` table with `path`, `inputs`, `arguments` and `timeout_seconds`, `seed = true` on a stage-zero tool, and `seed_sources = { path, provenance }`. | 5 |
| D07 Cache location | One shared cache under `MIST_CACHE_ROOT`, defaulting to the user's cache directory, keyed by toolchain identity, never inside a project. | 7.2, 7.3 |
| D08 Concurrency | NuGet-style: the provenance record is written last as the completion marker, a miss takes a per-key lock, a second builder waits up to the time limit, re-checks when the lock opens and reuses what was published; reuse never locks. | 7.1, 7.4 |
| D09 Diagnostics protocol | One unified-format line per diagnostic in a file beside the output list; warnings are reported and do not fail the build; an error fails it even on exit zero. | 4.1, 4.2 |
| D10 Namespaces | Generated code declares its namespaces explicitly; the synthesized project does not lend the consumer's `namespace_root` to inferred namespaces. | 5, Detailed Design 5 |
| D11 Package distribution | None: a generator is a project in the checkout. | 9.5 |
| D12 The stage-zero tool | The C# Storm frontend run as a process speaking the generator protocol; generation 0's Roslyn output may be captured as declared seed sources for the first consumer builds, warned about like any seed. | 6.2, 6.3 |

The implementation children are created under Self Hosting/Source Generation with this spec as their source
reference.

## Open Items

Settled on 2026-10-30; see the decision register. The list below is kept as the record of what the
investigation left open:

1. **The capability.** Option B as recommended, or A, with the transition rule of this design in either
   case.
2. **Names in the manifest.** `project_type` and `[source_generators]` as proposed, or other spellings; and
   whether `seed = true` and `seed_sources` are the shape of the seed declarations.
3. **The cache location.** A user cache under `MIST_CACHE_ROOT` as proposed, or per repository.
4. **The concurrency guarantee.** Wait up to the time limit as proposed, or fail fast with a diagnostic.
5. **The diagnostics protocol.** The unified text form in a file as proposed, or a structured document, and
   whether a generator may also report warnings that do not fail the build.
6. **Namespace policy for generated code.** Explicit namespaces required as proposed, or the generated
   project inheriting the consumer's `namespace_root` for inferred namespaces.
7. **Package distribution.** Whether a generator may ever come from a package rather than the checkout;
   this spec says no.
8. **What the Stage_Zero_Tool may be.** The C# Storm frontend as a process only, or also Generation 0's
   Roslyn output captured as Pre_Generated_Sources for the first consumer builds.
