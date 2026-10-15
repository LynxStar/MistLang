# 40 — Ordinary-call ownership boundaries

Complete this task independently, including implementation, verification, actual tracking updates and landing. Repository: `https://github.com/LynxStar/MistLang.git`. Target: `origin/main`. Suggested model: **Fable, high effort**, using the user's remaining subscription window. This is also Opus-capable; no Fable-only capability is claimed. OwnershipFlow, Origins, receiver effects and return provenance currently use different evidence. Their interpretation must agree at joins and owned destinations, while avoiding invented equivalences between generic-storage write effects and alias retention.

## Outcome and starting point

Reject mutable values claimed as uniquely owned after a call has made them available to another holder, including a retained ordinary argument and an ordinary call result alias. Enforce the accepted call-boundary ownership contract through existing ownership consumers rather than relying on explicit owned syntax as unconditional proof of uniqueness.

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

- `.kiro/specs/ownership-v2/requirements.md`: Requirement 1.3,1.5–1.6; Requirement 6.1–6.9; Ordinary mutable classes may have owned bindings; owned sources must yield unique ownership, shared assignments move, owned arguments move, use after move is refused, owned returns transfer ownership.
- `.kiro/specs/owned-fields/requirements.md`: Requirement 18.8–18.11; especially 18.9; Explicit ownership annotations are validated against all uses. Explicit example requires error when owned local is used after passing to a non-owned parameter. This is stronger than a retention-only reading of the historical MIST-068 lead.
- `Source/Mist/ProgramAnalysis/Rules/OwnershipAnalyzer.cs`: class summary; JudgeOwnedReturns; YieldsOwned; Current source explicitly interprets an owned return as a value nothing else holds, citing ownership-v2 R1.5/6.7.
- `Source/Mist/ProgramAnalysis/CLAUDE.md`: The contract; Proving a rule; Finished immutable model; shared Flow framework for flow-sensitive judgment; report positive failure evidence at existing syntax; direct decision/body tests at proper tiers.

Contract interpretation:

- The historical MIST-068 caution that every non-owned argument cannot be treated as an alias is not itself an accepted override of owned-fields 18.9. Preserve it as provenance; do not silently convert it into a new policy requiring ordinary nonretaining calls to retain explicit owned status.
- The nonretaining Inspect probe is deliberately included as a discriminator: its current success is not a required positive control. Under R18.9, subsequent explicit-owned reuse must be refused even though Inspect does not retain. Any more precise exception requires existing specific tier/parameter contract support, not worker policy invention.
- Ordinary mutable Cell is neither linear nor affine. MIST-122 concerns plain linear parameter transfer and drop insertion and remains excluded. This packet must not decide that policy or change cleanup to match it.
- No new language effect annotation, cross-assembly retention promise, or whole-program alias completeness is required. Use existing declared ownership/borrow tiers and selected callable identities; absent a body/promise, do not manufacture uniqueness.

- `Source/Mist/ProgramAnalysis/Origins.Survey.cs`: Survey invocation/allocation cases; MarkHanded; MarkEscaped; All non-owned ordinary arguments are marked escaped, but only direct stripped local references are recorded; no returned-alias relation is represented here.
- `Source/Mist/ProgramAnalysis/Origins.cs`: Escapes; OfLocal; Explicit owned local yields Origin.Own regardless escaped set. Other locals have distinct fresh/alias handling.
- `Source/Mist/ProgramAnalysis/Rules/OwnershipAnalyzer.cs`: YieldsOwned; Escape predicate is consulted only for locals whose ownership is not Owned; explicit owned binding bypasses it.
- `Source/Mist/ProgramAnalysis/Rules/OwnershipFlow.Judge.cs`: JudgeArguments; JudgeShared; return case; Call arguments are judged as moves only for explicit owned parameters. Current runtime probes show retained ordinary argument and Id result alias do not prevent owned return.
- `Source/Mist/ProgramAnalysis/Rules/OwnershipFlow.cs`: State / flow transfer and shared provenance; Existing central flow ownership state should remain the integration point; do not create a separate statement walker with its own branch semantics.
- `Source/Mist/ProgramAnalysis/ThisEscapes.cs`: Leaks / source method-root traversal; Existing receiver leak logic is related evidence; receiver leakage and argument retention must not be assumed interchangeable.

### Reproduction — owned-return-retained-call

```mist
using Standard; namespace Probe; public class Cell { public int N; } public class Program { public static Cell Saved = new Cell(); public static void Keep(Cell c) { Saved = c; } public static owned Cell Make() { owned var c = new Cell(); Keep(c); return c; } public static void Main() { owned var result = Make(); Saved.N = 7; Console.WriteLine(result.N); } }
```

Observed exit 0:

```text
7
```

### Reproduction — owned-return-nonretaining-call

```mist
using Standard; namespace Probe; public class Cell { public int N; } public class Program { public static void Inspect(Cell c) { Console.WriteLine(c.N); } public static owned Cell Make() { owned var c = new Cell(); Inspect(c); return c; } public static void Main() { owned var result = Make(); result.N = 9; Console.WriteLine(result.N); } }
```

Observed exit 0:

```text
0
9
```

### Reproduction — owned-return-identity-result-alias

```mist
using Standard; namespace Probe; public class Cell { public int N; } public class Program { public static Cell Saved = new Cell(); public static Cell Id(Cell c) { return c; } public static owned Cell Make() { owned var c = new Cell(); var alias = Id(c); Saved = alias; return c; } public static void Main() { owned var result = Make(); Saved.N = 7; Console.WriteLine(result.N); } }
```

Observed exit 0:

```text
7
```

The retaining argument and returned-alias examples execute Main and print 7 after Saved mutates the claimed owned object. Inspect currently prints 0 then 9, but its success is not a required admission: owned-fields R18.9 makes reuse after this ordinary non-owned-parameter call a negative control. Do not require a per-callee retention framework where the accepted ownership transition already rules the program out.

## Scope and acceptance

- Retaining Keep program must receive an owned-source/use-after-transfer finding before emission at the offending reuse or ownership claim; it must not successfully publish an owned result still reachable through Saved.
- Id-return alias program must likewise be refused before emission. Fixing the call boundary can suffice; do not require a new alias graph if the accepted transfer rule already rules the program out.
- Cover selected ordinary instance/static methods, constructors, callable/local-function wrappers and transitive helper calls using their existing bound parameter ownership; include a recursive wrapper if a new summary is actually introduced. Do not re-resolve overloads or infer new callable types.
- Cover branches where only one path hands an owned value away, loops with live values crossing iterations, and reassignment to a fresh owned allocation. A fresh replacement may regain ownership; the old instance must not regain it merely because the local has explicit owned syntax.
- Preserve accepted direct fresh construction/owned-return ownership sources and immutable/value copies; preserve owned-parameter move and use-after-move checks; reject shared static/field aliases as owned sources.
- Treat Inspect as the explicit 18.9 negative reuse control. For positive observation controls use already specified frozen/borrowed/readonly-ref parameter/receiver contracts and their existing valid fixtures; do not invent syntax or change frozen-lease MIST-061 policy.
- Exercise the same invariant through owned return, owned parameter and owned destination consumers where currently supported, with direct constructed-body/graph evidence rather than separate source compile loops.
- Keep findings attached to original source expressions; retain independent findings and existing no-cascade behavior. Do not blanket-mark failed trees as already diagnosed.

Implementation references:

- `Source/Mist/ProgramAnalysis/Origins.cs`
- `Source/Mist/ProgramAnalysis/Origins.Survey.cs`
- `Source/Mist/ProgramAnalysis/Rules/OwnershipAnalyzer.cs`
- `Source/Mist/ProgramAnalysis/Rules/OwnershipFlow.cs`
- `Source/Mist/ProgramAnalysis/Rules/OwnershipFlow.Judge.cs`
- `Source/Mist/ProgramAnalysis/Rules/OwnedDestinations.cs`
- `Source/Mist/ProgramAnalysis/ThisEscapes.cs`

## Independence and ownership

Own the Stage 5 ownership transfer/source invariant using existing bound callable, parameter and type identities. Packet 41 corrects generic receiver construction; consume its existing type/member identities without requiring a new service. Packet 42 realizes default storage and implicit constructor arguments; consume already selected constructor parameter ownership. Both can land before or after this packet. Generic-storage write summaries from landed 34 are distinct from alias retention: DoesNotWriteT never proves DoesNotRetain. Preserve operator selection, slicing and existing cleanup contracts.

This packet has no dependency on another workstream. As its parent session, you own every included task and its internal dependencies. Determine the execution order, decide which work to perform yourself or dispatch to subagents, and integrate their changes into your primary implementation. Resolve internal conflicts and verify the combined result; subagents return contributions to you rather than independently publishing partial work to the shared target. Use current repository contracts. Do not hand internal ordering, coordination or integration to the user, the planning session or another packet. Complete and close out every included item; a representative subset or a collection of subagent reports is not completion.

Allow narrowly necessary adjacent edits, including compatible bound metadata, consumers, generated artifacts and user/API/spec documentation needed to finish this outcome. Keep changes coherent and preserve already landed behavior. Shared files require narrow integration, not another packet's unpublished branch, service, test helper or design decision.

Excluded scope:

- MIST-091 ref-local heap-field/element lifetimes: no fresh accepted missing-case witness was established; preserve existing FrameScopes behavior.
- MIST-061 frozen-lease policy, MIST-119 closed-unknown readonly policy, MIST-121 mutable receiver visibility/owned-type cooperation, MIST-122 plain linear parameter transfer/drop policy, and MIST-157 return lifting remain separate decisions. Ordinary argument handoff/reuse does not authorize altering them.
- Reserved 35 structural containment, 38 Storm generation, and 39 native constants/tuple metadata. No new foreign retention annotations, whole-program alias completeness, purity promise redesign or mandatory summary architecture.

## Incidental fixes

If you uncover additional issues with clear expected behavior that are straightforward to fix and verify without user input, solve them in additional commits. This includes small fixes outside your primary files or objective; do not merely surface them as extras or make the user dispatch another task. Add appropriate verification and update actual documentation/backlog entries where relevant. Keep each coherent incidental fix separately committed and include it in the same landing workflow.

Avoid duplicating work already landed or assigned to an active workstream. Defer only when the finding requires a genuine user decision, substantial new work, unavailable access/authorization, or verification you cannot complete; state the concrete reason. Ordinary implementation choices and arbitrary file boundaries do not require user input. Finish the primary task and do not turn incidental cleanup into an open-ended campaign.

## Verification and closeout

Tier 1 for extracted effect/ownership decisions; Tier 2 hand-built bodies/graphs for call-boundary state, branching, joins and alias claims. Label traits. Do not parse/compile source to verify local analyzer decisions. Add only a small explicit seam witness where cross-stage gating itself requires it.

Existing verification anchors (inspect actual helpers/costs before extending):

- `Test/Mist.Tests/ProgramAnalysis/OwnershipDecisionTests.cs`: Existing ownership judgment controls; construct inputs directly for new tests.
- `Test/Mist.Tests/ProgramAnalysis/OwnershipRefusalTests.cs`: Existing ownership refusal behavior.
- `Test/Mist.Tests/Helpers/PurityFragments.RoundFive.cs`: OwnedReturnOfAFreshLocalKeptByAField; Existing direct retained-fresh-source refusal anchor; not proof of ordinary-call correctness.
- `Test/Mist.Tests/ProgramAnalysis/PurityRoundFiveTests.cs`: Current alias/purity regression surface; existing expensive fixture is an anchor, not license to repeat compiler loops.
- `Test/Mist.Tests/ProgramAnalysis/FrameScopeDecisionTests.cs`: Regression boundary only; not assigned ref-local feature work.

Focused commands, after building changed code:

```bash
tools/test-ledger/run.sh Test/Mist.Tests --filter "FullyQualifiedName~OwnershipDecisionTests|FullyQualifiedName~OwnershipRefusalTests|FullyQualifiedName~PurityRoundFiveTests|FullyQualifiedName~FrameScopeDecisionTests"
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

Update `MIST-068` in the actual `backlog/issues.yaml`. Record implemented behavior, tests/evidence and precise remaining limitations. Keep changes narrow; preserve historical evidence, stable IDs and other workers' updates. Move fully discharged records to the archive and retain genuinely unfinished residue. Do not merely return proposed closeout text or create another tracking system. Update necessary user/API/spec documentation and generated artifacts in their owning files.

Update MIST-068 with current reproduction, contract precedence, tested forms and precise remaining limitations. Close only the implemented category; retain any distinct unimplemented result/receiver alias form as explicit residue rather than claiming arbitrary alias completeness.

## Commit and land

You are authorized to commit and publish this task directly to remote `main` without another confirmation. Do not create pull requests. Commit implementation, appropriate tests/generated artifacts, required documentation and actual closeout updates together or in coherent task commits. Fetch and incorporate the latest target branch, resolve conflicts while preserving other landed work, and perform the required verification on the integrated result. Use a normal, non-force push. Work from your own isolated checkout; do not reset or commandeer another session's working tree.

Other workers may land concurrently. If publication is rejected because the target advanced, fetch, reconcile, rerun verification affected by the new combined result, and retry. Keep shared-register edits narrow. You own routine integration; do not hand it to the user or assume a central integrator. Do not force-push the target, bypass protections, silently accept failures or mark unfinished work complete. Preserve newer compatible Storm pins already incorporated by main.

Finish by fetching and confirming that the intended changes, additional incidental fixes and actual closeout are present on remote `main`. Report landed commit IDs, actual verification results, entries updated, completed incidental fixes and any remaining limitations. A local commit or pushed task branch alone is not completion.

If unavailable credentials or branch protection prevents landing, complete all unaffected work, preserve the result durably, and report the exact blocker and smallest required user action. Do not switch to a PR workflow. If a genuine unresolved product decision prevents implementation, identify it precisely and retain the affected item as open. Do not repeat an unchanged failed attempt indefinitely or claim success without verification.
