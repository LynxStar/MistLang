# 28 — CLR call contracts and protocol adaptation

Complete this bundle independently, including verification, actual tracking updates, commits and publication. Repository: `https://github.com/LynxStar/MistLang.git`; target: remote `main`. Suggested model tier: **Sol/Opus**, effort: **high**. Five contract fixes share the projection-to-call/conformance boundary. Optional/params selection, top-type normalization, nullable native annotations and inherited/default adapters need end-to-end consistency; existing metadata and conformance APIs constrain the design. These portable labels guide the same task and do not require a separate reviewer or model change.

## Starting point

Planning baseline: `0428515879b2d0245c996a5db98a676e441b9cbe` (2026-10-11). Compiler/source probes and the fresh solution build used `b1e556fb9d9528367f7de6bd7d71a13bd5c28aad`; intervening commits only reconcile canonical tracking evidence/contracts, with no source or test changes. Storm pin: `83410653c7d722868a5001d2ef71e6692ec012b3`. The build passed with zero warnings/errors. No fresh full-suite pass is claimed. Evidence below distinguishes executed probes, current source inspection and leads needing validation. Previous packets 17–25 have landed; revalidate current main rather than repeating those implementations.

Start from current remote main in an isolated checkout, retaining your existing isolated checkout and completed work when continuing. Follow the repository’s applicable instructions. Initialize with `git submodule update --init --recursive` and verify checked-out commits match current main’s recorded pins. Revalidate each item on current main; if already fixed, verify acceptance and complete any real closeout gap without duplicate code. Do not read or port retired classic implementation from Git history. Spec task documents are implemented and excluded from work discovery.

## Outcome and included obligations

Preserve CLR callable argument shapes and native contracts before applying source checking, and emit the inherited CLR protocol/default adaptations that existing Mist conformance already requires.

### MIST-003

assembly-type-provider requirement5.6 (requirements.md:88) explicitly projects optional/default arguments. ambient-type-provider-v2 requirements7.3 and9.1–5 (requirements.md:104,131–135) require source and assembly types to obey the same callable/member semantics. Existing homogeneous params call semantics must apply to CLR ParamArray signatures. variadics requirement13.1–6 (requirements.md:238–243) specifies homogeneous params array flattening; keep this existing call convention on imported ParamArray members.

**Current evidence:**

executed CLI
MIST_PM_NO_APPLICABLE_OVERLOAD: JsonSerializer.Serialize<int>(3) accepts1 supplied argument, but projected declared arities are2,3,4.

Observed exit code: 1.

```mist
using Standard;
namespace Probe;
public class Program{public static void Main(){Console.WriteLine(System.Text.Json.JsonSerializer.Serialize<int>(3));}}
```
executed CLI
MIST_PM_NO_APPLICABLE_OVERLOAD: Path.Combine with5 strings; declared arities1,2,3,4.

Observed exit code: 1.

```mist
using Standard;
namespace Probe;
public class Program{public static void Main(){Console.WriteLine(System.IO.Path.Combine("a","b","c","d","e"));}}
```
executed CLI
MIST_PM_UNRESOLVABLE_OVERLOAD for Dictionary<string,int>[1]. This older tracker example is already source-refused, not a currently confirmed Roslyn gap.

Observed exit code: 1.

```mist
using Standard;
namespace Probe;
public class Program{public static void Main(){var d=new System.Collections.Generic.Dictionary<string,int>();Console.WriteLine(d[1]);}}
```

**Owning source:**

- `Source/Mist/Backends/CSharpTranspiler/AmbientProvider/ParameterProjector.cs` — Project
- `Source/Mist/ProgramModel/Model/Members/Parameter.cs` — IsFlattened and absent native optional/default shape
- `Source/Mist/ProgramModel/Construction/Binding/OverloadResolution.cs` — candidate arity and argument applicability
- `Source/Mist/ProgramModel/Construction/Binding/Expressions/ArgumentBinder.cs` — selected argument pairing
- `Source/Mist/ProgramAnalysis/Rules/AssignabilityAnalyzer.cs` — CheckArguments projected skip; PairsWithIndex (audited line 177)

**Acceptance and controls:**

- JsonSerializer.Serialize<int>(3) runs and returns the JSON text3 through its optional JsonSerializerOptions parameter; supplying options explicitly keeps the same selected overload. Test omitted trailing parameters, multiple defaults, and null/primitive/enum native default metadata at Tier1/2. Missing required parameters and wrong supplied arguments remain source refusals.
- Path.Combine("a","b","c","d","e") runs with the platform-correct path. Exercise empty/one/many flattened native params values, an explicit params array, fixed parameters before params, and wrong element types. Preserve existing Mist pack and homogeneous array argument targeting.
- Only remove projected-call/index assignability skips after argument pairing/optional/params selection faithfully models those signatures. Add valid and invalid projected method, constructor and indexer controls plus ref/in/out and overload competitors, and preserve established contextual trial isolation/cascade ownership.
- Do not invent Mist-written optional-parameter syntax, native default evaluation policy or unsupported CLR features. Optional defaults are native metadata/call behavior. Record the already-correct bad dictionary-key refusal as a narrow historical closure.

### MIST-053

Projected System.Object must behave as its CLR top/boxing target without treating every projected class as a top type. ambient-type-provider-v2 requirement7.3 origin does not alter semantic outcomes; assembly-type-provider3/7 provide faithful type relationships. Preserve unknown and Object identities unless existing mapping explicitly unifies them.

**Current evidence:**

executed CLI
MIST_PA_NO_IMPLICIT_CONVERSION: int cannot convert to Object at local initializer.

Observed exit code: 1.

```mist
using Standard;
namespace Probe;
public class Program{public static void Main(){System.Object value=3;Console.WriteLine(value);}}
```

**Owning source:**

- `Source/Mist/Backends/CSharpTranspiler/AmbientProvider/AmbientTypeResolver.cs` — CLR type normalization
- `Source/Mist/Backends/CSharpTranspiler/AmbientProvider/BaseTypeProjector.cs` — ProjectBaseType (audited line 33)
- `Source/Mist/ProgramModel/Construction/Binding/ConversionRelation.cs` — WidensToUnknown and nominal conversion
- `Source/Mist/ProgramAnalysis/Rules/AssignabilityAnalyzer.cs` — local initializer Check (audited line 31)

**Acceptance and controls:**

- System.Object value=3 is admitted and preserves boxed value3; plain CLR/object parameters accept primitive, class, tuple and nullable values according to established CLR conversion semantics. Pair with equivalent unknown top-assignment controls.
- An unrelated projected class/interface remains a normal nominal target and refuses incompatible values. Do not erase purity/ownership/stampability guarantees merely because the native target can box; preserve existing conversion rules and no reverse Object-to-specific implicit conversion.
- Once native callable enforcement is enabled, valid object-target APIs must not become false positive source errors. Use component conversion tests rather than hiding Object mistakes behind the old projected skip.

### MIST-023

iterators-enumerables requirements describe IEnumerable/IEnumerator conformance through abstracts; assembly-type-provider18.1–5 (requirements.md:330–334) maps generic CLR protocols and normal foreach. Required CLR non-generic/IDisposable adapter members must also be supplied when the protocol is reached through a user abstract.

**Current evidence:**

executed CLI
MIST_CS_ERROR: Cursor does not implement IEnumerator.Reset, IEnumerator.Current(object) or IDisposable.Dispose.

Observed exit code: 1.

```mist
using Standard;
namespace Probe;
public abstract ICursor:IEnumerator<int>{} public class Cursor:ICursor{public int Current {get{return 3;}} public bool MoveNext(){return false;}} public class Program{public static void Main(){Console.WriteLine("ok");}}
```

**Owning source:**

- `Source/Mist/Backends/CSharpTranspiler/Emitter/IteratorBoilerplateEmitter.cs` — Emit iterates direct SlotLift.Reached (audited line 25)
- `Source/Mist/ProgramModel/Construction/TypeSlots.cs` — Reached (audited line 17)
- `Source/Mist/ProgramModel/Construction/Binding/AbstractInheritance.cs` — Reached
- `Source/Mist/Backends/CSharpTranspiler/Lowering/SlotLift.cs` — Reached (audited line 50)

**Acceptance and controls:**

- abstract ICursor:IEnumerator<int>{}; Cursor:ICursor with int Current and bool MoveNext builds with required CLR Reset/Current/Dispose boilerplate. Add inherited IEnumerable and foreach controls, generic substitution and two-level/diamond inheritance.
- Direct protocol conformers still emit each required adapter once; unrelated abstracts receive none. Preserve typed Current and element identity, existing user implementations, existing Reset refusal and iterator disposal/cleanup contract.
- Use existing AbstractInheritance traversal privately to obtain reached constructed protocols; do not change TypeSlots.Reached or require packet29 to publish a new traversal API. Consumer element arguments must come from the reached protocol substitution, not merely the direct parent type arguments.

### MIST-048

ambient-type-provider-v2 requirement7.3 and8.4 (requirements.md:104,116) demand CLR interfaces participate faithfully in conformance. abstract-type-unification14.3–4 (requirements.md:288–289) allows known usable defaults and lets exact user answers override them. abstract-type-unification Requirement 15.1–15.3 (requirements.md:299–301), together with origin parity, also requires direct concrete-conformer calls to known defaults and interface calls to agree; exact user overrides win.

**Current evidence:**

source inspection at current revision
A projected nonabstract interface method has no Body/native-default availability marker. Unanswered slots are exempt only for nonnull source Body/unresolved mapping. No current end-to-end native-default fixture executed.

**Owning source:**

- `Source/Mist/Backends/CSharpTranspiler/AmbientProvider/MethodProjector.cs` — ProjectMethod creates Method without native default availability (audited line 101)
- `Source/Mist/ProgramAnalysis/Rules/ConformanceAnalyzer.cs` — CheckSlots exempts only slot.Body default (audited line 114)
- `Source/Mist/ProgramModel/Model/Members/Method.cs` — native member availability metadata

**Acceptance and controls:**

- Compile a small native metadata fixture with an instance default interface method plus an abstract required method. A Mist conformer providing only the required method is accepted; calls through both the native interface and the concrete conformer execute the default with identical behavior. An exact written override wins in both routes.
- Required abstract slots still report missing; static, inaccessible or otherwise unusable bodies do not silently exempt instance obligations. Cover inherited usable defaults and overriding/reabstracting where existing CLR contract metadata settles availability.
- Record native body availability independently of a fabricated Mist source Body. Emit the adapter or interface-cast dispatch needed by accepted direct concrete/default calls; preserve default selection and override precedence. Do not execute native bodies during projection, import their source, or use metadata availability alone to admit a direct call that generated C# cannot perform.

### MIST-212

Generated consumers must match nullable native implemented-slot annotations under ProjectFileEmitter Nullable enable. CLAUDE.md zero-warning contract. This is native C# annotation fidelity; preserve current deliberate nonwrapping of ordinary CLR reference input parameters rather than adopting the outdated broad inbound-reference policy.

**Current evidence:**

executed CLI
CLI printsx; fresh emitted ToString(string format,System.IFormatProvider provider) has no nullable annotations while generated csproj enables Nullable.

Observed exit code: 0.

```mist
using Standard;using System;
namespace Probe;
public class Formatter:IFormattable{public readonly string ToString(string format,IFormatProvider provider){return "x";}} public class Program{public static void Main(){Standard.Console.WriteLine(new Formatter());}}
```
historical generated-consumer execution plus current source inspection
Canonical MIST212 records CS8767 pair for IFormattable format/provider at0998bc6e; current emission retains the same nonnullable parameter spellings. No fresh generated-consumer build was executed in this audit.

**Owning source:**

- `Source/Mist/Backends/CSharpTranspiler/Emitter/ParameterEmitter.cs` — Emit ordinary type spelling (audited line 17)
- `Source/Mist/Backends/CSharpTranspiler/AmbientProvider/ParameterProjector.cs` — Project parameter metadata
- `Source/Mist/Backends/CSharpTranspiler/AmbientProvider/ClrNullability.cs` — native nullability metadata
- `Source/Mist/Backends/CSharpTranspiler/Emitter/ProjectFileEmitter.cs` — Nullable enable
- `Test/Mist.Tests/Backend/ClrInboundOptionalProjectionTests.cs` — AClrReferenceParameter_IsNotWrapped (audited line 121)

**Acceptance and controls:**

- One IFormattable implementer and generic/inherited implementer controls build generated consumer with no CS8767 while formatting result remains unchanged. Both format and provider annotations agree with the native implemented slot.
- Ordinary user signatures, nonoptional references, ref/in/out and native nonnullable slots retain established spelling. Nullable parameter annotations must come from the selected implemented native slot/metadata, not a blanket reference-param widening or warning suppression.
- Existing callback readonly admission, owned mutation exceptions and native format dispatch stay unchanged. The consumer check must report all diagnostics and isolate CS8767 from unrelated never-assigned/unused warnings.

## Scope and independence

Own CLR argument-shape metadata, native optional/params pairing and source enforcement, Object conversion correctness, native-default availability and inherited iterator boilerplate, selected native parameter annotation fidelity. Narrow changes to Method/Parameter metadata and conformance consumers are allowed. Preserve current chosen overload identity, source default rules and generic witness APIs.

Packet29 owns generic context key channels and generic associated/witness signature lowering. This packet adds no new hidden generic witness representation: optional/default native args describe supplied/omitted native slots, not caller-carried Mist witnesses. Iterator adapters can privately use existing AbstractInheritance.Reached on direct bases; native default availability only changes the unanswered-slot availability decision. ParameterEmitter changes for native annotations must leave MethodEmitter hidden parameter ordering/forwarding untouched. No prerequisite API publication.


**Excluded work and decisions:**

- MIST047 raw non-enum constant metadata: retain current lead, but no accepted consuming constant-reference contract was established; do not combine merely because FieldProjector is nearby.
- MIST049 base/interface/constraint tuple-name metadata: retained named capability, but not required by these callable/protocol controls; row-level metadata work is independent substantial scope.
- MIST090 broad inherited CLR construction/member inventory remains unverified; do not expand this packet to unbounded CLR surface harvesting.
- No imported CLR stampability policy, broad inbound-reference optionality rewrite, new foreign operator/ref-return feature, user optional-parameter syntax, generic context or associated slot lift redesign.

## Focused verification

- Test/Mist.Tests/Backend/AmbientCallableProjectionTests.cs (Tier3 existing projected generic call shapes)
- Test/Mist.Tests/Backend/ClrInboundOptionalProjectionTests.cs:71–121 (return optionality and deliberate ordinary reference-param control)
- Test/Mist.Tests/Backend/ClrAccessorAccessibilityProjectionTests.cs
- Test/Mist.Tests/Backend/AmbientConstraintProjectionTests.cs
- Test/Mist.Tests/Backend/AmbientProviderTestHelper.cs
- Test/Mist.Tests/ProgramAnalysis/ConformanceDecisionTests.cs
- samples/Iterators/Program.mist
- samples/CSharpInteropV2/Program.mist

Tier1 native optional/default/params descriptor and conversion decisions; Tier2 selected call/index argument and conformance/adapter emission slices with hand-built models. Use existing metadata fixture/provider seams for reflection facts and classify genuine provider integration as its actual tier. One bounded native-default/interop runtime boundary and generated consumer NRT build; no per-rule full compiler fixtures.

Initial focused commands (extend filters to your new classes; these do not replace final gates):

- `tools/test-ledger/run.sh Test/Mist.Tests --filter "FullyQualifiedName~AmbientCallableProjectionTests|FullyQualifiedName~ClrInboundOptionalProjectionTests|FullyQualifiedName~ConformanceDecisionTests|FullyQualifiedName~ClrAccessorAccessibilityProjectionTests"`
- `dotnet run --project Source/Mist.CLI --no-build -- run <scratch-project> --quiet`
- `dotnet build <scratch-project>/out/Probe.csproj --nologo` — native formatting consumer; inspect all diagnostics

To reproduce inline cases, create a fresh scratch project containing `Program.mist` and this `mist.toml`, unless the case explicitly supplies an entry-point override:

```toml
name = "Probe"
namespace_root = "Probe"
```

After building current main, run `dotnet run --project Source/Mist.CLI --no-build -- run <scratch-project> --quiet` from the repository. For generated-consumer fidelity, also run `dotnet build <scratch-project>/out/Probe.csproj --no-incremental` and inspect every diagnostic. Scratch projects and generated `out/` remain untracked. Complete source snippets are reproductions; smaller prose examples are component inputs to construct in the existing narrow seams.

Owning canonical records: `MIST-003`, `MIST-053`, `MIST-023`, `MIST-048`, `MIST-212`. Each included bounded obligation requires its own verified disposition; preserve excluded or unverified residue rather than claiming the entire record fixed from one representative case.

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
