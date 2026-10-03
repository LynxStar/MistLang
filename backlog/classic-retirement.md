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
`AmbientProvider/` and `Lowering/` trees. `mist inspect` stays on the v2 provider.

**`Backends/CSharpTranspiler/Intrinsics/` went in a second commit** (decided 2026-10-02, Arthur), with
`IntrinsicEmissionTableTests`. `IntrinsicEmissionTable`, `IIntrinsicEmissionRegistry` and
`IntrinsicEmissionTemplate` were read only by `CSharpEmitter`, `SemanticModelBuilder` and `SymbolTable`;
the standard pipeline spells intrinsics in `Lowering/IntrinsicCalls.cs`.

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
parse, and stay Tier 3: the count makes a test a more expensive Tier 3, not a Tier 4 (Arthur,
2026-10-02). They are five in `NestedScopingPropertyTests`, and one each
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

`samples/AllFeatures` and `AllFeatures.out` went in a second commit (decided 2026-10-02, Arthur).
`UnknownType.mist` moved to `samples/AllFeaturesV2/` first, with its `UnknownTypeDemo.RunDemo()` call
and its seven baseline lines, so the orchestrator holds everything the classic one did: 93 references,
94 calls, 2,151 lines. `SampleProjectTomlTests` reads `AllFeaturesV2`'s manifest instead, and
`MemoizationCorrectnessAndPerformanceTests`, whose corpus was always every sample directory, checks
for that directory instead. `SpecProofCodeMatchesSampleTest` reads
`AllFeaturesV2.out` alone: all 26 expected-output blocks it checked are slices of it, and so is
`delegate-func-conversion`'s, which the classic baseline could not hold, so both exemption lists are
gone and 27 are checked. `Enums_StillCompilesAfterReadonlyModifierAdditions` compiled `samples/Enums`
on classic in-process and is covered by the orchestrator.

## What classic did that the standard pipeline did not

Found while classifying classic's tests, then probed (2026-10-01 and 2026-10-02). Classic's tests were
read as statements of what it accepted and printed; its source was not. Each row below was measured on
the standard pipeline before and after, and is closed unless it says otherwise.

| Gap | Closed by |
|---|---|
| A call needing a `with` context with none held compiled and threw in `MistContext.Peek` | `ContextAnalyzer`, analysis order 16: `MIST_CTX_0001`, and `0002`, `0004`, `0005`, `0006` for the rest of the discipline |
| A pack-typed abstract slot could not be filled at a concrete arity (variadics 22.2) | `ValuePack.Expand`: the slot is read at the arity the conformer binds; an explicit bridge unpacks the tuple the interface declares |
| An undefined `[global_usings]` namespace reached Roslyn (global-usings 4.3) | `UsingValidation`: warning `MIST_GLOBAL_USING_UNDEFINED_NAMESPACE`, the name withdrawn, the build continues |
| An explicit `using` of an undefined namespace reached Roslyn | `UsingValidation`: error `MIST_UNDEFINED_NAMESPACE` at the directive |
| `numbers.Count()` without `System.Linq` was refused by Roslyn | `MIST_PM_NOT_CALLABLE` at the call, naming the unimported namespace; general for any value that is not a function |
| A package or framework-reference error stopped the build before Stage 4 (csharp-interop-v2 2.6) | `Driver/ResolutionDiagnostics.cs`: Stage 1.5's diagnostics are kept apart from the bag the gates read and joined into the result |
| Entry points: statements beside a `Main` never ran, a second statements file was dropped, `entry_point = "<class>"` was never read, statements alone were refused (top-level-statements 2 to 4) | `EntryPointSelection.Decide`, read by `EntryPointAnalyzer` (order 15: `MIST_EP_0001` to `0005`, `0007`, `0008`) and by the emitter |
| `async` on an operator or cast operator was accepted and dropped | `MIST_PM_ASYNC_NOT_ALLOWED` from `OperatorBuilder` and `CastOperatorBuilder` |
| A promoted (inferred-async) call used as a receiver, index target or `?.` operand was emitted without parentheses | `BodyEmitter`: the await is written in one place and ranked as the prefix operator it is |
| Variadics refusals leaked or were silent: a bare pack as a type (6.3), several value-side `params` (7.9, 13.5), a function over 16 inputs (16.2), a wrongly typed argument to a `params` parameter (13.7) | `MIST_VAR_0002`, `MIST_VAR_0001`, new `MIST_VAR_0013`, `MIST_VAR_0010` (backend), and `ArgumentTargets` read by assignability and the refinement prover |
| Variadics forms missing: forwarding a pack (12.7), `(TArgs...)` as a parameter type (7.7, 7.8), `TArgs[i]` in a type position (6.4 to 6.6), a restated default reading its pack | `ArgumentBinder`, `PackTypePositions`, `MIST_VAR_0003` and `0004`, `MonomorphReference.Width` |

Probed and found not to be gaps: variadics 8.7 (unequal zip, `MIST_PA_NO_IMPLICIT_CONVERSION`), two
`params T[]` (`MIST_VAR_0012`), 10.5 (not expressible), `await` on a non-task, `async` on a
constructor, field or property, and a duplicate entry point.

### Decisions made where a spec was silent

Four were overruled by Arthur on 2026-10-02 and reversed the same day:

- **A build with only a resolution error now fails.** Lexing, parsing, binding and analysis still run
  so every diagnostic is reported beside it (csharp-interop-v2 2.3, 2.6), Stage 6 does not, and the
  CLI prints `Build failed  N error(s)` with exit 1; the resolution bag rides on `PipelineInput` and
  the emission gate reads it. It had been `Build succeeded  1 error(s)`, exit 0, as classic did.
- **Accessors, operators, conversions, extension properties, `Drop` and `IDisposable.Dispose` are
  transparent** to the context rule: judged where they are read, applied or run, including the drop
  the language inserts at a scope's end. Only a static initializer, an enum value, a `Clone` and an
  answer to another Standard or CLR slot remain roots. A key naming a type parameter still does not
  pass through a transparent generic method.
- **Top-level statements beside a block namespace are refused** as `MIST_EP_0006`, naming the file
  and each block, judged after the project-level refusals (top-level-statements 1.6 stands). Nothing
  banked combined the two.
- **`params` is admitted over every collection C# 13 gathers a flat list into**: an array, an optional
  array, a span, `IEnumerable<T>` and the four `IReadOnlyCollection`/`IReadOnlyList`/`ICollection`/
  `IList` interfaces, and a constructible class or struct enumerable with an `Add` of its element.
  `MIST_VAR_0013` keeps only what C# refuses: `params` on a non-collection type, or by reference.
  `ParamsCollection` decides the element once for the binder, `ArgumentTargets` and the purity judge.

Decided by Arthur (2026-10-02), as implemented:

- **The slot's own spelling says which form fills it.** `TResult Apply(params TArgs args)` at
  `IApplier<int, int, int>` is filled by `Apply(int a, int b)`, called `Apply(1, 2)`;
  `TResult Apply((TArgs...) args)` is filled by `Apply((int, int) pair)`, called `Apply((1, 2))`
  (variadics 7.7: `params` is the sole marker of the flat convention). A bare `TArgs args` is
  `MIST_VAR_0002` (6.3). The other form against either slot is `MIST_PA_SLOT_SIGNATURE_MISMATCH`.

Filed as a task, by decision (`variadic-monomorphization` in the emitter register):

- **An optional or union over an element of an unconstrained pack** is refused by the backend
  (`MIST_BE_UNION_OVER_PACK_ELEMENT`) until variadic declarations are emitted per instantiation.

Standing, not raised:

- **A referenced project's entry point is judged against its own manifest**, so a reference with two
  `Main`s and no `entry_point` is `MIST_EP_0004`. `MIST_EP_0004` lists classes in ordinal order; the
  book's example shows declaration order.
- **Context**: every caller must hold the key; alias and refinement share the underlying key in both
  directions.
- **An optional `func?` called without narrowing is still admitted**; a general union is refused
  (first-class-functions 6.10).
- **A promoted call inside a non-async lambda blocks** (`.GetAwaiter().GetResult()`), as the emitter
  already did for a declared-async one; first-class-functions 9.9 says the lambda is emitted `async`.
- **A class redeclaring an inherited member whose pack its base list closed** is
  `MIST_PA_MEMBER_REDECLARATION`.
- **`MIST_VAR_0010` is the backend's** (variadics 16.1, 16.4).
- **A forwarded pack reaches its callee as N arguments** (12.3, 7.5); classic passed one tuple and
  printed 1 where 3 is right.
- **An explicit undefined `using` is an error** (inferred-namespaces design.md); its requirements.md
  7.2 says warning.
- **A `Type[N]` pack closed with another count is `MIST_PA_CONSTRAINT_NOT_SATISFIED`**; it was
  admitted before and the spec names no diagnostic.
- **A forwarded pack must declare each constraint of the pack it is forwarded into**; implication
  between two different constraints is not attempted.

### Still open

Not built, by rule: a `with` block naming a variadic abstract. Neither spec defines the combination
and classic, run, refused it. It needs `ConformanceBuilder` to resolve through `BindPack` and compare
the expanded slot, and `ConformanceEmitter` to unpack the tuple.

Found while closing the gaps, each measured, none established as something classic did:

- **Context**: a refined clause is not proven (`with 0;` satisfies `with PositiveInt n`); `string?` and
  `string` share a runtime key; a lambda that outlives its context's scope fails at run time; the
  emitter drops the clause on extension methods and abstract default bodies (`MIST_CS_ERROR`).
- **Async**: an extension-block method ignores `async` and is never promoted; an `await` in a property
  getter or constructor body is emitted where C# refuses it; the `async` functor's lift lambda is not
  async; a promoted method used as a method group has the wrong return type; promotion does not reach
  an abstract slot; `some?.M()` on a promoted `M`.
- **Usings** -- closed 2026-10-03 (`7705ba3a`): a `using` of a namespace holding only type aliases
  reached Roslyn (the emitter wrote a using for a namespace it emits nothing into); a namespace holding
  only `with` declarations was never registered, so a `using` of it was `MIST_UNDEFINED_NAMESPACE`.
  `TypeBuilder` now registers a `with` declaration's namespace where it stashes it, as it did an
  extension block's, and `NamespaceBuilder` registers a block nested in a file-scoped namespace, which
  it skipped (`namespace Outer; namespace Empty { }` made `using Outer.Empty;` undefined).
  `UsingSelection` keeps an emitted using, explicit, global or synthesized for top-level statements,
  only for a namespace the C# compilation declares: one an emitted declaration, an extension block, a
  CLR assembly or the Void stand-in sits in, or one enclosing it. A namespace nothing declares is still
  `MIST_UNDEFINED_NAMESPACE`. Unchanged: a conformance is active whether or not its namespace is
  imported (emitter order 73).
- **CLR binding** — qualified callable naming, LINQ callable inference and nominal delegate
  invocation closed 2026-10-03. Qualified `System.Func`/`System.Action` use the existing structural
  callable family (zero through sixteen inputs); projected Invoke shapes feed lambda targeting and
  inference. Sparse CLR extension ordinals remain metadata positions while inference/substitution use
  each declaration's argument order, and emission restores CLR order for receiver plus method arguments.
  Nominal delegate calls use their closed Invoke parameters/result and retain nominal receiver identity;
  Invoke passing modes and ref-result metadata also reach emission and analysis. Tier 1–3 coverage is in
  ClrCallableNameTests, CallableArgumentInferenceTests, ProjectedCallableOverloadSelectionTests,
  AmbientCallableProjectionTests, AmbientDelegateTupleNameTests, DelegateInvocationBindingTests,
  DelegateInvocationEmissionTests, DelegateInvocationSignatureTests and CallableOverloadPinningTests.
  Focused ledger `20261003-185946` passed 536/536. The preceding `20261003-185729` passed
  529/533: `ClrProjectedCallableArgumentKeepsClrContextualConversion` caught a global emitter target
  regression (fixed by keeping callee identity separate from Invoke parameter metadata), and three
  `LinqProjectedExtensionSelectsTheClosedUnarySignature` rows included unrelated LINQ receiver families
  (fixture narrowed to IEnumerable). Private CLI probes print `2, 4, 6` then `2, 3` for Select/Where,
  `2`, `42`, `done` for qualified
  callables and `True` for a Predicate invocation. **Capacity history remains unclosed**: the old
  `List<int>.Capacity` observation resolved Standard.List and is not evidence of a qualified CLR provider
  defect. No replacement Capacity task or provider closeout is inferred from that probe.
- **Variadics**: a spread-typed parameter `M(TArgs... x)` and a spread into a non-pack slot
  `List<TArgs...>` reach Roslyn; a variadic method inside a variadic type, or in an extension block,
  is not specialized; `if (TArgs is Type[2] narrowed)` (6.7); `where Ts : IRank<Ts>` on a pack;
  `Buffer<true>` over `Buffer<Size:int>`; a newtype over a tuple; `: this(args...)` and
  `: base(args...)`; a base-list spread `: TBases...` (9.3).
- **Generics, the same with and without packs**: an unconstrained type parameter forwarded into a
  constrained one is refused at the forwarding site since 2026-10-03 (ConstraintEntailment, analysis
  register order 6), except that a pack or pack element forwarded is still judged for stampable alone;
  `where T : default` on a class's type parameter does not emit;
  an implicit `new Box<Widget>()` over a constructorless generic uses C#'s `default(T)` where
  `default(Box<Widget>)` uses Widget's own default; `default(T)` on an unconstrained parameter is
  refused by the backend and not by Stage 5.
- **Modifiers**: `async`, `static`, `readonly`, `owned`, `affine` on an enum, alias, newtype or class
  were dropped silently, and `ref` on an operator reached Roslyn. Audited against the specs on
  2026-10-03 and closed where a spec decides (`b199134c`): `async` on an enum, alias, newtype, class,
  struct or abstract is `MIST_PM_ASYNC_NOT_ALLOWED` (structured-concurrency 2.3, read as the member
  builders already read it), and `ref` on an operator, `readonly ref` included, is
  `MIST_PM_REF_ON_OPERATOR` (standard-library 4.2; ref-parameters 4.1 gives ref returns to methods
  only). Both come from `DeclarationModifierPlacement`, called by `ModifierBuilder.Build`, pinned by
  `DeclarationModifierPlacementTests` (Tier 1) and `ModifierPlacementBuildTests` (Tier 2); full ledger
  `20261003-005944`, 7,851 / 105 / 812 / 46, all passed. `readonly`, `owned` and `affine` on a class
  or struct, and `readonly` on an enum, were valid and already held. Still dropped, and decided by
  no spec: `static` on any type (nested-scoping 1.7 admits it on a nested type with no meaning),
  `owned` and `affine` on an enum, alias or newtype, `affine` on an abstract, `readonly` on an alias,
  and `ref` on a cast operator. Valid but not carried: `readonly` on a newtype over a mutable class
  or struct (purity Deferred 2) never reaches the clone, since `ConcreteTypeBuilder.BuildDeclaration`
  takes only the newtype's accessibility. A `ref` operator parameter (ref-parameters 1.2) reaches
  Roslyn, and `sealed`, `readonly`, `owned` and `affine` on an operator compile, though
  standard-library 4.2 refuses any operator modifier beyond `public static`.
- **Packages**: csharp-interop-v2 3.5 (the error surfaced during Stage 1.5, before the next stage) is
  not implemented; the diagnostic prints after the summary.
- **CLR conformances**: a CLR `List<int>` is not assignable to a CLR `IReadOnlyList<int>`,
  `IReadOnlyCollection<int>`, `ICollection<int>` or `IList<int>` anywhere (the projection records the
  bases; assignability over projected conformances does not read them), which keeps a whole list from
  being handed to a `params` of those types. `List<int>` to `IEnumerable<int>` works.
- **Context**: a `Clone` the language inserts has a locatable site and could be transparent like
  `Drop`; it stays a root. A Mist `IDisposable` handed to a CLR API that disposes it runs its `Dispose`
  with no site in the program.
- **Refusals named by no test**: `MIST_PM_AWAIT_OF_NON_TASK`, `MIST_PM_INITIALIZER_TARGET_NOT_ASSIGNABLE`,
  `MIST_PM_TYPE_ARGUMENT_COUNT_MISMATCH`. CLOSED 2026-10-03. Each is held at Tier 2, at the binder
  seam that reports it, by `AwaitOperandBindingTests`, `InitializerTargetBindingTests` and
  `ExplicitTypeArgumentCountTests` (`724cab90`). They pin the id, the severity, the message arguments
  and the span, with an accepted control beside each refusal and no second complaint. With all three
  refusals removed, the full suite failed only those classes' five refusal rows. Two defects found on
  the way are fixed. An `await` over a value that already failed added `MIST_PM_AWAIT_OF_NON_TASK`
  naming `'<error>'` (`d7dcda16`; program-model-body-tasks, error-expression sentinel, item (6)). Both
  storage refusals carried an `EnumMemberResult` arm that `ResolveStorage` cannot produce
  (`c5d33302`). Still open: an enum variant, or a static field or method, named in an initializer or
  a property pattern is `MIST_PM_UNRESOLVED_MEMBER`, "'Widget' has no member named 'Count'", though
  the member is declared. An `await` over an ErrorType value nothing reported keeps its `'<error>'`
  wording until that entry's item (5) lands.

Specs that now disagree with the implementation, beyond the decisions above: project-references
Requirement 6 (transitive visibility; `MistProject` documents the flat list as deliberate), the
structured-context design's keying and message templates and its Requirement 8.4/8.5, the variadics
diagnostic table (no `MIST_VAR_0012` or `0013`) and its two-pipeline Architecture section, the
Glossary and Introduction of `compiler-progress-output`, and inferred-namespaces Requirement 8.6 and
global-usings Requirement 6.1, which emit a using for every one written where a using naming a
namespace C# does not have is now left out.

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

### The follow-up work (2026-10-02)

The sample fold-in, the deletions and the gap fixes above, as 26 commits on top of `306b142f`.

- `dotnet build Mist.slnx` and the SnippetEmitter: 0 warnings, 0 errors.
  `dotnet format Mist.slnx --verify-no-changes`: exit 0.
- Full ledger `20261002-041727`, unfiltered: `mist.tests.dll` 7,658, `mist.cli.tests.dll` 105,
  `storm.sourcegenerator.tests.dll` 812, `storm.consumerintegration.tests.dll` 46, all passed, exit 0
  after 93 s. The 579 added Mist tests are the fixes' own, at Tiers 1 to 3 with six Tier 4 compiles
  (three entry-point manifest seams and three package-resolution seams).
- Each fix was probed with the CLI before and after, mutation-checked, and ledgered alone in its own
  worktree before it was merged. The two variadics changes were then cross-checked together: 244
  probe programs through the merged compiler, every difference from each change alone accounted for,
  and the only programs still reaching Roslyn are the ones listed under "Still open".
- `samples/AllFeaturesV2` prints its 2,151-line baseline with nothing on stderr.
- Book snippets over a scratch copy: 381 pairs, 0 failed, the same ten stale tabs, and the whole
  emitted tree byte-identical to the `ba321ab0` emitter's. No fix changed any snippet's C#.
- After the four reversals (commits `03de12ed`, `bd4b7f37`, `520e3d45`, `a14ca057` and their docs):
  full ledger `20261002-202000`, 7,796 / 105 / 812 / 46, all passed; `dotnet format` exit 0; the
  snippet run over a scratch copy again 381 pairs, 0 failed, the same ten, byte-identical to the base.
- The usings fix (`7705ba3a`, 2026-10-03): full ledger `20261002-230524`, 7,813 / 105 / 812 / 46, all
  passed; `dotnet format` exit 0; the snippet run over a scratch copy 381 pairs, 0 failed, the same
  ten, no `using` line changed. Its 17 tests are Tier 2 (`UsingSelectionTests` and five rows of
  `NamespaceConstructionTests`) and one Tier 4 model-to-emitter seam
  (`ErasedNamespaceImportEmissionTests`); each of seven parts of the fix, reverted alone, failed its
  test.

## Left open

- `backlog/storm.md` defers four residue items "until classic is sunset"; that condition is now met.
  No Storm change was made. Closed 2026-10-03 by Storm `56945a87`, recorded in `backlog/storm.md`
  under "Children() yields each node a node holds once".
- `compiler-progress-output` had Requirements 6 and 7 alone rewritten; criteria 7.10 to 7.12 are
  cross-stage and sit there for that reason.
- Plans written against classic types: `backlog/multi-project-emission.md`,
  `backlog/refactor-ast-on-compilation-unit.md` and `backlog/grammar.md`.

## Closeout: book output and live guidance (2026-10-03)

- **The ten stale book tabs are regenerated.** Built the current SnippetEmitter with
  `dotnet build tools/SnippetEmitter/SnippetEmitter.csproj` (0 warnings, 0 errors). On a scratch copy
  of the complete snippets tree, including sidecars, companions and `.support` folders, the first
  `dotnet run --no-build --project tools/SnippetEmitter/SnippetEmitter.csproj -- /private/tmp/mist-snippets-refresh`
  pass found 381 pairs: 10 updated, 371 unchanged, 0 failed. The second pass found 0 updated, 381
  unchanged, 0 failed. The ten outputs are the inventory listed above; a final tree comparison found
  no other snippet differences.
  After integrating `origin/main` at `796bf3ad`, rebuilt SnippetEmitter (0 warnings, 0 errors) and
  reran the full tree twice: each pass found 0 updated, 381 unchanged, 0 failed, and the emitted
  scratch tree matched the checked-in tree.
- **Live guidance is corrected.** The emitter register now distinguishes representability/emission
  work from Stage 5 rules and points to `backlog/analysis-v2-capability-progression.yaml`. The
  compiler-progress-output Introduction and Glossary now describe the active reporter contract,
  `IProgramModel`, Stage 5 analyzers, the current results, and Stage 4 top-level construct count;
  Requirements 6 and 7 are unchanged. The variadics Architecture introduction describes the sole
  standard pipeline and labels the following dual-pipeline account as a pre-retirement snapshot.
  Current references are `Source/Mist/Driver/CompilerDriver.cs`,
  `Source/Mist/Driver/StandardPipeline.cs`, and `Source/Mist/Driver/TopLevelConstructs.cs`.
- `git diff --check` passed, local Markdown links in the edited documentation resolve, and
  `dotnet format Mist.slnx --verify-no-changes` exited 0 after integrating current `main`. This
  follow-up added no compiler-source, test, sample-baseline, or language-acceptance changes; the full
  compiler ledger was not rerun for this documentation and generated-output task.

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
