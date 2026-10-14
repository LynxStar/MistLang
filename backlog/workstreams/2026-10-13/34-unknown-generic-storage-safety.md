# 34 — Unknown generic storage safety

Complete this task independently, including implementation, verification, tracking updates and landing. Repository: `https://github.com/LynxStar/MistLang.git`. Target: `origin/main`. Suggested model: **Fable, high effort**. The storage-write summary must preserve declaration identity through nested/base generic substitutions, reach a sound recursion fixed point and distinguish each access direction. It must become available at the correct binding/selection boundary despite later body binding, without source-order-dependent overload decisions or Stage 5 mutating the finished model. These are portable user model categories, not API identifiers.

## Outcome and starting point

A closed generic view containing unknown exposes only operations that cannot write storage through those erased argument positions. Compute the existing contract's per-parameter, per-access-direction effects across recursion, inheritance and selected calls; filter unsafe access while preserving useful readers, open declarations and restored concrete views.

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

- `.kiro/specs/unknown-type/requirements.md`: Glossary: Storage_Location, T_Typed_Storage_Location, Write_To_T; locals/parameters differ from persistent writable storage
- `.kiro/specs/unknown-type/requirements.md`: Requirement 9.1–9.11 complete per-enclosing-parameter write reachability; separate accessor directions, known/foreign/dynamic effects and recursive fixed point
- `.kiro/specs/unknown-type/requirements.md`: Requirement 10.1–10.7 filtering on Closed_Generic_With_Unknown; safe reads and open-body exception; source diagnostic explanation
- `.kiro/specs/unknown-type/requirements.md`: Requirement 11.1–11.5 concrete pattern narrowing restores access; unknown/closed-unknown tautological targets refused
- `.kiro/specs/unknown-type/requirements.md`: Requirement 5 constructor/storage bans remain separate from member visibility

**Evidence 1 — executed CLI source-admission/artifact probe, `c80c36823175514e5448fc2296bb174fbb360ec2`.**

```mist
using Standard; namespace Probe; public class Box<T> { public T Value; public Box(T value) { this.Value = value; } public void Set(T value) { this.Value = value; } public T Get() { return this.Value; } } public class Program { public static void Unsafe(Box<unknown> b) { b.Set("bad"); } public static void Main() { Console.WriteLine("bound"); } }
```

Exit 0. Observed:

```text
bound
```

**Evidence 2 — executed CLI source-admission/artifact probe, `c80c36823175514e5448fc2296bb174fbb360ec2`.**

```mist
using Standard; namespace Probe; public class Box<T> { public T Value; public Box(T value) { this.Value = value; } public void Set(T value) { this.Value = value; } public void Forward(T value) { Set(value); } public T Get() { return this.Value; } } public class Program { public static void Unsafe(Box<unknown> b) { b.Forward("bad"); } public static void Main() { Console.WriteLine("bound"); } }
```

Exit 0. Observed:

```text
bound
```

**Evidence 3 — executed CLI source-admission/artifact probe, `c80c36823175514e5448fc2296bb174fbb360ec2`.**

```mist
using Standard; namespace Probe; public class Box<T> { public T Value; public Box(T value) { this.Value = value; } public void Set(T value) { this.Value = value; } public T Get() { return this.Value; } } public class Program { public static unknown Read(Box<unknown> b) { return b.Get(); } public static void Main() { Console.WriteLine("bound"); } }
```

Exit 0. Observed:

```text
bound
```

Unsafe(Box<unknown>) methods are bound, compiled and emitted but Main never invokes them; these prove invalid source admission, not an executed runtime corruption. Reader control likewise verifies source admission. Compilation confirms existing unknown view lowers to dynamic; that representation is intentionally preserved. The solution build and its limits are recorded above; no full-suite pass is claimed.

Implementation references:

- `Source/Mist/ProgramModel/Model/Scopes/TypeMemberLookup.cs`: Gather / AddState / AddMethods collect members without unknown storage-write effects
- `Source/Mist/ProgramModel/Construction/Binding/MemberResolution.cs`: Resolve preserves constructed receiver and base paths for selected member identity
- `Source/Mist/ProgramModel/Construction/Binding/Expressions/PostfixBinder.cs`: BindMemberAccess / ResolveReceiverMember and selected invocation
- `Source/Mist/ProgramModel/Construction/Binding/Expressions/IndexBinder.cs`: bound indexer lookup and selected accessor/parameter identities
- `Source/Mist/ProgramModel/Construction/Binding/Expressions/ExpressionBinder.cs`: assignment target direction and bound source trees
- `Source/Mist/ProgramModel/Construction/Binding/MemberBinder.cs`: BindAll declaration bodies/accessors/initializers; ordering challenge
- `Source/Mist/ProgramModel/Construction/Binding/TypeSubstitution.cs`: existing declaration-identity generic/base/outer argument mapping
- `Source/Mist/ProgramModel/Construction/Binding/ConversionRelation.cs`: unknown covariant widening remains class-only and one-way
- `Source/Mist/Backends/CSharpTranspiler/Emitter/TypeNameEmitter.cs`: existing dynamic closed-unknown representation, unchanged
- `Source/Mist/ProgramModel/Model/Scopes/CandidateSet.cs`: existing resolution and selected overload contracts to preserve

## Scope and acceptance

- Classify fields, automatic/explicit-backed properties, indexers, methods, constructors and declared operators for every enclosing type parameter using R9. Direct T/T-array/nested persistent storage writes are writers; pure reads and unrelated non-T state writes do not become writers merely because T occurs in a method parameter/result/local. Preserve parameter declaration identity, not names/ordinals alone.
- Getter and setter/read and write classifications are independent. Safe T getter stays available with unknown result; an auto setter or getter that performs a T-storage write is unavailable. Test field reads versus writes, indexer directions, compound assignments and already modeled ref/out effects through T storage.
- Transitive same-type/base/helper calls and generic method substitutions preserve which enclosing parameter is written. Mutual recursion with one real writer converges to writer; a read-only cycle remains reader. Inherited closed base paths, outer/nested generic parameters, two independent type parameters and nested closed-unknown argument positions are covered with handbuilt fixed-point/substitution cases.
- Operator-call edges use the selected source Operator/Method declaration identity and existing enclosing/base substitution. Preserve that identity when a closed selection adds call-site types. Add hand-built controls where a generic wrapper invokes a selected operator that writes T-storage (writer) or only reads it (reader), including renamed and inherited owner parameters. This packet owns summaries and filtering; packet 37 owns generic operator applicability/ranking. Neither requires the other's unpublished service.
- Unknown/virtual/abstract/callback/foreign calls follow exactly the conservative existing R9.8–9.9 rule for relevant T-containing arguments and storage, not an invented purity exception. Use already bound selected targets where known; do not turn any mention of T anywhere into a storage write.
- At a receiver closed with unknown, unsafe selected operations are unavailable with the existing member-not-found family and explanatory member/receiver/storage reason. Safe reader overloads remain selectable when a same-name writer is hidden; preserve candidate applicability and generic/operator ranking. Declaration/source order and helper placement do not change the result.
- Fully open generic body access remains legal; concrete constructions remain mutable. Concrete type-pattern narrowing restores the known receiver operations. Existing unknown construction/writable-storage bans, covariant dynamic spelling and direction of conversion remain; no new unknown representation or variance policy.
- Cover all finite named surfaces in R9/R10, with both rejection and meaningful admission controls. Do not close 086 after Set/Forward alone. Bound summaries belong to the appropriate construction/analysis phase, with immutable finished model and stable existing bound identities. Implementation may make necessary pipeline-order changes within this bundle, but it must not defer stage/candidate correctness to another task.

## Independence and ownership

Own per-type-parameter storage effects and closed-unknown filtering across existing bound member/candidate surfaces. Generic-operator packet may expand selected operator closure and user-slicing packet may expand selected range/indexer binding; this packet must read their existing member/parameter/receiver identities and preserve public selection/emission APIs. Neither packet may require a new unpublished storage-summary service to finish. Shared lookup/IndexBinder/operator sites need narrow compatible edits and normal integration in either landing order. Property/list proof packet 35 owns pattern containment and reads existing bound property/list identity, never this filtering summary.

This packet has no dependency on another workstream. As its parent session, you own every included task and its internal dependencies. Determine the execution order, decide which work to perform yourself or dispatch to subagents, and integrate their changes into your primary implementation. Resolve internal conflicts and verify the combined result; subagents return contributions to you rather than independently publishing partial work to the shared target. Use current repository contracts. Do not hand internal ordering, coordination or integration to the user, the planning session or another packet. Complete and close out every included item; a representative subset or a collection of subagent reports is not completion.

Allow narrowly necessary adjacent edits, including compatible bound metadata, consumers, generated artifacts and user/API/spec documentation needed to finish this outcome. Keep changes coherent and preserve already landed behavior. Shared files require narrow integration, not another packet's unpublished branch, service, test helper or design decision.

Excluded scope:

- MIST-119 generic readonly-array ownership policy and all other explicit variance/readonly alias/unknown representation decisions.
- No generalized whole-program effect architecture replacement, foreign layout introspection, new reflection/unknown-list pattern behavior, readonly/purity promise redesign, or solved-flow return lifting 157.
- Do not adopt unrelated ordinary-call alias retention 068 or ownership movement/ref alias 091 policies under the storage-write umbrella.

## Incidental fixes

If you uncover additional issues with clear expected behavior that are straightforward to fix and verify without user input, solve them in additional commits. This includes small fixes outside your primary files or objective; do not merely surface them as extras or make the user dispatch another task. Add appropriate verification and update actual documentation/backlog entries where relevant. Keep each coherent incidental fix separately committed and include it in the same landing workflow.

Avoid duplicating work already landed or assigned to an active workstream. Defer only when the finding requires a genuine user decision, substantial new work, unavailable access/authorization, or verification you cannot complete; state the concrete reason. Ordinary implementation choices and arbitrary file boundaries do not require user input. Finish the primary task and do not turn incidental cleanup into an open-ended campaign.

## Verification and closeout

Most new invariants are Tier 1 summary/containment/substitution decisions on directly constructed inputs, Tier 2 summary fixed-point and lookup/binder direction paths with in-memory bound trees and stubbed cross-module collaborators. Do not use ProgramModelProbe parsing upstream source to make new low-tier inputs. One existing bounded seam may validate diagnostic ownership if needed; runtime sample bank preserves safe widened-reader behavior. Follow current testing-philosophy cost limits: Tier 4 under ten seconds and stub heavyweight collaborators unless the test owns them; no new per-case compiler/MSBuild loops.

Existing verification anchors (inspect their actual helpers/costs before extending):

- `Test/Mist.Tests/Semantics/UnknownCovariantWideningTests.cs`: existing dynamic spelling, class-only one-way covariance and unknown reader control; old component fixture is a lead, not license for new expensive tests
- `Test/Mist.Tests/Semantics/UnknownApplicabilityTests.cs`: unknown relation/applicability controls
- `Test/Mist.Tests/Semantics/UnknownInheritanceProhibitionTests.cs`: unknown inheritance contract
- `Test/Mist.Tests/Integration/UnknownStorageBanTests.cs`: existing storage seam; preserve current tier/cost rules
- `Test/Mist.Tests/ProgramAnalysis/BodyWalkerCoverageTest.cs`: if new bound node consumer coverage is required

Focused commands:

```bash
tools/test-ledger/run.sh Test/Mist.Tests --filter "FullyQualifiedName~UnknownCovariantWideningTests|FullyQualifiedName~UnknownApplicabilityTests|FullyQualifiedName~UnknownInheritanceProhibitionTests|FullyQualifiedName~UnknownStorageBanTests"
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

Update `MIST-086` in the actual `backlog/issues.yaml` as part of this change. Record implemented behavior, supporting tests/evidence and any precise remaining limitation. Preserve historical evidence and other workers' updates; keep edits to owning records narrow. Close only completed portions and move fully discharged records to the archive with their stable IDs. Do not merely return proposed closeout text or create a new tracking system. Update necessary contract/API/book documentation and generated artifacts in their owning files.

## Commit and land

You are authorized to commit and publish this task directly to remote `main` without another confirmation. Do not create pull requests. Commit implementation, appropriate tests/generated artifacts, required documentation and actual closeout updates together or in coherent task commits. Fetch and incorporate the latest target branch, resolve conflicts while preserving other landed work, and perform the required verification on the integrated result. Use a normal, non-force push. Work from your own isolated checkout; do not reset or commandeer another session's working tree.

Other workers may land concurrently. If publication is rejected because the target advanced, fetch, reconcile, rerun verification affected by the new combined result, and retry. Keep shared-register edits narrow. You own routine integration; do not hand it to the user or assume a central integrator. Do not force-push the target, bypass protections, silently accept failures or mark unfinished work complete. Preserve newer compatible Storm pins already incorporated by main.

Finish by fetching and confirming that the intended changes, additional incidental fixes and actual closeout are present on remote `main`. Report landed commit IDs, actual verification results, entries updated, completed incidental fixes and any remaining limitations. A local commit or pushed task branch alone is not completion.

If unavailable credentials or branch protection prevents landing, complete all unaffected work, preserve the result durably, and report the exact blocker and smallest required user action. Do not switch to a PR workflow. If a genuine unresolved product decision prevents implementation, identify it precisely and retain the affected item as open. Do not repeat an unchanged failed attempt indefinitely or claim success without verification.
