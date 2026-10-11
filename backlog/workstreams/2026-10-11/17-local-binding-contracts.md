# 17 — Local binding contracts

Complete this independent bundle, including verification, actual backlog updates, commits and publication. Repository: `https://github.com/LynxStar/MistLang.git`. Target: remote `main`. Suggested model tier: **Sol/Opus**, effort: **high**. Several local forms share registration and initializer plumbing; source origins, hidden discard identity, failure provenance and existing cleanup consumers must agree. Established contracts do not require a language decision. These portable labels are guidance, not API identifiers or a requirement to launch a separate review.

## Starting point

Planning baseline: `7a14a1a7648c30c40b0e83da932255558dea64de` (2026-10-11). Executed compiler probes and the clean solution build used `8390675bd27f8e683c50664a94b4bc728daa7007`; the intervening published changes are publishing scripts/README and four backlog evidence corrections, with no compiler or test changes. Storm pin: `b4e5853ccca45623e59fcdb9ee0afd6e90e836ce`, also verified as Storm remote main during planning. The solution build passed with zero warnings/errors; no fresh full-suite pass is claimed. Each item below distinguishes executed behavior from source inspection or an unverified lead.

Start from current remote main in an isolated task checkout; retain your existing isolated checkout and completed work when continuing. Follow the repository’s applicable instructions. Run `git submodule update --init --recursive` and verify checked-out commits match the revision’s recorded pins; a setup hook alone is not evidence. Revalidate every item against current main. If already fixed, verify acceptance and finish any real tracking gap without duplicate implementation. Do not read or port retired classic implementation from Git history. `.kiro/specs/**/tasks.md` documents are implemented and excluded from work discovery.

## Outcome and included obligations

Complete declaration identity, contextual initialization, lexical scope and diagnostic ownership for local bindings without changing language shadowing or generic-local support.

### MIST-036

Report duplicate local declarations at the second declaration token/site with the originating source path instead of default Location and <unknown>. Thread the declaration origin once through all actual registration paths rather than patching one syntax form.

**Current evidence:** Fresh baseline CLI: var _ = 1; var _ = 2; and foreach iterator/body conflict each produce an unlocated PM_DUPLICATE_LOCAL. BodyScope.DeclareLocal constructs that unlocated Diagnostic directly.

**Owning source and existing seams:**

- Source/Mist/ProgramModel/Model/Scopes/BodyScope.cs::DeclareLocal
- Source/Mist/ProgramModel/Construction/Binding/StatementBinder.cs::BindVariableDeclaration, BindForInitializer, BindForEach
- Source/Mist/ProgramModel/Construction/Binding/BodyBinder.cs::BindDelegate, ParameterLocal
- Source/Mist/ProgramModel/Construction/Binding/LocalFunctionBinder.cs::BindBody

**Acceptance and controls:**

- A real repeated name in one scope has exactly one PM_DUPLICATE_LOCAL with correct path, line/column/span at the repeat.
- Cover ordinary typed/inferred declarations, for declarations, parameters and local-function parameter/local collisions at the owning binder seam; cover deconstruction/pattern registrations only where they share this registration route.
- A nested block shadowing the outer local remains admitted; duplicate local-function diagnostics retain their current owning declaration and descriptor.

### MIST-037

A declaration spelled _ evaluates and validates its initializer but introduces no source-visible binding. Cover every local-declaration grammar path admitting DISCARD, including ordinary typed/inferred, for and foreach declarations; preserve established tuple/pattern discard behavior.

**Current evidence:** Fresh baseline repeats var _ and gets a spurious duplicate. StatementBinder creates LocalVariable(Name.Lexeme) unconditionally. Grammar/Mist.storm admits IDENTIFIER | DISCARD in declaration and loop names.

**Owning source and existing seams:**

- Source/Mist/Grammar/Mist.storm::VariableDeclaration, InferredVariableDeclaration, ForInitializer, ForeachStatement
- Source/Mist/ProgramModel/Construction/Binding/StatementBinder.cs
- Source/Mist/ProgramModel/Model/Scopes/BodyScope.cs
- Source/Mist/Backends/CSharpTranspiler/Emitter/BodyEmitter.cs::local and loop statement emission
- Source/Mist/Backends/CSharpTranspiler/Emitter/LocalNames.cs

**Acceptance and controls:**

- Several discards in one scope are admitted and _ does not resolve as a named local.
- Initializer side effects occur once in source order and invalid initializer conversions remain diagnosed.
- Any internal hidden local needed by existing lifetime/lowering machinery remains a distinct semantic identity with no source lookup entry; do not invent a new ownership/disposal policy for ignored values.
- Typed, var, for and foreach discard forms emit compilable C# without conflicting named _ declarations; existing pattern/deconstruction controls stay green.

### MIST-038

Give the foreach body a child lexical scope beneath the readonly iteration binding, consistently with block/while/for bodies.

**Current evidence:** Fresh baseline foreach(var x in [1]) { var x = 2; Console.WriteLine(x); } reports PM_DUPLICATE_LOCAL. BindForEach passes foreachScope itself to BindNestedBlock; BodyScope explicitly permits outer shadowing.

**Owning source and existing seams:**

- Source/Mist/ProgramModel/Construction/Binding/StatementBinder.cs::BindForEach
- Source/Mist/ProgramModel/Construction/Binding/BodyBinder.cs::BindNestedBlock
- Source/Mist/ProgramModel/Model/Scopes/BodyScope.cs
- Source/Mist/Backends/CSharpTranspiler/Emitter/LocalNames.cs

**Acceptance and controls:**

- The body can shadow the iterator and emits distinct C# names; body reads refer to the shadow while the collection and iterator retain their original identities.
- Sibling loops and nested blocks remain isolated, duplicate body declarations still refuse, and iterator reassignment remains readonly-refused.
- Local functions inside the body retain correct hoisting, captures and scope visibility.

### MIST-039

Use the same declared target and conversion/ownership pipeline for a typed for initializer as an ordinary typed local.

**Current evidence:** Fresh baseline for(Thing t = new(); false;) reports PM_TARGET_TYPED_NEW_NO_TARGET. BindForInitializer resolves declaredType but does not PushTargetType or apply declaration conversions.

**Owning source and existing seams:**

- Source/Mist/ProgramModel/Construction/Binding/StatementBinder.cs::BindForInitializer, BindVariableDeclaration
- Source/Mist/ProgramModel/Construction/Binding/BindingContext.cs::PushTargetType
- Source/Mist/ProgramModel/Construction/Binding/ImplicitConversions.cs
- Source/Mist/ProgramModel/Construction/Binding/OwnedProvenance.cs

**Acceptance and controls:**

- for(Thing t = new(); false;) binds as its equivalent typed local; typed array [] and default receive their declared targets too.
- Numeric/user-declared implicit conversions and supported ownership provenance use the ordinary-local contract rather than a second conversion implementation.
- An incompatible initializer still reports at its value; an inferred var initializer has no invented target; unresolved declared type/initializer retains one-root-cause reporting.

### MIST-015

Keep the unsupported generic local-function declaration refusal while suppressing diagnostics derived from its unbound type parameters and calls to the rejected signature.

**Current evidence:** Fresh baseline T Same<T>(T x) { return x; } Same<int>(2); produces LOCAL_FUNCTION_GENERIC, two UNRESOLVED_TYPE and TYPE_ARGUMENT_COUNT_MISMATCH. LocalFunctionBinder.Signature reports rejection and then resolves T in the outer scope.

**Owning source and existing seams:**

- Source/Mist/ProgramModel/Construction/Binding/LocalFunctionBinder.cs::Signature, Declare, BindBody
- Source/Mist/ProgramModel/Construction/Binding/Expressions/PostfixBinder.cs::BindGenericInvocation
- Source/Mist/ProgramModel/Construction/Binding/PriorFailure.cs

**Acceptance and controls:**

- The declaration produces one PM_LOCAL_FUNCTION_GENERIC with correct source ownership; its T occurrences and calls do not add derivative unresolved-type/type-argument complaints.
- Independent real failures in the same body still report; do not silence every expression in a rejected declaration or hide an unrelated missing name.
- Nongeneric local functions, calls before declarations, recursion, captures and permitted nested shadowing retain behavior; generic local functions remain unsupported.

## Contracts and implementation

- .kiro/specs/local-functions/requirements.md::Requirement 3.6 nested shadowing, 3.8 generic refusal; Requirement 4 body scope; Requirement 7.5 C# name mangling
- .kiro/specs/target-typed-new/requirements.md::Requirement 3.2 typed local initializer target, 3.6 inferred var has no target; Requirement 4 constructor checks
- Source/Mist/ProgramModel/Model/Scopes/BodyScope.cs::explicit existing outer-shadowing invariant
- Source/Mist/Grammar/Mist.storm::DISCARD in ordinary and loop local names; _ has no named-binding semantics as existing pattern/context discard contracts establish

Own BodyScope registration, StatementBinder local/loop declaration methods, LocalFunctionBinder rejected-signature provenance and narrowly necessary hidden-local/emission changes. Routine adjacent callers and source-anchor plumbing are included. Preserve existing bound-node and ownership semantics for supported declarations.

**Independence:**

- Expression packet owns expression binder algorithms, argument candidate targeting and expression result types. This packet calls those algorithms with correct typed-declaration targets; it does not change their selection policies.
- Linear packet owns LocalLifetimes/drop/argument handoff. This packet must preserve existing local semantic identities and cleanup consumers, including any hidden discard locals, without redesigning that plan.
- Generic packet owns ordinary type/method generic constraints and constructor witnesses. Unsupported generic local declarations are a distinct diagnostic provenance route.
- Storm packet owns dependency APIs/pin; this packet does not change Mist grammar or Storm representation.

**Excluded work:**

- No generic local-function support, new shadowing policy or new ownership/discard disposal policy.
- No general local lifetime/linear drop redesign, constraint inference changes or optional AST architecture cleanup.

## Focused verification

- Add Tier 1 tests for direct registration/location/lookup and rejected-signature decisions; Tier 2 for hand-built statement/binder fragments proving scope, targeting, retained independent errors and discard evaluation shape.
- Existing Test/Mist.Tests/ProgramAnalysis/LocalFunctionDecisionTests.cs and Test/Mist.Tests/Semantics/TargetTypedNewTests.cs are regression references; they use ProgramModelProbe/component infrastructure and must not become the template for many new Tier 1/2 cases.
- Use existing BodyEmitter/LocalNames unit seams to assert distinct shadow/discard spelling; one already-owned component boundary may prove combined model-to-emitter behavior if lower tiers cannot. No new per-case whole-pipeline tests.

**Initial focused commands** (extend filters to include your new test classes; these do not replace final gates):

- `tools/test-ledger/run.sh Mist.slnx --filter "FullyQualifiedName~LocalBindingContract|FullyQualifiedName~LocalFunction|FullyQualifiedName~TargetTypedNew"`
- `dotnet format Mist.slnx --verify-no-changes --include Source/Mist/ProgramModel/ Source/Mist/Backends/CSharpTranspiler/Emitter/ Test/Mist.Tests/`
- `git diff --check`

For inline source probes, create a fresh scratch project with this `mist.toml` and one reproduction as `Program.mist`:

```toml
name = "Probe"
namespace_root = "Probe"
```

After a current solution build, run `dotnet run --project Source/Mist.CLI --no-build -- run <scratch-project> --quiet` from the Mist repository. Scratch directories and generated `out/` are not tracked deliverables. For artifact fidelity, also build that scratch project’s `out/Probe.csproj` and inspect all diagnostics. Snippets inside prose are component cases to construct in existing test seams, not claims that an incomplete fragment is a runnable program.

Tracking records owned by this packet: `MIST-036`, `MIST-037`, `MIST-038`, `MIST-039`, `MIST-015`.

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
