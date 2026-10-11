# 22 — Readonly capability preservation

Complete this independent bundle, including verification, actual backlog updates, commits and publication. Repository: `https://github.com/LynxStar/MistLang.git`. Target: remote `main`. Suggested model tier: **Sol/Opus**, effort: **high**. Receiver provenance, cloned type promises and callbacks hidden behind open generic formatting must agree with inferred member capabilities while preserving constructor/owned-instance exceptions. These portable labels are guidance, not API identifiers or a requirement to launch a separate review.

## Starting point

Planning baseline: `7a14a1a7648c30c40b0e83da932255558dea64de` (2026-10-11). Executed compiler probes and the clean solution build used `8390675bd27f8e683c50664a94b4bc728daa7007`; the intervening published changes are publishing scripts/README and four backlog evidence corrections, with no compiler or test changes. Storm pin: `b4e5853ccca45623e59fcdb9ee0afd6e90e836ce`, also verified as Storm remote main during planning. The solution build passed with zero warnings/errors; no fresh full-suite pass is claimed. Each item below distinguishes executed behavior from source inspection or an unverified lead.

Start from current remote main in an isolated task checkout; retain your existing isolated checkout and completed work when continuing. Follow the repository’s applicable instructions. Run `git submodule update --init --recursive` and verify checked-out commits match the revision’s recorded pins; a setup hook alone is not evidence. Revalidate every item against current main. If already fixed, verify acceptance and finish any real tracking gap without duplicate implementation. Do not read or port retired classic implementation from Git history. `.kiro/specs/**/tasks.md` documents are implemented and excluded from work discovery.

## Outcome and included obligations

Readonly obligations survive deep parameter member receivers, a valid readonly newtype declaration and the actual generic formatter closure that readonly code invokes.

### MIST-013

**Current evidence:** executed CLI at baseline

exit 0 prints 9, no readonly diagnostic. Direct readonly Inner parameter item.Bump()/item.N=9 correctly reports READONLY_PARAMETER_CALLS_MUTABLE and FIELD_ASSIGNED_ON_READONLY_PARAMETER.

```mist
using Standard; namespace Probe; public class Inner { public int N; public void Bump() { this.N = this.N + 1; } } public class Outer { public Inner Nested = new Inner(); } public class Program { public static void Touch(readonly Outer item) { item.Nested.Bump(); item.Nested.N = 9; } public static void Main() { var o = new Outer(); Touch(o); Console.WriteLine(o.Nested.N); } }
```

### MIST-017

**Current evidence:** executed CLI at baseline; exact types/route validated against closed generic control

exit 0 prints box then1. Replace generic Read<T>(T) with readonly Read(Box<int>) and the current compiler correctly reports READONLY_CONTEXT_CALLS_MUTABLE. Thus ordinary constructed generic lookup works; the retained issue is open type-parameter instantiation.

```mist
using Standard; using System; namespace Probe; public class Box<T> : IFormattable { public int N; public string ToString(string format, IFormatProvider provider) { this.N = this.N + 1; return "box"; } } public class Program { public static readonly string Read<T>(T item) where T : IFormattable { return $"{item}"; } public static void Main() { var x = new Box<int>(); Standard.Console.WriteLine(Read(x)); Standard.Console.WriteLine(x.N); } }
```

### MIST-030

**Current evidence:** executed CLI at baseline

exit 0 prints 5; readonly declaration is admitted but cloned type receives no readonly promise. Correctness should match an ordinary readonly type, including validation of cloned mutable bodies.

```mist
using Standard; namespace Probe; public class Mutable { public int N; public void Bump() { this.N = this.N + 1; } } public readonly type ReadonlyCopy = new Mutable; public class Program { public static void Main() { var copy = new ReadonlyCopy(); copy.Bump(); copy.N = 5; Console.WriteLine(copy.N); } }
```

**Acceptance for all included obligations:**

- Direct, two-level and longer field/property receiver chains rooted in a readonly value parameter refuse mutable method calls, field writes and nonreadonly setter calls. Keep field reads and readonly method/setter access valid under existing parameter rules; accepted getter-call permissiveness is preserved.
- Equivalent nonreadonly parameter chains remain mutable. Preserve readonly ref/byref behavior and ordinary readonly block/member owned-instance exceptions rather than making every reachable object immutable.
- Readonly newtype over a class or struct carries the declared readonly modifier in its cloned model and applies the same member/body/storage rules as an ordinary readonly class or struct. Test an admissible immutable source with constructor initialization, reads and inferred readonly methods; retain accessibility/source modifiers and refuse mutable cloned member behavior. Plain newtypes remain mutable.
- Readonly generic interpolation over a type parameter cannot invoke a mutating Mist IFormattable at a closed use. Check direct type arguments and constructed generic formatter arguments; readonly/inferred-readonly formatter closures remain admissible. Preserve the already working Read(Box<int>) refusal and ordinary nonreadonly formatting admission.
- Cover interpolation and the already modeled string concatenation callback route where they use the same formatting capability consumer; do not broaden callback contracts beyond existing IFormattable support.
- Use the existing readonly/purity capability inference and closure records. Diagnostics must be source diagnostics explaining the actual mutable callback/receiver, with no derivative Roslyn failure. Any needed narrow diagnostic site changes must be tested as reporting separately from the core decisions.

## Contracts and implementation

- `.kiro/specs/readonly-contexts/requirements.md` — Definitions: readonly type members; Requirement 7 readonly calls; Requirement 11 context rules (audited line 15)
- `.kiro/specs/readonly-contexts/requirements.md` — Requirement 12.6 preserves readonly member status through generic instantiation (audited line 194)
- `.kiro/specs/readonly-contexts/requirements.md` — Requirement 14.2–14.4 readonly parameter mutable calls, field writes and mutable setters refused (audited line 217)
- `.kiro/specs/purity/requirements.md` — Requirement 4 readonly writes no External; Requirement 11 allows owned mutation and local-on-own calls (audited line 153)
- `.kiro/specs/purity/requirements.md` — Deferred item2 explicitly admits readonly on newtypes while other promises remain deferred (audited line 371)

**Primary source:**

- `Source/Mist/ProgramAnalysis/Rules/ReadonlyAnalyzer.cs` — Judge direct readonly parameter call receiver only (audited line 137)
- `Source/Mist/ProgramAnalysis/Rules/ReadonlyAnalyzer.cs` — JudgeAssignment direct readonly parameter field/setter receiver only (audited line 427)
- `Source/Mist/ProgramAnalysis/Rules/ReadonlyAnalyzer.cs` — JudgeTriggers formatting IFormattable callback judgment (audited line 258)
- `Source/Mist/ProgramAnalysis/ReadonlyInference.Triggers.cs` — FormatWrites callback lookup on formatted.Type (audited line 32)
- `Source/Mist/ProgramAnalysis/ClrCallbacks.cs` — Implements / Answering / Concrete accepts concrete definitions, not type-parameter closures (audited line 18)
- `Source/Mist/ProgramAnalysis/PurityJudge.Exits.cs` — DefinitionOf unwraps ConstructedType/aliases/refinements, not a type-parameter callsite closure (audited line 306)
- `Source/Mist/ProgramAnalysis/ReadonlyInference.cs` — inferred readonly member fixed point
- `Source/Mist/ProgramModel/Construction/Types/ConcreteTypeBuilder.cs` — BuildDeclaration newtype overlay copies only Accessibility (audited line 48)

**Independence:**

Own ReadonlyAnalyzer receiver judgments, readonly inference formatting consumers, ClrCallbacks readonly callback resolution and ConcreteTypeBuilder readonly newtype modifier overlay. Generic-contract packet owns constraint entailment, associated slots and generic constructor contracts; readonly must consume existing closure data without requiring an unpublished solver or conformance API. Newtype changes are confined to readonly modifier preservation; runtime representation packet does not own clone conversion or provenance. Linear packet owns LocalLifetimes/disposal, so do not redesign ownership origins or release policy.

**Excluded work:**

- Getter-call permissiveness on readonly parameters is an accepted policy, not this defect.
- No general alias escape/borrow redesign, readonly field reachability policy, ownership clone policy, owned/affine newtype promises, or placement admission of local/pure newtypes.
- MIST-054 setter purity in synthesized defaults is unverified and excluded.
- No broad CLR callback projection or purity promise policy change; preserve current IFormattable behavior and readonly ownership exceptions.

## Focused verification

**Existing regression anchors:**

- `Test/Mist.Tests/ProgramAnalysis/ReadonlyContextGapTests.cs` — Tier2 handbuilt readonly, owned-instance and callback-related module judgments
- `Test/Mist.Tests/Helpers/PurityFragments.RoundFive.Sites.cs` — ReadonlyMemberInterpolatingAMistIFormattable direct formatter refusal (audited line 304)
- `Test/Mist.Tests/ProgramAnalysis/ReadonlyOwnedWritesTests.cs` — ownership exceptions control
- `Test/Mist.Tests/ProgramAnalysis/ReadonlyReferenceDecisionTests.cs` — Tier1 readonly byref decision controls
- `Test/Mist.Tests/Construction/ModifierPlacementBuildTests.cs` — model construction modifier seam
- `Test/Mist.Tests/Backend/ReadonlyTypeEmissionTests.cs` — readonly class/struct storage emission and parameter erasure
- `Test/Mist.Tests/Backend/ReadonlyInferenceEmissionTests.cs` — existing inferred readonly fixed point and emitted sites

**Initial focused commands** (extend filters to include your new test classes; these do not replace final gates):

- `tools/test-ledger/run.sh Test/Mist.Tests --filter "FullyQualifiedName~Readonly|FullyQualifiedName~PuritySpec|FullyQualifiedName~ModifierPlacementBuildTests"`
- `dotnet build Mist.slnx`
- `dotnet format Mist.slnx --verify-no-changes`

For inline source probes, create a fresh scratch project with this `mist.toml` and one reproduction as `Program.mist`:

```toml
name = "Probe"
namespace_root = "Probe"
```

After a current solution build, run `dotnet run --project Source/Mist.CLI --no-build -- run <scratch-project> --quiet` from the Mist repository. Scratch directories and generated `out/` are not tracked deliverables. For artifact fidelity, also build that scratch project’s `out/Probe.csproj` and inspect all diagnostics. Snippets inside prose are component cases to construct in existing test seams, not claims that an incomplete fragment is a runnable program.

Tracking records owned by this packet: `MIST-013`, `MIST-017`, `MIST-030`.

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
