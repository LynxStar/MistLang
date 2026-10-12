# 31 — Ref and stack-storage lifetime safety

Complete this bundle independently, including verification, actual tracking updates, commits and publication. Repository: `https://github.com/LynxStar/MistLang.git`; target: remote `main`. Suggested model tier: **Sol/Opus**, effort: **high**. Referent lifetime and stack backing must propagate through bindings, member paths, aggregate construction and control-flow joins; conflating alias location with referent storage would reject valid field/array returns. These portable labels guide the same task and do not require a separate reviewer or model change.

## Starting point

Planning baseline: `0428515879b2d0245c996a5db98a676e441b9cbe` (2026-10-11). Compiler/source probes and the fresh solution build used `b1e556fb9d9528367f7de6bd7d71a13bd5c28aad`; intervening commits only reconcile canonical tracking evidence/contracts, with no source or test changes. Storm pin: `83410653c7d722868a5001d2ef71e6692ec012b3`. The build passed with zero warnings/errors. No fresh full-suite pass is claimed. Evidence below distinguishes executed probes, current source inspection and leads needing validation. Previous packets 17–25 have landed; revalidate current main rather than repeating those implementations.

Start from current remote main in an isolated checkout, retaining your existing isolated checkout and completed work when continuing. Follow the repository’s applicable instructions. Initialize with `git submodule update --init --recursive` and verify checked-out commits match current main’s recorded pins. Revalidate each item on current main; if already fixed, verify acceptance and complete any real closeout gap without duplicate code. Do not read or port retired classic implementation from Git history. Spec task documents are implemented and excluded from work discovery.

## Outcome and included obligations

Ref returns, stack-backed aggregate escapes, ref fields and ref-struct storage positions are judged at Mist source before generated C# while preserving safe heap-backed references and frame-local stack use.

### MIST-080 — Source judgment for local and parameter ref-return lifetime

**Current evidence:**

executed current CLI run; no test-suite execution claimed
<scratch-project>/out/Probe/Program.cs: error MIST_CS_ERROR: Cannot return local 'x' by reference because it is not a ref local

Observed exit code: 1.

```mist
using Standard; namespace Probe; public class Program { public static ref int Bad() { int x = 1; return ref x; } public static void Main() { Console.WriteLine(Bad()); } }
```
executed current CLI run; no test-suite execution claimed
<scratch-project>/out/Probe/Program.cs: error MIST_CS_ERROR: Cannot return 'alias' by reference because it was initialized to a value that cannot be returned by reference

Observed exit code: 1.

```mist
using Standard; namespace Probe; public class Program { public static ref int Bad() { int x = 1; ref int alias = x; return ref alias; } public static void Main() { Console.WriteLine(Bad()); } }
```
executed current CLI run; no test-suite execution claimed
9

Observed exit code: 0.

```mist
using Standard; namespace Probe; public class Holder { public int Value = 7; public ref int Get() { return ref this.Value; } } public class Program { public static void Main() { var h = new Holder(); ref int x = h.Get(); x = 9; Console.WriteLine(h.Value); } }
```

**Contract references:**

- `.kiro/specs/ref-parameters/requirements.md` — Requirement4.3–4.6 lifetime of ref and readonly-ref returns (audited line 78)
- `.kiro/specs/ref-parameters/requirements.md` — Requirement6.1–6.8 ref-safety aliases, local/non-ref parameter refusal and valid heap/static returns (audited line 107)

**Owning source:**

- `Source/Mist/ProgramAnalysis/Rules/PositionAnalyzer.cs` — ReturnStatement.IsRef calls JudgeReference for location/writability only (audited line 58)
- `Source/Mist/ProgramAnalysis/Rules/EscapeAnalyzer.cs` — Analyze / JudgeStackSpans has no general ref-return referent lifetime (audited line 21)
- `Source/Mist/ProgramAnalysis/StorageLocations.cs` — existing lvalue/storage path decisions

**Acceptance and controls:**

- Refuse ref and readonly-ref returns of ordinary locals/non-ref parameters and aliases rooted in their storage, at the offending source return. Distinguish the alias variable scope from its referent: a ref local to a valid long-lived field may be returned, while a ref local to a local may not.
- Admit forwarding a ref parameter, static fields, heap object instance fields/chains and array element storage that actually survives the call. Current class instance Get()→this.Value control prints9 after mutation through its returned alias; preserve it. Keep non-location/readonly writable-ref errors owned by PositionAnalyzer instead of reporting derivatives.
- Use shared FlowGraph/IFlowLattice for flow-sensitive alias reassignment/branch/scope lifetime facts. Support already parsed ref reassignment as required by the existing ref spec; do not add grammar or a new ref representation.

### MIST-064 — Stack backing carried through containing ref-struct values

**Current evidence:**

executed current CLI run; no test-suite execution claimed
<scratch-project>/out/Probe/Program.cs: error MIST_CS_ERROR: Cannot use variable 'x' in this context because it may expose referenced variables outside of their declaration scope
<scratch-project>/out/Probe/Program.cs: error MIST_CS_ERROR: Cannot use a result of 'Holder.Holder(Span<int>)' in this context because it may expose variables referenced by parameter 'data' outside of their declaration scope

Observed exit code: 1.

```mist
using Standard; namespace Probe; public ref struct Holder { public Span<int, 3> Data; public Holder(Span<int, 3> data) { this.Data = data; } } public class Program { public static Holder Bad() { Span<int, 3> x = stackalloc int[3]; return new Holder(x); } public static void Main() { var h = Bad(); Console.WriteLine(h.Data.Length); } }
```

**Contract references:**

- `.kiro/specs/stack-only-types/requirements.md` — Requirement8.1–8.6 frame escape; criterion5 expressly propagates backing through containing ref struct (audited line 153)

**Owning source:**

- `Source/Mist/ProgramAnalysis/Rules/EscapeAnalyzer.cs` — JudgeStackSpans / IsStackBacked currently direct stackalloc, local declaration and Slice only (audited line 41)
- `Source/Mist/ProgramModel/Model/Expressions/NewExpression.cs` — selected constructor/arguments
- `Source/Mist/ProgramAnalysis/Flow/FlowGraphBuilder.cs` — existing graph and nested scope roots

**Acceptance and controls:**

- A stackalloc-backed Span<int,3> stored in Holder.Data inside a ref struct must taint the Holder value; returning new Holder(span), a Holder local/alias, or a field-containing nested ref struct must be source-refused. Track stored backing and assignment provenance rather than only syntactic return-of-span.
- Cover direct and sliced backing, reassignment, branch joins and outer-lifetime stores where a valid source route exists. Source error identifies the escaping expression; no Roslyn escape diagnostic should be needed. Direct property/field heap illegality is judged by093, not counted as proof of a reachable frame escape.
- Admit frame-local holders/spans, local indexing/slicing/foreach and nonescaping calls, and safe values backed by heap arrays or caller-provided lifetime. Follow stack-only R8.4 safety priority with explicit conservative limits; avoid body-independent assumptions that any ref struct necessarily contains current-frame backing.

### MIST-092 — Reject ref fields under the existing no-ref-storage contract

**Current evidence:**

executed current CLI run; no test-suite execution claimed
1

Observed exit code: 0.

```mist
using Standard; namespace Probe; public class Holder { public ref int Value; public Holder() { this.Value = 1; } } public class Program { public static void Main() { Console.WriteLine(new Holder().Value); } }
```

**Contract references:**

- `.kiro/specs/ref-parameters/requirements.md` — Requirement6.5: refs are stack-only aliases and cannot be stored in fields (audited line 111)

**Owning source:**

- `Source/Mist/ProgramModel/Construction/Types/ConcreteTypeBuilder.cs` — field modifier construction
- `Source/Mist/ProgramAnalysis/Rules/DisciplineAnalyzer.cs` — declaration legality
- `Source/Mist/Backends/CSharpTranspiler/Emitter/FieldEmitter.cs` — Emit silently ignores field IsRef

**Acceptance and controls:**

- An admitted public ref int Value field must receive a source placement/storage diagnostic instead of silently becoming int. Validate both reference and readonly-reference field spellings supported by grammar, class/struct/ref-struct containers and static fields under the same current no-ref-field contract.
- Preserve ordinary fields, ref method returns, ref parameters and ref locals. Do not implement C# ref-field support as a new Mist feature merely because the target permits some forms. Invalid field declaration gets one primary source complaint, retaining unrelated errors.

### MIST-093 — Ref-struct heap/storage legality through real positions and wrappers

**Current evidence:**

executed current CLI run; no test-suite execution claimed
<scratch-project>/out/Probe/Program.cs: error MIST_CS_ERROR: Field or auto-implemented property cannot be of type 'Span<int>' unless it is an instance member of a ref struct.

Observed exit code: 1.

```mist
using Standard; namespace Probe; public class Holder { public Span<int, 3> Data { get; set; } public Holder(Span<int, 3> data) { this.Data = data; } } public class Program { public static void Main() { } }
```

**Contract references:**

- `.kiro/specs/ref-structs/requirements.md` — Requirement3.4–3.12: permitted stack positions and refused heap field/property, generic, union/optional and boxing positions (audited line 68)
- `.kiro/specs/stack-only-types/requirements.md` — Requirement9.3–9.4 general ref-struct heap positions and frame distinction (audited line 170)

**Owning source:**

- `Source/Mist/ProgramAnalysis/Rules/DisciplineAnalyzer.cs` — JudgeContainer only fields; IsRefStruct only direct/constructed definition (audited line 69)
- `Source/Mist/ProgramAnalysis/Rules/EscapeAnalyzer.cs` — Why capture checks direct/constructed type only
- `Source/Mist/ProgramModel/Construction/Binding/Expressions/PatternBinder.cs` — normalized aliases/types as existing consumer
- `Source/Mist/ProgramAnalysis/Rules/CastAnalyzer.cs` — boxing/cast storage consumers

**Acceptance and controls:**

- Non-ref class/struct Span<int,3> auto/backing property is source-refused before C# CS8345. Apply the accepted ref-struct restrictions through aliases, closed generic/composite positions and nested containing shapes, using actual backing storage rather than only field syntax.
- Preserve direct ref-struct field in another ref struct, parameters/locals/returns with valid lifetime, interface implementation declaration and usage-site boxing refusal. Preserve direct existing field/generic/capture errors and avoid double complaints from invalid inner shapes.
- Validate each finite position named by the ref-struct contract: field, property, generic argument, union constituent, optional and interface conversion/capture. Ref-struct type arguments, union constituents and optional constituents are categorically refused under the current contract even in locals or returns; no allocation-based exception or allows-ref-struct feature is introduced. Apply heap/storage distinctions to fields, properties and captures, while preserving legal direct ref-struct locals, parameters and returns whose referent lifetime is safe.

## Scope and independence

Own source ref/frame lifetime decisions and EscapeAnalyzer/DisciplineAnalyzer ref-struct position consumers. Ownership32 owns ownership moves/leases/provenance and the discipline containment matrix; maintain shared DisciplineAnalyzer helpers compatible for either landing order. Neither task may require the other task's unpublished alias/location representation. Read existing bound storage paths and constructor argument targets; narrowed pattern/carrier APIs from30 remain stable. Source-only guards should minimize emitter changes; FieldEmitter only verifies/refuses erased ref modifier behavior, not resource release scaffolding.


**Excluded work and decisions:**

- 091 field/element ref-local liveness is not independently confirmed. Include only ordinary lexical referent lifetime cases strictly required by080; retain ownership movement of heap-field aliases as unresolved091 residue rather than inventing a borrow policy.
- No new ref-field feature, generic allows-ref-struct feature, scoped keyword syntax, pointer support, arbitrary foreign method escape-summary system or boxing policy redesign.
- Frozen ownership call policy061 and generic discipline/nested ownership containment095 belong elsewhere; stack backing is distinct from owned object provenance.

## Focused verification

- `Test/Mist.Tests/ProgramAnalysis/StorageLocationTests.cs` — Tier1 location and readonly-reference decisions
- `Test/Mist.Tests/ProgramAnalysis/ReferenceLocationSiteTests.cs` — Tier2 existing report sites
- `Test/Mist.Tests/ProgramAnalysis/OwnershipRefusalTests.cs` — capture and direct span return module judgments
- `Test/Mist.Tests/Helpers/OwnershipFragments.cs` — handbuilt ownership/escape fragments
- `Test/Mist.Tests/Helpers/OwnershipDeclarations.cs` — direct ref-struct field/generic control
- `Test/Mist.Tests/ProgramAnalysis/OwnershipDecisionTests.cs` — existing safe local span/field/lease admissions
- `Test/Mist.Tests/Backend/ReadonlyRefResultTests.cs` — ref-return emission control

Tier1 direct decisions/facts; Tier2 handbuilt graph/body/binder/emitter fragments and separately source report ownership; only a handful of existing runtime seams for actual predicate/carrier/reference/clone behavior. No per-case full compiler pipeline tests. Complete combined bundle with root full gates and actual canonical closeout; bounded records retain precisely excluded residue.

Initial focused commands (extend filters to your new classes; these do not replace final gates):

- `tools/test-ledger/run.sh Test/Mist.Tests --filter "FullyQualifiedName~StorageLocationTests|FullyQualifiedName~ReferenceLocationSiteTests|FullyQualifiedName~OwnershipRefusalTests|FullyQualifiedName~OwnershipDecisionTests|FullyQualifiedName~ReadonlyRefResultTests"`

To reproduce inline cases, create a fresh scratch project containing `Program.mist` and this `mist.toml`, unless the case explicitly supplies an entry-point override:

```toml
name = "Probe"
namespace_root = "Probe"
```

After building current main, run `dotnet run --project Source/Mist.CLI --no-build -- run <scratch-project> --quiet` from the repository. For generated-consumer fidelity, also run `dotnet build <scratch-project>/out/Probe.csproj --no-incremental` and inspect every diagnostic. Scratch projects and generated `out/` remain untracked. Complete source snippets are reproductions; smaller prose examples are component inputs to construct in the existing narrow seams.

Owning canonical records: `MIST-080`, `MIST-064`, `MIST-092`, `MIST-093`. Each included bounded obligation requires its own verified disposition; preserve excluded or unverified residue rather than claiming the entire record fixed from one representative case.

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
