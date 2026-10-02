# Classic retirement: the remaining boundary

Recorded 2026-09-29 by retirement tasks 2 and 3 (preparation and v2 progress reporting). Task 4 deletes
classic, flips the default and removes classic's tests; this file is what it needs so that it does not
have to investigate the architecture again. Re-run the checks at the end before trusting any list: they
were measured at the commit that added this file, and again on 2026-10-01 at `75299f31`, where every
list below still held.

## What no longer needs classic

- **Driver.** `new CompilerDriver(ILexer, IParser, ICompilationProgressReporter? = null)` is the v2 entry
  point. It knows one backend id, `BackendId.CSharp`, and throws `InvalidOperationException` before any
  stage runs if handed `PipelineMode.Classic`, rather than reinterpreting it. The legacy constructors
  (taking `ISemanticModelBuilder`, `IAnalyzer`s and `IBackend`s) live in `Driver/CompilerDriver.Classic.cs`,
  the only file that composes `ClassicPipeline`; the rest of the driver names no classic type.
- **Contracts.** `PipelineInput` carries no `IBackend`; `StdlibLoader.LoadFor(DiagnosticBag)` takes none.
  `ClassicPipeline` resolves its own backend from its registrations and applies the classic-only drop of
  `Standard/Experimental` units itself.
- **CLI.** `build`, `run` and `test` with `--pipeline experimental` construct only the clean driver.
  Classic services are built in `Source/Mist.CLI/Composition/ClassicCompilerComposition.cs`, and only
  when the classic pipeline is selected (still the default).
- **SnippetEmitter** uses the clean constructor.
- **`mist inspect`** (`TypeInspector.Inspect`, same public signature) creates the v2 provider exactly as
  `ExperimentalPipeline` does and projects it beside Standard, because the provider cannot project
  without Standard's well-known types. Visible members are the projected members, spelled by v2; hidden
  reasons are the projectors' own decisions, exposed through `MethodProjector.Judge`,
  `PropertyProjector.Judge`, `IndexerProjector.Judge` and `AmbientProvider/Withholding.cs`, which the
  build's projection loops call too. A projected indexer is listed between the properties and the
  methods as Mist declares one, `indexer this[int]: char`, since its CLR name (`Item`, `Chars`) is not
  something Mist code can write; a withheld one is listed under that CLR name. The inspector adds only
  two reasons of its own, both v2 facts: events (v2 projects none) and members of a base the Mist type
  does not inherit. Fields, constructors and operators are projected by v2 but are not part of the
  report, as they were not before (csharp-interop-v2 Requirement 10 names properties and methods).
  Formatting is `Inspection/InspectionReport.cs`.
- **Parser.** `MistParser`'s multiple-access-modifier check uses `Parsing/AccessModifierSyntax.cs`
  (full-accessibility-model Requirement 2.6) instead of classic `AccessibilityFacts`, which only classic
  and its `MultipleAccessModifierRejectionPropertyTests` still reach.
- **Progress.** The experimental pipeline reports Stages 4 (`Program Model`), 5 (`Analysis`) and 6
  (`Emit`); see "Progress accounting" below.

## The classic-only boundary

### Driver and contracts

| Item | Status | Task 4 |
|---|---|---|
| `Source/Mist/Driver/CompilerDriver.Classic.cs` | legacy constructors | delete |
| `Source/Mist/Driver/ClassicPipeline.cs` | classic Stages 4-6 | delete |
| `CompilerDriver.cs`: `classicPipeline` field, the private constructor's `backendIds`/`classicPipeline` parameters, the `PipelineMode.Classic` branch in `Compile` | dispatch | collapse onto the experimental pipeline; keep the unknown-backend check against `BackendId.CSharp` |
| `PipelineMode`, `CompilerOptions.Pipeline` (defaults to `Classic`) | selector | keep the selector, remove `Classic`, rename `Experimental` to `Standard` and default to it (decided, see the checklist) |
| `CompilerOptions.EnabledAnalyzerIds` | read only by `ClassicPipeline` | remove (v2 runs every Stage 5 rule; do not reinterpret it as a v2 filter) |
| `CompilerOptions.EmitToDisk`, `CompilerResult.EmittedFiles` | classic in-memory emission; v2 always writes to `OutputDirectory` and leaves `EmittedFiles` null | remove, or give v2 a real in-memory mode first if a test still needs one |
| `PipelineInput.DirectReferencePaths` | read only by `ClassicPipeline` | remove, with the driver's `LoadResult` field that feeds it |
| `CompilationUnit.IsExperimentalOnly`, and `StdlibLoader` setting it | read only by `ClassicPipeline`'s drop | remove; `Standard/Experimental` becomes ordinary Standard |
| `Source/Mist/Backends/Contracts/IBackend.cs`, `BackendContext.cs` | legacy emission interface, unused placeholder | delete |
| `BackendId`, `BackendResult` | shared: the CLI and options carry the id; `CSharpCompilationDriver` returns `BackendResult` | keep |

### CLI

| Item | Task 4 |
|---|---|
| `Source/Mist.CLI/Composition/ClassicCompilerComposition.cs` | delete; `CompileProject` constructs the clean driver unconditionally |
| `CliArguments.Pipeline` (default `"classic"`), `--pipeline` parsing in `CliArgumentParser` | keep the flag, default to v2 under the name `standard`, accept it only (decided, see the checklist) |

### Backend files only classic reaches

No file under `ProgramModel/`, `ProgramAnalysis/`, `Backends/CSharpTranspiler/{Emitter,AmbientProvider,Lowering,Intrinsics}`
or the experimental driver path references these:

`AmbientExtensionMethodInfo`, `AmbientExtensionScanner`, `AssemblyTypeProvider`, `CSharpAmbientTypeResolver`,
`CSharpEmitter`, `CSharpTranspilerBackend`, `CSharpTypeMap`, `CarrierStructRegistry`,
`DependentTypeHelpersGenerator`, `IntrinsicTypeMap`, `MistContextGenerator`, `NeedsAsyncPropagationPass`,
`TypeProjector` (the top-level one; `AmbientProvider/TypeProjector.cs` is v2's), `UnionHelperGenerator`,
`UnresolvedRelationship`, `ValueTaskUsageAnalysisPass`, `VariadicMonomorphizer`, `VisibilityFilter` -- all
in `Source/Mist/Backends/CSharpTranspiler/`. Since `mist inspect` moved, `AssemblyTypeProvider.FindClrTypeForInspection`
has no caller, and `VisibilityFilter.Classify`, `MemberClassification` and `MemberHiddenReason` are
reached only from the classic `RefStructProjectionTests`.

Shared with v2 and kept: `CSharpCompilationDriver.cs`, `Diagnostics/RoslynDiagnosticMapper.cs`,
`Runtime/`, `TypeInspector.cs` and `Inspection/` (now on the v2 provider).

### Whole trees

`Source/Mist/Semantics/` and `Source/Mist/Analysis/`, and the diagnostic descriptors only they report.
`Source/Mist/ProgramModel/CLAUDE.md` plans renaming `Mist.ProgramModel` to `Mist.Semantics` afterwards;
that is a separate change.

### Tests that run v2 through the legacy constructor

These pass `PipelineMode.Experimental` to the legacy constructor, so they keep working now but must move to
the clean constructor before `CompilerDriver.Classic.cs` goes:

`Test/Mist.Tests/Integration/`: `WithBlockEmissionTests`, `UnitHoistingRuntimeTests`,
`ResolvedPackagesReachV2CompilationTests`, `RegexEmissionTests`, `RefinementEmissionTests`,
`MonomorphEmissionTests`, `FunctionTypeBridgeTests`, `FieldInitializerRuntimeTests`,
`CultureInvariantEmissionTests`, `ClrTupleSignatureRuntimeTests`, `ClrDelegateLambdaTests`,
`CloneConversionRuntimeTests`; `Test/Mist.Tests/Driver/ReferencedManifestDependenciesTests`;
`Test/Mist.Tests/Backend/LibraryOutputEmissionTests`. Helpers that construct classic services:
`Test/Mist.Tests/Helpers/VariadicsCompilerFixture.cs`, `Test/Mist.Tests/Helpers/StdlibTestHelper.cs`.
`CompilerDriverConstructionTests` holds both routes; its legacy-route case goes with the legacy route.
`CompilerDriverProgressReportingTests` and `TopLevelConstructCountPropertyTests` are classic's.

## Task 4 checklist

Steps 2 to 8 were confirmed as written on 2026-10-01; step 1 carries the one decision that was open.

1. Flip the defaults (`CompilerOptions.Pipeline`, `CliArguments.Pipeline`) to v2. **Decided 2026-10-01
   (Arthur):** the selector survives, in case another pipeline is added later, but v2 is the default and
   the only option. So `--pipeline` and `PipelineMode` stay, `classic` and `PipelineMode.Classic` go, and
   `--pipeline classic` becomes the parser's existing unknown-pipeline error rather than a silent
   fallback. The remaining value is renamed: `experimental` becomes `standard` (`--pipeline standard`,
   `PipelineMode.Standard`), and `--pipeline experimental` is then an unknown pipeline too. The value is
   spelled as a string in `CliArgumentParser`, `CliArguments`, `Program.cs`, `RepeatedCliInvocationTests`
   and `SamplesCompileAndMatchBaselineTest`; `PipelineMode.Experimental` is named in 20 files under
   `Source`, `Test` and `tools`; and `--pipeline experimental` is written in the root `CLAUDE.md`,
   `Source/Mist/ProgramModel/CLAUDE.md`, three specs and two other backlog files. The types named for
   the old value (`ExperimentalPipeline`, `ExperimentalStageDetails` and their tests) follow it. Unrelated
   and left alone: `Source/Standard/Experimental/` and `CompilationUnit.IsExperimentalOnly`, which mean
   Standard-library files classic cannot compile (step 4 removes the flag).
2. Move the tests listed above to `new CompilerDriver(lexer, parser[, reporter])`.
3. Delete `CompilerDriver.Classic.cs`, `ClassicPipeline.cs`, `ClassicCompilerComposition.cs`; collapse the
   driver's dispatch and private constructor.
4. Remove the classic-only contract members in the table above.
5. Delete the classic-only backend files, `IBackend.cs` and `BackendContext.cs`.
6. Delete `Semantics/`, `Analysis/`, their tests, and descriptors nothing else reports.
7. Update `.kiro/specs/compiler-progress-output/requirements.md` Requirements 6 and 7, whose wording
   names classic symbols, to the v2 definitions below.
8. Verify:

   ```bash
   grep -rlE 'Mist\.(Semantics|Analysis)\b' Source tools Test --include='*.cs'
   ```

   ```bash
   grep -rn 'IBackend\b\|ClassicPipeline\|PipelineMode\.Classic' Source tools Test --include='*.cs'
   ```

## Progress accounting (v2)

What `ExperimentalPipeline` reports, for when the spec's Requirements 6 and 7 are rewritten. Details are
passed unindented; seconds are `F2` in the invariant culture.

- **Stage 4 `Program Model`** covers ambient-provider creation and `ProgramModelBuilder.Build`. Detail:
  `{N} top-level constructs`, from `Driver/TopLevelConstructs.cs`: the class, struct and abstract types in
  the `Types` of every compilation unit of the Self and Reference assemblies. `Types` holds only
  top-level declarations, so a nested type is never counted, and monomorphs, union carriers and closures
  are the emitter's and never appear there. A newtype's clone replaces the newtype in its unit and is
  excluded explicitly; enums and aliases are not counted. Abstract is what the spec's "interface" and
  "trait" mean today. Standard is excluded; referenced projects are included because classic includes
  them (measured on a probe: classic counts a referenced project's class, struct and abstract types).
  Classic's figure also includes Standard and nested types, so the two pipelines print different numbers
  for the same program. A null model completes the stage with 0 and stops.
- **Stage 5 `Analysis`** runs only with a model. `ProgramAnalysis/AnalysisTimings.cs` times each existing
  `IProgramAnalyzer.Analyze` call and adds it to that analyzer *instance's* entry, over every scope the
  runner analyzes: the program, each clone conversion actually judged (one with a `BindingFailure` is
  not), each cloned type. Details: `{A} analyzers over {S} scopes: {P} program, {C} clone conversions,
  {K} cloned types`; then per analyzer in registration order `{Id} ({TypeName})  {X.XXs}` -- the Id is
  shared by several rules (five are `ownership-borrow-checking`), so the type name keeps them apart and no
  entry is summed under an Id; then `outside analyzers  {X.XXs}`, the stage's elapsed time minus the
  analyzers' sum clamped at zero, so the lines account for the stage.
- **Stage 6 `Emit`** runs only when the combined Stage 4-5 gate passes. Detail: `{N} .cs files written`
  from `CSharpEmitterV2.EmittedFileCount` (the files this run wrote, on every return path; the output
  directory is never recounted), then the artifact path when there is one. A failed emission still
  completes the stage.
- A stage that starts completes on every handled path; an exception propagates with no completion. The
  build summary is the CLI's alone. That holds for the shared stages too: a load that fails (Standard
  does not parse, a manifest or a project reference does not resolve) completes Stage 1 with no detail
  lines, since no group was fully loaded, and a clean that fails completes the CLI's Stage 0 the same
  way. The driver has one completion point for Stage 1, in `CompilerDriver.LoadCompilationUnits`.
