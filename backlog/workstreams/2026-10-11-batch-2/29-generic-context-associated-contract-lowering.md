# 29 — Generic context keys and associated contracts

Complete this bundle independently, including verification, actual tracking updates, commits and publication. Repository: `https://github.com/LynxStar/MistLang.git`; target: remote `main`. Suggested model tier: **Astra/Fable**, effort: **high**. Semantic context identity is lost under generic erasure, while weaker slot answers and associated types carry different hidden channels. One owner must make declarations, calls, abstract dispatch and forwarding agree without dropping a required key or witness; this is demonstrated deep signature/substitution work. These portable labels guide the same task and do not require a separate reviewer or model change.

## Starting point

Planning baseline: `0428515879b2d0245c996a5db98a676e441b9cbe` (2026-10-11). Compiler/source probes and the fresh solution build used `b1e556fb9d9528367f7de6bd7d71a13bd5c28aad`; intervening commits only reconcile canonical tracking evidence/contracts, with no source or test changes. Storm pin: `83410653c7d722868a5001d2ef71e6692ec012b3`. The build passed with zero warnings/errors. No fresh full-suite pass is claimed. Evidence below distinguishes executed probes, current source inspection and leads needing validation. Previous packets 17–25 have landed; revalidate current main rather than repeating those implementations.

Start from current remote main in an isolated checkout, retaining your existing isolated checkout and completed work when continuing. Follow the repository’s applicable instructions. Initialize with `git submodule update --init --recursive` and verify checked-out commits match current main’s recorded pins. Revalidate each item on current main; if already fixed, verify acceptance and complete any real closeout gap without duplicate code. Do not read or port retired classic implementation from Git history. Spec task documents are implemented and excluded from work discovery.

## Outcome and included obligations

Carry semantic generic context keys and associated-type/witness obligations through emitted generic declarations, dispatch bridges, inheritance and nested alias applications without changing source value representation.

### MIST-022

structured-context5.1 and5.6–8 (requirements.md:92,97–99) requires exact nominal/constructed key identity with alias normalization and separate union/constituent keys. Generic erasure must preserve required-vs-optional reference keys while leaving their ordinary value representation unchanged.

**Current evidence:**

executed CLI
CLI printsrequired instead ofoptional.

Observed exit code: 0.

```mist
using Standard;
namespace Probe;
public class Program { public static T Get<T>() with T value { return value; } public static void Main() { string? opt="optional"; with opt; with "required"; Console.WriteLine(Get<string?>()); } }
```

**Owning source:**

- `Source/Mist/ProgramAnalysis/ContextKeys.cs` — IsOpen and semantic key matching
- `Source/Mist/Backends/CSharpTranspiler/Emitter/TypeNameEmitter.cs` — EmitContextKey
- `Source/Mist/Backends/CSharpTranspiler/Emitter/BodyEmitter.cs` — method context entry Peek (audited line 157)
- `Source/Mist/Backends/CSharpTranspiler/Lowering/ContextLowering.cs` — runtime Push/Pop/Peek
- `Source/Mist/Backends/CSharpTranspiler/Emitter/MethodEmitter.cs` — EmitSignature; ForwardingTypeArguments; ForwardingArguments (audited line 63)

**Acceptance and controls:**

- Under optional-string context "optional" and required-string context "required", Get<string?>() with T value returns optional; Get<string>() returns required. Same source CLR string payload cannot collapse those semantic key identities. Missing exact context stays the established source diagnostic; no inheritance lookup is introduced.
- Cover generic method and generic class/receiver consumers, nested forwarding through another generic, extension receiver/method closures, constructor clauses, aliases and optional keys nested inside tuple/callable/constructed types already accepted. Required/optional value-type controls and pure-function key identity remain correct.
- Include abstract generic dispatch and existing default/constructor-witness channels in the matrix, plus a callable reference to a contextual generic method where currently admitted. Context key metadata must reach the callee once without re-evaluating receivers/arguments, altering source arity/overload selection or value null representation.
- Preserve with push/pop, shadowing/restoration and lifetime cleanup; scope entry reads each declared clause even if unused. Choose a coherent existing-contract lowering internally, not a new public/source syntax or runtime contextual subtype search.

### MIST-218

MIST011 implemented contract admits a generic answer whose required constraints are entailed by its slot promises; native C# witness arity must be adapted. abstract-type-unification14.1/14.4 and28.4 exact member-contract resolution plus current SlotSignatures.Strengthened/ConformanceAssociatedSlotTests define accepted weaker answers; do not revert to C# textual equality.

**Current evidence:**

executed CLI
MIST_CS_ERROR: Maker does not implement IMaker.Make<T>(Func<T>,T).

Observed exit code: 1.

```mist
using Standard;
namespace Probe;
public abstract IMaker { string Make<T>(T value) where T:default; } public class Maker:IMaker{public string Make<U>(U value){return "answer";}} public class Program{public static void Main(){IMaker m=new Maker();Console.WriteLine(m.Make<int>(3));}}
```

**Owning source:**

- `Source/Mist/Backends/CSharpTranspiler/Emitter/ConstraintSlotBridgeEmitter.cs` — Emit; SameWitnesses refuses bridge
- `Source/Mist/Backends/CSharpTranspiler/Emitter/MethodEmitter.cs` — EmitSignature and ForwardingArguments (audited line 63)
- `Source/Mist/Backends/CSharpTranspiler/Lowering/DefaultConstraintWitness.cs` — witness parameter and name
- `Source/Mist/Backends/CSharpTranspiler/Lowering/ConstructorWitnesses.cs` — Of/Parameters/Names
- `Source/Mist/ProgramAnalysis/SlotSignatures.cs` — Strengthened (audited line 73)

**Acceptance and controls:**

- IMaker.Make<T>(T value) where T:default answered by unconstrained Make<U>(U value) builds; interface and direct calls reach the answer once, and the answer direct signature stays weaker. Add a slot constructor witness answered by less constrained/no-witness generic method.
- Adapt each hidden channel by slot/answer identity: default, constructor, stored-owner witnesses, lifted associated parameters and generic context-key metadata. Forward witnesses an answer still requires, omit those it does not; preserve type-param renaming, inherited answer substitution, ordinary/ref argument passing and return behavior.
- Equal constraints/witness sets require no duplicate explicit bridge. Stronger answers remain source-refused under existing conformance checking. Preserve pack and async bridges and all existing promotion/default factory behavior; no new async witness policy.

### MIST-219

associated-types4.1–4 and5.2 (requirements.md:76–79,88) retain slot identity and concrete substitution; abstract inheritance must retain the base contract. Derived abstract interfaces must carry inherited lifted associated arguments in declarations, bases and member uses.

**Current evidence:**

executed CLI
MIST_CS_ERROR: IBag<Element> requires1 type argument at derived INamedBag base.

Observed exit code: 1.

```mist
using Standard;
namespace Probe;
public abstract IBag{type Element; Element Get();} public abstract INamedBag:IBag{} public class Strings:INamedBag{public type Element=string;public string Get(){return "s";}} public class Program{public static void Main(){Console.WriteLine(new Strings().Get());}}
```

**Owning source:**

- `Source/Mist/Backends/CSharpTranspiler/Lowering/SlotLift.cs` — Lifted; Base; Synthesized; Through (audited line 31)
- `Source/Mist/Backends/CSharpTranspiler/Emitter/TypeEmitter.cs` — EmitBaseList and Parameters (audited line 460)
- `Source/Mist/ProgramModel/Construction/TypeSlots.cs` — Closing and Filler
- `Source/Mist/ProgramModel/Construction/Binding/AbstractInheritance.cs` — Reached

**Acceptance and controls:**

- IBag{type Element;Element Get();}, INamedBag:IBag, Strings:INamedBag with Element=string and string Get builds and returns s. Direct IBag conformance remains correct.
- Cover generic derived abstracts, two-level/diamond inheritance, multiple inherited slots and slot names that coincide in different contracts. Lift each actual slot identity once with deterministic stable parameter ordering and correct base type substitution.
- Inherited and nested class/alias fillers close the base contract; missing or mismatched fillers remain source-refused, not erased to arbitrary wildcard. Integrate associated slot type channels with contextual/witness dispatch rather than requiring another owner’s API.

### MIST-221

associated-types glossary Concrete_Type_Member permits a nested type alias, and requirement3.9 (requirements.md:68) matches name and arity. A parameterized slot can be filled by a nested generic alias expanded at each actual application and judged against its declared bound.

**Current evidence:**

executed CLI
MIST_PA_TYPE_SLOT_UNFILLED: Shelf declares no nested Holder for IShelf.Holder.

Observed exit code: 1.

```mist
using Standard;
namespace Probe;
public abstract IShelf{type Holder<T>;} public class Shelf:IShelf{public type Holder<T> = System.Collections.Generic.List<T>;} public class Program{public static void Main(){Console.WriteLine("ok");}}
```

**Owning source:**

- `Source/Mist/ProgramModel/Construction/TypeSlots.cs` — Filler accepts TypeAlias only arity0 (audited line 40)
- `Source/Mist/ProgramAnalysis/Rules/ConformanceAnalyzer.cs` — TypeSlots and parameterized slot constraints
- `Source/Mist/Backends/CSharpTranspiler/Lowering/SlotLift.cs` — Erased and bound application
- `Source/Mist/ProgramModel/Construction/Types/TypeAliasBuilder.cs` — nested generic alias construction

**Acceptance and controls:**

- abstract IShelf{type Holder<T>;} class Shelf:IShelf{public type Holder<T> = System.Collections.Generic.List<T>;} is accepted as a type-slot filler. Component cases apply Holder<int>/Holder<string> and check the resulting target types, substitutions and current erasure contract.
- Wrong name/arity or violated class/struct/nominal/constructor bound remains refused. Ordinary zero-arity aliases and nested nominal generic fillers remain correct. Include inherited alias filler, alias forwarding and recursive/error alias controls to avoid nontermination/cascades.
- Do not invent higher-kinded C# generic parameters: retain parameterized-slot bound erasure strategy and expand the alias per application before conformance/emission checks. Keep source aliases’ underlying nominal identity and inherited closing arguments.

## Scope and independence

Own TypeSlots/SlotLift associated closing/lifting, generic hidden context-key channels and all corresponding signature/call/delegate/dispatch consumers, ConstraintSlotBridgeEmitter and default/constructor witness forwarding adaptations. Preserve established binder-selected method identity, contextual clause matching, inference and constraint-strengthening decisions.

MethodEmitter.EmitSignature presently appends default+constructor witness strings and explicit source parameters; ForwardingArguments forwards every slot witness without an answer-channel mapping. Generic optional context keys require additional semantic key information across those same generic declarations and calls, including abstract dispatch. A separate witness-bridge worker would need to agree on or retrofit an unpublished forwarding representation. Keeping022 and218 with inherited/parameterized slot channels219/221 under one owner avoids this real dependency; no excluded abstract generic consumer is used to manufacture isolation.

Packet28 owns native callable metadata and protocol/default adapters; it does not redesign emitted Mist hidden generic signatures. This owner retains existing direct nominal-base traversal API so iterator adapter28 can privately walk AbstractInheritance.Reached. Native-default availability recognition may change adjacent ConformanceAnalyzer lines but does not alter TypeSlots/SlotLift contracts this packet needs. Structural pattern packet owns carrier names, refined predicates and tuple runtime test realization; preserve TypeNameEmitter.Emit/EmitContextKey public contracts while adding internally coordinated generic key propagation.


**Excluded work and decisions:**

- No source optional-parameter/default-entry policy, generic inference redesign, native parameter nullability policy or CLR default-implementation body import.
- No new higher-kinded CLR representation, scalar witness domain redesign, broad relational solver, union literal selection policy, context subtype lookup, async promotion policy or resource cleanup redesign.

## Focused verification

- Test/Mist.Tests/Backend/StructuredContextKeyEmissionTests.cs:28,41,60,77,97 (Tier1/2 exact marker identities and push/pop/entry controls)
- Test/Mist.Tests/Backend/StructuredContextTests.cs:149 generic clause recorded closed key (Tier3 existing seam)
- Test/Mist.Tests/ProgramAnalysis/StructuredContextDecisionTests.cs
- Test/Mist.Tests/Backend/ConstraintSlotBridgeEmissionTests.cs (Tier2 current weaker-answer bridge test explicitly leaves new(Int32)→no-witness unbridged)
- Test/Mist.Tests/Backend/PackSlotBridgeEmissionTests.cs
- Test/Mist.Tests/Backend/AsyncSlotBridgeEmissionTests.cs
- Test/Mist.Tests/ProgramAnalysis/ConformanceAssociatedSlotTests.cs (Tier2 concrete slot/stronger generic answer controls)
- Test/Mist.Tests/ProgramAnalysis/SlotSignatureDecisionTests.cs
- Test/Mist.Tests/Semantics/TypeSlotFillerTests.cs:37 currently asserts generic aliases cannot fill parameterized slots; revise this outdated expectation to accepted glossary contract with bounded controls

Tier1 semantic key/substitution and slot application decisions; Tier2 signature/channel forwarding, context entry and associated interface/base emission slices. Use one bounded runtime seam for optional-vs-required context through generic/abstract forwarding and witness-bridge behavior. Existing provider/runtime or legacy tests retain actual tier; do not copy whole pipeline into new per-case unit tests.

Initial focused commands (extend filters to your new classes; these do not replace final gates):

- `tools/test-ledger/run.sh Test/Mist.Tests --filter "FullyQualifiedName~StructuredContextKeyEmissionTests|FullyQualifiedName~ConstraintSlotBridgeEmissionTests|FullyQualifiedName~TypeSlotFillerTests|FullyQualifiedName~ConformanceAssociatedSlotTests|FullyQualifiedName~PackSlotBridgeEmissionTests|FullyQualifiedName~AsyncSlotBridgeEmissionTests"`
- `dotnet run --project Source/Mist.CLI --no-build -- run <scratch-project> --quiet`

To reproduce inline cases, create a fresh scratch project containing `Program.mist` and this `mist.toml`, unless the case explicitly supplies an entry-point override:

```toml
name = "Probe"
namespace_root = "Probe"
```

After building current main, run `dotnet run --project Source/Mist.CLI --no-build -- run <scratch-project> --quiet` from the repository. For generated-consumer fidelity, also run `dotnet build <scratch-project>/out/Probe.csproj --no-incremental` and inspect every diagnostic. Scratch projects and generated `out/` remain untracked. Complete source snippets are reproductions; smaller prose examples are component inputs to construct in the existing narrow seams.

Owning canonical records: `MIST-022`, `MIST-218`, `MIST-219`, `MIST-221`. Each included bounded obligation requires its own verified disposition; preserve excluded or unverified residue rather than claiming the entire record fixed from one representative case.

## Ownership and incidental fixes

This packet has no dependency on another workstream. As its parent session, you own every included task and its internal dependencies. Choose the execution order and what to do yourself or delegate; integrate contributions into your primary implementation, resolve internal conflicts and verify the combined result. Subagents return contributions to you rather than independently publishing partial work. Use current repository contracts. Do not hand ordering, coordination or integration to the user, planner or another packet. Complete every included bounded obligation and its item-level disposition; a representative fix or collection of reports is not completion.

Narrowly necessary adjacent source, diagnostic, test, API and documentation edits are included. Product and architectural exclusions above define scope, rather than arbitrary file bans. If you uncover additional issues with clear expected behavior that are straightforward to fix and verify without user input, solve them in additional coherent commits, including small fixes outside the primary files. Add appropriate verification and actual documentation/backlog updates, and land them through the same workflow. Do not merely report them as extras or ask the user to dispatch cleanup. Avoid duplicating landed work or another active packet. Defer only a genuine product decision, substantial new work, unavailable access/authorization or verification you cannot complete; give the concrete reason and finish unaffected work. Ordinary implementation choices do not require approval, and incidental cleanup must remain bounded.

## Verification and actual closeout

Use focused checks for individual fixes and broader checks at meaningful integration checkpoints. New decision tests belong at Tier 1; body/binder/emitter fragments at Tier 2. Classify each test with the actual `Tier` trait and use the narrowest owning seam. Whole-program compilation remains Tier 4 or higher; use existing runtime seams when genuinely necessary. AllFeaturesV2 remains the sole full-system stdout bank. Do not mechanically repeat a full suite per micro fix.

Finish the combined bundle with `dotnet build Mist.slnx` (zero warnings/errors), `dotnet format Mist.slnx --verify-no-changes`, `git diff --check`, and the required full regression through `tools/test-ledger/run.sh`. Run the suite in the background and inspect its `summary.txt` and `failures.txt` before any rerun. Record interrupted runs as interrupted and retain unexplained failures in the matching canonical issue’s observations; a green rerun does not erase them. On macOS, use the documented polling-file-watcher setting if that known environment condition is observed and record it. Repeat checks only when new changes, failures or integration invalidate their evidence. Include generated consumer/artifact checks specified above; generated `out/` directories remain scratch.

Update the named records in the actual `backlog/issues.yaml` with implementation, revisions, acceptance evidence and precise remaining limitations. Preserve stable IDs, historical provenance and other workers’ edits; move fully discharged records to its archive using the established format, and leave unresolved portions open. Do not return proposed closeout text or create another ledger. Spec task documents are already implemented and are not a work-discovery or closeout queue. Update relevant user/API/spec documentation for changed supported behavior without selecting an excluded language policy.

## Commit and land

You are authorized to commit and publish directly to `LynxStar/MistLang` remote `main` without another confirmation. Do not create pull requests. Commit implementation, appropriate tests/generated artifacts, required documentation and actual closeout together or in coherent task commits; keep each coherent incidental fix in an additional commit. Fetch and incorporate latest main, preserve concurrent landed changes, resolve conflicts and verify the integrated result. Use your own isolated checkout and a normal non-force push; do not reset another session’s worktree.

If publication is rejected because main advanced, fetch, reconcile, rerun affected verification and retry. Keep shared-register edits narrow. You own routine integration; do not hand it to the user or assume a central integrator. Do not force-push, bypass protections, silently accept failures or mark unfinished work complete.

Finish by fetching and confirming the intended changes, incidental fixes and closeout are present on remote main. Report landed commit IDs, actual verification results, records updated, completed incidental fixes and remaining limitations. A local commit or pushed task branch alone is not completion. If credentials/protection or another external blocker prevents landing, finish unaffected work, preserve it durably and report the exact blocker and smallest required user action. Do not switch to a PR workflow or repeat an unchanged failed attempt indefinitely. Retain genuinely undecided product residue as open.
