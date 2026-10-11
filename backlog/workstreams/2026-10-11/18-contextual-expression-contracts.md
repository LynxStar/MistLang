# 18 — Contextual expression contracts

Complete this independent bundle, including verification, actual backlog updates, commits and publication. Repository: `https://github.com/LynxStar/MistLang.git`. Target: remote `main`. Suggested model tier: **Sol/Opus**, effort: **high**. Candidate-dependent new/default/empty-array targeting, common-type inference and target isolation must preserve failed-expression provenance and chosen call identity. This is a substantial binder workstream; no language-policy expansion is required. Escalate within the same owner only if candidate trial reasoning warrants it. These portable labels are guidance, not API identifiers or a requirement to launch a separate review.

## Starting point

Planning baseline: `7a14a1a7648c30c40b0e83da932255558dea64de` (2026-10-11). Executed compiler probes and the clean solution build used `8390675bd27f8e683c50664a94b4bc728daa7007`; the intervening published changes are publishing scripts/README and four backlog evidence corrections, with no compiler or test changes. Storm pin: `b4e5853ccca45623e59fcdb9ee0afd6e90e836ce`, also verified as Storm remote main during planning. The solution build passed with zero warnings/errors; no fresh full-suite pass is claimed. Each item below distinguishes executed behavior from source inspection or an unverified lead.

Start from current remote main in an isolated task checkout; retain your existing isolated checkout and completed work when continuing. Follow the repository’s applicable instructions. Run `git submodule update --init --recursive` and verify checked-out commits match the revision’s recorded pins; a setup hook alone is not evidence. Revalidate every item against current main. If already fixed, verify acceptance and finish any real tracking gap without duplicate implementation. Do not read or port retired classic implementation from Git history. `.kiro/specs/**/tasks.md` documents are implemented and excluded from work discovery.

## Outcome and included obligations

Bind expression lookup failures, result types and contextual arguments to the semantic expression they denote, so valid programs do not reach Roslyn with wrong types and invalid programs receive their owning source diagnostic.

### MIST-020

Own real AmbiguousResult failures in bare expression position; do not reopen imported-type ambiguity that already reports correctly.

**Current evidence:** Fresh baseline: public abstract A { public int Value { get; } } public abstract B { public int Value { get; } } public abstract C : A, B { public int Read() { return Value; } } plus empty Program.Main reaches MIST_BE_UNSUPPORTED_EXPRESSION at Value. CandidateSet can return AmbiguousResult, and IdentifierBinder falls through to UnresolvedExpression. Control: two imported homonym types already produce one PM_AMBIGUOUS_TYPE from ImportingScope.

**Owning source and existing seams:**

- Source/Mist/ProgramModel/Construction/Binding/Expressions/IdentifierBinder.cs::BindIdentifier
- Source/Mist/ProgramModel/Model/Scopes/CandidateSet.cs::selection of nearest/closest candidates
- Source/Mist/ProgramModel/Model/Scopes/ImportingScope.cs::existing imported-type ambiguity ownership
- Source/Mist/ProgramModel/Construction/Binding/MemberResolution.cs::ReportLookup

**Acceptance and controls:**

- The inherited-property bare-name ambiguity produces one frontend ambiguity diagnostic at Value, identifying both candidates, and never reaches backend unsupported-expression.
- An unambiguous inherited property resolves; repeated overload names remain a valid method group.
- Imported type homonyms retain their existing single diagnostic and do not receive a duplicate; unfinished binder cases remain distinguishable from actual ambiguity.

### MIST-032

Distinguish declared non-instance/non-storage members from genuinely missing initializer/property-pattern targets without admitting writes or storage reads to those members.

**Current evidence:** Fresh baseline new Widget() { Count = 1 } over public static int Count produces PM_UNRESOLVED_MEMBER claiming Widget has no Count. ResolveStorage filters InstanceOnly before classifying the result.

**Owning source and existing seams:**

- Source/Mist/ProgramModel/Construction/Binding/MemberResolution.cs::ResolveStorage
- Source/Mist/ProgramModel/Construction/Binding/Expressions/ObjectInitializerBinder.cs::ResolveTarget, ReportNotStorage
- Source/Mist/ProgramModel/Construction/Binding/Expressions/PatternBinder.Properties.cs::property subpattern resolution
- Test/Mist.Tests/Semantics/InitializerTargetBindingTests.cs

**Acceptance and controls:**

- Static field/property/method and enum variant targets report an owning not-storage/not-instance diagnostic at the entry rather than a false no-such-member claim.
- Cover initializer and property-pattern paths; genuine absent member remains unresolved-member, and valid instance field/property still binds.
- Preserve existing private setter/readonly/accessibility refusals and one-root-cause behavior; do not admit static/enum/method storage targets.

### MIST-033

Isolate the target of a postfix result from the parenthesized receiver expression; its switch arms must infer the receiver result type before the following member access produces the outer expected type.

**Current evidence:** Fresh baseline int x=1; int n=(x switch {1=>"five", _=>"six"}).Length; reports int.Length unresolved plus two string-to-int conversions. The untargeted var n equivalent builds correct C#: int n=(x switch strings).Length. The trigger is the enclosing int target, not every parenthesized switch member access.

**Owning source and existing seams:**

- Source/Mist/ProgramModel/Construction/Binding/Expressions/PostfixBinder.cs::BindPostfix, BindSwitch, BindMemberAccess
- Source/Mist/ProgramModel/Construction/Binding/Expressions/ExpressionBinder.cs::BindPrimary ParenthesizedExpressionPrimaryNode
- Source/Mist/ProgramModel/Construction/Binding/BindingContext.cs::target stack

**Acceptance and controls:**

- Typed int local and int return of the string-valued switch .Length bind and emit correctly; var/direct-call controls remain valid.
- A directly targeted switch without a postfix member still uses its declared result target.
- Parenthesized postfix chains use the receiver own type; method-group/lambda contextual conversion controls remain intact.

### MIST-040

Replace identity-only uniformity with shared supported common-type inference for untargeted array elements, switch results and block-lambda returns.

**Current evidence:** Fresh baseline [1,2L] reports ARRAY_LITERAL_NO_COMMON_TYPE. Current LiteralBinder.CommonElementType, PostfixBinder.CommonArmType and LambdaBinder.InferredReturnType compare ReferenceEquals. The switch and lambda contract explicitly permits a common base/conversion type.

**Owning source and existing seams:**

- Source/Mist/ProgramModel/Construction/Binding/Expressions/LiteralBinder.cs::CommonElementType
- Source/Mist/ProgramModel/Construction/Binding/Expressions/PostfixBinder.cs::CommonArmType
- Source/Mist/ProgramModel/Construction/Binding/Expressions/LambdaBinder.cs::InferredReturnType, Returns
- Source/Mist/ProgramModel/Construction/Binding/ConversionRelation.cs
- Source/Mist/ProgramModel/Construction/Binding/ImplicitConversions.cs

**Acceptance and controls:**

- int/long and other established numeric widening combinations infer an order-independent common result across all three consumers.
- Existing nominal common-base/implicit-target cases infer the supported common type and each value uses normal conversions; same-type and explicitly targeted controls remain unchanged.
- Ignore positively failed inputs without derivative common-type complaints; genuine incompatible/no-unique-common-target input gets the existing owning diagnostic.
- Nested lambda/local-function returns are not collected as outer returns. Existing union constituents/targets remain supported; do not manufacture a new blanket union or unknown fallback policy.

### MIST-041

Make assignment expression result typing agree with the assigned storage/conversion contract and generated C# result.

**Current evidence:** Fresh baseline long n=0; var r=(n=1); reaches MIST_CS_ERROR long-to-int. ExpressionBinder builds AssignmentExpression(..., value.Type) even when the storage is long and the emitted assignment is long.

**Owning source and existing seams:**

- Source/Mist/ProgramModel/Construction/Binding/Expressions/ExpressionBinder.cs::assignment branch
- Source/Mist/ProgramModel/Model/Expressions/AssignmentExpression.cs
- Source/Mist/Backends/CSharpTranspiler/Emitter/BodyEmitter.cs::assignment expression emission
- Source/Mist/ProgramAnalysis/Rules/AssignabilityAnalyzer.cs::AssignedType

**Acceptance and controls:**

- The result of ordinary assignment after numeric or supported declared conversion has the actual assignment/storage result type, with one RHS evaluation.
- Cover local, field/property and index storage through existing model seams; invalid assignments keep their existing source owner.
- Retain established ??= target unwrapping and subsequent narrowing behavior; distinguish its result contract from ordinary assignment rather than mechanically replacing all types.

### MIST-042

Report the intrinsic non-indexable receiver refusal at a sound index expression while preserving failed/unfinished ownership controls.

**Current evidence:** Fresh baseline var r=5[0]; Console.WriteLine(r); reports only derivative PM_UNRESOLVABLE_OVERLOAD at WriteLine. IndexBinder no-indexer branch returns ArrayElement ?? ErrorType silently.

**Owning source and existing seams:**

- Source/Mist/ProgramModel/Construction/Binding/Expressions/IndexBinder.cs::Bind
- Source/Mist/ProgramModel/Construction/Binding/Indexability.cs
- Source/Mist/ProgramModel/Construction/Binding/PriorFailure.cs
- Test/Mist.Tests/ProgramAnalysis/IntrinsicIndexDecisionTests.cs
- Test/Mist.Tests/Semantics/RangeAndIndexResolutionTests.cs

**Acceptance and controls:**

- 5[0] receives one owning frontend not-indexable diagnostic at the index site, and later use does not add a derivative overload finding.
- Arrays, strings and supported user indexers retain lookup/conversion results; index operand type checking follows existing intrinsic index contracts.
- A receiver/index already positively failed preserves that root cause and retained subtree; genuinely unfinished ErrorType remains reportable by the existing fallback owner.
- Do not broaden user slicing protocol support or bounds proof here.

### MIST-044

Contextually bind new(), default and empty arrays in candidate trials when multiple overloads share arity; select using all known arguments before settling the chosen contextual expression.

**Current evidence:** Fresh baseline public static int Pick(Thing x,int k){return 1;} and public static int Pick(Other x,string k){return 2;} with Pick(new(),1) gets TARGET_TYPED_NEW_NO_TARGET; Pick(new Thing(),1) builds. ArgumentBinder currently pushes targets only for SoleByArity and Settle handles lambdas/method groups only.

**Owning source and existing seams:**

- Source/Mist/ProgramModel/Construction/Binding/Expressions/ArgumentBinder.cs::Bind, SoleByArity, Settle
- Source/Mist/ProgramModel/Construction/Binding/OverloadResolution.cs::Select, IsApplicable
- Source/Mist/ProgramModel/Construction/Binding/Expressions/LambdaTrials.cs
- Source/Mist/ProgramModel/Construction/Binding/Expressions/ExpressionBinder.cs::BindConstructorCall

**Acceptance and controls:**

- A sound other argument uniquely selecting Pick(Thing,int) allows new() to target Thing and keeps exactly that selected overload.
- Cover default and [] against distinct candidate parameter types, plus equivalent constructor/this/base-chain argument selection through existing call adapters.
- A truly ambiguous pair remains ambiguous; trial diagnostics/failed constructors from rejected candidates do not escape or poison the chosen result.
- Retain lambda/method-group candidate trials, optional/variadic/ref flags and implicit-conversion settlement. Do not infer an unknown generic parameter from an untyped new() without existing contract evidence.

## Contracts and implementation

- .kiro/specs/switch-expressions/requirements.md::Requirement 5 type inference acceptance1–5 and postfix expression result typing
- .kiro/specs/array-literals/requirements.md::Requirement 5 element inference and Requirement 6 element checking
- .kiro/specs/first-class-functions/requirements.md::Requirement 6.4/6.7 common block return type; 6.8 candidate contextual typing
- .kiro/specs/target-typed-new/requirements.md::Requirement 3.4 parameter targets and Requirement 4 constructor selection
- .kiro/specs/method-overloading/requirements.md::Requirement 3 candidate applicability/selection; preserve declared overload identity
- Source/Mist/ProgramModel/CLAUDE.md::binding owns lookup, overload selection and conversion classification; bound selections are authoritative
- .kiro/specs/mist-compiler-architecture/requirements.md::source anchored stage diagnostics; source ownership rather than backend failure

One owner of expression production and contextual argument trials: IdentifierBinder, MemberResolution storage classification, PostfixBinder target isolation/result inference, LiteralBinder/LambdaBinder shared common-type use, assignment result model, IndexBinder refusal, and noncallable contextual argument settlement in ArgumentBinder/OverloadResolution. Adjacent helpers/diagnostics/emission adjustments needed by those contracts are included. This is a finite cohesive binder graph consistency packet, not seven unrelated feature expansions.

**Independence:**

- Generic packet owns constraint/domain entailment, dependent alias projection and constructor witness generation, not candidate contextual-expression binding. Keep any new trial representation/helper internal to this packet; do not require another packet to add a generic inference API. Existing selected-callee identity and generic substitution contracts remain unchanged.
- Constructor packet owns explicit/implicit constructor declaration/default/DA behavior. This packet owns only contextual argument trials through the shared method/constructor selector; constructor witness signatures remain untouched.
- Pattern proof/erased representation packets own coverage/refinement/runtime lowering. MIST-032 changes storage lookup diagnostics only, MIST-033/040 change switch result binding only, and MIST-042 changes intrinsic refusal only; do not change pattern semantics, sized-union representations or length proofs.
- Local packet owns declaration targeting calls in StatementBinder, while this packet owns how the supplied target is consumed. Shared diagnostic/model files require narrow edits and ordinary main rebasing, not negotiated APIs.

**Excluded work:**

- No new implicit-union/unknown common-type policy, full user-defined generic inference redesign or typed witness API redesign.
- No slicing protocol implementation, pattern exhaustiveness/runtime representation, general static/member namespace grammar work or CLR metadata expansion.
- No blanket suppression of ErrorType/UnresolvedExpression; preserve unfinished and independent failure controls.

## Focused verification

- Add Tier 1 direct common-type/assignment-result/ambiguity/index decision checks and Tier 2 hand-built binder fragments for target stack, candidate trial diagnostic isolation and exact source spans. Use controlled source origins and stub ambient dependencies.
- Existing Test/Mist.Tests/Semantics/ScopeResolverAmbiguityTests.cs, InitializerTargetBindingTests.cs and RangeAndIndexResolutionTests.cs and Test/Mist.Tests/ProgramAnalysis/LambdaBindingRefusalTests.cs, IntrinsicIndexDecisionTests.cs are regression anchors; their helper implementations determine tier, not their names.
- Review Test/Mist.Tests/Semantics/TargetTypedNewTests.cs::WithNothingToTarget_TheConstructionIsRefused: its var no-target row stays valid, but historical multi-candidate row must be assessed for true ambiguity rather than requiring unconditional no-target refusal.
- Use existing backend expression emission seams to prove model/C# agreement after widening and target isolation. No new whole-pipeline test for each refusal.

**Initial focused commands** (extend filters to include your new test classes; these do not replace final gates):

- `tools/test-ledger/run.sh Mist.slnx --filter "FullyQualifiedName~ContextualExpressionContract|FullyQualifiedName~TargetTypedNew|FullyQualifiedName~LambdaBinding|FullyQualifiedName~ScopeResolverAmbiguity|FullyQualifiedName~InitializerTargetBinding|FullyQualifiedName~IntrinsicIndex|FullyQualifiedName~RangeAndIndex"`
- `dotnet format Mist.slnx --verify-no-changes --include Source/Mist/ProgramModel/ Source/Mist/ProgramAnalysis/Rules/AssignabilityAnalyzer.cs Source/Mist/Backends/CSharpTranspiler/Emitter/BodyEmitter.cs Test/Mist.Tests/`
- `git diff --check`

For inline source probes, create a fresh scratch project with this `mist.toml` and one reproduction as `Program.mist`:

```toml
name = "Probe"
namespace_root = "Probe"
```

After a current solution build, run `dotnet run --project Source/Mist.CLI --no-build -- run <scratch-project> --quiet` from the Mist repository. Scratch directories and generated `out/` are not tracked deliverables. For artifact fidelity, also build that scratch project’s `out/Probe.csproj` and inspect all diagnostics. Snippets inside prose are component cases to construct in existing test seams, not claims that an incomplete fragment is a runnable program.

Tracking records owned by this packet: `MIST-020`, `MIST-032`, `MIST-033`, `MIST-040`, `MIST-041`, `MIST-042`, `MIST-044`.

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
