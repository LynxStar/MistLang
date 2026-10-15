# 42 — Defaults and implicit constructor realization

Complete this task independently, including implementation, verification, actual tracking updates and landing. Repository: `https://github.com/LynxStar/MistLang.git`. Target: `origin/main`. Suggested model: **Fable, high effort**, using the user's remaining subscription window. This is also Opus-capable; no Fable-only capability is claimed. All three failures occur when the source omits an operation that lowering must realize: implicit storage initialization or implicit parameterless/base construction. The worker must make semantic constructor eligibility agree with generated calls and keep default data separate from behavior.057 includes both invalid nonempty-pack admission and valid empty-pack failure; this is more than changing one boolean.054 needs a canonical backing-storage initialization route, not silencing a diagnostic or hiding a setter print.236 is a small adjacent source-boundary fix, not the principal scope.

## Outcome and starting point

Synthesized defaults initialize backing storage without running custom setters; omitted base chains obey closed value-pack cardinality and materialize valid empty tuples; supplying arguments to a source-only implicit parameterless constructor is refused before Roslyn. Preserve user-constructor behavior and existing default/constructor separation.

Planning baseline: `4b7b5cc5a3acf0e4f826685e10ed7b25292fb837`, 2026-10-15. Current source inspection, bounded CLI reproductions and a zero-warning/error solution build used `0f41d2b8ea4a4576a8a5fedc28ad091f645c7618`; the intervening change only adds current evidence to MIST-054/057/068. Storm pin: `83410653c7d722868a5001d2ef71e6692ec012b3`. No fresh full-suite pass is claimed. Packets 34, 36 and 37 are landed. Packets 35, 38 and 39 are reserved and must not be duplicated.

Start from current remote `main` in an isolated task checkout, reusing an existing checkout or Git worktree/object database where available. Preserve completed work when continuing. Follow the repository's applicable instructions. Initialize submodules and verify their checked-out commits match this revision's recorded pins. Revalidate every included issue on latest main. If already fixed, verify acceptance and finish any real closeout gap without duplicating implementation. Spec task documents are implemented; use requirements/design and grammar as contracts. Do not read or derive code from retired classic source in Git history.

For each inline Mist reproduction, build the current solution once, make a separate temporary project, and put that complete source in `Program.mist`:

```bash
packet_probe_dir=$(mktemp -d)
cat > "$packet_probe_dir/mist.toml" <<'TOML'
name = "Probe"
namespace_root = "Probe"
TOML
```

After saving the selected inline source, run from the repository root:

```bash
dotnet run --project Source/Mist.CLI --no-build -- run "$packet_probe_dir" --quiet
```

These are manual audit reproductions, not a template for new process-heavy test matrices. Scratch output is not a deliverable.

## Contracts and current evidence

- `.kiro/specs/defaultability/requirements.md`: Introduction Default(data)/Construction(behavior); R3.4; R4.1–4; R6.1–6; R9.7–8; R10.1; R12.2–4,8
- `.kiro/specs/variadics/requirements.md`: R4.3 empty pack; R6.8 empty expansion; R7.1/7.4–7.7 value-pack tuple/flattened calling convention; R8.1–8.2
- `.kiro/specs/class-inheritance/requirements.md`: amendment header preserves R8.3 for user constructors: unmet base constructor arguments require source diagnostic
- `.kiro/specs/properties/requirements.md`: R5 field resolution and backing storage; R12 emitter backing storage; do not extend computed-getter policy

### Reproduction — setter

```mist
using Standard; namespace Probe; public class Box { public int Value { get{return field;} set{Console.WriteLine("setter");field=value;} } } public class Program { public static void Main(){Console.WriteLine(default(Box).Value);Console.WriteLine(new Box().Value);} }
```

Observed exit 0:

```text
setter
0
setter
0
```

### Reproduction — noctor

```mist
using Standard; namespace Probe; public class Bag {} public class Program {public static void Main(){var x=new Bag(1);}}
```

Observed exit 1:

```text
MIST_CS_ERROR: 'Bag' does not contain a constructor that takes 1 arguments
```

### Reproduction — zero-pack-bad

```mist
using Standard;namespace Probe;public class Base<params Ts>{public Base(params Ts args){}}public class Derived:Base<int>{public Derived(){}}public class Program{public static void Main(){new Derived();Console.WriteLine("bad");}}
```

Observed exit 1:

```text
MIST_CS_ERROR missing required args parameter: Base_1<int>.Base_1(ValueTuple<int>)
```

### Reproduction — zero-pack-good

```mist
using Standard;namespace Probe;public class Base<params Ts>{public Base(params Ts args){}}public class Derived:Base<[]>{public Derived(){}}public class Program{public static void Main(){new Derived();Console.WriteLine("ok");}}
```

Observed exit 1:

```text
MIST_CS_ERROR missing required args parameter: Base_0.Base_0(ValueTuple)
```

These five CLI programs ran on current binaries. Source/test references below are implementation and verification anchors, not executed test passes. The getter-only historical policy lead MIST-055 is excluded.

## Scope and acceptance

### 054 synthesized data

Valid Box with custom int property getter returning field/setter printing setter must keep that public setter callable, yet default(Box) and implicit new Box() print only the stored zero. Ordinary explicit property assignment still prints setter. Defaultable initializer expressions remain judged for consistency; no blanket rejection of unrelated impure setter behavior.

- class and struct
- auto-property and custom setter using field
- defaultable reference storage obtains fresh semantic default, not CLR null
- explicit field/property initializer evaluated once
- generic where T:default deferred initializer/witness
- explicit user constructor plus default block uses sentinel for default only
- derived class sentinel and flattened closed struct inheritance initialize each storage slot once
- this-chain does not repeat storage initialization
- custom setter invoked by an explicit user body still runs exactly as written

### 057 implicit base invocation

No written chain: Base<int> with only Base(params Ts args) cannot be called with zero source values and receives a Mist diagnostic before C#. Base<[]> with the same declaration is callable and emits the empty packed tuple representation rather than bare base(). Semantic selection and emitter agree after closed substitutions.

- zero and one/multiple-element value packs
- accessible parameterless overload takes priority over pack-only overload
- ordinary homogeneous params array permits zero arguments
- closed generic base reached through intervening generic substitutions
- class and flattened struct implicit chain routes
- private/inaccessible constructor remains refused
- explicit base(args...) forwarding remains accepted
- this(...) constructor delegation and static constructors retain behavior
- Ambient/native/intrinsic projected constructors preserve current conservative exceptions
- constructorless derived default still follows base default sentinel rather than executing a user base constructor

### 236 implicit source constructor

Source class/struct with no declared instance constructor accepts only empty argument list for implicit parameterless construction. new Bag(1) and object/collection initializer variant retain primary Mist argument/overload failure and never reach Roslyn.

- new Bag() remains admitted when defaultable
- ordinary declared overloaded constructors unchanged
- static constructor alone does not create an instance overload
- do not infer no-constructor refusal from incomplete Ambient/native/intrinsic constructor lists
- contextual new follows the same established construction path
- preserve evaluated argument failure ownership and avoid duplicate derivative errors

An unresolved symbolic pack must not be assumed empty: an omitted generic base chain must be valid for every permitted width or receive the owning source refusal. Explicit same-pack base(args...) forwarding remains a passing control; it is already delivered.

Implementation references:

- `Source/Mist/ProgramAnalysis/Rules/PurityAnalyzer.cs`: JudgeDefaults currently examines initializer/default-entry expressions only
- `Source/Mist/ProgramModel/Construction/InstanceStorage.cs`: Of, IsStorage, Name; property Name is a surface member rather than backing-storage target
- `Source/Mist/Backends/CSharpTranspiler/Emitter/DefaultEmitter.cs`: EmitAssignments emits this.<property> assignments
- `Source/Mist/Backends/CSharpTranspiler/Lowering/DefaultSynthesis.cs`: Assignments, FlattenedAssignments, StorageZero
- `Source/Mist/Backends/CSharpTranspiler/Lowering/DefaultWitnesses.cs`: EmitInitializers also emits deferred/zero property assignments
- `Source/Mist/Backends/CSharpTranspiler/Emitter/PropertyEmitter.cs`: Emit and explicit/automatic backing storage emission; necessary representation changes permitted
- `Source/Mist/Backends/CSharpTranspiler/Emitter/ConstructorEmitter.cs`: Emit: omitted-chain branch only adds base witnesses at current lines148–153; no empty packed value synthesis
- `Source/Mist/ProgramAnalysis/ImplicitChains.cs`: Of, CalledWithNothing, UnsatisfiedBase, UnsatisfiedStructBase; IsFlattened alone ignores closed tuple arity
- `Source/Mist/ProgramAnalysis/Rules/DeclarationShapeAnalyzer.cs`: implicit chain consumers
- `Source/Mist/ProgramAnalysis/Rules/DefaultabilityAnalyzer.cs`: implicit chain/defaultability consumers
- `Source/Mist/ProgramModel/Construction/Binding/Expressions/ExpressionBinder.cs`: SelectConstructor current lines448–479 returns Selected=null for empty ConstructorsList without checking supplied arguments
- `Source/Mist/ProgramModel/Construction/Binding/BodyBinder.cs`: BindChainArguments existing written-chain path is a compatibility control, not the only chain route
- `Source/Mist/Backends/CSharpTranspiler/Emitter/StructInitEmitter.cs`: flattened struct constructor/chain realization; inspect actual existing helper path before editing
- `Source/Mist/Backends/CSharpTranspiler/Emitter/BodyEmitter.cs`: BackingFieldReferenceExpression emits C# field around line1681; if explicit backing storage names are needed, preserve accessor reads/writes with the existing model identity rather than changing source semantics

## Independence and ownership

Own default backing-storage writes, source implicit constructor argument checks and closed omitted-base eligibility/materialization. Packet 40 owns ordinary-call ownership boundaries in Stage 5; packet 41 owns instance-expression self construction. Shared ExpressionBinder edits are disjoint; preserve existing receiver and source constructor/member identity. Preserve witness signatures/call conventions and generic slots; no new hidden witness representation is needed. Reserved 35 owns pattern containment only, 38 owns Storm and the gitlink, and 39 owns native constants/tuple decoration. This packet needs no unpublished service from any of them.

This packet has no dependency on another workstream. As its parent session, you own every included task and its internal dependencies. Determine the execution order, decide which work to perform yourself or dispatch to subagents, and integrate their changes into your primary implementation. Resolve internal conflicts and verify the combined result; subagents return contributions to you rather than independently publishing partial work to the shared target. Use current repository contracts. Do not hand internal ordering, coordination or integration to the user, the planning session or another packet. Complete and close out every included item; a representative subset or a collection of subagent reports is not completion.

Allow narrowly necessary adjacent edits, including compatible bound metadata, consumers, generated artifacts and user/API/spec documentation needed to finish this outcome. Keep changes coherent and preserve already landed behavior. Shared files require narrow integration, not another packet's unpublished branch, service, test helper or design decision.

Excluded scope:

- MIST-055 computed-getter field contract tension; do not choose a new storage/refusal policy or claim closure.
- MIST-056 unmeasured diagnostic wording, MIST-112/113/146 default policy choices and MIST-243 unknown representation remain outside this bundle.
- No new null/callback policy, default block auto-property syntax, source const syntax, generic self receiver fix, broad overload rewrite, driver lifecycle or unrelated indexer/native warning work.
- No pattern proof or generic-write summary implementation. Preserve all established explicit user constructor/setter behavior and current ambient/intrinsic constructor-list exceptions.

## Incidental fixes

If you uncover additional issues with clear expected behavior that are straightforward to fix and verify without user input, solve them in additional commits. This includes small fixes outside your primary files or objective; do not merely surface them as extras or make the user dispatch another task. Add appropriate verification and update actual documentation/backlog entries where relevant. Keep each coherent incidental fix separately committed and include it in the same landing workflow.

Avoid duplicating work already landed or assigned to an active workstream. Defer only when the finding requires a genuine user decision, substantial new work, unavailable access/authorization, or verification you cannot complete; state the concrete reason. Ordinary implementation choices and arbitrary file boundaries do not require user input. Finish the primary task and do not turn incidental cleanup into an open-ended campaign.

## Verification and closeout

Use direct hand-built Tier 1 closed eligibility/cardinality decisions and Tier 2 backing-storage/constructor emission fragments. Use own-phase binding inputs for constructorless source argument refusals. Keep raw source CLI examples as manual acceptance evidence; do not add a compiler/process matrix per case. Reuse existing bounded integration only where actual setter execution cannot be proven lower.

Existing verification anchors (inspect actual helpers/costs before extending):

- `Test/Mist.Tests/ProgramAnalysis/ImplicitChainsTests.cs`: Tier 1: extend real closed pack cardinality/accessibility decisions, rather than existing flattened scalar fake
- `Test/Mist.Tests/ProgramAnalysis/StructImplicitChainsTests.cs`: Tier 1 same canonical implicit decision
- `Test/Mist.Tests/ProgramAnalysis/BaseConstructorRefusalTests.cs`: own-phase source refusal controls
- `Test/Mist.Tests/Semantics/EmptyConstructorChainBindingTests.cs`: Tier 2 written empty chain compatibility
- `Test/Mist.Tests/Backend/StorageDefaultEmissionTests.cs`: Tier 2 property storage emission; existing assertions target surface property assignment and may require principled updates
- `Test/Mist.Tests/Backend/ConstructorDefaultEmissionTests.cs`: Tier 2 storage defaults/once-only/this chain/flattened generic base
- `Test/Mist.Tests/Backend/DefaultConsistencyTests.cs`: Tier 2 existing consistency diagnostic seam
- `Test/Mist.Tests/Backend/DefaultableInitializerEmissionTests.cs`: Tier 3 initializer once-only own-phase behavior
- `Test/Mist.Tests/Backend/DefaultSynthesisTests.cs`: default factory/sentinel compatibility

Focused commands, after building changed code:

```bash
tools/test-ledger/run.sh Test/Mist.Tests --filter "FullyQualifiedName~ImplicitChainsTests|FullyQualifiedName~StructImplicitChainsTests|FullyQualifiedName~BaseConstructorRefusalTests|FullyQualifiedName~EmptyConstructorChainBindingTests"
```

```bash
tools/test-ledger/run.sh Test/Mist.Tests --filter "FullyQualifiedName~StorageDefaultEmissionTests|FullyQualifiedName~ConstructorDefaultEmissionTests|FullyQualifiedName~DefaultConsistencyTests|FullyQualifiedName~DefaultableInitializerEmissionTests|FullyQualifiedName~DefaultSynthesisTests"
```

Choose test tiers from the invariant and its owner. Tier 1 decisions take directly constructed values; Tier 2 module chains use hand-built in-memory inputs and stub unrelated collaborators; Tier 3 cases stay in their domain and cost tens of milliseconds. Tier 4 covers a named seam, stays under ten seconds, and respects the existing boundary case budget. Heavy resource loading, Roslyn, compilers or processes are stubbed below Tier 5 unless that collaborator is the subject owned by the tested area. Existing expensive fixtures do not authorize new expensive lower-tier tests. Give each new or migrated test exactly one effective runnable `Tier` trait. Bank justified runtime examples in the existing sample/AllFeaturesV2 path; do not create a second full-system orchestrator or per-case MSBuild/compiler loops.

Use focused checks for individual fixes, broader checks at meaningful integration checkpoints, and the required final regression gates on the complete bundle. Do not repeat a full suite mechanically after every small change; repeat checks when new changes or failures invalidate their evidence. Keep coherent commits and item-level verification/closeout evidence throughout the session.

Final Mist gates on the integrated result:

```bash
dotnet build Mist.slnx
dotnet format Mist.slnx --verify-no-changes
tools/test-ledger/run.sh
git diff --check
```

Start full ledger runs in the background and retain their output. Read `summary.txt` and `failures.txt` before rerunning; do not discard failures through output filters. Record every intermittent failure sighting on its matching canonical issue, with actual failing/passing evidence and unknown cause when unestablished. Require zero build/format warnings and errors under the repository policy; fix infrastructure paths rather than silently suppressing warnings. Report actual results and environment limits, never a hypothetical Windows or full-suite pass.

Update `MIST-054`, `MIST-057`, `MIST-236` in the actual `backlog/issues.yaml`. Record implemented behavior, tests/evidence and precise remaining limitations. Keep changes narrow; preserve historical evidence, stable IDs and other workers' updates. Move fully discharged records to the archive and retain genuinely unfinished residue. Do not merely return proposed closeout text or create another tracking system. Update necessary user/API/spec documentation and generated artifacts in their owning files.

Finish all three included records. Default initialization must write canonical storage rather than merely hide output or forbid a type because its unused setter is impure. MIST-057 needs both nonempty-pack refusal and valid empty-pack materialization; MIST-236 must retain native/Standard exceptions and correct primary argument-error ownership.

## Commit and land

You are authorized to commit and publish this task directly to remote `main` without another confirmation. Do not create pull requests. Commit implementation, appropriate tests/generated artifacts, required documentation and actual closeout updates together or in coherent task commits. Fetch and incorporate the latest target branch, resolve conflicts while preserving other landed work, and perform the required verification on the integrated result. Use a normal, non-force push. Work from your own isolated checkout; do not reset or commandeer another session's working tree.

Other workers may land concurrently. If publication is rejected because the target advanced, fetch, reconcile, rerun verification affected by the new combined result, and retry. Keep shared-register edits narrow. You own routine integration; do not hand it to the user or assume a central integrator. Do not force-push the target, bypass protections, silently accept failures or mark unfinished work complete. Preserve newer compatible Storm pins already incorporated by main.

Finish by fetching and confirming that the intended changes, additional incidental fixes and actual closeout are present on remote `main`. Report landed commit IDs, actual verification results, entries updated, completed incidental fixes and any remaining limitations. A local commit or pushed task branch alone is not completion.

If unavailable credentials or branch protection prevents landing, complete all unaffected work, preserve the result durably, and report the exact blocker and smallest required user action. Do not switch to a PR workflow. If a genuine unresolved product decision prevents implementation, identify it precisely and retain the affected item as open. Do not repeat an unchanged failed attempt indefinitely or claim success without verification.
