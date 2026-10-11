# 19 — Constructor initialization contracts

Complete this independent bundle, including verification, actual backlog updates, commits and publication. Repository: `https://github.com/LynxStar/MistLang.git`. Target: remote `main`. Suggested model tier: **Sol/Opus**, effort: **high**. Existing default/witness mechanisms must agree with constructor entry, definite assignment and chain accessibility without duplicate evaluation; this needs careful cross-layer implementation rather than new language design. These portable labels are guidance, not API identifiers or a requirement to launch a separate review.

## Starting point

Planning baseline: `7a14a1a7648c30c40b0e83da932255558dea64de` (2026-10-11). Executed compiler probes and the clean solution build used `8390675bd27f8e683c50664a94b4bc728daa7007`; the intervening published changes are publishing scripts/README and four backlog evidence corrections, with no compiler or test changes. Storm pin: `b4e5853ccca45623e59fcdb9ee0afd6e90e836ce`, also verified as Storm remote main during planning. The solution build passed with zero warnings/errors; no fresh full-suite pass is claimed. Each item below distinguishes executed behavior from source inspection or an unverified lead.

Start from current remote main in an isolated task checkout; retain your existing isolated checkout and completed work when continuing. Follow the repository’s applicable instructions. Run `git submodule update --init --recursive` and verify checked-out commits match the revision’s recorded pins; a setup hook alone is not evidence. Revalidate every item against current main. If already fixed, verify acceptance and finish any real tracking gap without duplicate implementation. Do not read or port retired classic implementation from Git history. `.kiro/specs/**/tasks.md` documents are implemented and excluded from work discovery.

## Outcome and included obligations

User constructors begin with semantic defaults for defaultable storage, never borrow default-block entries as definite assignment, and refuse inaccessible written constructor chains before C# emission.

### MIST-008

**Current evidence:** executed CLI at baseline

run accepted source, exits 134 with NullReferenceException; expected7

```mist
using Standard; namespace Probe; public class Inner { public int Value = 7; } public class Holder { public Inner Item; public Holder(int n) { } } public class Program { public static void Main() { Console.WriteLine(new Holder(1).Item.Value); } }
```

### MIST-009

**Current evidence:** executed CLI plus source at baseline

run accepted source, exits 134 with NullReferenceException; expected source FIELD_NOT_DEFINITELY_ASSIGNED, because Need has user constructor and no default block

ConstructorEmitter.Emit invokes DefaultWitnesses.EmitAssignments (stored witnesses), EmitInitializers (written deferred initializers only), then the user body. BlockEntry affects Defaultability.CanDefault and DefaultEmitter, not the user-constructor route.

```mist
using Standard; namespace Probe; public class Need { public int Value; public Need(int value) { this.Value = value; } } public class Holder { public Need Item; default { Item = new Need(7) } public Holder(int ignored) { } } public class Program { public static void Main() { Console.WriteLine(new Holder(1).Item.Value); } }
```

### MIST-010

**Current evidence:** executed CLI at baseline

MIST_CS_ERROR Base.Base inaccessible; expected a source accessibility diagnostic at written chain

```mist
using Standard; namespace Probe; public class Base { private Base() { } } public class Derived : Base { public Derived() : base() { } } public class Program { public static void Main() { var d = new Derived(); } }
```

**Acceptance for all included obligations:**

- Inject semantic default for uninitialized defaultable instance field and real auto/backing-storage property at entry to explicit user constructors. Cover class, struct, designated enum default, optional value and closed constructed storage using existing witness support. Explicit initializers and explicit assignments retain their semantics.
- This-constructor chains initialize storage exactly once; base chains initialize each owning storage portion once. Do not run a user constructor when producing default(T), and do not run default-block overrides during new T(args).
- For a nondefaultable storage type, a default-block entry does not excuse an explicit constructor from assigning it on every path. Refuse no assignment or one-sided branch; admit assignment on all branches, initializer supplied, or valid this-chain assignment.
- A defaultable type whose default block overrides scalar Count to7 still begins an otherwise empty explicit constructor from Count=0, while default(T).Count=7. This distinguishes injected type default from whole containing-type default.
- Written this/base chains use existing accessibility rules for their actual constructor target. Refuse private base access; admit public/protected base and same-declaring-type private this-chain access. Preserve selection, argument compatibility, implicit chain behavior and class/struct lowering.
- Get-only auto properties and constructor privilege stay valid; static storage and computed properties do not receive per-instance injections. Preserve existing default initializers and factory emission checks.

## Contracts and implementation

- `.kiro/specs/defaultability/requirements.md` — Requirement 4.1–4.4: user construction and explicit defaults are separate (audited line 138)
- `.kiro/specs/defaultability/requirements.md` — Requirement 9.4/9.7: inherited default state; synthesized default never runs user base constructor (audited line 218)
- `.kiro/specs/defaultability/requirements.md` — Requirement 10.1–10.3: defaults at constructor entry; nondefaultable fields assigned on every path (audited line 230)
- `.kiro/specs/defaultability/requirements.md` — Requirement 12.2–12.4: default factory and once-only initialization (audited line 250)
- `.kiro/specs/mist-language-v1/requirements.md` — Requirement 9.1: private members accessible only within declaring type (audited line 183)
- `.kiro/specs/struct-inheritance/requirements.md` — Requirement 7: explicit base constructor selection and inline lowering (audited line 117)

**Primary source:**

- `Source/Mist/ProgramAnalysis/Rules/DefiniteAssignmentAnalyzer.cs` — Unassigned: StorageDefaults.HasDefault excludes storage from constructor assignment obligations (audited line 76)
- `Source/Mist/ProgramAnalysis/Rules/StorageDefaults.cs` — HasDefault (audited line 14)
- `Source/Mist/ProgramModel/Construction/Defaultability.cs` — CanDefault: BlockEntry can supply synthesized default (audited line 176)
- `Source/Mist/ProgramModel/Construction/InstanceStorage.cs` — Of: fields and properties that really have backing storage
- `Source/Mist/Backends/CSharpTranspiler/Lowering/DefaultWitnesses.cs` — EmitInitializers: only written deferred initializer emitted (audited line 154)
- `Source/Mist/Backends/CSharpTranspiler/Lowering/DefaultSynthesis.cs` — Assignments / FlattenedAssignments / StorageZero
- `Source/Mist/Backends/CSharpTranspiler/Emitter/ConstructorEmitter.cs` — Emit: witness assignments and deferred initializers before body; skip this-chain duplication (audited line 150)
- `Source/Mist/Backends/CSharpTranspiler/Emitter/DefaultEmitter.cs` — EmitAssignments: distinct synthesized-default route (audited line 92)
- `Source/Mist/ProgramAnalysis/Rules/AccessibilityAnalyzer.cs` — Analyze / Judge: NewExpression constructor check does not visit written chain (audited line 51)
- `Source/Mist/ProgramAnalysis/ImplicitChains.cs` — constructor chain selection and existing implicit accessibility rules

**Independence:**

Own ConstructorEmitter construction-entry injection, DefaultWitnesses.EmitInitializers, StorageDefaults/DA distinctions and AccessibilityAnalyzer written-chain judgments. Generic-contract packet owns ConstructorWitnesses, constructor-constraint interfaces and constraint forwarding. Both can independently use the existing StorageZero/default witness APIs; do not require an unpublished helper. Narrow shared DefaultWitnesses edits to construction initialization and rebase if another worker lands first. Linear cleanup packet owns disposal paths, so do not edit LocalLifetimes or constructor ownership policy.

**Excluded work:**

- Constructor pack arity and generic constructor entailment (MIST-021/MIST-028) belong to generic contracts.
- Default-synthesis setter purity (MIST-054) is unverified and excluded; a written property initializer control currently runs no setter.
- No default-block grammar or member-visibility policy change; no CLR constructor projection expansion.

## Focused verification

**Existing regression anchors:**

- `Test/Mist.Tests/ProgramAnalysis/ConstructorStorageAssignmentTests.cs` — Tier2 constructor assignment module fixtures; extend with block-only storage and every-path controls
- `Test/Mist.Tests/ProgramAnalysis/AccessibilityRefusalTests.cs` — direct accessibility decisions and module reporting
- `Test/Mist.Tests/Construction/DefaultabilityTests.cs` — defaultability decisions
- `Test/Mist.Tests/Backend/StorageDefaultEmissionTests.cs` — Tier2 handbuilt default storage emission
- `Test/Mist.Tests/Backend/DefaultableInitializerEmissionTests.cs` — existing component once-only initialization
- `Test/Mist.Tests/Backend/DerivedDefaultEmissionTests.cs` — inherited default route
- `Test/Mist.Tests/Backend/DefaultSynthesisTests.cs` — explicit constructors versus sentinel default factory

**Initial focused commands** (extend filters to include your new test classes; these do not replace final gates):

- `tools/test-ledger/run.sh Test/Mist.Tests --filter "FullyQualifiedName~ConstructorStorageAssignmentTests|FullyQualifiedName~AccessibilityRefusalTests|FullyQualifiedName~DefaultabilityTests|FullyQualifiedName~StorageDefaultEmissionTests|FullyQualifiedName~DefaultableInitializerEmissionTests|FullyQualifiedName~DerivedDefaultEmissionTests|FullyQualifiedName~DefaultSynthesisTests"`
- `dotnet build Mist.slnx`
- `dotnet format Mist.slnx --verify-no-changes`

For inline source probes, create a fresh scratch project with this `mist.toml` and one reproduction as `Program.mist`:

```toml
name = "Probe"
namespace_root = "Probe"
```

After a current solution build, run `dotnet run --project Source/Mist.CLI --no-build -- run <scratch-project> --quiet` from the Mist repository. Scratch directories and generated `out/` are not tracked deliverables. For artifact fidelity, also build that scratch project’s `out/Probe.csproj` and inspect all diagnostics. Snippets inside prose are component cases to construct in existing test seams, not claims that an incomplete fragment is a runnable program.

Tracking records owned by this packet: `MIST-008`, `MIST-009`, `MIST-010`.

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
