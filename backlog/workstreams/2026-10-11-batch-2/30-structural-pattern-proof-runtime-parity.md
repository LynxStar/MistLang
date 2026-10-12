# 30 — Type-pattern proofs and runtime representation

Complete this bundle independently, including verification, actual tracking updates, commits and publication. Repository: `https://github.com/LynxStar/MistLang.git`; target: remote `main`. Suggested model tier: **Sol/Opus**, effort: **high**. The hard part is preserving soundness across interval/product containment, predicate composition, union tags/factories and runtime binding provenance. One worker owns the whole dependency chain; proofs must describe actual emitted predicates. These portable labels guide the same task and do not require a separate reviewer or model change.

## Starting point

Planning baseline: `0428515879b2d0245c996a5db98a676e441b9cbe` (2026-10-11). Compiler/source probes and the fresh solution build used `b1e556fb9d9528367f7de6bd7d71a13bd5c28aad`; intervening commits only reconcile canonical tracking evidence/contracts, with no source or test changes. Storm pin: `83410653c7d722868a5001d2ef71e6692ec012b3`. The build passed with zero warnings/errors. No fresh full-suite pass is claimed. Evidence below distinguishes executed probes, current source inspection and leads needing validation. Previous packets 17–25 have landed; revalidate current main rather than repeating those implementations.

Start from current remote main in an isolated checkout, retaining your existing isolated checkout and completed work when continuing. Follow the repository’s applicable instructions. Initialize with `git submodule update --init --recursive` and verify checked-out commits match current main’s recorded pins. Revalidate each item on current main; if already fixed, verify acceptance and complete any real closeout gap without duplicate code. Do not read or port retired classic implementation from Git history. Spec task documents are implemented and excluded from work discovery.

## Outcome and included obligations

Supported structural/refined patterns mean the same thing to source containment, narrowed bindings, semantic constituent identity and emitted runtime tests. Proven unreachable patterns are source-refused, nested refinements retain every predicate, and union carriers distinguish semantically distinct refined constituents.

### MIST-012 — Bounded containment and inherited refinement predicate parity

**Current evidence:**

executed current CLI run; no test-suite execution claimed
<scratch-project>/out/Probe/Program.cs: error MIST_CS_ERROR: The pattern is unreachable. It has already been handled by a previous arm of the switch expression or it is impossible to match.
<scratch-project>/out/Probe/Program.cs: error MIST_CS_ERROR: The pattern is unreachable. It has already been handled by a previous arm of the switch expression or it is impossible to match.

Observed exit code: 1.

```mist
using Standard; namespace Probe; public class Program { public static int Pick(ulong x) { return x switch { >= 0 => 1, 1 => 2, _ => 3 }; } public static void Main() { Console.WriteLine(Pick(1)); } }
```
executed current CLI run; no test-suite execution claimed
<scratch-project>/out/Probe/Program.cs: error MIST_CS_ERROR: The pattern is unreachable. It has already been handled by a previous arm of the switch expression or it is impossible to match.

Observed exit code: 1.

```mist
using Standard; namespace Probe; public class Program { public static int Pick(int? x) { return x switch { >= 0 => 1, > 10 => 2, _ => 3 }; } public static void Main() { Console.WriteLine(Pick(1)); } }
```
executed current CLI run; no test-suite execution claimed
<scratch-project>/out/Probe/Program.cs: error MIST_CS_ERROR: The pattern is unreachable. It has already been handled by a previous arm of the switch expression or it is impossible to match.

Observed exit code: 1.

```mist
using Standard; namespace Probe; public class Program { public static int Pick((bool, bool) x) { return x switch { (true, _) => 1, (true, false) => 2, _ => 3 }; } public static void Main() { Console.WriteLine(Pick((true,false))); } }
```
executed current CLI run; no test-suite execution claimed
True

Observed exit code: 0.

```mist
using Standard; namespace Probe; type Positive = int > 0; type SmallPositive = Positive < 10; public class Program { public static void Main() { int x = -1; Console.WriteLine(x is SmallPositive); } }
```

**Contract references:**

- `.kiro/specs/switch-expressions/requirements.md` — Requirement7.1–7.3: subsumed/duplicate arms receive source unreachable finding (audited line 114)
- `.kiro/specs/refinement-types/requirements.md` — Requirement4: refined base/predicate model; Requirement6.2–6.4 runtime test and safe refined binding (audited line 112)

**Owning source:**

- `Source/Mist/ProgramAnalysis/Refinements/MatchedValues.cs` — Covered / IntegralDomain / Read: currently long-domain integral or real; nested refined base returns null (audited line 20)
- `Source/Mist/ProgramAnalysis/Rules/PatternAnalyzer.cs` — JudgeUnreachable / ArmFinding / Covers (audited line 282)
- `Source/Mist/ProgramModel/Construction/Binding/Expressions/PatternBinder.cs` — RefinementOf / BindTuplePattern / bound TypeTestPattern
- `Source/Mist/Backends/CSharpTranspiler/Emitter/BodyEmitter.cs` — EmitTypeTest currently emits typeTest.Refinement alone (audited line 2515)
- `Source/Mist/ProgramAnalysis/Refinements/Requirement.cs` — Of / OfType / Refined: facts trusted by narrowing

**Acceptance and controls:**

- For UInt64 comparisons, optional/finite union constituent partitions and statically tuple-typed product patterns, prove an arm unreachable only when all values the later arm can actually match are already covered by earlier unguarded arms. Handle comparison/logical composition and catchalls in these domains; guards never add definite earlier coverage. Diagnostics occur at the later source arm, with current descriptor/severity, before Roslyn.
- Cover unsigned endpoints including values beyond Int64.MaxValue without truncation/overflow; preserve existing signed integral, real NaN/infinity and unknown predicate behavior. Reuse existing integral constant representation; extend value-set representation only as narrowly required.
- Compose every predicate in a refined-on-refined alias chain for runtime is/type-pattern tests and successful binding knowledge. Positive=int>0;SmallPositive=Positive<10 must reject -1 and10 and accept1. Preserve actual input value and single evaluation; test alias wrappers and logical predicates without trusting merely the outer predicate.
- Scope012 to the reproduced UInt64, optional/finite union partition, statically tuple-typed product and refined-on-refined predicate categories. Require positive overlap/non-subsumption, valid signed/real controls and guarded earlier-arm controls for each. General symbolic/list/property containment remains open and excluded; do not close all012 after these bounded repairs.
- Preserve already landed tuple missing-witness exhaustiveness. This packet does not resolve the universal-arm-versus-collective-coverage policy from tuple R3.5 or alter unreachable/enum diagnostic severity policy.

### MIST-216 — Semantic refinement identity in erased-equal union carriers

**Current evidence:**

fresh executed typed-union source probe; runtime blocked by carrier collision, no separate solver defect asserted
A current non-exhaustive warning is followed by duplicate carrier IsInt32/AsInt32/AsInt32OrNull/FromInt32 and ambiguous factory/retrieval C# errors. The typed Small source and constituent pattern avoid the excluded raw-unknown-to-whole-union membership policy. Runtime branch remains unobservable until216 is repaired inside this packet.

Observed exit code: 1.

```mist
using Standard; namespace Probe; type Small = int < 10; type Big = int > 100; public class Program { public static void TakeSmall(Small x) { Console.WriteLine(x); } public static void Main() { Small a = 5; Small | Big value = a; if (value is Small s) { TakeSmall(s); } Console.WriteLine(value switch { Small s => "small", Big b => "big" }); } }
```

**Contract references:**

- `.kiro/specs/type-unions/requirements.md` — Requirement9.1–9.6: distinct carrier/tag/retrieval/factories and order-independent reuse (audited line 132)
- `.kiro/specs/refinement-types/requirements.md` — Requirement10.1–10.4 refined constituents (audited line 166)

**Owning source:**

- `Source/Mist/Backends/CSharpTranspiler/Lowering/UnionCarrierNames.cs` — ConstituentName RefinedType arm erases to base (audited line 43)
- `Source/Mist/Backends/CSharpTranspiler/Lowering/UnionEntry.cs` — Entered identity-first typed constituent route
- `Source/Mist/Backends/CSharpTranspiler/Emitter/CSharpEmitterV2.cs` — EmitCarrierStruct
- `Source/Mist/Backends/CSharpTranspiler/Emitter/BodyEmitter.cs` — union entry, narrowing, widening and pattern retrieval consumers

**Acceptance and controls:**

- Both typed Small and typed Big values enter the correct constituent factory and retrieve/narrow with the corresponding refined static type and predicate. Generated members and carrier names remain unique even when CLR types are identical. Do not ask an int-based implicit operator to infer which refinement was selected.
- Use stable semantic constituent identity for named and anonymous refinements; distinguish different refined union sets, reuse equivalent ordering/alias uses according to existing Mist identity, and keep tags/member identifiers consistent across entry/access/widening/pattern consumers.
- Preserve sized-array erased-equal carrier factories from landed031, ordinary int|string unions and optional representations. No runtime choice for unrefined literals and no new ambiguous constituent policy; use already selected constituent evidence.

### MIST-065 — Validate runtime refined constituent binding and flow end to end

**Current evidence:**

fresh executed typed-union source probe; runtime blocked by carrier collision, no separate solver defect asserted
A current non-exhaustive warning is followed by duplicate carrier IsInt32/AsInt32/AsInt32OrNull/FromInt32 and ambiguous factory/retrieval C# errors. The typed Small source and constituent pattern avoid the excluded raw-unknown-to-whole-union membership policy. Runtime branch remains unobservable until216 is repaired inside this packet.

Observed exit code: 1.

```mist
using Standard; namespace Probe; type Small = int < 10; type Big = int > 100; public class Program { public static void TakeSmall(Small x) { Console.WriteLine(x); } public static void Main() { Small a = 5; Small | Big value = a; if (value is Small s) { TakeSmall(s); } Console.WriteLine(value switch { Small s => "small", Big b => "big" }); } }
```

**Contract references:**

- `.kiro/specs/refinement-types/requirements.md` — Requirement10.3–10.4 runtime match narrows to the specific refined constituent (audited line 168)
- `.kiro/specs/type-unions/requirements.md` — Requirement8.1–8.4 constituent patterns and binding type (audited line 120)

**Owning source:**

- `Source/Mist/ProgramModel/Construction/Binding/Expressions/PatternBinder.cs` — TypeTestPattern constituent and refined Binding type
- `Source/Mist/ProgramAnalysis/Rules/RefinementFlow.cs` — FlowStepKind.Assume and bound pattern knowledge
- `Source/Mist/ProgramAnalysis/Rules/RefinementAnalyzer.cs` — Proving: constituent/refined argument proof
- `Source/Mist/Backends/CSharpTranspiler/Emitter/BodyEmitter.cs` — carrier constituent extraction and predicate tests

**Acceptance and controls:**

- For a value already typed as a union of refined constituents, matching Small s gives s static Small and enough proof to pass it to a Small parameter; matching Big b gives Big. Cover same-base constituents after216 plus distinct-base refined constituents, predicates that fail and values in neither accepted predicate where source construction is valid.
- Compare runtime membership, introduced binding type and flow knowledge. No path may bind a value as a refinement whose full composed predicate was not tested; assignment and calls inside branches must agree, and branch facts do not leak outside/reassignment.
- Close065 only after the finite runtime/flow controls demonstrate correct behavior; if current analysis is already correct, record that precise verification rather than invent a solver expansion. Raw unknown constituent tested against a whole carrier union is excluded as a separate membership policy.

### MIST-088 — Lossless enum conversion to its actual union underlying type

**Current evidence:**

sibling backend auditor executed current CLI
Source MIST_PA_NO_IMPLICIT_CONVERSION from Mixed to its actual int|string underlying type.

```mist
using Standard; namespace Probe; public enum Mixed : int | string { Number = 3, Text = "s" } public class Program { public static void Main() { int | string x = Mixed.Text; Console.WriteLine(x); } }
```

**Contract references:**

- `.kiro/specs/enums/requirements.md` — Requirement7.1–7.3: enum assignment/argument to underlying type; reverse implicit conversion refused (audited line 98)

**Owning source:**

- `Source/Mist/ProgramModel/Construction/Binding/ConversionClassifier.cs` — Within target-union branch precedes source enum whole-underlying edge (audited line 122)
- `Source/Mist/ProgramModel/Construction/Binding/ConversionRelation.cs` — enum underlying conversion relation
- `Source/Mist/Backends/CSharpTranspiler/Emitter/EnumEmitter.cs` — Value and implicit conversion to actual underlying union (audited line 28)

**Acceptance and controls:**

- Mixed.Number and Mixed.Text assign and pass as arguments to int|string without a cast, preserving the underlying tag/value. Preserve primitive-base enum conversion and variant identity. Reverse underlying→enum implicit conversion stays refused, with existing optional try-cast behavior untouched.
- Generated conversion targets the existing underlying carrier as a whole. Preserve selected semantic constituent identity and do not bypass refined predicates or convert an enum to an unrelated union.

## Scope and independence

One owner controls MatchedValues/PatternAnalyzer containment, composed refinement tests, refined union factory naming/entry/retrieval and enum-to-underlying conversion. Internal216→065 validation is deliberate and needs no other task. Preserve stable bound model and public emitter interfaces. Ref/heap packet31 owns EscapeAnalyzer/DisciplineAnalyzer ref legality; ownership packet32 owns field move/lease/provenance state. Shared BodyEmitter edits are confined to pattern/union/conversion branches, not release or asynchronous lowering. Do not make a changed value-set helper API a prerequisite for another workstream.


**Excluded work and decisions:**

- 217 unknown positional tuple discard/var support is policy residue: tuple-pattern R4.1 (line75) expressly refuses nonTuple static inputs, whereas PatternBinder exempts Unknown and accepts fully typed forms. No newer explicit requirement or banked sample found to authorize untyped positional unknown extension. Do not implement it or rewrite that policy in this packet.
- MIST108 unrefined literal-to-refined-union selection, general raw unknown value matching a whole union carrier, enum unreachable-arm severity, tuple exhaustiveness policy, new structural pattern syntax, arbitrary Deconstruct/ITuple protocol and unsupported sized sequence element scans.
- Symbolic/property/list containment beyond narrowly proved supported static forms remains open on012. Avoid all-record closeout from fixing just the reproduced domains.

## Focused verification

- `Test/Mist.Tests/ProgramAnalysis/PatternDecisionTests.cs` — Tier1 direct arm subsumption decisions
- `Test/Mist.Tests/ProgramAnalysis/PatternRefusalTests.cs` — Tier1 decisions and Tier2 source report sites
- `Test/Mist.Tests/ProgramAnalysis/PatternAssignmentTests.cs` — Tier2 binding/assignment facts
- `Test/Mist.Tests/Backend/UnionCarrierNamesTests.cs` — carrier names and identity
- `Test/Mist.Tests/Backend/UnionCarrierEmissionTests.cs` — carrier factory/retrieval emission
- `Test/Mist.Tests/Backend/UnionEntryTests.cs` — selected typed constituent entry
- `Test/Mist.Tests/Backend/UnionPatternMatchingEmissionTests.cs` — union narrowing
- `Test/Mist.Tests/Backend/SizedSequencePatternEmissionTests.cs` — landed sized/wide tuple controls
- `Test/Mist.Tests/Integration/PredicatePatternRuntimeBoundaryTests.cs` — bounded existing Tier4 predicate runtime seam
- `Test/Mist.Tests/Semantics/EnumUnderlyingInferenceTests.cs` — enum underlying conversion seam
- `Test/Mist.Tests/Backend/EnumVariantIdentityTests.cs` — enum identity

Tier1 direct decisions/facts; Tier2 handbuilt graph/body/binder/emitter fragments and separately source report ownership; only a handful of existing runtime seams for actual predicate/carrier/reference/clone behavior. No per-case full compiler pipeline tests. Complete combined bundle with root full gates and actual canonical closeout; bounded records retain precisely excluded residue.

Initial focused commands (extend filters to your new classes; these do not replace final gates):

- `tools/test-ledger/run.sh Test/Mist.Tests --filter "FullyQualifiedName~PatternDecisionTests|FullyQualifiedName~PatternRefusalTests|FullyQualifiedName~PatternAssignmentTests|FullyQualifiedName~UnionCarrier|FullyQualifiedName~UnionEntryTests|FullyQualifiedName~UnionPatternMatchingEmissionTests|FullyQualifiedName~PredicatePatternRuntimeBoundaryTests|FullyQualifiedName~EnumUnderlyingInferenceTests|FullyQualifiedName~EnumVariantIdentityTests"`

To reproduce inline cases, create a fresh scratch project containing `Program.mist` and this `mist.toml`, unless the case explicitly supplies an entry-point override:

```toml
name = "Probe"
namespace_root = "Probe"
```

After building current main, run `dotnet run --project Source/Mist.CLI --no-build -- run <scratch-project> --quiet` from the repository. For generated-consumer fidelity, also run `dotnet build <scratch-project>/out/Probe.csproj --no-incremental` and inspect every diagnostic. Scratch projects and generated `out/` remain untracked. Complete source snippets are reproductions; smaller prose examples are component inputs to construct in the existing narrow seams.

Owning canonical records: `MIST-012`, `MIST-216`, `MIST-065`, `MIST-088`. Each included bounded obligation requires its own verified disposition; preserve excluded or unverified residue rather than claiming the entire record fixed from one representative case.

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
