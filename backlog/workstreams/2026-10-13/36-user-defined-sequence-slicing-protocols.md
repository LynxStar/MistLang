# 36 — User-defined sequence slicing

Complete this task independently, including implementation, verification, tracking updates and landing. Repository: `https://github.com/LynxStar/MistLang.git`. Target: `origin/main`. Suggested model: **Opus, high effort**. This is one sizable end-to-end language feature, not a one-line classifier patch: both protocols need selection, receiver substitution, access/result metadata, range offset realization, pattern binding and correct evaluation order. It can be completed with the current member/overload and range-fact APIs. These are portable user model categories, not API identifiers.

## Outcome and starting point

User-defined countable Range indexers and Count/Length plus Slice(int,int) work consistently across expression slicing, list-slice patterns, selected result types, source judgments and emitted calls.

Planning baseline: `50f9fd7c0116d87a821d6a7000bff2ea6f35c27a`, 2026-10-13. Source inspection, bounded CLI/generator probes and the clean solution build used `c80c36823175514e5448fc2296bb174fbb360ec2`; the only intervening change confirms evidence on three canonical records. Storm pin and checked remote main: `83410653c7d722868a5001d2ef71e6692ec012b3`. Packets 26–33 have landed and are not reserved or rescheduled. The build passed with zero warnings/errors after using a writable temporary NuGet HTTP cache. No fresh full-suite pass is claimed.

Start from current remote `main` in an isolated task checkout; use your existing isolated checkout and retain completed work when continuing. Follow the repository's applicable instructions. Initialize submodules and verify their checked-out commits match this revision's recorded pins; a successful worktree hook alone does not prove initialization. Revalidate every included issue on current main. If already fixed, verify the acceptance cases and finish any real closeout gap without duplicating implementation. Use grammar and requirements/design as contracts; spec task documents are implemented, and retired classic source is not a reference to read or port from Git history.

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

- `.kiro/specs/ranges-and-indices/requirements.md`: Countable_Type and Sliceable_Type require an integer Length/Count property and Range indexer or Slice(int,int)
- `.kiro/specs/ranges-and-indices/requirements.md`: Requirement 8.5 selected declared slice result;8.6–7 nonindexable/nonsliceable refusals
- `.kiro/specs/ranges-and-indices/requirements.md`: Requirement 10 inclusive start/exclusive end and omitted/from-end bounds; Requirement 11.5 supported Slice member realization
- `.kiro/specs/ranges-and-indices/requirements.md`: Requirements 20.1–5 slice subpattern selected type;21.3 slice contents;22.1–5 emission
- `.kiro/specs/properties/requirements.md`: Requirement 11.4–6 selected indexer access and compatibility

**Evidence 1 — executed scratch CLI, `c80c36823175514e5448fc2296bb174fbb360ec2`.**

```mist
using Standard;
namespace Probe;
public class Box {public int Length {get{return 3;}} public int this[Range r] {get{return 7;}}} public class Program{public static void Main(){Console.WriteLine(new Box()[0..1]);}}
```

Exit 1. Observed:

```text
MIST_PM_RECEIVER_NOT_SLICEABLE despite public Length=3 and public Range indexer; derivative WriteLine overload error.
```

**Evidence 2 — executed scratch CLI, `c80c36823175514e5448fc2296bb174fbb360ec2`.**

```mist
using Standard;
namespace Probe;
public class Box {public int Count {get{return 3;}}public int Slice(int start,int count){return count;}} public class Program{public static void Main(){Console.WriteLine(new Box()[0..1]);}}
```

Exit 1. Observed:

```text
MIST_PM_RECEIVER_NOT_SLICEABLE despite public Count=3 and public Slice(int,int); derivative WriteLine overload error.
```

**Evidence 3 — executed scratch CLI, `c80c36823175514e5448fc2296bb174fbb360ec2`.**

```mist
using Standard;
namespace Probe;
public class Box{public int Count{get{return 3;}} public int this[int index]{get{return index+1;}}public int Slice(int start,int count){return count;}} public class Program{public static void Main(){if(new Box() is [1,.. var middle,3]){Console.WriteLine(middle);}}}
```

Exit 1. Observed:

```text
MIST_PM_RECEIVER_NOT_SLICEABLE on [1,..var middle,3], then derivative unresolved middle; int indexer and Count exist.
```

**Evidence 4 — executed scratch CLI, `c80c36823175514e5448fc2296bb174fbb360ec2`.**

```mist
using Standard;
namespace Probe;
public class Box {public int Length {get{return 3;}}private int Slice(int start,int count){return count;}} public class Program{public static void Main(){var part=new Box()[0..1];}}
```

Exit 1. Observed:

```text
Receiver remains refused with private Slice; negative access control, not a missing capability.
```

Implementation references:

- `Source/Mist/ProgramModel/Construction/Binding/Indexability.cs`: SliceType only arrays/span/string; IsSliceable; selected/indexed members
- `Source/Mist/ProgramModel/Construction/Binding/Expressions/IndexBinder.cs`: Range branch and SliceType bypass user selected indexer/method
- `Source/Mist/ProgramModel/Construction/Binding/Expressions/PatternBinder.cs`: BindListPattern sliceType and narrowed binding
- `Source/Mist/ProgramModel/Model/Expressions/IndexExpression.cs`: retain selected receiver/index/result/parameter identity
- `Source/Mist/ProgramModel/Model/Members/CountedIndexer.cs`: count/member substitution and normalized positional parameter
- `Source/Mist/Backends/CSharpTranspiler/Emitter/BodyEmitter.cs`: IndexExpression emission and EmitListPattern/EmitPattern slice consumers
- `Source/Mist/ProgramAnalysis/Rules/RangeKnowledge.cs`: existing range/decoded offset facts
- `Source/Mist/ProgramAnalysis/Rules/RefinementAnalyzer.cs`: selected index/range argument proof consumers

## Scope and acceptance

### Range indexer

Complete counted range_indexer fixture prints 7. Public Range getter selected on actual constructed receiver; result type is the indexer’s declared/closed type. Exercise inherited and generic owners, int and Range indexer overload competitors, Length and Count names.

Missing count property, wrong count kind, inaccessible/missing Range getter and wrong range arguments are source-refused at owning site; ordinary int indexing retains existing behavior. Selected setter/getter availability and readonly restrictions follow current indexer rules, not a new slicing-write policy.

### Slice(int,int)

Complete count_slice fixture prints 1 for 0..1. Cover Length and Count, all four omitted-bound forms, ^ from-end start/end, stored Range values and declared/custom slice result types. Start is inclusive, end exclusive.

Missing/inaccessible method, wrong parameter arity/type, missing/noninteger count and unsupported receivers stay source-refused. Overload selection/access uses existing rules; preserve established preference/ambiguity semantics rather than creating one.

### List and slice patterns

Complete list_slice fixture [1,..var middle,3] binds middle as the selected int Slice result and prints 1. Add Range-indexer-backed slice binding and ordinary sequence-returning Slice controls; existing supported full optional subpatterns must be typed against actual slice result. Test is and switch forms, nested/empty slices and no-subpattern .. controls.

Two slices/nonindexable/nonsliceable/mismatched subpatterns keep current source diagnostics. Preserve failed element/binding children with one primary cause; unavailable middle should not create an unrelated dependent overload/lookup cascade. Do not introduce raw unknown list/ITuple/Deconstruct protocols or unsupported sized-element scans.

### Evaluation and semantic parity

Receiver, range expression, count and bounds are evaluated in source order at most as specified by existing operation semantics; selected Slice/indexer executes once, including side-effect/count/receiver/throw controls. Body/pattern/argument judgments and emitter consume the same recorded selected member and receiver substitution.

Do not re-resolve candidates in emission or let C# choose a different overload. Slice-method result is an ordinary result; do not accidentally classify it as writable storage. A genuinely settable Range indexer follows its existing selected accessor contract.

### Regression bank

Arrays copy their slices; spans retain aliasing and kind/length behavior; strings preserve slicing; already-working ^ int indexers/count facts remain correct. Generic, optional and CLR call binding from 26–33 remains intact.

No Span→ReadOnlySpan permission is inferred from slicing support: MIST-067 is separate and its accepted bridge contract was not established. No new runtime guards, solver domains or range syntax.

## Independence and ownership

Own the full MIST-029 feature chain, including narrow new selected-slice metadata if needed and its consumers. Packet 34 owns generic write-effect summaries/member filtering; this worker routes count/indexer/Slice discovery through existing member/access/overload contracts and retains actual selected member identity and receiver constructions so effect filtering can consume it. It must not add a separate visibility policy or require an unpublished effect API. Packet 35 owns value-set containment, not slice classification or range-call realization. Packet 37 owns generic operator candidate/witness work; packet 36 only uses currently selected built-in range/index operators and existing method selection. Packet 39 changes native literal values/tuple-name cursors, not range protocol selection. Shared Indexability/PatternBinder/BodyEmitter edits are narrow different branches and normal main integration.

This packet has no dependency on another workstream. As its parent session, you own every included task and its internal dependencies. Determine the execution order, decide which work to perform yourself or dispatch to subagents, and integrate their changes into your primary implementation. Resolve internal conflicts and verify the combined result; subagents return contributions to you rather than independently publishing partial work to the shared target. Use current repository contracts. Do not hand internal ordering, coordination or integration to the user, the planning session or another packet. Complete and close out every included item; a representative subset or a collection of subagent reports is not completion.

Allow narrowly necessary adjacent edits, including compatible bound metadata, consumers, generated artifacts and user/API/spec documentation needed to finish this outcome. Keep changes coherent and preserve already landed behavior. Shared files require narrow integration, not another packet's unpublished branch, service, test helper or design decision.

Excluded scope:

- MIST-067 mutable-to-readonly span conversion policy; intrinsic span reconciliation alone does not permit a cross-kind conversion.
- New source syntax, arbitrary Deconstruct/ITuple or raw-unknown list matching, generic operator inference/witness redesign, new write-summary representation, effect-policy changes, unrelated defaults/native warning residues.

## Incidental fixes

If you uncover additional issues with clear expected behavior that are straightforward to fix and verify without user input, solve them in additional commits. This includes small fixes outside your primary files or objective; do not merely surface them as extras or make the user dispatch another task. Add appropriate verification and update actual documentation/backlog entries where relevant. Keep each coherent incidental fix separately committed and include it in the same landing workflow.

Avoid duplicating work already landed or assigned to an active workstream. Defer only when the finding requires a genuine user decision, substantial new work, unavailable access/authorization, or verification you cannot complete; state the concrete reason. Ordinary implementation choices and arbitrary file boundaries do not require user input. Finish the primary task and do not turn incidental cleanup into an open-ended campaign.

## Verification and closeout

Tier 1 direct classifier/selection/fact decisions with hand-built input; Tier 2 binder/pattern/emitter fragments with downstream stubs. No heavy provider, Roslyn or process collaborator below its owning tier. Use the existing named seam only where emitter/compiler or compiler/runtime agreement cannot be established lower; keep each integration seam under 10 seconds and within existing case budget. Bank representative runtime behavior in an existing sample/AllFeaturesV2 path if needed, not a new generated-program system test. Every new/migrated test gets exactly one effective Tier trait. Scratch CLI proof is audit evidence, not the test architecture.

Existing verification anchors (inspect their actual helpers/costs before extending):

- `Test/Mist.Tests/Semantics/RangeAndIndexResolutionTests.cs`
- `Test/Mist.Tests/Semantics/ListPatternInputBindingTests.cs`
- `Test/Mist.Tests/Semantics/ListPatternResolutionTests.cs`
- `Test/Mist.Tests/Semantics/ContextualExpressionIndexContractTests.cs`
- `Test/Mist.Tests/ProgramAnalysis/RangeIndexMemberDecisionTests.cs`
- `Test/Mist.Tests/ProgramAnalysis/RangeBoundSnapshotTests.cs`
- `Test/Mist.Tests/ProgramAnalysis/DecodedSliceDecisionTests.cs`
- `Test/Mist.Tests/ProgramAnalysis/StoredRangeDecisionTests.cs`
- `Test/Mist.Tests/ProgramAnalysis/RefinementRangeRefusalTests.cs`
- `Test/Mist.Tests/Backend/SizedSequencePatternEmissionTests.cs`

Focused commands:

```bash
tools/test-ledger/run.sh Test/Mist.Tests --filter "FullyQualifiedName~RangeAndIndexResolutionTests|FullyQualifiedName~ListPattern|FullyQualifiedName~RangeIndexMemberDecisionTests|FullyQualifiedName~RangeBoundSnapshotTests|FullyQualifiedName~DecodedSliceDecisionTests|FullyQualifiedName~StoredRangeDecisionTests"
```

```bash
dotnet run --project Source/Mist.CLI --no-build -- run "$packet_probe_dir" --quiet
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

Update `MIST-029` in the actual `backlog/issues.yaml` as part of this change. Record implemented behavior, supporting tests/evidence and any precise remaining limitation. Preserve historical evidence and other workers' updates; keep edits to owning records narrow. Close only completed portions and move fully discharged records to the archive with their stable IDs. Do not merely return proposed closeout text or create a new tracking system. Update necessary contract/API/book documentation and generated artifacts in their owning files.

## Commit and land

You are authorized to commit and publish this task directly to remote `main` without another confirmation. Do not create pull requests. Commit implementation, appropriate tests/generated artifacts, required documentation and actual closeout updates together or in coherent task commits. Fetch and incorporate the latest target branch, resolve conflicts while preserving other landed work, and perform the required verification on the integrated result. Use a normal, non-force push. Work from your own isolated checkout; do not reset or commandeer another session's working tree.

Other workers may land concurrently. If publication is rejected because the target advanced, fetch, reconcile, rerun verification affected by the new combined result, and retry. Keep shared-register edits narrow. You own routine integration; do not hand it to the user or assume a central integrator. Do not force-push the target, bypass protections, silently accept failures or mark unfinished work complete. Preserve newer compatible Storm pins already incorporated by main.

Finish by fetching and confirming that the intended changes, additional incidental fixes and actual closeout are present on remote `main`. Report landed commit IDs, actual verification results, entries updated, completed incidental fixes and any remaining limitations. A local commit or pushed task branch alone is not completion.

If unavailable credentials or branch protection prevents landing, complete all unaffected work, preserve the result durably, and report the exact blocker and smallest required user action. Do not switch to a PR workflow. If a genuine unresolved product decision prevents implementation, identify it precisely and retain the affected item as open. Do not repeat an unchanged failed attempt indefinitely or claim success without verification.
