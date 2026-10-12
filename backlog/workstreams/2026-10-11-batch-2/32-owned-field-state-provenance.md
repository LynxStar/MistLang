# 32 — Owned fields and receiver capabilities

Complete this bundle independently, including verification, actual tracking updates, commits and publication. Repository: `https://github.com/LynxStar/MistLang.git`; target: remote `main`. Suggested model tier: **Astra/Fable**, effort: **high**. The hard part is a shared-flow per-field lattice with composite restrictions and method field-use effects, while provenance insertion and closed type substitution remain consistent with those state decisions. These portable labels guide the same task and do not require a separate reviewer or model change.

## Starting point

Planning baseline: `0428515879b2d0245c996a5db98a676e441b9cbe` (2026-10-11). Compiler/source probes and the fresh solution build used `b1e556fb9d9528367f7de6bd7d71a13bd5c28aad`; intervening commits only reconcile canonical tracking evidence/contracts, with no source or test changes. Storm pin: `83410653c7d722868a5001d2ef71e6692ec012b3`. The build passed with zero warnings/errors. No fresh full-suite pass is claimed. Evidence below distinguishes executed probes, current source inspection and leads needing validation. Previous packets 17–25 have landed; revalidate current main rather than repeating those implementations.

Start from current remote main in an isolated checkout, retaining your existing isolated checkout and completed work when continuing. Follow the repository’s applicable instructions. Initialize with `git submodule update --init --recursive` and verify checked-out commits match current main’s recorded pins. Revalidate each item on current main; if already fixed, verify acceptance and complete any real closeout gap without duplicate code. Do not read or port retired classic implementation from Git history. Spec task documents are implemented and excluded from work discovery.

## Outcome and included obligations

Owned-field extraction/loans/restoration preserve independent child state and whole-parent restrictions, this-argument lends respect receiver tier, clonable field destinations receive required copies and closed wrappers cannot hide forbidden field discipline.

### MIST-062 — Per-owned-field state and composite parent restrictions

**Current evidence:**

executed current CLI run; no test-suite execution claimed
1

Observed exit code: 0.

```mist
using Standard; namespace Probe; public class Engine { public int N = 1; } public class Pair { public owned Engine Left = new Engine(); public owned Engine Right = new Engine(); } public class Program { public static void Main() { owned var p = new Pair(); owned var taken = p.Left; Console.WriteLine(p.Left.N); } }
```

**Contract references:**

- `.kiro/specs/owned-fields/requirements.md` — Requirement2 and3: owned parent extraction and all fields of owned types (audited line 64)
- `.kiro/specs/owned-fields/requirements.md` — Requirement16.1–16.24: moves/leases/restoration, composite versus actual state, siblings and method field access (audited line 567)

**Owning source:**

- `Source/Mist/ProgramAnalysis/Rules/OwnershipAnalyzer.cs` — State.MovedBindings/Leases keys are LocalVariable; Declare/Stored/Judge ignore owned field path consumption
- `Source/Mist/ProgramAnalysis/ThisTiers.cs` — field ownership and method this-tier/field-use facts
- `Source/Mist/ProgramAnalysis/Flow/FlowGraphBuilder.cs` — shared ownership graph
- `Source/Mist/ProgramAnalysis/Flow/LocalLifetimes.cs` — existing cleanup consequences to preserve, not redefine

**Acceptance and controls:**

- Moving non-clonable owned Pair.Left into an owned or shared destination marks only Left moved; a later read/move/borrow of Left is source-refused. Right remains available. Restoring Left from a fresh owned source makes it Live again without changing the actual parent binding state.
- Implement per-field readonly/frozen/borrowed loans with lexical scope expiry, parent external loan constraints, field sibling independence and whole-object composite restrictions precisely as R16 specifies. Branch joins and loop backedges use the shared solver, not a second statement walk.
- Whole-parent move/lend fails while a child is moved or incompatibly lent, yet assignment restoring a moved child remains possible. Distinguish actual parent moved from composite child-moved restrictions. Method calls consult the fields actually touched and allowed action; a method using a Live sibling remains admissible while a method accessing the unavailable child is refused.
- Preserve already supported clonable extraction behavior and explicit clone independence; validate whether the bound expression is a move or copy before setting field state. Shared-parent extraction follows current owned-fieldsR2, including clonable read fallback and nonclonable refusal.
- Existing old ClassicAdmits per-field tests are stale compatibility leads against newer explicit R16, not proof the contract is implemented. Update only relevant incorrect assertions with contract-grounded decision/module controls; do not reverse excluded061 policy.

### MIST-063 — This argument lending obeys the method receiver tier

**Current evidence:**

executed current CLI run; no test-suite execution claimed
9

Observed exit code: 0.

```mist
using Standard; namespace Probe; public class Cell { public int N; public static void Borrow(borrowed Cell x) { x.N = 9; } public void Bad() with frozen { Borrow(this); } } public class Program { public static void Main() { owned var x = new Cell(); x.Bad(); Console.WriteLine(x.N); } }
```

**Contract references:**

- `.kiro/specs/owned-fields/requirements.md` — Requirement17.8–17.11 with-tier sugar and body consistency;17.15–17.16 callsite tiers (audited line 627)
- `.kiro/specs/ownership-v2/requirements.md` — Requirement9.5–9.6 ordinary lend tier compatibility (audited line 179)

**Owning source:**

- `Source/Mist/ProgramAnalysis/Rules/TierAnalyzer.cs` — JudgeLends / RefusesLend only LocalReferenceExpression (audited line 80)
- `Source/Mist/ProgramAnalysis/ThisTiers.cs` — Provided(receiver,member) knows ThisExpression and constructor tier
- `Source/Mist/ProgramModel/Model/Expressions/ThisExpression.cs` — bound this source identity

**Acceptance and controls:**

- A method declared with frozen cannot pass this to a borrowed parameter; with readonly ref cannot lend this frozen/borrowed; borrowed and owned receivers may make compatible lends. Use the method's actual this tier, constructor privilege and value-type exemptions, exactly as receiver calls do.
- Apply the same check to instance and static calls, constructor arguments and selected callable argument targets where existing bound parameter metadata supports them. This is tier compatibility, not adopting the separate outstanding-frozen-lease-by-call policy061.
- Preserve valid constructor this use, value-copy arguments, compatible rereads and method capability inference. Source diagnostic identifies this and the required/provided tier without relying on a local-variable-only cast.

### MIST-094 — Clone insertion at owned field initializers and assignments

**Current evidence:**

executed current CLI run; no test-suite execution claimed
<scratch-project>/Program.mist(1,250): error MIST_PA_OWNED_SOURCE_NOT_OWNED: Cannot move this value into owned field 'Item': parameter 'x' is not owned, so the caller may still hold its value. An owned destination takes only what nothing else holds: a construction, a clone, a call returning owned, a move from an owned binding, or a value of a type that cannot change.

Observed exit code: 1.

```mist
using Standard; namespace Probe; public class Thing : IClonable<Thing> { public int N; public owned Thing Clone() { var c = new Thing(); c.N = this.N; return c; } } public class Holder { public owned Thing Item; public Holder(Thing x) { this.Item = x; } } public class Program { public static void Main() { var shared = new Thing(); var h = new Holder(shared); h.Item.N = 8; Console.WriteLine(shared.N); } }
```
executed current CLI run; no test-suite execution claimed
0

Observed exit code: 0.

```mist
using Standard; namespace Probe; public class Thing : IClonable<Thing> { public int N; public owned Thing Clone() { var c = new Thing(); c.N = this.N; return c; } } public class Program { public static void Main() { var shared = new Thing(); owned var copy = shared; copy.N = 8; Console.WriteLine(shared.N); } }
```

**Contract references:**

- `.kiro/specs/owned-fields/requirements.md` — Requirement8.1–8.4 provenance and mandatory clone of nonowned clonable sources (audited line 134)
- `.kiro/specs/ownership-v2/requirements.md` — Requirement8 IClonable shared-to-owned bridge (audited line 160)

**Owning source:**

- `Source/Mist/ProgramModel/Construction/Binding/OwnedProvenance.cs` — At / AtParameters existing clone/provenance decisions (audited line 23)
- `Source/Mist/ProgramModel/Construction/Binding/MemberBinder.cs` — BindInitializers currently lacks owned destination adaptation
- `Source/Mist/ProgramModel/Construction/Binding/Expressions/ExpressionBinder.cs` — assignment/initializer binding target slots
- `Source/Mist/ProgramAnalysis/Rules/OwnedDestinations.cs` — Require validates field destinations and already bound clone ownership

**Acceptance and controls:**

- Valid Thing : IClonable<Thing> with public owned Thing Clone receives exactly one implicit clone when shared Thing moves into an explicitly owned field or an implicit owned field of an owned type. Cover declaration initializers, constructor/method field assignments and supported object initializer destinations; preserve actual converted target slot identity.
- Clone produces independent storage: mutating the destination does not mutate shared source. Existing owned-local clone control prints0 after copy mutation; equivalent owned-field case should admit and demonstrate the same independence.
- Fresh allocation, default, already owned sources and explicit Clone()/owned-return sources are not cloned a second time. Nonclonable shared source stays source-refused with field/site explanation; primitive/value exemptions and intrinsic no-clone behavior remain.
- Preserve selected implicit conversions and clone-conversion ownership; do not replace source judgments with an unconditional emitter-level .Clone suffix. Per-field state062 consumes the final bound move-versus-copy representation.

### MIST-095 — Closed nested field containment cannot hide a forbidden discipline

**Current evidence:**

executed current CLI run; no test-suite execution claimed
made

Observed exit code: 0.

```mist
using Standard; namespace Probe; public linear class Resource { public void Drop() { } } public struct Box<T> { public T Item; public Box(T item) { this.Item = item; } } public class Program { public static void Main() { var r = new Resource(); var box = new Box<Box<Resource>>(new Box<Resource>(r)); Console.WriteLine("made"); } }
```

**Contract references:**

- `.kiro/specs/substructural-field-discipline/requirements.md` — Requirement1 container/field discipline matrix;Requirements3–5 constrained fields and instantiated constraint satisfaction (audited line 23)

**Owning source:**

- `Source/Mist/ProgramAnalysis/Rules/DisciplineAnalyzer.cs` — JudgeContainer / JudgeField / Constrained inspect open field.Type; no closed-wrapper field substitution (audited line 69)
- `Source/Mist/ProgramAnalysis/Rules/OwnershipAnalyzer.cs` — Substructural returns constructed definition discipline only (audited line 200)
- `Source/Mist/ProgramModel/Model/Types/ConstructedType.cs` — closed argument/field substitution

**Acceptance and controls:**

- An ordinary Box<T> with T Item may remain an open valid generic declaration and Box<int> usable, but Box<Resource> and Box<Box<Resource>> cannot launder a forbidden linear Resource field into unrestricted storage when actual closed field substitution makes that containment illegal. Diagnose at the meaningful closed usage/owning field path.
- Validate finite nested constructions, alias wrappers and forwarded discipline-constrained parameters against the existing matrix; preserve legal linear containers and legal affine/relevant combinations. Check declaration and use-site effects with stable type identity and cycle guards.
- Do not infer a new discipline for wrappers or reclassify arbitrary CLR containers by searching their runtime layout. Keep current Task/ValueTask discipline classification and plain-linear-parameter transfer policy unchanged. Record limits of unknown/open layouts rather than pretending they are proven safe.

## Scope and independence

One owner controls owned-field path state, composite restrictions, owned-destination binding, ThisExpression tier lends and discipline closure of actual fields. Preserve existing bound field identity and clone model APIs so31's ref/stack safety does not depend on unpublished field-path helpers. Shared DisciplineAnalyzer changes must be limited to substructural closure/containment, preserving31's ref-struct legality consumers. BodyEmitter/LocalLifetimes changes only if required to preserve already established release consequences of moved/restored fields; no cleanup or async redesign.30 owns patterns/carrier/type conversions, so adapt ownership after the existing selected conversion rather than requiring a changed converter API.


**Excluded work and decisions:**

- 061 is now an explicit canonical decision (3e53). Current frozen-f var→Borrow(owner) prints9, but OwnershipDecisionTests deliberately banks that admission and its header identifies unresolved frozen-lease call policy. Do not select the stricter ownership-v2R9.8 policy in this packet. Preserve decision residue and current unrelated admissions.
- 068 ordinary call/result alias provenance remains unverified: Origins.Survey.MarkHanded already marks nonowned arguments escaped and ordinary results have External origin; no exact accepted unsound route established. Do not blanket treat every nonowned argument as an alias or reject Print(o);Bump(o). Retain open.
- No general plain-linear-parameter transfer policy, container ownership redesign, borrowed/frozen newtype modifier policy, global cooperation/method visibility hiding (superseded by owned-fieldsR6), async promotion, cleanup-on-abort guarantee or new CLR layout introspection.

## Focused verification

- `Test/Mist.Tests/ProgramAnalysis/OwnershipRefusalTests.cs` — Tier1/2 move/loan/source report controls
- `Test/Mist.Tests/Helpers/OwnershipFragments.cs` — handbuilt flow and this-tier inputs
- `Test/Mist.Tests/Helpers/OwnershipDeclarations.cs` — containment matrix and direct constrained fields
- `Test/Mist.Tests/ProgramAnalysis/OwnershipDecisionTests.cs` — current compatible moves and stale per-field admissions to distinguish
- `Test/Mist.Tests/Semantics/OwnedProvenanceTests.cs` — Tier1/2 existing clone conversion/provenance contracts
- `Test/Mist.Tests/ProgramAnalysis/PurityRoundFiveTests.cs` — owned slot and ref write consumers
- `Test/Mist.Tests/Backend/ClrConstructedContainmentTests.cs` — existing constructed type closure

Tier1 direct decisions/facts; Tier2 handbuilt graph/body/binder/emitter fragments and separately source report ownership; only a handful of existing runtime seams for actual predicate/carrier/reference/clone behavior. No per-case full compiler pipeline tests. Complete combined bundle with root full gates and actual canonical closeout; bounded records retain precisely excluded residue.

Initial focused commands (extend filters to your new classes; these do not replace final gates):

- `tools/test-ledger/run.sh Test/Mist.Tests --filter "FullyQualifiedName~Ownership|FullyQualifiedName~OwnedProvenanceTests|FullyQualifiedName~PurityRoundFiveTests|FullyQualifiedName~ClrConstructedContainmentTests"`

To reproduce inline cases, create a fresh scratch project containing `Program.mist` and this `mist.toml`, unless the case explicitly supplies an entry-point override:

```toml
name = "Probe"
namespace_root = "Probe"
```

After building current main, run `dotnet run --project Source/Mist.CLI --no-build -- run <scratch-project> --quiet` from the repository. For generated-consumer fidelity, also run `dotnet build <scratch-project>/out/Probe.csproj --no-incremental` and inspect every diagnostic. Scratch projects and generated `out/` remain untracked. Complete source snippets are reproductions; smaller prose examples are component inputs to construct in the existing narrow seams.

Owning canonical records: `MIST-062`, `MIST-063`, `MIST-094`, `MIST-095`. Each included bounded obligation requires its own verified disposition; preserve excluded or unverified residue rather than claiming the entire record fixed from one representative case.

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
