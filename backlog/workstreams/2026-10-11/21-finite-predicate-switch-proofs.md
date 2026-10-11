# 21 — Finite predicate and switch proofs

Complete this independent bundle, including verification, actual backlog updates, commits and publication. Repository: `https://github.com/LynxStar/MistLang.git`. Target: remote `main`. Suggested model tier: **Astra/Fable**, effort: **high**. Sound finite interval/product coverage needs conservative reasoning for guards and unknown predicates; the same work must preserve proof provenance and avoid inventing facts for runtime-dependent values. These portable labels are guidance, not API identifiers or a requirement to launch a separate review.

## Starting point

Planning baseline: `7a14a1a7648c30c40b0e83da932255558dea64de` (2026-10-11). Executed compiler probes and the clean solution build used `8390675bd27f8e683c50664a94b4bc728daa7007`; the intervening published changes are publishing scripts/README and four backlog evidence corrections, with no compiler or test changes. Storm pin: `b4e5853ccca45623e59fcdb9ee0afd6e90e836ce`, also verified as Storm remote main during planning. The solution build passed with zero warnings/errors; no fresh full-suite pass is claimed. Each item below distinguishes executed behavior from source inspection or an unverified lead.

Start from current remote main in an isolated task checkout; retain your existing isolated checkout and completed work when continuing. Follow the repository’s applicable instructions. Run `git submodule update --init --recursive` and verify checked-out commits match the revision’s recorded pins; a setup hook alone is not evidence. Revalidate every item against current main. If already fixed, verify acceptance and finish any real tracking gap without duplicate implementation. Do not read or port retired classic implementation from Git history. `.kiro/specs/**/tasks.md` documents are implemented and excluded from work discovery.

## Outcome and included obligations

Report demonstrably missing finite tuple/optional cases, refuse declaration predicates outside their value domain, and retain declared sized sequence facts on direct calls.

### MIST-001

**Current evidence:** current source inspection, no new tuple execution claimed

Missing selects only bool, enum and union domains; tuple defaults to OnlyConstants. CoversAll recognizes CatchAllPattern/identity type tests, not product coverage; current class comment explicitly retains nine relational tuple arms as merely unrefused.

Bounded obligation: find demonstrably missing tuple cells. The complete-partition versus universal-arm policy is explicitly excluded below.

### MIST-002

**Current evidence:** executed CLI at baseline

exit 0 prints 1 with no missing-void source diagnostic

```mist
using Standard; namespace Probe; public class Program { public static int Pick(bool? b) { return b switch { true => 1, false => 2 }; } public static void Main() { Console.WriteLine(Pick(true)); } }
```

### MIST-006

**Current evidence:** executed CLI at baseline

declaration admitted; call reports MIST_PA_CONSTRAINT_UNPROVEN. The incompatible constant must be refused at the declared predicate even if never called.

```mist
using Standard; namespace Probe; public class Program { public static void Ne(string key) where key not 18 { } public static void Main() { Ne("x"); } }
```

### MIST-035

**Current evidence:** source inspection plus executed CLI probe

The executed audit probe reports direct int[3] call indexing refused at baseline; Lengths.OfCall fallback discards type-carried length. This does not ask for inference from function bodies.

```mist
using Standard; namespace Probe; public class Program { public static int[3] Make() { return new int[3]; } public static void Main() { Console.WriteLine(Make()[2]); } }
```

**Acceptance for all included obligations:**

- For (bool,bool), arms (true,_) and (false,true) report a demonstrably missing (false,false). Extend missing-witness detection to finite enum products and supported signed-integral rectangular partitions with a proven gap. Preserve unguarded top-level and all-catchall tuple arms.
- For bool? and other finite optional domains, complete present-value arms do not cover void; adding an unguarded void/catchall arm does. Guarded arms and disjunctions retain accurate coverage and do not hide known missing constituents.
- Report a tuple witness only when it is outside every arm’s possible pattern space. Unknown/unsupported predicates and guards must not create speculative findings. Preserve the current no-finding behavior of collectively complete partitions, including the nine-cell relational sample, as a regression limit; this does not settle the contradictory universal-arm policy.
- Equality and inequality declaration predicates with incompatible constant types are refused at that predicate site, including wrapped negation and logical composition and declarations never called. Keep compatible bool/integral/char/string/real constants admitted according to existing comparison rules; ordering refusals keep their diagnostics.
- An ordinary call with return type int[3] proves index 2 and rejects index 3, just like an equivalent typed local. Preserve recognized copy/slice length operations, alias/refinement wrappers and from-end bounds. An unsized/unknown returned array gains no invented concrete length.
- Preserve current source diagnostic descriptor/severity for switch coverage; this is proof capability work, not the open warning/error policy decision. Existing unreachable and invalid-pattern judgments remain unchanged except necessary reuse of correct finite facts.

## Contracts and implementation

- `.kiro/specs/switch-expressions/requirements.md` — Requirement 6.1–6.8: static-domain coverage, constituent coverage, guards and catchalls (audited line 105)
- `.kiro/specs/tuple-pattern-matching/requirements.md` — Requirement 3.4–3.5: tuple catchall completeness and nonexhaustive tuples (audited line 67)
- `.kiro/specs/pattern-matching/requirements.md` — Requirement 6.3: incompatible constant versus scrutinee refused (audited line 120)
- `.kiro/specs/adhoc-refinement-constraints/requirements.md` — Requirement 11 and 13.6: predicate/type compatibility at declaration (audited line 182)
- `.kiro/specs/array-bounds-flow-analysis/requirements.md` — Requirements 1 and 2: concrete dependent length and ordinary index refinement proof (audited line 27)

**Primary source:**

- `Source/Mist/ProgramAnalysis/Rules/ExhaustivenessAnalyzer.cs` — Missing / CoversAll / MightMatch: tuple unbounded fallback and optional constituent overapproximation (audited line 45)
- `Source/Mist/ProgramAnalysis/Refinements/MatchedValues.cs` — finite interval/domain coverage and pattern intersection
- `Source/Mist/ProgramAnalysis/Rules/PatternAnalyzer.cs` — ComparisonFinding / ArmFinding; reuse established pattern compatibility
- `Source/Mist/ProgramAnalysis/Rules/DeclarationShapeAnalyzer.cs` — CheckPredicate checks Misordered and StableMembers, missing equality-domain validation (audited line 252)
- `Source/Mist/ProgramAnalysis/Comparability.cs` — Misordered
- `Source/Mist/ProgramAnalysis/Refinements/Lengths.cs` — Of dispatches calls to OfCall; unrecognized call returns null rather than Typed(invocation.Type) (audited line 69)
- `Source/Mist/ProgramAnalysis/Rules/RefinementAnalyzer.cs` — NeedIndex and existing length-driven requirements (audited line 263)

**Independence:**

Own ExhaustivenessAnalyzer, finite MatchedValues coverage consumers, CheckPredicate equality compatibility and Lengths.OfCall typed fallback. Generic-contract worker owns Requirement.Symbol and argument/dependent substitution; it need not change finite-domain representations for this packet. Runtime representation worker owns sized tuple/union emission, no pattern emitter changes required here. Prefer direct private helpers or already stable APIs, with focused edits if central RefinementAnalyzer consumers need adjustment.

**Excluded work:**

- MIST-012 broad property/list/symbolic/nested-refinement closure is excluded; do not claim entire refined-pattern backlog complete.
- No symbolic theorem prover, receiver/other-argument substitution (MIST-014/MIST-016), widened unsigned representation or unbounded real product enumeration.
- No array/tuple carrier runtime emission repair (MIST-031/MIST-043), body-derived function return lengths, diagnostic severity policy, or runtime refinement guards.
- Tuple-pattern-matching Requirement 3.5 demands one universal unguarded tuple arm, whereas switch-expressions Requirement 6.1 admits collective coverage. Do not rewrite either policy or claim that conflict resolved. Complete the bounded missing-witness obligation and retain this explicit residue on MIST-001; it must remain open if the whole record is not discharged.

## Focused verification

**Existing regression anchors:**

- `Test/Mist.Tests/ProgramAnalysis/ExhaustivenessRefusalTests.cs` — Tier1 finite-domain decisions and Tier2 switch site/name
- `Test/Mist.Tests/ProgramAnalysis/ExhaustivenessDecisionTests.cs` — existing component admissions
- `Test/Mist.Tests/ProgramAnalysis/PatternDecisionTests.cs` — direct pattern decisions
- `Test/Mist.Tests/ProgramAnalysis/PatternRefusalTests.cs` — direct and module mismatch reporting
- `Test/Mist.Tests/ProgramAnalysis/RefinementContractRefusalTests.cs` — declaration contract reporting
- `Test/Mist.Tests/ProgramAnalysis/RefinementIndexRefusalTests.cs` — index bound source diagnostics
- `Test/Mist.Tests/ProgramAnalysis/SequenceLengthDecisionTests.cs` — sequence operation facts
- `Test/Mist.Tests/ProgramAnalysis/RangeLengthFactsTests.cs` — Tier1 interval facts checked against concrete successful slices

**Initial focused commands** (extend filters to include your new test classes; these do not replace final gates):

- `tools/test-ledger/run.sh Test/Mist.Tests --filter "FullyQualifiedName~Exhaustiveness|FullyQualifiedName~PatternDecisionTests|FullyQualifiedName~PatternRefusalTests|FullyQualifiedName~RefinementContractRefusalTests|FullyQualifiedName~RefinementIndexRefusalTests|FullyQualifiedName~SequenceLengthDecisionTests|FullyQualifiedName~RangeLengthFactsTests"`
- `dotnet build Mist.slnx`
- `dotnet format Mist.slnx --verify-no-changes`

For inline source probes, create a fresh scratch project with this `mist.toml` and one reproduction as `Program.mist`:

```toml
name = "Probe"
namespace_root = "Probe"
```

After a current solution build, run `dotnet run --project Source/Mist.CLI --no-build -- run <scratch-project> --quiet` from the Mist repository. Scratch directories and generated `out/` are not tracked deliverables. For artifact fidelity, also build that scratch project’s `out/Probe.csproj` and inspect all diagnostics. Snippets inside prose are component cases to construct in existing test seams, not claims that an incomplete fragment is a runnable program.

Tracking records owned by this packet: `MIST-001`, `MIST-002`, `MIST-006`, `MIST-035`.

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
