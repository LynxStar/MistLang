# 20 — Generic constraints and dependent projections

Complete this independent bundle, including verification, actual backlog updates, commits and publication. Repository: `https://github.com/LynxStar/MistLang.git`. Target: remote `main`. Suggested model tier: **Sol/Opus**, effort: **high**. Six related substitution obligations span binding, proof, conformance and emission. One worker must own identity-preserving substitution end to end; the mechanisms already exist, so no new language design or architecture dispatch is needed. These portable labels are guidance, not API identifiers or a requirement to launch a separate review.

## Starting point

Planning baseline: `7a14a1a7648c30c40b0e83da932255558dea64de` (2026-10-11). Executed compiler probes and the clean solution build used `8390675bd27f8e683c50664a94b4bc728daa7007`; the intervening published changes are publishing scripts/README and four backlog evidence corrections, with no compiler or test changes. Storm pin: `b4e5853ccca45623e59fcdb9ee0afd6e90e836ce`, also verified as Storm remote main during planning. The solution build passed with zero warnings/errors; no fresh full-suite pass is claimed. Each item below distinguishes executed behavior from source inspection or an unverified lead.

Start from current remote main in an isolated task checkout; retain your existing isolated checkout and completed work when continuing. Follow the repository’s applicable instructions. Run `git submodule update --init --recursive` and verify checked-out commits match the revision’s recorded pins; a setup hook alone is not evidence. Revalidate every item against current main. If already fixed, verify acceptance and finish any real tracking gap without duplicate implementation. Do not read or port retired classic implementation from Git history. `.kiro/specs/**/tasks.md` documents are implemented and excluded from work discovery.

## Outcome and included obligations

Close generic signatures, constructor witnesses and symbolic dependent constraints through the actual construction or call; preserve the declared meaning of each forwarded parameter and alias projection.

### MIST-014

Extension receiver member bounds must have the same proof outcome as equivalent declaring-instance bounds. adhoc-refinement-constraints requirements6,8–9 and refinement-types13.8 (requirements.md:208) read the member signature/lift, never its body at the call.

**Current evidence:** source inspection at current baseline

Requirement.Symbol recognizes MemberAccess receivers null/This; it does not recognize the extension receiver local identity. No fresh CLI execution claimed.

**Owning source and existing seams:**

- `Source/Mist/ProgramAnalysis/Refinements/Requirement.cs` — Symbol (audited line 204)
- `Source/Mist/ProgramAnalysis/Rules/RefinementAnalyzer.cs` — Proving.NeedArguments (audited line 231)

**Acceptance and controls:**

- For extension(Tally self), Pick(int n) where n>=0 and <self.Count with Count lifting to3: admit2, reject7 as violating, keep a Count with no fixed value UNPROVEN. Equivalent this.Count instance controls must remain unchanged.
- Receiver identity must distinguish the extension receiver from captured or other-parameter receivers; do not accept bounds merely because a local has the same name.

### MIST-016

Bind another parameter/dependent value in a where bound and substitute its corresponding call or constructed argument. adhoc-refinement-constraints requirements6,8–9; dependent-types dependent value identity; preserve conservative unproven outcomes.

**Current evidence:** source inspection at current baseline

Requirement can name local/type-parameter symbols, but call proof does not carry every corresponding argument knowledge; exact other-parameter CLI fixture not rerun.

**Owning source and existing seams:**

- `Source/Mist/ProgramModel/Construction/Binding/Expressions/PatternBinder.cs` — symbolic bound binding
- `Source/Mist/ProgramModel/Construction/Resolution/TypeParameterDomainResolver.cs` — Resolve and ProjectValues (audited line 84)
- `Source/Mist/ProgramAnalysis/Refinements/Requirement.cs` — Symbol and bound Read (audited line 204)
- `Source/Mist/ProgramAnalysis/Rules/RefinementAnalyzer.cs` — NeedArguments (audited line 231)

**Acceptance and controls:**

- Take(int lo,int hi) where lo<hi admits literal(2,3), rejects(5,3), leaves unknown hi UNPROVEN. Source diagnostics must locate the written argument/bound, with no derivative Roslyn failure.
- A bound naming a closed dependent N reads the value of that construction, including forwarding through aliases; sibling parameters with equal names remain distinct. Do not introduce general relational/SAT solving.

### MIST-019

dependent-types requirement7.5 (requirements.md:131) requires an alias-declared dependent name to project its supplied value, even when the target declaration names that position differently.

**Current evidence:** executed CLI at current baseline

MIST_PM_UNRESOLVED_MEMBER: Box<7> has no member named M.

```mist
using Standard;
namespace Probe;
public class Box<N:int> {} type Alias<M:int> = Box<M>; public class Program { public static void Main() { Console.WriteLine(Alias<7>.M); } }
```

**Owning source and existing seams:**

- `Source/Mist/ProgramModel/Construction/Types/TypeAliasBuilder.cs` — alias construction
- `Source/Mist/ProgramModel/Construction/Resolution/TypeParameterDomainResolver.cs` — ProjectValues (audited line 84)
- `Source/Mist/ProgramModel/Model/Scopes/TypeMemberLookup.cs` — qualified static dependent projection lookup

**Acceptance and controls:**

- class Box<N:int>{} type Alias<M:int>=Box<M>; Alias<7>.M evaluates7. A nested forwarding alias must preserve the alias-declared name and domain.
- Existing Box<7>.N and inherited class projections remain correct; ordinary type parameters/packs project no value; instance access and writes to the get-only static projection remain refused. Alias expansion must preserve underlying nominal identity.

### MIST-021

generic-constraints requirement8.4 (requirements.md:170) requires an equivalent factory lowering for parameterized constructor constraints. The factory selected for a closed generic type must respect that type constructor’s own substitution.

**Current evidence:** executed CLI at current baseline

MIST_CS_ERROR: Argument1 cannot convert int to X.

```mist
using Standard;
namespace Probe;
public class Crate<X> { public X Held; public Crate(X held) { Held=held; } } public class Factory<T> where T:new(int) { public T Make() { return new T(7); } } public class Program { public static void Main() { Console.WriteLine(new Factory<Crate<int>>().Make().Held); } }
```

**Owning source and existing seams:**

- `Source/Mist/Backends/CSharpTranspiler/Lowering/ConstructorConstraintInterfaces.cs` — Closed; DeclaredParameters (audited line 62)
- `Source/Mist/Backends/CSharpTranspiler/Emitter/TypeEmitter.cs` — factory Parameters(signature) (audited line 426)
- `Source/Mist/Backends/CSharpTranspiler/Emitter/TypeEmitter.cs` — ConstructorConstraintInterfaces.Closed (audited line 472)
- `Source/Mist/Backends/CSharpTranspiler/Emitter/CSharpEmitterV2.cs` — EmitConstructorConstraintInterfaces (audited line 669)

**Acceptance and controls:**

- Factory<Crate<int>> where T:new(int), Crate<X>(X held), new T(7) builds and Held equals7. Exercise new(A), mixed open/closed parameters, and two distinct instantiations of the same generic definition without duplicate or misclosed factories.
- Missing, private or mismatched constructors remain source refusals; preserve already-correct open constructor-interface arguments and ordinary new() lowering. Do not add support for currently unsupported foreign witness emission.

### MIST-028

variadics requirement11.1–3 (requirements.md:211–213): every pack element satisfies every declared constraint, with failed positions identified. Forwarded packs/pack elements must entail nominal/kind requirements as ordinary forwarded parameters do.

**Current evidence:** source inspection at current baseline

Judged filters pack and PackElementType constraints to Stampable; Arguments filters TypeParameter/SpreadType positions for other constraint kinds.

**Owning source and existing seams:**

- `Source/Mist/ProgramAnalysis/Rules/ConstraintAnalyzer.cs` — Judged pack forwarding stampable-only filter (audited line 54)
- `Source/Mist/ProgramAnalysis/Rules/ConstraintAnalyzer.cs` — Arguments nonstampable pack element filtering (audited line 115)
- `Source/Mist/ProgramAnalysis/Rules/ConstraintEntailment.cs` — Decide

**Acceptance and controls:**

- Admit forwarding where the pack or projected element declares all required nominal/kind constraints; reject absent class/struct/interface/base/constructor constraints and conjunctive partial satisfaction. Preserve stampability and keyword lattice semantics.
- Exercise empty packs vacuously, one/many elements, explicit and forwarded positions, and constructed bounds substituted before comparison. Failed bound/type trees remain silent under existing diagnostic ownership.

### MIST-011

associated-types requirement4.3–4 (requirements.md:78–79; modern abstract terminology supersedes old trait/interface spelling) requires concrete associated members in answering signatures. Generic fillers cannot demand capabilities absent from the slot contract; exact constraint equality is not established as a blanket Mist policy.

**Current evidence:** source inspection at current baseline

Answers checks method arity, passing and substituted signature types but no generic constraint contract; abstract-associated parameters remain indeterminate/wildcards.

**Owning source and existing seams:**

- `Source/Mist/ProgramAnalysis/SlotSignatures.cs` — Answers; Closing; IsIndeterminate (audited line 41)
- `Source/Mist/ProgramAnalysis/SlotSignatures.cs` — abstract-declared parameter wildcard (audited line 200)
- `Source/Mist/ProgramAnalysis/Rules/ConformanceAnalyzer.cs` — CheckSlots (audited line 78)

**Acceptance and controls:**

- With associated Element=string, a string Get answer is accepted and int Get refused; nested List<Element>, parameters/returns, inheritance and independently named method type parameters close exactly. Unresolved erroneous slots retain existing cascade suppression.
- Generic answer requiring a stronger constraint than its slot must not admit calls allowed by the slot; equivalent renamed constraints and contract-compatible weaker answers are controls. Use existing entailment/substitution. Do not invent blanket textual/equal-list matching; if a remaining relationship is genuinely unspecified, record that narrow residue rather than silently deciding it.

## Implementation and independence

**Independence:**

One owner owns symbolic PatternBinder sites, TypeParameterDomainResolver, alias/dependent projection identity, Requirement bound substitution, ConstraintAnalyzer/ConstraintEntailment, associated-slot signature closing and constructor witness registry/emission. Proof packet owns finite predicate/switch and direct-call length facts, not these symbolic substitutions. Default constructor packet owns StorageDefaults/DefaultSynthesis/DefaultWitnesses.EmitInitializers/DA and constructor access; this packet must preserve those APIs and only change witness signature closing. Expression packet owns overload trial selection; this packet consumes selected call/parameter identity and may make narrowly necessary compatible helper edits without changing overload policy.

**Excluded work:**

- No generic local functions, generic optional-context key redesign, broad solver domains, default-entry policy, alias grammar expansion, foreign factory feature expansion, new abstract/refinement filler policy beyond accepted contracts.
- No broad MIST-012 pattern containment work or fixed-arity spread-length proof task owned elsewhere.

## Focused verification

**Existing regression anchors:**

- Test/Mist.Tests/ProgramAnalysis/ConstraintEntailmentTests.cs:261 ConstructorSignatureIsComparedAfterTheClosingSubstitution
- Test/Mist.Tests/ProgramAnalysis/ConstraintEntailmentTests.cs:190 EachRequirementIsJudgedOnItsOwn
- Test/Mist.Tests/Backend/ConstructorConstraintInterfaceEmissionTests.cs:63,74,86,96 existing open-position and factory dedup controls
- Test/Mist.Tests/Semantics/DependentValueLookupTests.cs:22,49,76,115 static/inherited/shadowed/get-only controls
- Test/Mist.Tests/Semantics/DependentValueReadTests.cs
- Test/Mist.Tests/Backend/DependentValueEmissionTests.cs
- Test/Mist.Tests/ProgramAnalysis/ConformancePackSlotTests.cs:23,39,49 expanded slot conformance controls
- Test/Mist.Tests/ProgramAnalysis/FlattenedArgumentRefinementTests.cs:28,40,46 argument identity/refinement controls

Tier1 hand-built entailment, alias projection and substitution decisions; Tier2 binder/conformance and factory emission slices. Add only small existing runtime-boundary demonstrations for end-to-end alias7 and Crate7; do not copy ProgramModelProbe full pipeline into new unit tests.

**Initial focused commands** (extend filters to include your new test classes; these do not replace final gates):

- `tools/test-ledger/run.sh Test/Mist.Tests --filter "FullyQualifiedName~ConstraintEntailmentTests|FullyQualifiedName~ConstructorConstraintInterfaceEmissionTests|FullyQualifiedName~DependentValue|FullyQualifiedName~ConformancePackSlotTests|FullyQualifiedName~FlattenedArgumentRefinementTests"`

For inline source probes, create a fresh scratch project with this `mist.toml` and one reproduction as `Program.mist`:

```toml
name = "Probe"
namespace_root = "Probe"
```

After a current solution build, run `dotnet run --project Source/Mist.CLI --no-build -- run <scratch-project> --quiet` from the Mist repository. Scratch directories and generated `out/` are not tracked deliverables. For artifact fidelity, also build that scratch project’s `out/Probe.csproj` and inspect all diagnostics. Snippets inside prose are component cases to construct in existing test seams, not claims that an incomplete fragment is a runnable program.

Tracking records owned by this packet: `MIST-014`, `MIST-016`, `MIST-019`, `MIST-021`, `MIST-028`, `MIST-011`.

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
