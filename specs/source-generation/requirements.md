# Requirements Document

## Introduction

Mist has no source-generator capability. The compiler's own frontend is produced today by Storm running as a
Roslyn incremental generator inside the C# build of `Source/Mist`: the project references
`Storm.SourceGenerator` as an analyzer, hands it five `.storm` files as `AdditionalFiles` and one MSBuild
property, and receives four C# sources back inside the compilation. That edge is the last place in the
self-hosting plan where a handwritten C# implementation and a Roslyn host are unavoidable. The Self Hosting
workstream (MIST-1038 to MIST-1073) requires that generation 1 regenerate the frontend from the `.storm`
inputs and build generation 2 with no handwritten C# Mist or Storm implementation compiled, and that a
C# Storm emitting Mist text, or pre-generated sources kept in the final graph, count as transition only.

This spec is the design investigation MIST-1045 asked for (audit item SH-001). It states the edge as it
stands, enumerates the ways a Mist-authored Storm can supply the generated frontend to a Mist build without
a Roslyn generator, names their costs, states what a pinned stage-zero C# tool and pre-generated sources may
do during the transition and the criterion that removes them, and ends with a recommendation and the
questions the Decision MIST-1046 must settle. The choice itself is the owner's; nothing here is implemented
under this task.

The owner recorded the decision on 2026-10-30 (MIST-1046): option B, the build-chain reference, with a
shared NuGet-style cache; the design's decision register holds the twelve settled points. The requirements
below were written to bind whichever capability the Decision recorded and now bind that one. They were
written against an executed probe: the native source generator spike of 2026-10-29 (branch `spike/native-source-generators`,
commit `26b4f30e`, reviewed and re-executed against main `87f8f3f8`), whose measurements and defects are
recorded as a finding on MIST-1045 dated 2026-10-30. Where a requirement closes a defect that probe
exposed, the design says so. This spec layers on `project-based-build-model`, `project-references`,
`named-references`, `csharp-and-assembly-references`, `compiler-progress-output` and `diagnostics`; where it
restates one of their rules it does so only to extend it.

Out of scope: a language-level generator capability with access to the program model, generator sandboxing
or purity enforcement, incremental compilation of the consumer, generators that emit C#, and MSBuild as a
driver of Mist builds.

## Glossary

- **Grammar_Inputs**: the `.storm` files the frontend is generated from. Today `Source/Mist/Grammar/Mist.storm`
  and the four files under `Source/Mist/Grammar/tokens/` it imports, 1,016 lines in all.
- **Frontend_Artifacts**: the sources the Storm_Generator produces from the Grammar_Inputs: the shared runtime,
  the AST, the lexer and the parser. Today four C# files of 30,195 lines in the namespaces `Storm.Runtime`
  and `Mist.Generated`; under self-hosting, Mist sources as the Storm Mist output contract (SE-06, MIST-1054)
  defines them.
- **Storm_Generator**: the program that turns Grammar_Inputs into Frontend_Artifacts. Today the C# Roslyn
  component under `Source/Storm.SourceGenerator`; in the end state the Mist-authored Storm of
  `Self Hosting/Storm in Mist` (MIST-1064 to MIST-1067, MIST-1054 to MIST-1060).
- **Consumer**: the project whose compilation includes the Frontend_Artifacts. Today the C# project
  `Source/Mist`; in the end state the Mist-authored compiler (MIST-1061, MIST-1063, MIST-1068, MIST-1069).
- **Generator_Host**: the part of the Mist toolchain that compiles a Storm_Generator, runs it over the
  Declared_Inputs and places its outputs in the Consumer's compilation. It is the capability this spec asks
  the Decision to choose.
- **Generator_Reference**: the Consumer's manifest entry naming a Storm_Generator, its Declared_Inputs and
  its arguments.
- **Declared_Inputs**: the files a Generator_Reference names as the generator's inputs, in declared order.
- **Run**: one execution of a compiled Storm_Generator over one set of Declared_Inputs and arguments, with
  its outputs, its output list and its diagnostics.
- **Staleness_Key**: the content-derived identity under which a Run's outputs are reused instead of
  regenerated.
- **Toolchain_Identity**: the identity of the compiler generation that hosts the Run: its own assemblies,
  its bundled Standard, the reference assemblies it compiles against and the runtime it runs on.
- **Provenance_Record**: the record kept with a published Run or with Pre_Generated_Sources naming the
  Toolchain_Identity, the generator's compiled identity, every input path with its content hash, the
  arguments and the Staleness_Key.
- **Generation_N**: a compiler binary in the bootstrap sequence. Generation 0 is the C# seed compiler built
  by MSBuild with the Roslyn generator; Generation N+1 is built by Generation N.
- **Seed**: an input to a build that no earlier generation produced: a Stage_Zero_Tool or Pre_Generated_Sources.
- **Stage_Zero_Tool**: a C#-built program standing in for the Storm_Generator during the transition: the C#
  Storm frontend run as a process, or Generation 0 itself.
- **Pre_Generated_Sources**: Frontend_Artifacts checked in or captured from an earlier generation rather than
  produced by the current build's Run.
- **Later_Stage_Graph**: the build graph from Generation 1 onward, in which no Seed may remain.
- **Integrated_Build**: a build of the Mist compiler from declared seed inputs that produces Generation N+1
  (SH-007, MIST-1072).
- **Output_Directory**: the consumer's `out` folder, as `project-based-build-model` defines it; cleaned by the CLI on
  every build and excluded from Source_Discovery.
- **Source_Discovery**: the rule of `project-based-build-model` Requirement 2 that enumerates a project's
  `.mist` sources and excludes `out`, `bin` and any directory holding a `.mist-generated` file.
- **Project_File_Parser**: `ProjectFileParser`, which reads `mist.toml`.
- **Compiler_Driver**: `CompilerDriver`, which reads the manifest, resolves references and runs the pipeline.
- **CLI**: `Source/Mist.CLI`, whose `build`, `run` and `test` commands share one compile path.

## Requirements

### Requirement 1: Regeneration inside the integrated build

**User Story:** As a compiler maintainer, I want the Mist frontend regenerated from the `.storm` inputs by
the Mist toolchain itself, so that generation 2 is built with no Roslyn generator and no handwritten C#
Storm in its graph.

#### Acceptance Criteria

1. THE Integrated_Build SHALL obtain the Frontend_Artifacts by running the Storm_Generator over the
   Grammar_Inputs during `mist build` of the Consumer, with no MSBuild target, Roslyn analyzer or script
   outside the Mist toolchain in the Later_Stage_Graph.
2. THE Generator_Host SHALL accept a Storm_Generator that is an ordinary Mist executable project, compiled
   by the same generation that compiles the Consumer, so that Generation N+1 depends on no binary other
   than Generation N and the declared Seed.
3. THE Consumer SHALL declare its Generator_Reference in `mist.toml`: the generator project by path, every
   Declared_Input, and the ordered arguments. For the real edge these are the five Grammar_Inputs and the
   node-origin type name that `StormNodeOriginType` carries today.
4. THE Generator_Host SHALL run wherever the Compiler_Driver reads a manifest and builds its C# references,
   before any source is read, so that `build`, `run`, `test` and `inspect` all obtain generated sources and
   none can compile a Consumer without them.
5. THE Generator_Host SHALL run the generator of every project in the composition, not only the root's, in
   dependency order, so that a referenced Mist project with a Generator_Reference is complete when it is
   composed.
6. THE Generator_Host SHALL hand Frontend_Artifacts to the Compiler_Driver as sources that Source_Discovery
   would otherwise never see, so that a removed or failed generator can leave no source behind
   (`project-based-build-model` Requirement 2.6).

### Requirement 2: Staleness by content

**User Story:** As a Mist developer, I want a generator rerun exactly when something it depends on changed,
so that warm builds are fast and no edit is missed.

#### Acceptance Criteria

1. THE Generator_Host SHALL reuse a Run only under a Staleness_Key over the Toolchain_Identity, the
   generator's compiled identity, the Consumer's name and manifest path, the ordered arguments, and the path
   and content hash of every Declared_Input. Timestamps and sizes SHALL NOT enter the key. The spike
   measured this rule over same-size, same-mtime edits on 2026-10-29; the Consumer's identity was the one
   input it omitted.
2. THE Generator_Host SHALL key a compiled generator by the Toolchain_Identity, the generator's manifest and
   sources, the identity of every project it references, and the host's runtime identifier. A generator
   SHALL be compiled for the host it runs on; the Consumer's `--rid` and `--platform` SHALL NOT enter its key
   or its build.
3. THE Toolchain_Identity SHALL be derived from the compiler's own assemblies, its bundled Standard sources,
   the reference assemblies the backend resolved and the runtime version. It SHALL NOT depend on the process
   working directory or on invoking the SDK, so that a `global.json` above the user's shell cannot change or
   abort a build.
4. WHEN a cached Run is reused, THE Generator_Host SHALL verify every published file by content hash against
   the Provenance_Record, and a missing, edited or extra file SHALL force regeneration.
5. WHEN a cached Run is reused, THE Generator_Host SHALL leave its files unmodified.
6. THE Generator_Host SHALL present the Declared_Inputs to the generator as copies inside the Run's staging
   directory, preserving their layout relative to the Consumer's folder, and SHALL pass only those paths, so
   that an import of an undeclared file fails inside the generator with a diagnostic naming the missing file
   instead of silently reading the source tree. The Roslyn adapter resolves imports among its
   `AdditionalFiles` alone today and never reads the disk; the Mist-authored Storm SHALL keep that rule
   (MIST-1054).
7. THE Generator_Host SHALL write the Provenance_Record beside every published Run, so that an Integrated_Build
   can attribute every generated source to its inputs and generator (MIST-1072).

### Requirement 3: Failure isolation and the output contract

**User Story:** As a Mist developer, I want a failed or misbehaving generator to leave nothing behind, so
that the next build cannot compile half an output set.

#### Acceptance Criteria

1. THE Generator_Host SHALL give each Run a fresh staging directory and SHALL publish it only after the
   generator exited zero and the output list validated.
2. THE Generator_Host SHALL require the generator to list its outputs, and SHALL refuse a list that is empty,
   repeats a path, names a path that is absolute or escapes the staging directory, names a file that does not
   exist or is not `.mist`, omits a `.mist` file present under staging, or names a path under `out`, `bin` or
   a directory holding `.mist-generated`, which Source_Discovery would drop without a word.
3. IF the generator exits nonzero, exceeds its time limit or fails validation, THEN THE Generator_Host SHALL
   delete the staging directory, publish nothing, leave previously published Runs untouched and report the
   failure under Requirement 4, and the Consumer SHALL NOT be compiled.
4. THE time limit SHALL be declared per Generator_Reference with a default of 600 seconds, the limit the
   driver gives a referenced C# project build.
5. THE Generator_Host SHALL capture the generator's stdout and stderr and SHALL NOT echo them; on failure the
   lines that carry errors SHALL be summarized into the diagnostic, as `CSharpProjectBuilder` does for
   `dotnet build`.
6. THE Generator_Host SHALL copy the published outputs of the Run in use into the Consumer's Output_Directory
   under a path stable across Runs, and the Compiler_Driver SHALL load them from there, so that a diagnostic
   in a generated source names a path an editor can open and that the CLI's clean already removes.

### Requirement 4: Diagnostics

**User Story:** As a Mist developer, I want a grammar error to surface at its file, line and column, and a
host failure to name what was wrong, so that generation failures are diagnosed like any other.

#### Acceptance Criteria

1. THE protocol between host and generator SHALL carry diagnostics as data, not prose: a file beside the
   output list holding one diagnostic per line in the text form of `unified-diagnostic-formatting`
   Requirement 5, `<file>(<line>,<column>): <severity> <id>: <message>`, so that Storm's own codes
   (`STORM0001` to `STORM0025` today) reach the user unchanged, as Provider_Codes, with their locations.
2. THE Generator_Host SHALL report each diagnostic the generator wrote through the Compiler_Driver's
   diagnostic bag as an Occurrence of `GeneratorReportedError` or `GeneratorReportedWarning`, chosen by the
   line's severity, carrying the generator's code as a Provider_Code qualified by the generator's name and
   its message unparsed in the wrapper's single `Text` Slot (`diagnostics` Requirements 1.8 and 7.11). An
   error among them SHALL fail the build even when the generator exited zero.
3. THE Generator_Host SHALL report its own failures, and wrap the generator's, through Descriptors in the
   `diagnostics` Registry, one per situation, each with its own Number allocated under `diagnostics`
   Requirement 1.4, the fields of its Requirement 2, named Slots and no exception text. A field name below
   stands for the Number it receives; no `MIST_` spelling is emitted (`diagnostics` Requirement 1.10):

   | Descriptor | Category | Reported when |
   |---|---|---|
   | `GeneratorProjectInvalid` | `CFG` | a generator project declares a type or output the host does not accept (5.1) |
   | `GeneratorReferenceInvalid` | `CFG` | a Generator_Reference is malformed or names a project that is not a generator (5.2, 5.3) |
   | `GeneratorInputNotFound` | `CFG` | a Declared_Input does not exist |
   | `GeneratorCycle` | `DEP` | a generator edge closes a cycle (5.5) |
   | `GeneratorBuildFailed` | `DEP` | the generator project did not compile |
   | `GeneratorRunFailed` | `IOX` | the generator exited nonzero or timed out (3.3) |
   | `GeneratorOutputInvalid` | `IOX` | the output list failed validation (3.2) |
   | `GeneratorSeedUsed` | `CFG` | a build consumed Pre_Generated_Sources (6.3); a `Promotable` warning, so a closed build can raise it to an error |
   | `GeneratorReportedError` | `IOX` | the generator wrote an `error` line (4.2) |
   | `GeneratorReportedWarning` | `IOX` | the generator wrote a `warning` line (4.2); `Promotable` |

4. Each host diagnostic SHALL be located at the manifest that declares the thing at fault: the Consumer's
   `mist.toml` for a Generator_Reference, its inputs and its Run; the generator's `mist.toml` for its own
   configuration and build.
5. THE Generator_Host SHALL NOT judge ordinary `[references]` edges. Name matching, cycles and missing
   manifests for those edges remain `ProjectReferenceResolver`'s, reported under its Descriptors, and a
   reference of a kind other than a Mist project SHALL pass through the host untouched. The spike's graph
   walk parsed every referenced folder as a Mist manifest and refused every C# project and assembly
   reference on main.
6. THE Generator_Host SHALL write no console output of its own; its progress SHALL go through the
   progress reporter and its diagnostics through the feedback writer, so that `--quiet` keeps the promise
   `compiler-progress-output` Requirements 10.2 and 12.5 make.

### Requirement 5: Declaration

**User Story:** As a Mist developer, I want to declare a generator the way I declare a reference, so that
the manifest stays one file with one shape.

#### Acceptance Criteria

1. A generator project SHALL be marked as such in its own `mist.toml` and SHALL be an executable; THE
   Project_File_Parser SHALL refuse a generator that declares `output_type = "library"`.
2. THE Consumer SHALL declare generators in a table of their own, distinct from `[references]`, each entry
   naming the generator project by a path resolved against the declaring manifest under the same rules as a
   reference path, its Declared_Inputs as paths resolved the same way, its arguments as an ordered array of
   strings, and optionally its time limit.
3. THE Project_File_Parser SHALL refuse an ordinary `[references]` entry whose target is a generator project,
   and THE Generator_Host SHALL refuse a generator entry whose target is not one, so that a generator enters
   a build only through a generator edge.
4. A generator project MAY declare ordinary `[references]` to Mist libraries, which are compiled into it.
5. THE Generator_Host SHALL refuse a cycle through any mix of generator and ordinary edges before compiling
   or running anything, naming the chain.
6. Every path and name in a Generator_Reference SHALL be validated with the diagnostics of Requirement 4.3,
   and the synthesized identity of a generated output set SHALL respect the 128-character limit
   `named-references` places on a reference key, since it is used as one.

### Requirement 6: The seed and its removal

**User Story:** As a compiler maintainer, I want every transitional C# tool and every pre-generated source
identifiable and removable, so that self-hosting can be proven rather than asserted.

#### Acceptance Criteria

1. Generation 0 SHALL be the C# compiler built by MSBuild with the Roslyn Storm generator, and it MAY remain
   the compiler that builds Generation 1. Nothing it embeds (the Roslyn host, `Storm.SourceGenerator` as an
   analyzer, `Microsoft.CodeAnalysis`) SHALL appear in the Later_Stage_Graph.
2. Until the Mist-authored Storm exists, a Stage_Zero_Tool MAY stand in for the Storm_Generator: the C# Storm
   frontend run as a process that emits Mist under the SE-06 contract. Such a tool SHALL be declared where a
   generator is declared, SHALL be named as a Seed in the Provenance_Record of every Run it produces, and
   SHALL count as transition only, never as completion.
3. Pre_Generated_Sources MAY enter a Consumer only through an explicit declaration naming their
   Provenance_Record, never as ordinary sources under Source_Discovery, and THE Generator_Host SHALL report
   `GeneratorSeedUsed` naming them, so that a build that used none can prove it from its diagnostics.
4. A build is Seed-free, and the Later_Stage_Graph closed, WHEN its manifests declare no Stage_Zero_Tool and
   no Pre_Generated_Sources, every Frontend_Artifact in it was produced in that build by a Storm_Generator
   compiled from Mist sources by Generation N, and the Provenance_Record of every Run names only
   Grammar_Inputs, that generator and Generation N's Toolchain_Identity. This is the criterion SH-008
   (MIST-1073) proves and SH-007 (MIST-1072) packages.

### Requirement 7: Concurrency and the cache

**User Story:** As a Mist developer, I want two builds that share a generator to both succeed, and the cache
to live where it cannot pollute a source tree.

#### Acceptance Criteria

1. WHEN two builds compile or run the same generator at once, THE Generator_Host SHALL let one proceed and
   make the other wait on a per-key lock for its result, bounded by the time limit, re-check the cache when
   the lock opens and reuse what was published, and SHALL NOT fail a build for the contention; a reuse of a
   published Run SHALL take no exclusive lock. This is the shape NuGet gives its shared package folder. The
   spike failed the second build outright, even on a cache hit.
2. THE cache of compiled generators and published Runs SHALL live outside every project's source tree, at a
   location derived from the Toolchain_Identity and configurable for a hermetic build, so that a read-only
   generator folder can be used and nothing generated is ever committed by accident.
3. THE CLI SHALL offer a way to remove the cache, and the Integrated_Build SHALL run against an empty one.
4. Publication of a Run SHALL be atomic, with the Provenance_Record written last as the completion marker, so
   that an entry without one is never reused; a concurrent publisher of the same Staleness_Key SHALL find the
   winner's Run and reuse it.

### Requirement 8: Verification

**User Story:** As a compiler maintainer, I want the capability proven at the tiers the testing philosophy
assigns, so that no rule is covered only by a manual probe.

#### Acceptance Criteria

1. Staleness_Key and compiled-generator key composition SHALL be pure functions over precomputed hashes,
   proven at Tier 1, including that the Consumer's identity and each argument boundary enter the key.
2. Output-list validation SHALL be a pure function over the listed and the actual relative paths, proven at
   Tier 1 over every refusal of Requirement 3.2.
3. Manifest refusals of Requirement 5 SHALL be proven at Tier 1 over in-memory TOML tables, one case per
   Descriptor of Requirement 4.3 that the parser reports.
4. Generator graph ordering and cycle refusal SHALL be proven at Tier 2 over hand-built configurations with
   stubbed loaders, including a diamond, a shared generator and a generator with its own generator.
5. The host's process seam SHALL take an injectable runner, as `CSharpProjectBuilder` does, so that compile,
   run, timeout and failure handling are proven at Tier 1 without a process.
6. Run integrity over a real directory, including same-size same-mtime tampering, SHALL be proven at Tier 3.
7. The overlay that enters generated sources into the resolver's dependency order SHALL be proven at Tier 2
   with stub loaders, root and transitive.
8. One sample with a pure-Mist generator SHALL be banked in `samples/AllFeaturesV2`, so that the Tier 6 test
   exercises the host end to end; no other test SHALL spawn a generator process.
9. No test SHALL compile a program to watch it fail; refusals are proven beside the rules that report them.

### Requirement 9: Non-goals

#### Acceptance Criteria

1. THE capability SHALL NOT give a generator access to the Consumer's program model, syntax trees or
   semantic information; a generator sees only its Declared_Inputs and arguments.
2. THE capability SHALL NOT sandbox a generator or prove it pure; correctness of reuse rests on the generator
   being deterministic over its Declared_Inputs, which the Provenance_Record makes checkable.
3. THE capability SHALL NOT make the Consumer's own compilation incremental.
4. THE capability SHALL NOT accept generator output in any language but Mist, and SHALL NOT drive MSBuild.
5. THE capability SHALL NOT distribute generators as packages; a generator is a project in the same
   checkout. Package distribution is an open item for the Decision.
