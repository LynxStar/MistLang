# Classic retirement: completed

Classic was deleted on 2026-10-01, from `ba321ab0`. The compiler has one pipeline, named **standard**:
the former experimental (v2) pipeline. This file records what was removed, what happened to classic's
tests, what classic did that the standard pipeline does not, and how the change was verified.

## The selector

`--pipeline`, `CliArguments.Pipeline`, `CompilerOptions.Pipeline` and `PipelineMode` are kept, in case
another pipeline is added later (decided 2026-10-01, Arthur). The one CLI value is `standard` and the
one enum member is `PipelineMode.Standard`; both are the default. `--pipeline classic` and
`--pipeline experimental` are the parser's unknown-pipeline error, with no alias and no fallback
(`CliPipelineArgumentTests`). `CompilerDriver.Compile` throws `InvalidOperationException` for a
`PipelineMode` value that is not `Standard`, and `Program.CompileProject` for a pipeline string that is
not `standard`. `ExperimentalPipeline` and `ExperimentalStageDetails` are `StandardPipeline` and
`StandardStageDetails`, and their tests follow. Nothing else named V2 or Experimental was renamed:
`CSharpEmitterV2`, `samples/AllFeaturesV2`, `Source/Standard/Experimental/` and the rest keep their
names, and renaming `Mist.ProgramModel` to `Mist.Semantics` remains a separate change.

## What was removed

- **Trees:** `Source/Mist/Semantics/` (71 files) and `Source/Mist/Analysis/` (30 files).
- **Backend:** the 18 classic files at the top of `Source/Mist/Backends/CSharpTranspiler/`
  (`CSharpEmitter`, `CSharpTranspilerBackend`, `CSharpTypeMap`, `IntrinsicTypeMap`,
  `AssemblyTypeProvider`, `CSharpAmbientTypeResolver`, the top-level `TypeProjector`, `VisibilityFilter`,
  `AmbientExtensionScanner`, `AmbientExtensionMethodInfo`, `CarrierStructRegistry`,
  `DependentTypeHelpersGenerator`, `MistContextGenerator`, `NeedsAsyncPropagationPass`,
  `UnionHelperGenerator`, `UnresolvedRelationship`, `ValueTaskUsageAnalysisPass`,
  `VariadicMonomorphizer`), and `Backends/Contracts/IBackend.cs` and `BackendContext.cs`.
- **Driver and CLI:** `ClassicPipeline.cs`, `CompilerDriver.Classic.cs` and
  `Mist.CLI/Composition/ClassicCompilerComposition.cs`. `CompilerDriver` has one public constructor,
  `(ILexer, IParser, ICompilationProgressReporter? = null)`, checks the backend against
  `BackendId.CSharp`, and is no longer partial.
- **Contract members:** `CompilerOptions.EnabledAnalyzerIds` (not reinterpreted as a filter: the runner
  runs every Stage 5 rule), `CompilerOptions.EmitToDisk`, `CompilerResult.EmittedFiles`,
  `PipelineInput.DirectReferencePaths` with the driver's `LoadResult` field, and
  `CompilationUnit.IsExperimentalOnly` with the loader's assignment. `Source/Standard/Experimental/` is
  ordinary Standard.
- **Left with no reader by the deletion, and removed:** `CompilationUnit.HasTopLevelStatements` on the
  driver's unit with the detection in `CompilerDriver.Parse` (the program model derives the fact in
  `CompilationUnitBuilder`), and `MistCompilation.GetGlobalUsingsForUnit`.
- **Descriptors nothing reports:** `GenericsDiagnostics`, `OwnershipDiagnostics`, `ReadonlyDiagnostics`,
  `SemanticDiagnostics`, `TypeCheckDiagnostics` and `VariadicsDiagnostics` whole (279 descriptors), and
  36 more: 27 in `CommonDiagnostics`, 7 in `EntryPointDiagnostics`, 2 in `BackendDiagnostics`. Each was
  referenced only from deleted code at `ba321ab0`. Four descriptors that had no reference there either
  were left: `CommonDiagnostics.NamespaceNotFound`, `TypeAmbiguous`, `PackageNoCompatibleTfm` and
  `BackendDiagnostics.UnresolvedAmbientType`.

Kept, as shared: `BackendId`, `BackendResult`, `CSharpCompilationDriver`,
`Diagnostics/RoslynDiagnosticMapper`, `Runtime/`, `TypeInspector`, `Inspection/`, and the `Emitter/`,
`AmbientProvider/`, `Lowering/` and `Intrinsics/` trees. `mist inspect` stays on the v2 provider.

**`Intrinsics/` has no caller left in `Source/`.** `IntrinsicEmissionTable`, `IIntrinsicEmissionRegistry`
and `IntrinsicEmissionTemplate` were read only by `CSharpEmitter`, `SemanticModelBuilder` and
`SymbolTable`; the standard pipeline spells intrinsics in `Lowering/IntrinsicCalls.cs`. The tree and
`IntrinsicEmissionTableTests` were kept because the retirement brief named `Intrinsics/` as surviving.
Deleting both is a decision left open.

## What happened to the tests

704 test files were deleted and 596 remain. Each legacy-dependent file was judged by what it proved:
OBSOLETE (its subject is deleted code), COVERED (a surviving test or a sample in `AllFeaturesV2`
already proves it), or UNIQUE (a supported contract about surviving code that nothing else proved,
kept and migrated at its owning tier).

| Area | Deleted | Disposition |
|---|---|---|
| `Analysis/` | 280 | Direct tests of the classic analyzers and their engines over a classic model, and compile-and-diagnose tests asserting classic ids. The accept halves are in samples; the refusals are the Stage 5 rules' own tests. `ClrAccessibilityProjectionPropertyTests` and `ClrSealedProjectionPropertyTests` survive. |
| `Semantics/` | 186 | Classic `SemanticModelBuilder`, model shapes and `AccessibilityFacts`. 61 files survive, the direct ProgramModel tests among them. |
| `Backend/` | 139 | Classic CLR projection (26), classic type maps (10), classic-emitted C# text or "classic's output compiles" (95), and 8 compile-and-run tests covered by samples. 129 files survive. |
| `Variadics/`, `ReadonlyContexts/`, `Generators/`, loose | 56 | Classic model shapes, `TypeCheckerAnalyzer` rules, classic emission and `VariadicMonomorphizer`; three generators of classic types and one with no user. |
| `Integration/`, `Driver/` | 31 | Sample runs through classic (covered by `AllFeaturesV2`, sample by sample), generated-program property tests through classic, classic progress accounting, and tests that observed the driver through a wrapped classic builder. |
| `Mist.Tests.InteropFixtures/` | 12 | Fixtures only the classic projection tests named. |

Unique checks kept or moved:

- **Through the clean constructor, unchanged otherwise (Tier 4):** `WithBlockEmissionTests`,
  `UnitHoistingRuntimeTests`, `ResolvedPackagesReachV2CompilationTests`, `RegexEmissionTests`,
  `RefinementEmissionTests`, `MonomorphEmissionTests`, `FunctionTypeBridgeTests`,
  `FieldInitializerRuntimeTests`, `CultureInvariantEmissionTests`, `ClrTupleSignatureRuntimeTests`,
  `ClrDelegateLambdaTests`, `CloneConversionRuntimeTests`, `ReferencedManifestDependenciesTests`,
  `LibraryOutputEmissionTests`.
- **Driver contracts cut out of classic tests:** Stage 1, 2 and 3 detail lines
  (`CompilerDriverProgressReportingTests`, Tier 4); an unresolvable project reference
  (`CompilerDriverReferenceIntegrationTests`, Tier 4); a source file that cannot be read
  (`EndToEndCompilationTests`, Tier 4); a unit whose lexing failed is not parsed
  (`PipelineBackendPropertyTests`, Tier 4); the resolver-only properties of
  `ProjectReferencePropertyTests` (Tier 2); package resolution (`PackageResolutionStageTests`, Tier 4);
  the stdlib parse cache (`StdlibParseCacheTests`); the unknown-backend refusal
  (`CompilerDriverConstructionTests`).
- **Rewritten at the seam:** `CompilerDriverGlobalUsingsTests` read the compilation through a wrapped
  classic builder. It is three Tier 4 compiles: the root manifest's `[global_usings]` reach its units,
  `CompilerOptions.GlobalUsings` apply without a manifest, and a manifest wins.
- **Parser tests split out of mixed files (Tier 2-3):** `NestedScopingPropertyTests`,
  `UnknownInheritanceProhibitionTests`, `StdlibParseCompletenessTests`, `PrettyPrinterPropertyTests`,
  `AttributeDeclarationPositionDisambiguationPropertyTests`,
  `DeclarationPositionDisambiguationPropertyTests`, `BinaryLiteralVerbatimEmissionPropertyTests`,
  `PipelineDebugTests`, `VariadicsFixtureSmokeTests`.
- **Carried down to the owning tier as one row each (Tier 3):** a Mist extension through `?.`
  (`ExtensionLookupTests.AnExtensionMemberIsFoundThroughOptionalChaining`), a spread of an open pack
  refused with `MIST_VAR_0011` (`ValuePackTests.SpreadOfAnOpenPack_IsRefused`), and a `var` pattern over
  a union (`UnionPatternMatchingEmissionTests`, now an emitter test of that branch).
- **Moved:** the two `SourceText.ToLinePositionSpan` properties, to
  `Common/SourceText/SpanToLocationPropertyTests` (Tier 1).

Nine kept property tests run 100 or 200 iterations at Tier 3, over its limit of 50. They only lex and
parse, were tagged as they are and not restructured: five in `NestedScopingPropertyTests`, and one each
in `BinaryLiteralVerbatimEmissionPropertyTests`, `DeclarationPositionDisambiguationPropertyTests`,
`AttributeDeclarationPositionDisambiguationPropertyTests` and `VariadicsFixtureSmokeTests`.

Test helpers: `StdlibTestHelper` keeps `FindRepoRoot` and `LoadStdlib`; `VariadicsCompilerFixture` keeps
`Parse`, `ParsedCleanly` and `ParseOutcome`, and no longer compiles anything.

The refusal migration (`backlog/refusal-corpus-migration.md`) was not repeated and its replacements
are untouched.

## The sample check

`AllFeaturesV2_CompilesRunsAndMatchesBaseline` is the one Tier 6 test, and it names no pipeline, so it
holds the CLI's default. `Testing_RunsUnderTheTestCommand` holds `samples/Testing` under `mist test`,
also on the default. `RepeatedCliInvocationTests` names `--pipeline standard`.

Before the classic guard `AllFeatures_CompilesRunsAndMatchesBaseline` was removed, measured on
2026-10-01:

- All 70 references in `samples/AllFeatures/mist.toml` are among the 93 in
  `samples/AllFeaturesV2/mist.toml`.
- 69 of its 71 `RunDemo()` calls are in `AllFeaturesV2/Program.mist` as written. `VoidType.Program.RunDemo()`
  is there as `VoidType.VoidDemo.RunDemo()`, which is all it calls. `UnknownTypeDemo.RunDemo()` is a
  file of the classic orchestrator itself, `samples/AllFeatures/UnknownType.mist`; its three behaviours
  (an `unknown` local, narrowing from `unknown`, covariant widening with a narrow back) are in
  `samples/UnknownType`, which `AllFeaturesV2` composes. Its seven lines are the only lines of
  `AllFeatures.out`, besides the header, absent from `AllFeaturesV2.out`.
- The standard pipeline runs `samples/AllFeatures` with exit 0, nothing on stderr, and stdout equal to
  `samples/__baselines__/AllFeatures.out` (1,796 lines).

`samples/AllFeatures` and `AllFeatures.out` are kept as historical data and nothing compiles them:
`MemoizationCorrectnessAndPerformanceTests` reads the directory as a parse corpus and
`SampleProjectTomlTests` as a project-file fixture. `SpecProofCodeMatchesSampleTest` reads
`AllFeaturesV2.out` alone: all 26 expected-output blocks it checked are slices of it, and so is
`delegate-func-conversion`'s, which the classic baseline could not hold, so both exemption lists are
gone and 27 are checked. `Enums_StillCompilesAfterReadonlyModifierAdditions` compiled `samples/Enums`
on classic in-process and is covered by the orchestrator.

## What classic did that the standard pipeline does not

Found while classifying classic's tests. None was fixed here; each is language work. The measured
ones are entered in `backlog/general.md`.

Measured on 2026-10-01:

- **Structured-context discipline.** A call needing a `with` context and having none compiles and
  throws in `MistContext.Peek`. Classic refused it (`MIST_CTX_0001` to `MIST_CTX_0006`).
- **A pack-typed abstract slot at a concrete arity** (variadics 22.2) is refused with
  `MIST_PA_SLOT_SIGNATURE_MISMATCH`. The generic, spreading form is admitted.
- **An undefined namespace in `[global_usings]`** (global-usings 4.3) reaches Roslyn as `MIST_CS_ERROR`
  where classic warned `MIST_GLOBAL_USING_UNDEFINED_NAMESPACE` and continued.
- **A CLR extension called without its namespace**, `numbers.Count()` on a `List<int>`, is refused by
  Roslyn (`MIST_CS_ERROR`) and not by the binder.

Read from the source and the specs, not probed:

- Entry-point validation beyond a duplicate entry point, a missing one and a library with top-level
  statements: classic's `MIST_EP_0001` to `MIST_EP_0007`.
- Variadics refusals with no reporter: Requirements 6.3, 6.4, 6.6 (`MIST_VAR_0002` to `0004`), 8.7
  (`0006`), 7.9 and 13.5 (`0001` on a value-parameter list), 16.2 (`0010`), 10.5 (`0008`).
- Transitive reference visibility (project-references Requirement 6.2). `MistProject` documents the
  flat project list as deliberate, so this is a spec to amend and not a rule to write.
- Continuing past a package-resolution error (csharp-interop-v2 Requirement 2.6): `StandardPipeline`
  returns at its entry gate on any error.
- Five refusals the standard pipeline does report are named in no test:
  `MIST_PM_AWAIT_OF_NON_TASK`, `MIST_PM_ASYNC_NOT_ALLOWED`, `MIST_PM_DUPLICATE_ENTRY_POINT`,
  `MIST_PM_INITIALIZER_TARGET_NOT_ASSIGNABLE` and `MIST_PM_TYPE_ARGUMENT_COUNT_MISMATCH`. That predates
  the deletion.

## Verification

Run on 2026-10-01 and 2026-10-02 against the tree that became this change; no file under `Source/`,
`Test/`, `tools/` or `samples/` changed after the full ledger started.

- **Build.** `dotnet build Mist.slnx`, with the pinned Storm submodule at `4848cb01`, and
  `dotnet build tools/SnippetEmitter/SnippetEmitter.csproj`: 0 warnings, 0 errors.
  `dotnet format Mist.slnx --verify-no-changes`: exit 0.
- **Audit**, over `*.cs` in `Source`, `tools` and `Test`: no file matches `Mist\.(Semantics|Analysis)\b`;
  nothing matches `IBackend\b`, `ClassicPipeline`, `PipelineMode\.(Classic|Experimental)`,
  `EnabledAnalyzerIds`, `EmitToDisk`, `EmittedFiles\b`, `DirectReferencePaths`, `IsExperimentalOnly`,
  `ExperimentalPipeline`, `ExperimentalStageDetails`, `SemanticModelBuilder`, `TypeCheckerAnalyzer`,
  `CSharpTranspilerBackend` or `CSharpEmitter\b`. `"classic"` and `"experimental"` appear only as the
  refused values in `CliPipelineArgumentTests`. Comments that cite classic's verdict as the reason for
  a rule were left as the evidence they are.
- **Full ledger**, `tools/test-ledger/run.sh`, unfiltered, `20261001-234707`: test exit 0 after 92 s.

  | Assembly | At `ba321ab0` | After | |
  |---|---|---|---|
  | `mist.tests.dll` | 10,610 | 7,079 | all passed |
  | `mist.cli.tests.dll` | 99 | 105 | all passed |
  | `storm.sourcegenerator.tests.dll` | 812 | 812 | all passed |
  | `storm.consumerintegration.tests.dll` | 46 | 46 | all passed |

  The `ba321ab0` column is the same script on an untouched worktree of that commit, the same night
  (180 s, all passed). Mist.Tests is 3,531 lower: the 692 deleted files under `Test/Mist.Tests` held
  classic's tests, and the rows added above are a handful. The CLI's six are `CliPipelineArgumentTests`.
  In the run: `AllFeaturesV2_CompilesRunsAndMatchesBaseline` with its zero-warning assertion,
  `Testing_RunsUnderTheTestCommand`, the refusal replacements, and the Storm and Mist integration
  assemblies.
- **Book snippets.** The updated SnippetEmitter over a scratch copy of the whole of `book/snippets`
  (404 `.mist`, 382 `.cs`, the three sidecar manifests, the one `.support` directory): 381 pairs, 0
  failed, 371 unchanged, 10 regenerated; a second pass regenerated none. The `ba321ab0` emitter over
  another scratch copy regenerates the same ten, byte for byte, so they are stale tabs that predate
  this change: each loses or gains `__CreateDefault` and default-initialisation lines, from the
  defaultability commits of 2026-10-01 (`fcb2d967`, `60ca47cb`, `9421ad87`), after the tabs were last
  generated on 2026-09-27. They are `advanced/attributes/type-arguments`,
  `advanced/default-keyword/constructor-and-default`, `advanced/purity/accessor-promises`,
  `basics/functions/static-methods`, `object-model/classes/constructor-chaining`,
  `object-model/classes/static-methods`, `object-model/method-overloading/constructor-overloading`,
  `object-model/properties/initializer-blocks`, `type-system/accessibility/defaults` and
  `type-system/accessibility/protected-in-sealed`. No book output was regenerated in this change.
- **Probes** behind the sample check and the list above were run with the built CLI on throwaway
  projects.

## Left open

- Whether to delete `samples/AllFeatures`, `AllFeatures.out` and the `Intrinsics/` tree, each kept here
  on the brief's wording and each with the evidence above.
- The ten stale book tabs, which one `dotnet run --project tools/SnippetEmitter` regenerates.
- `backlog/storm.md` defers four residue items "until classic is sunset"; that condition is now met.
  No Storm change was made.
- Specs that now describe classic only: project-references Requirement 6, csharp-interop-v2
  Requirement 2.6, global-usings Requirement 4.3, and the Glossary and Introduction of
  `compiler-progress-output`, whose Requirements 6 and 7 alone were rewritten (criteria 7.10 to 7.12
  are cross-stage and sit there for that reason).
- Plans written against classic types: `backlog/multi-project-emission.md`,
  `backlog/refactor-ast-on-compilation-unit.md` and `backlog/grammar.md`.

## Progress accounting (v2)

What `StandardPipeline` reports. `.kiro/specs/compiler-progress-output/requirements.md` Requirements 6
and 7 state the same thing as acceptance criteria. Details are
passed unindented; seconds are `F2` in the invariant culture.

- **Stage 4 `Program Model`** covers ambient-provider creation and `ProgramModelBuilder.Build`. Detail:
  `{N} top-level constructs`, from `Driver/TopLevelConstructs.cs`: the class, struct and abstract types in
  the `Types` of every compilation unit of the Self and Reference assemblies. `Types` holds only
  top-level declarations, so a nested type is never counted, and monomorphs, union carriers and closures
  are the emitter's and never appear there. A newtype's clone replaces the newtype in its unit and is
  excluded explicitly; enums and aliases are not counted. Abstract is what the spec's "interface" and
  "trait" mean today. Standard is excluded; referenced projects are included, as classic included
  them. Classic's figure also counted Standard and nested types, so a number recorded from classic is not
  comparable. A null model completes the stage with 0 and stops.
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
