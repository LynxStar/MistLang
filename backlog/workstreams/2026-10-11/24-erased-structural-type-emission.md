# 24 — Erased structural type emission

Complete this independent bundle, including verification, actual backlog updates, commits and publication. Repository: `https://github.com/LynxStar/MistLang.git`. Target: remote `main`. Suggested model tier: **Sol/Opus**, effort: **high**. One owner controls semantic identity versus CLR representation, carrier naming/conversion/accessors, nested ValueTuple shape and ordinary nullable spelling. Fixes must preserve all consumers and generated-artifact behavior without changing the runtime language contract. These portable labels are guidance, not API identifiers or a requirement to launch a separate review.

## Starting point

Planning baseline: `7a14a1a7648c30c40b0e83da932255558dea64de` (2026-10-11). Executed compiler probes and the clean solution build used `8390675bd27f8e683c50664a94b4bc728daa7007`; the intervening published changes are publishing scripts/README and four backlog evidence corrections, with no compiler or test changes. Storm pin: `b4e5853ccca45623e59fcdb9ee0afd6e90e836ce`, also verified as Storm remote main during planning. The solution build passed with zero warnings/errors; no fresh full-suite pass is claimed. Each item below distinguishes executed behavior from source inspection or an unverified lead.

Start from current remote main in an isolated task checkout; retain your existing isolated checkout and completed work when continuing. Follow the repository’s applicable instructions. Run `git submodule update --init --recursive` and verify checked-out commits match the revision’s recorded pins; a setup hook alone is not evidence. Revalidate every item against current main. If already fixed, verify acceptance and finish any real tracking gap without duplicate implementation. Do not read or port retired classic implementation from Git history. `.kiro/specs/**/tasks.md` documents are implemented and excluded from work discovery.

## Outcome and included obligations

Emit valid C# for semantically distinct sized-array union constituents, tuples above the CLR seven-element boundary and optional reference values under the generated nullable context.

### MIST-031

type-unions requirement9.1–6 (requirements.md:132–137; not requirement10) requires a unique discriminated carrier with recoverable constituents and order-independent reuse. The source implicit assignment contract must work when distinct constituents erase to the same CLR type; C# duplicate conversion operators are not a valid realization.

**Current evidence:** executed CLI confirmation plus current source inspection

An executed audit probe reconfirmed duplicate members/operators for sized union at the audited compiler revision. UnionCarrierNames ignores length; EmitCarrierStruct emits a member and conversion per semantic constituent despite identical C# int[] signatures.

```mist
using Standard; namespace Probe; public class Program { public static void Main() { int[3] | int[5] value = new int[3]; Console.WriteLine("made"); } }
```

**Owning source and existing seams:**

- `Source/Mist/Backends/CSharpTranspiler/Lowering/UnionCarrierNames.cs` — ConstituentName erases array length and Lifted value (audited line 35)
- `Source/Mist/Backends/CSharpTranspiler/Emitter/CSharpEmitterV2.cs` — EmitCarrierStruct (audited line 564)
- `Source/Mist/Backends/CSharpTranspiler/Emitter/BodyEmitter.cs` — union assignment/conversion and narrowing consumers

**Acceptance and controls:**

- int[3]|int[5] emits one valid carrier, stores/retrieves the correct constituent and narrows through existing type-pattern rules; assignment of each stated constituent builds/runs. Reverse constituent order and repeated aliases reuse the same carrier.
- Two different union sets containing lengths must receive distinct deterministic names. Accessor/member identifiers and tags are consistent with construction, retrieval and widening. Keep ordinary int|string unions and optionals working.
- Changing member names alone is insufficient: duplicate implicit operators from int[] must be replaced with an equivalent source-assignment lowering using the selected constituent identity. Do not add runtime tag-selection semantics for unstated-length int[] values or new union narrowing policy.

### MIST-043

value-tuples requirement1.1 (requirements.md:28 n>=2),3.3 and6.1–7 require supported arbitrary tuple arities and positional access; type patterns must emit actual CLR ValueTuple Rest nesting while preserving Mist element positions.

**Current evidence:** executed CLI at current baseline

MIST_CS_ERROR: non-generic ValueTuple cannot be used with type arguments.

```mist
using Standard;
namespace Probe;
public class Program { public static void Main() { unknown box=(1,2,3,4,5,6,7,8,"x"); if(box is (int,int,int,int,int,int,int,int,string) back) { Console.WriteLine(back.Item9); } } }
```

**Owning source and existing seams:**

- `Source/Mist/Backends/CSharpTranspiler/Emitter/TypeNameEmitter.cs` — EmitAsPatternType flat System.ValueTuple (audited line 157)
- `Source/Mist/Backends/CSharpTranspiler/Emitter/ErasedShape.cs` — Members flat ItemN field paths (audited line 23)

**Acceptance and controls:**

- Nine-element unknown tuple type test builds and Item9 equals x. Exercise7,8,9,14,15 elements, named/nested tuples, is and switch tests, and a mismatch that does not match. Preserve ordinary tuple construction, access and deconstruction.
- When an already-supported nested sized fact occurs beyond element7, the erased-shape fact follows the real Rest path. Preserve existing unsupported sequence-element sized-test refusal and optional null handling; no broad pattern-containment or refined-on-refined runtime semantic expansion.

### MIST-027

Generated ProjectFileEmitter enables Nullable; optional-types requirement7.1 uses null for reference absence. Keep that CLR representation and add correct ordinary C# nullable annotations, syncing outdated bare-T/no-nullable-context implementation wording. Repo CLAUDE.md:95–100 requires zero warnings. This is annotation/artifact fidelity, not a new optional representation policy.

**Current evidence:** executed CLI and generated MSBuild at current baseline

none

Build succeeds with CS8618 on Holder.note plus separate CS0649. Both captured; CS8618 is the confirmed optional annotation defect.

```mist
using Standard;
namespace Probe;
public class Holder { private string? note; public Holder() {} public string Show() { return note ?? "none"; } } public class Program { public static void Main() { Console.WriteLine(new Holder().Show()); } }
```

**Owning source and existing seams:**

- `Source/Mist/Backends/CSharpTranspiler/Emitter/TypeNameEmitter.cs` — EmitOptional stale no-context premise (audited line 189)
- `Source/Mist/Backends/CSharpTranspiler/Emitter/FieldEmitter.cs` — Emit type spelling (audited line 46)
- `Source/Mist/Backends/CSharpTranspiler/Emitter/ProjectFileEmitter.cs` — Nullable enable (audited line 29)
- `.kiro/specs/optional-types/requirements.md` — Requirement7.1 reference null representation (audited line 115)

**Acceptance and controls:**

- Valid absent string? field retains runtime none behavior and emits a nullable reference spelling so generated-project build has no CS8618. Use an explicitly assigned void/null control to avoid conflating unrelated CS0649 never-assigned warning with this obligation.
- Cover fields/parameters/returns/properties and constructed reference optionals; value/struct-constrained optionals remain Nullable<T>. Preserve ordinary nonoptional reference spelling, ?. and ?? runtime behavior.
- Context-key marker identity stays Optional<T> as currently defined, never a nullable annotation as a key discriminator. Unconstrained optional T representation policy remains outside scope; do not fix it by treating unconstrained T as always reference.

## Implementation and independence

**Independence:**

Own UnionCarrierNames plus all selected carrier conversion/accessor call sites, TypeNameEmitter ordinary optional and tuple pattern spelling, ErasedShape tuple Rest paths and generated artifact controls. Preserve constructor-factory and dependent-property emitters’ TypeNameEmitter contracts; generic packet may use the unchanged public Emit API, no planned new API dependency. Proof packet owns refinement/index knowledge, not physical carrier names or CLR tuple nesting. Expression packet owns switch-result typing and candidate trials, so only the specific type-test emission/representation consumers may be edited here. If shared helper cleanup is necessary it must be compatible for either landing order.

**Excluded work:**

- No MIST-012 broad switch containment, refined-on-refined predicates, unknown union carrier unwrapping policy, new runtime selection for erased unstated-length arrays, Span-union storage design or scalar witness representation changes.
- No generic optional-context key metadata redesign, unconstrained optional-type representation choice, unrelated native warning suppression, wide tuple overload/result typing or parser grammar changes.

## Focused verification

**Existing regression anchors:**

- Test/Mist.Tests/Backend/ErasedShapeTests.cs:38,44,61,75,81 (Tier1 fact and intentional refusal controls)
- Test/Mist.Tests/Backend/SizedSequencePatternEmissionTests.cs (Tier2)
- Test/Mist.Tests/Backend/ClrValueTupleTests.cs:26 Cardinality_FlattensTheRestAndRefusesWhatIsNotATuple
- Test/Mist.Tests/Semantics/TupleTypeIdentityTests.cs
- Test/Mist.Tests/Integration/SizedSequenceRuntimeBoundaryTests.cs (Tier4 existing runtime seam)
- Test/Mist.Tests/Backend/OptionalOverTypeParameterEmissionTests.cs:59,78,93,106 existing legacy struct/class/unconstrained/closed controls; update outdated bare-reference expectation while preserving unconstrained-residue exclusion
- Test/Mist.Tests/Backend/UnionCarrierOpenTypeTests.cs existing open constituent carrier controls

Tier1 semantic/CLR shape naming and tuple Rest mapping; Tier2 hand-built carrier/conversion/type-test/optional spelling emission; bounded existing Tier4 runtime boundary covers constituent identity and wide tuple testing. One generated-project MSBuild boundary verifies nullable context; do not add full compiler setup to per-name tests.

**Initial focused commands** (extend filters to include your new test classes; these do not replace final gates):

- `tools/test-ledger/run.sh Test/Mist.Tests --filter "FullyQualifiedName~ErasedShapeTests|FullyQualifiedName~SizedSequencePatternEmissionTests|FullyQualifiedName~ClrValueTupleTests|FullyQualifiedName~OptionalOverTypeParameterEmissionTests|FullyQualifiedName~SizedSequenceRuntimeBoundaryTests"`

For inline source probes, create a fresh scratch project with this `mist.toml` and one reproduction as `Program.mist`:

```toml
name = "Probe"
namespace_root = "Probe"
```

After a current solution build, run `dotnet run --project Source/Mist.CLI --no-build -- run <scratch-project> --quiet` from the Mist repository. Scratch directories and generated `out/` are not tracked deliverables. For artifact fidelity, also build that scratch project’s `out/Probe.csproj` and inspect all diagnostics. Snippets inside prose are component cases to construct in existing test seams, not claims that an incomplete fragment is a runnable program.

Tracking records owned by this packet: `MIST-031`, `MIST-043`, `MIST-027`.

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
