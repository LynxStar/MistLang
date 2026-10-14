# 35 — Structural pattern containment

Complete this task independently, including implementation, verification, tracking updates and landing. Repository: `https://github.com/LynxStar/MistLang.git`. Target: `origin/main`. Suggested model: **Opus, high effort**. This is substantial mathematical implementation over the already shipped MatchedSpaces/ValueAlgebra infrastructure: list length/slice products, member projections, typed/nested shapes and source reporting. The domains and boundaries can be finite and explicit; it does not need the new cross-phase effect availability and generic write fixed point of 34. These are portable user model categories, not API identifiers.

## Outcome and starting point

Finish finite property/list switch-arm subsumption using the existing conservative value-space algebra, preserving actual emitted match semantics and keeping unsupported symbolic cases explicit.

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

- `.kiro/specs/switch-expressions/requirements.md`: Requirement 7.1–7.3 subsumed later arms
- `.kiro/specs/pattern-matching/requirements.md`: property/list/logical pattern grammar and runtime emission semantics
- `.kiro/specs/tuple-pattern-matching/requirements.md`: existing nested positional pattern controls; do not resolve R3.5 exhaustiveness policy

**Evidence 1 — executed current CLI artifact refusal probe, `c80c36823175514e5448fc2296bb174fbb360ec2`.**

```mist
using Standard; namespace Probe; public class Box { public int N { get; set; } } public class Program { public static int Pick(Box b) { return b switch { { N: >= 0 } => 1, { N: 1 } => 2, _ => 3 }; } public static void Main() { Console.WriteLine(Pick(new Box())); } }
```

Exit 1. Observed:

```text
<scratch-project>: error MIST_CS_ERROR: The pattern is unreachable. It has already been handled by a previous arm of the switch expression or it is impossible to match.
```

**Evidence 2 — executed current CLI artifact refusal probe, `c80c36823175514e5448fc2296bb174fbb360ec2`.**

```mist
using Standard; namespace Probe; public class Program { public static int Pick(int[] a) { return a switch { [_, ..] => 1, [1, 2] => 2, _ => 3 }; } public static void Main() { Console.WriteLine(Pick([1, 2])); } }
```

Exit 1. Observed:

```text
<scratch-project>: error MIST_CS_ERROR: The pattern is unreachable. It has already been handled by a previous arm of the switch expression or it is impossible to match.
```

**Evidence 3 — executed current CLI source-refusal control, `c80c36823175514e5448fc2296bb174fbb360ec2`.**

```mist
using Standard; namespace Probe; type Below<N:int> = int < N; public class Program { public static int Pick<N:int>(int x) { return x switch { Below<N> a => 1, Below<N> b => 2, _ => 3 }; } public static void Main() { Console.WriteLine(Pick<3>(1)); } }
```

Exit 1. Observed:

```text
<scratch-project>: error MIST_PA_PATTERN_BOUND_NOT_CONSTANT: The bound of a relational pattern must be a constant.
<scratch-project>: error MIST_PA_PATTERN_BOUND_NOT_CONSTANT: The bound of a relational pattern must be a constant.
```

**Evidence 4 — executed current CLI source-refusal control, `c80c36823175514e5448fc2296bb174fbb360ec2`.**

```mist
using Standard; namespace Probe; type Below<N:int> = int < N; type NonNegativeBelow<N:int> = int >= 0 and < N; public class Program { public static int Pick<N:int>(int x) { return x switch { Below<N> a => 1, NonNegativeBelow<N> b => 2, _ => 3 }; } public static void Main() { Console.WriteLine(Pick<3>(1)); } }
```

Exit 1. Observed:

```text
<scratch-project>: error MIST_PA_PATTERN_BOUND_NOT_CONSTANT: The bound of a relational pattern must be a constant.
<scratch-project>: error MIST_PA_PATTERN_BOUND_NOT_CONSTANT: The bound of a relational pattern must be a constant.
```

Implementation references:

- `Source/Mist/ProgramAnalysis/Refinements/MatchedValues.cs`: Covered delegates to MatchedSpaces; scalar numeric/real/UInt64 facts already shipped
- `Source/Mist/ProgramAnalysis/Refinements/MatchedSpaces.cs`: Of / Structure / Product / Sum / Boxes / BoxLimit256; no property/list space
- `Source/Mist/ProgramAnalysis/Refinements/ValueAlgebra.cs`: Read and conjunction/disjunction/negation/containment operations
- `Source/Mist/ProgramAnalysis/Rules/PatternAnalyzer.cs`: JudgeUnreachable / ArmFinding / Covers and existing bound-constant/type validation
- `Source/Mist/ProgramModel/Construction/Binding/Expressions/PatternBinder.Properties.cs`: bound member identity and property subpatterns
- `Source/Mist/ProgramModel/Construction/Binding/Expressions/PatternBinder.cs`: bound list/tuple/refined shapes and failures
- `Source/Mist/Backends/CSharpTranspiler/Emitter/BodyEmitter.cs`: property/list pattern emission and hoisted guard consumers
- `Source/Mist/ProgramAnalysis/Refinements/Requirement.cs`: existing symbolic bound declaration/receiver identity; no broad new solver

## Scope and acceptance

- Property patterns over supported statically known receivers: same resolved member {N:>=0} subsumes {N:1}; conjunction/disjunction and multiple-member products must use declaration/receiver/member identity. Nested property/tuple/optional element shapes are supported only where every underlying algebra can soundly read the pattern. Different declarations or hidden members sharing a name do not collapse.
- List patterns over already supported static array/string/indexable shapes: distinguish exact lengths, prefix/suffix/slice length bounds and supported per-element value spaces. [_,..] subsumes[1,2]; [1,2] does not subsume[1,3] or different length. Empty lists, open slices, fixed prefixes/suffixes and nested supported shapes have both subsumed/non-subsumed controls.
- Apply a hard per-pattern admission gate before treating repeated member/index projections as stable: ordinary native C# structural patterns use the existing decision semantics, while helper/Boolean/unknown-property/enum/regex guard lowering may repeat reads and is not automatically a value-space projection. Declaration identity or readonly status alone does not prove stability. Preserve existing getter/indexer evaluation and guard counts; do not introduce caching merely to justify an unreachable finding. Include a reachable changing-getter guard/helper control.
- Use each list/slice subpattern's actual bound ScrutineeType/NarrowedType. A custom Slice(int,int) or Range indexer may return a scalar or unrelated object, so its result is opaque unless an existing recorded matcher relation proves a faithful sequence projection. Restrict the initial list algebra to native array/string and already established faithful projections; decline arbitrary effectful Count/indexer/Slice spaces. Cover opaque custom slice results and repeated side effects as no-verdict controls, without depending on packet 36's new metadata. Var/discard slices and length/prefix/suffix facts remain eligible only where existing semantics prove them.
- Use one conservative containment mechanism on the finished bound shapes, not a second ad hoc pattern walker. Preserve earlier-arm guard exclusion, impossible-pattern ownership and reporting at the later source arm. Refuse only what the actual emitted matcher cannot reach; hoisted regex/enum/dynamic effect guards are not ordinary unconditional pattern coverage.
- Preserve shipped UInt64/real/NaN, optional, union carrier sentinel, tuple product and composed refinement behavior, including subclass/base overlap and the 256-box conservative limit. Do not reopen already closed portions of 012 or erase unsupported-space unknowns into full coverage.
- Symbolic 012 is a bounded validation/disposition item, not permission to admit new runtime patterns. The provided dependent alias cases currently fail PATTERN_BOUND_NOT_CONSTANT before containment; retain those existing refusals. Add symbolic containment only for an already accepted closed/lifted bound identity whose emitted runtime predicate is proven equivalent, after establishing a portable admission and rejection control. If no such accepted finite form exists, retain that symbolic residue open with evidence and finish property/list coverage.
- Actual closeout explicitly narrows remaining 012: fully discharge supported property/list obligations demonstrated by controls, preserve any unsupported symbolic/member-effect/guard domain as open. No all-record archive based on two example fixes.
- Report MIST_PA_SWITCH_ARM_UNREACHABLE at the later source arm using the current descriptor in Source/Mist/Common/Diagnostics/Descriptors/ProgramAnalysisDiagnostics.cs. The current severity is Error; resolving the older Requirement 7 warning wording against that severity is a separate policy choice outside this packet. Do not change severity or route an established containment failure to Roslyn.

Additional controls:

- Earlier guarded covering pattern followed by matching unguarded pattern stays reachable.
- Overlapping but not contained property intervals and list elements/lengths stay reachable.
- User property/indexer getter side effects and hoisted guards preserve actual matching/evaluation behavior.
- Errored subpattern does not produce a derivative unreachable finding; independent findings remain.
- Carrier catchall after all constituents and tuple universal-arm/collective coverage policy remain unchanged.

No accepted symbolic runtime arm has been established in this audit. Symbolic direct generics refuse at the original refinement predicate. Do not rewrite PATTERN_BOUND_NOT_CONSTANT or use enum-guard/runtime-effect containment to silently choose a different unreachable-arm policy. Broad symbolic arithmetic theorem proving is excluded.

## Independence and ownership

Own MatchedSpaces/ValueAlgebra structural containment customers and narrow PatternAnalyzer reporting changes. 34 owns closed-unknown filtering/storage effects, not this algebra. User-slicing packet owns runtime range/custom-indexer binding/sequence shape; consume the existing resolved list/count/indexer identities and do not depend on its new helpers. Generic-operator packet owns generic selected operator applicability/closure; read existing ComparisonPattern.Operator and decline unsupported comparisons. Necessary shared BodyEmitter changes must be confined to matcher parity and preserve range/operator entry points. Both packets land independently against the same baseline.

This packet has no dependency on another workstream. As its parent session, you own every included task and its internal dependencies. Determine the execution order, decide which work to perform yourself or dispatch to subagents, and integrate their changes into your primary implementation. Resolve internal conflicts and verify the combined result; subagents return contributions to you rather than independently publishing partial work to the shared target. Use current repository contracts. Do not hand internal ordering, coordination or integration to the user, the planning session or another packet. Complete and close out every included item; a representative subset or a collection of subagent reports is not completion.

Allow narrowly necessary adjacent edits, including compatible bound metadata, consumers, generated artifacts and user/API/spec documentation needed to finish this outcome. Keep changes coherent and preserve already landed behavior. Shared files require narrow integration, not another packet's unpublished branch, service, test helper or design decision.

Excluded scope:

- No 157 solved-flow return inference (explicitly prohibited by current refinement-types R18.5); no real/string/regex body lift from 155.
- No general symbolic solver, ownership/purity alias summaries, runtime type deconstruction for unknown tuples 217, enum severity policy or tuple exhaustion policy.
- No arbitrary list element scanning on erased sized sequence elements or new custom indexer semantics.

## Incidental fixes

If you uncover additional issues with clear expected behavior that are straightforward to fix and verify without user input, solve them in additional commits. This includes small fixes outside your primary files or objective; do not merely surface them as extras or make the user dispatch another task. Add appropriate verification and update actual documentation/backlog entries where relevant. Keep each coherent incidental fix separately committed and include it in the same landing workflow.

Avoid duplicating work already landed or assigned to an active workstream. Defer only when the finding requires a genuine user decision, substantial new work, unavailable access/authorization, or verification you cannot complete; state the concrete reason. Ordinary implementation choices and arbitrary file boundaries do not require user input. Finish the primary task and do not turn incidental cleanup into an open-ended campaign.

## Verification and closeout

Tier 1 directly constructed value-space/subsumption pairs and finite enumeration oracles are the core proof. Tier 2 handbuilt pattern/reporting and emitter fragments prove module chains. Existing higher-tier controls are regression anchors, not license to compile each pattern case or exceed seam budgets. Follow current tier/cost rules and final integrated full regression contract.

Existing verification anchors (inspect their actual helpers/costs before extending):

- `Test/Mist.Tests/ProgramAnalysis/ArmContainmentTests.cs`: existing direct Tier 1 containment/report-site controls
- `Test/Mist.Tests/ProgramAnalysis/PatternRefusalTests.cs`: existing scalar and source findings
- `Test/Mist.Tests/ProgramAnalysis/PatternDecisionTests.cs`: direct pattern decisions
- `Test/Mist.Tests/Backend/PropertyPatternEmissionTests.cs`: existing emitted structural semantics
- `Test/Mist.Tests/Semantics/ListPatternInputBindingTests.cs`: existing bound list input metadata
- `Test/Mist.Tests/Integration/PropertyPatternRuntimeBoundaryTests.cs`: existing named seam; current cost limits constrain extensions

Focused commands:

```bash
tools/test-ledger/run.sh Test/Mist.Tests --filter "FullyQualifiedName~ArmContainmentTests|FullyQualifiedName~PatternRefusalTests|FullyQualifiedName~PatternDecisionTests|FullyQualifiedName~PropertyPatternEmissionTests|FullyQualifiedName~ListPatternInputBindingTests|FullyQualifiedName~PropertyPatternRuntimeBoundaryTests"
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

Update `MIST-012` in the actual `backlog/issues.yaml` as part of this change. Record implemented behavior, supporting tests/evidence and any precise remaining limitation. Preserve historical evidence and other workers' updates; keep edits to owning records narrow. Close only completed portions and move fully discharged records to the archive with their stable IDs. Do not merely return proposed closeout text or create a new tracking system. Update necessary contract/API/book documentation and generated artifacts in their owning files.

MIST-012 is partially implemented already: finish the supported property/list obligations and record the symbolic disposition, retaining any unproved residue as open. Packet completion does not require inventing an unsupported symbolic solver or archiving the whole umbrella record.

## Commit and land

You are authorized to commit and publish this task directly to remote `main` without another confirmation. Do not create pull requests. Commit implementation, appropriate tests/generated artifacts, required documentation and actual closeout updates together or in coherent task commits. Fetch and incorporate the latest target branch, resolve conflicts while preserving other landed work, and perform the required verification on the integrated result. Use a normal, non-force push. Work from your own isolated checkout; do not reset or commandeer another session's working tree.

Other workers may land concurrently. If publication is rejected because the target advanced, fetch, reconcile, rerun verification affected by the new combined result, and retry. Keep shared-register edits narrow. You own routine integration; do not hand it to the user or assume a central integrator. Do not force-push the target, bypass protections, silently accept failures or mark unfinished work complete. Preserve newer compatible Storm pins already incorporated by main.

Finish by fetching and confirming that the intended changes, additional incidental fixes and actual closeout are present on remote `main`. Report landed commit IDs, actual verification results, entries updated, completed incidental fixes and any remaining limitations. A local commit or pushed task branch alone is not completion.

If unavailable credentials or branch protection prevents landing, complete all unaffected work, preserve the result durably, and report the exact blocker and smallest required user action. Do not switch to a PR workflow. If a genuine unresolved product decision prevents implementation, identify it precisely and retain the affected item as open. Do not repeat an unchanged failed attempt indefinitely or claim success without verification.
