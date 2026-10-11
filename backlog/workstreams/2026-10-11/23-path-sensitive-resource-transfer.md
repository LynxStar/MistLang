# 23 — Path-sensitive resource transfer

Complete this independent bundle, including verification, actual backlog updates, commits and publication. Repository: `https://github.com/LynxStar/MistLang.git`. Target: remote `main`. Suggested model tier: **Sol/Opus**, effort: **high**. The finite scope shares one ownership/lifetime plan and emitter realization; parameters, fields, scopes and exceptional argument evaluation require coordinated path-sensitive reasoning and source identity rather than independent patches. These portable labels are guidance, not API identifiers or a requirement to launch a separate review.

## Starting point

Planning baseline: `7a14a1a7648c30c40b0e83da932255558dea64de` (2026-10-11). Executed compiler probes and the clean solution build used `8390675bd27f8e683c50664a94b4bc728daa7007`; the intervening published changes are publishing scripts/README and four backlog evidence corrections, with no compiler or test changes. Storm pin: `b4e5853ccca45623e59fcdb9ee0afd6e90e836ce`, also verified as Storm remote main during planning. The solution build passed with zero warnings/errors; no fresh full-suite pass is claimed. Each item below distinguishes executed behavior from source inspection or an unverified lead.

Start from current remote main in an isolated task checkout; retain your existing isolated checkout and completed work when continuing. Follow the repository’s applicable instructions. Run `git submodule update --init --recursive` and verify checked-out commits match the revision’s recorded pins; a setup hook alone is not evidence. Revalidate every item against current main. If already fixed, verify acceptance and finish any real tracking gap without duplicate implementation. Do not read or port retired classic implementation from Git history. `.kiro/specs/**/tasks.md` documents are implemented and excluded from work discovery.

## Outcome and included obligations

Give each accepted custom-linear resource exactly one release or ownership transfer on every reachable path, including parameter/field cleanup, with scopes and additional binding forms.

### MIST-024

drop-semantics requirements4.1–7,9.4–6,10.7–8 and14.3 require per-path elision, accepted owning parameter cleanup, remaining field cleanup and exception-safe release.

**Current evidence:** executed CLI at current baseline

Explicit-owned parameter calls false Token 1 and true Token 2 print only2, omitting1. Custom linear Holder with conditional Inner.Drop and false branch prints no field drop. No plain-parameter policy inference.

```mist
using Standard;
namespace Probe;
public linear class Token { public int Id; public Token(int id) { Id=id; } public void Drop(){Console.WriteLine(Id);} } public class Program { public static void Use(owned Token t, bool take){ if(take){t.Drop();} } public static void Main(){Use(new Token(1),false); Use(new Token(2),true);} }
```

```mist
using Standard;
namespace Probe;
public linear class Token { public int Id; public Token(int id) { Id=id; } public void Drop(){Console.WriteLine(Id);} } public linear class Holder{Token Inner; bool Take; public Holder(int id,bool take){Inner=new Token(id);Take=take;} public void Drop(){if(Take){Inner.Drop();}}} public class Program{public static void Main(){var h=new Holder(1,false);}}
```

**Owning source and existing seams:**

- `Source/Mist/Backends/CSharpTranspiler/Lowering/DropInsertion.cs` — FieldsOwedBy whole-subtree Moves (audited line 163)
- `Source/Mist/Backends/CSharpTranspiler/Lowering/DropInsertion.cs` — DroppedParameters and MovesParameter (audited line 302)
- `Source/Mist/ProgramAnalysis/Flow/LocalLifetimes.cs` — Plan; Changes
- `Source/Mist/Backends/CSharpTranspiler/Emitter/BodyEmitter.cs` — EmitParameterDrops (audited line 147)

**Acceptance and controls:**

- Use(owned Token t,bool take){if(take)t.Drop();}: false call drops token1 once, true call token2 once. Include early return, local destination declaration/reassignment, conditional move and shadowed names using actual binding/field identity.
- Manual Drop with if(Take)Inner.Drop() must release Inner on the false path and never twice on true. Preserve declaration-order field drops and reverse-order local/parameter nesting; synthesized/manual Drop precedence remains unchanged.
- Readonly-ref/frozen/borrowed leases keep the owner responsible and receive no release themselves. Relevant references retain idempotent release, unrestricted/affine scopes retain existing rules.

### MIST-025

drop-semantics5.1–7 (requirements.md:95–101): a linear with satisfies the enclosing obligation, retains ambient push/pop and pops before dropping.

**Current evidence:** executed CLI at current baseline

1
1

```mist
using Standard;
namespace Probe;
public linear class Token { public int Id; public Token(int id) { Id=id; } public void Drop(){Console.WriteLine(Id);} } public class Program { public static void Main(){var t=new Token(1); with(t){} } }
```

**Owning source and existing seams:**

- `Source/Mist/ProgramAnalysis/Flow/LocalLifetimes.cs` — Changes has no WithBlockStatement transfer (audited line 165)
- `Source/Mist/Backends/CSharpTranspiler/Emitter/BodyEmitter.cs` — EmitWithScope (audited line 752)

**Acceptance and controls:**

- var t=new Token(1);with(t){} prints 1 exactly once; conditional with on only one branch releases on both routes once. With inner resources releases them before the with resource.
- All accepted with forms retain key identity, ambient restoration, pop-before-Drop and exception cleanup. Preserve unrestricted/affine with release and non-with discipline. Use-after-move diagnostic behavior must agree with existing accepted ownership contract.

### MIST-076

drop-semantics4.1–3,9.5–6 and14.3 establish cleanup until an actual transfer reaches the callee; argument/receiver failures cannot make a resource disappear before callee entry.

**Current evidence:** current source inspection plus inconclusive scratch probes

Settled falls back to handed expression when the final throwing argument lacks natural type; receiver/callee exclusion remains. Fresh receiver fixture was refused CAST_HAS_NO_CONVERSION; fresh collection fixture was refused UNSUPPORTED_EXPRESSION. These do not prove a runtime leak. Canonical status remains unverified; validate before implementation.

**Owning source and existing seams:**

- `Source/Mist/ProgramAnalysis/Flow/LocalLifetimes.cs` — Settled; HasNaturalType; MayThrow (audited line 224)
- `Source/Mist/Backends/CSharpTranspiler/Lowering/LifetimeLowering.cs` — Open ownership handoff helpers (audited line 102)
- `Source/Mist/Backends/CSharpTranspiler/Emitter/BodyEmitter.cs` — EmitExpression ownership marks (audited line 969)

**Acceptance and controls:**

- First validate null-receiver failure and trailing lambda/collection expression routes with accepted bound models and deterministic later evaluation failure. For a confirmed route, caller releases once when call never enters; callee owns once after successful entry, including a callee that throws.
- Preserve evaluation order, receiver/argument single evaluation, overload target types, ref arguments, closure capture semantics and natural typed trailing-argument existing controls. If a route is unreachable/refused or already correct, close that narrow lead with concrete evidence. Do not manufacture new source semantics to trigger it.

### MIST-078

drop-semantics4,6.2,13.1–5 and14.3 establish exactly-once scope cleanup for accepted bindings; verify each inventoried form and its already-established ownership contract.

**Current evidence:** executed CLI for for initializer; source-only other forms

Token 1 is never released on break. This upgrades that finite form, not all six inventoried routes.

body

```mist
using Standard;
namespace Probe;
public linear class Token { public int Id; public Token(int id) { Id=id; } public void Drop(){Console.WriteLine(Id);} } public class Program { public static void Main(){for(var t=new Token(1); t.Id<2; ){Console.WriteLine("body"); break;} } }
```

**Owning source and existing seams:**

- `Source/Mist/ProgramAnalysis/Flow/LocalLifetimes.cs` — IsTracked excludes ref struct/task; Declared scans block LocalDeclarationStatement (audited line 44)
- `Source/Mist/ProgramAnalysis/Flow/LocalLifetimes.cs` — Declared (audited line 107)
- `Source/Mist/Backends/CSharpTranspiler/Emitter/BodyEmitter.cs` — EmitForEach (audited line 851)
- `Source/Mist/Backends/CSharpTranspiler/Emitter/BodyEmitter.cs` — EmitFor (audited line 917)

**Acceptance and controls:**

- Accepted for(var t=new Token(1);t.Id<2;){WriteLine("body");break;} must print body then1. Also initializer created with zero iterations, normal termination, early return and exception must release once; body-local resources still release every iteration.
- Finite validation inventory: linear ref structs; foreach bindings; pattern bindings; deconstruction bindings; with bindings. Establish grammar admission and whether each binding owns or borrows before changing it. Add per-form positive/negative controls and close forms already correct. Preserve no-boxing/ref-safety for ref structs and avoid generic wrapper strategies that cannot carry them.
- Mist has no continue statement; do not add syntax or tests for continue. No leak is claimed for all remaining forms merely because Declared omits them.

### Required MIST-078 dispositions

- **linear ref structs:** Excluded by LocalLifetimes.IsTracked; no current executed leak claim. Build an accepted custom linear ref-struct owning binding component fixture; establish obligation from drop/ref-struct contracts. Fix path-sensitive cleanup if it leaks, or record the exact current correct/refused/borrowed route with a control. Preserve no-boxing/ref escape rules; no new ref-struct language feature.
- **for initializer:** Confirmed fresh leak: for_linear prints body without Token 1 release. Fix initializer scope release on zero iterations, normal termination, break, return and exception; distinguish body locals released per iteration. The lifetime owner owns release scaffolding; local declaration packet17 owns type/stampability/keyword validation, with no dependency on its new API.
- **foreach binding:** Outside block-local declaration inventory; ownership not inferred merely from omission. Validate accepted iteration over existing resource protocol; establish whether current binding owns/transfers or borrows under current contract. Fix exactly-once release for owning binding; borrowed binding must not create a release or move. Preserve iterator disposal/protocol unchanged. Keep an explicit policy residue if ownership is not established, rather than invent it.
- **pattern binding:** Outside block-local declaration inventory; no all-pattern leak claim. Validate accepted owning and nonowning pattern forms and alias identity. Owning binding gets the established obligation and path cleanup; a borrowed view receives no independent drop. No new extraction/transfer semantics or type-pattern containment expansion.
- **deconstruction binding:** Outside block-local declaration inventory; no current executed leak claim. Validate accepted deconstruction with a custom-linear element and ordinary nonowning elements. Transfer each owning element according to existing contract; release surviving owners on all exits. Do not invent tuple-container ownership, borrow or copy policy.
- **with binding:** with(t) confirmed duplicate release; declaration-with variants require per-form controls. Fix confirmed scoped handoff; validate with(expr), with var, with Type name, discard and expression forms already in grammar. Each must retain context restoration and pop-before-Drop, avoid enclosing duplicate cleanup, and preserve nonowning/affine/relevant behavior.

Finish validation and the accepted-contract fixes for all six forms. Do not close the whole record after fixing only for initializers or with. Preserve a precisely evidenced policy residue where ownership is genuinely unspecified.

## Implementation and independence

**Independence:**

This owner owns lifetime plans, DropInsertion, LifetimeLowering, resource binding/transfer sites and their release emission. Preserve existing ReleasedBindings/NeedsDrop/TaskResult/AsyncPromotion contracts: custom-linear additions must not change Task/ValueTask promotion, blocking getter/constructor policy or async slot dispatch. PurityJudge exit-drop consumers must read the same reachable releases. Shared BodyEmitter sites are limited to release scaffolding and affected binding constructs; structural packet owns carrier/type-test conversion branches. Expression packet owns overload selection and target binding: handoff lowering must consume existing bound targets, not change overload APIs. Context key/value representation is unchanged. Packet17 owns local admission, explicit stampability and declaration diagnostics; this owner may adapt existing bound local/binding release metadata but cannot make packet17 API changes a prerequisite. Keep binder scope discovery compatibility in either landing order.

**Excluded work:**

- Plain unannotated parameter transfer policy is explicitly excluded. No new ownership policy for containers, patterns or foreach; only accepted obligations.
- No async promotion redesign, Task/ValueTask lifecycle expansion, implicit owned-field Clone MIST-094, context key specialization, continue syntax, cleanup-on-abort guarantee or general affine/relevant redesign.

## Focused verification

**Existing regression anchors:**

- Test/Mist.Tests/ProgramAnalysis/LocalLifetimeTests.cs (Tier2):76 branch Drop,98–133 throwing handoff,146 leases,181 loop movement,236 lambda independence,248 tracked disciplines
- Test/Mist.Tests/Backend/LocalLifetimeEmissionTests.cs (Tier2):164 trailing throwing arguments,205 reverse order,254 loop break,288 leases,376 Task completion compatibility
- Test/Mist.Tests/ProgramAnalysis/ExitDropReachabilityTests.cs (Tier2) reachable exit effects
- Test/Mist.Tests/Backend/DropInsertionTests.cs
- Test/Mist.Tests/Helpers/LifetimeFragments.cs
- samples/DropSemantics/Program.mist
- samples/SubstructuralFieldDiscipline/Program.mist

Tier2 hand-built flow/lifetime and emission fragments own most new branches. Use actual binding identity to test shadowing. Add bounded runtime boundary cases for branch counters, with, for initializer and validated exceptional handoff; do not create full-compiler tests for every lattice decision.

**Initial focused commands** (extend filters to include your new test classes; these do not replace final gates):

- `tools/test-ledger/run.sh Test/Mist.Tests --filter "FullyQualifiedName~LocalLifetime|FullyQualifiedName~DropInsertionTests|FullyQualifiedName~ExitDropReachabilityTests"`

For inline source probes, create a fresh scratch project with this `mist.toml` and one reproduction as `Program.mist`:

```toml
name = "Probe"
namespace_root = "Probe"
```

After a current solution build, run `dotnet run --project Source/Mist.CLI --no-build -- run <scratch-project> --quiet` from the Mist repository. Scratch directories and generated `out/` are not tracked deliverables. For artifact fidelity, also build that scratch project’s `out/Probe.csproj` and inspect all diagnostics. Snippets inside prose are component cases to construct in existing test seams, not claims that an incomplete fragment is a runnable program.

Tracking records owned by this packet: `MIST-024`, `MIST-025`, `MIST-076`, `MIST-078`.

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
