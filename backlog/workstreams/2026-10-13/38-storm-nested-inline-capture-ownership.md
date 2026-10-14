# 38 — Storm nested inline capture ownership

Complete this task independently, including implementation, verification, tracking updates and landing. Repository: `https://github.com/LynxStar/MistLang.git`. Target: `origin/main`. Suggested model: **Opus, high effort**. Recursive prefix/frame reuse, emitted subtype identity and capture/parent/origin ownership form a large coupled generator obligation. One owner can finish it using existing inline/anonymous-group contracts; optional AST feature policies are excluded. Opus high is appropriate; no independent Fable claim or runtime pass is asserted. These are portable user model categories, not API identifiers.

## Outcome and starting point

Complete the already specified nested-inline grammar path through prefix verification, recursive lowering, scoped parser generation and flat AST/capture ownership, publishing the authoritative Storm fix and a verified Mist consumer pin.

Planning baseline: `50f9fd7c0116d87a821d6a7000bff2ea6f35c27a`, 2026-10-13. Source inspection, bounded CLI/generator probes and the clean solution build used `c80c36823175514e5448fc2296bb174fbb360ec2`; the only intervening change confirms evidence on three canonical records. Storm pin and checked remote main: `83410653c7d722868a5001d2ef71e6692ec012b3`. Packets 26–33 have landed and are not reserved or rescheduled. The build passed with zero warnings/errors after using a writable temporary NuGet HTTP cache. No fresh full-suite pass is claimed.

Start from current remote `main` in an isolated task checkout; use your existing isolated checkout and retain completed work when continuing. Follow the repository's applicable instructions. Initialize submodules and verify their checked-out commits match this revision's recorded pins; a successful worktree hook alone does not prove initialization. Revalidate every included issue on current main. If already fixed, verify the acceptance cases and finish any real closeout gap without duplicating implementation. Use grammar and requirements/design as contracts; spec task documents are implemented, and retired classic source is not a reference to read or port from Git history.

Also own `https://github.com/LynxStar/StormLang.git`, authoritative remote `main`. Storm references below are paths as seen from the Mist checkout; inside the dependency checkout strip `Source/Storm.SourceGenerator/`. Revalidate both remote mains and retain newer compatible dependency work.

## Included obligations and acceptance

### MIST-101 — Valid nested inline grammar must generate compilable flat nodes and parser scopes

**Evidence 1 — executed direct current generator and generated-artifact Roslyn diagnostic probe, `83410653c7d722868a5001d2ef71e6692ec012b3`.**

```storm
grammar Nested; token A="a"; token B="b"; token C="c"; token D="d"; rule Outer=a:A; rule Inner=a:Outer b:B; rule Leaf=b:Inner c:C; rule Other=a:Outer d:D; rule Parent=inline Outer:{inner:inline Inner:{leaf:Leaf as LeafArm} as NestedArm,other:Other as OtherArm};
```

StormGrammar/import resolution, NameResolutionPass, ModeValidationPass, LeftRecursionPass, InlinePrefixVerification, ReachabilityPass and TraitValidationPass produced no diagnostic text; GrammarIRLowering.Succeeded=True. Inner begins Outer and Leaf begins Inner; Other begins Outer. This is the valid fixture, distinct from the invalid-prefix control.

Observed:

```text
lowering: True
(425,31): error CS0136: A local or parameter named '_A' cannot be declared in this scope because that name is used in an enclosing local scope to define a local or parameter
(427,25): error CS0136: A local or parameter named '_inlineOk' cannot be declared in this scope because that name is used in an enclosing local scope to define a local or parameter
(428,25): error CS0136: A local or parameter named '_m0' cannot be declared in this scope because that name is used in an enclosing local scope to define a local or parameter
(446,25): error CS0136: A local or parameter named '_inlinePrefixEnd' cannot be declared in this scope because that name is used in an enclosing local scope to define a local or parameter
(447,25): error CS0136: A local or parameter named '_inlinePrefixSpan' cannot be declared in this scope because that name is used in an enclosing local scope to define a local or parameter
(450,29): error CS0136: A local or parameter named '_peek' cannot be declared in this scope because that name is used in an enclosing local scope to define a local or parameter
(475,48): error CS0246: The type or namespace name 'LeafArmParentNode' could not be found (are you missing a using directive or an assembly reference?)
```

Direct generator emission plus CSharpCompilation.GetDiagnostics, not a full Storm/Mist suite or runtime parser execution. The generated artifact cannot currently compile. A private harness built warning-free against matching net9 Roslyn/runtime references; no repo build/source edits or restore were needed.

Contract references:

- `.kiro/specs/inline-rule-prefix/requirements.md`: Req 2.4 recursive parser arms;4 prefix verification;6.6 recursive IR;7 prefix once/prediction/backtracking;8 flat subtype/capture fidelity;9 hard mismatch refusal
- `.kiro/specs/storm-anonymous-groups/requirements.md`: canonical capture ownership, grammar-order Children and nested group projections
- `.kiro/specs/inline-rule-prefix/design.md`: existing flat-node and shared-prefix design, not an optional new AST API

Implementation references:

- `Source/Storm.SourceGenerator/Validate/InlinePrefixVerification.cs`: VerifyArm nested branch and structural prefix relation
- `Source/Storm.SourceGenerator/IR/GrammarIRLowering.cs`: LowerInlineBlock nested placeholder target and prefix/tail ownership
- `Source/Storm.SourceGenerator/Emit/ParserEmitter.cs`: EmitInlineBlock and EmitInlineArm recursion
- `Source/Storm.SourceGenerator/Emit/ParserEmitter.InlineProjection.cs`: ProjectInlineArm, ProjectInlinePrefix, MapExpressions and MergeInlineFrames
- `Source/Storm.SourceGenerator/Emit/AstEmitter.cs`: EmitPolymorphicNode and EmitInlineArmNode immediate arm emission
- `Source/Storm.SourceGenerator/Emit/AstEmitter.Children.cs`: canonical child projection
- `Source/Storm.SourceGenerator/Emit/AstEmitter.AnonymousGroups.cs`: nested/repeated group slots
- `Source/Storm.SourceGenerator/Generator/StormIncrementalGenerator.cs`: validation/lowering/output suppression

Acceptance:

- The complete valid grammar generates C# with no CS0136/CS0246. All parser-created alternative types are emitted as legal, consistent flat subtypes of ParentNode; no undefined inner-alt parent subtype or unusable synthetic prefix target remains. Existing nonnested public names/property types/base classes remain compatible.
- Generated ParseParent over abc selects the nested leaf and over ad selects the sibling with correct consumed positions. Extend to a bounded second nesting depth and nested/sibling ordered alternatives, proving prefix-once and no re-reading/re-parsing of the outer prefix when continuing the inner prefix.
- Nested prefixes extending their parent via direct reference and supported expanded structural matching reuse the already parsed outer prefix and its captures. Preserve label renaming, directly retained prefix-node captures and anonymous/repeated/optional supported capture slots. Each selected canonical node/capture appears once in grammar order, with correct Parent and source origin/span.
- Parser temporaries, node capture locals/list builders and inner prediction variables are unique/scoped across nesting, including later sibling attempts and recovery/backtracking. Failed nested tails restore the correct pre-prefix position without stale captures and do not block a succeeding sibling.
- Check ordinary and memoized generation against the same accepted nested contracts; memo replay preserves the same nodes/captures/consumed position. Preserve existing trait interfaces and nonnested generated AST compatibility using current generated-fidelity seams. No new memoization or inline FIRST-k policy is chosen.

### MIST-101 — Reject a nested prefix that does not extend its enclosing prefix

**Evidence 1 — executed current validation/lowering control plus source inspection, `83410653c7d722868a5001d2ef71e6692ec012b3`.**

```storm
grammar Nested; token A="a"; token B="b"; token C="c"; token D="d"; rule Outer=a:A; rule Inner=b:B; rule Leaf=b:Inner c:C; rule Other=a:Outer d:D; rule Parent=inline Outer:{inner:inline Inner:{leaf:Leaf as LeafArm} as NestedArm,other:Other as OtherArm};
```

Observed:

```text
The first private fixture passed name/left-recursion/prefix checking and lowered successfully despite Inner=b:B not extending Outer=a:A. It must not be cited as valid emission evidence. VerifyArm recursively checks inner arms only, skipping the outer/nested prefix relation.
```

Contract references:

- `.kiro/specs/inline-rule-prefix/requirements.md`: Requirement 4 each arm extends declared prefix and Requirement 9 STORM0017 hard error/output suppression

Implementation references:

- `Source/Storm.SourceGenerator/Validate/InlinePrefixVerification.cs`: VerifyArm nested case
- `Source/Storm.SourceGenerator/Generator/StormIncrementalGenerator.cs`: validation error aborts output

Acceptance:

- The complete invalid grammar reports STORM0017 Error at the owning nested arm/prefix relation and produces no partial parser/AST output. Continue checking independent failing arms as existing error ownership specifies.
- Valid direct and structural prefix extensions remain accepted. Repetition/optional prefix differences not covered by existing supported structural equality remain refused rather than silently inventing a new matching equivalence.
- Do not normalize every malformed nested grammar into an emitted tree or conflate the invalid relationship control with the valid generator failure.

### MIST-226 — Bounded namespaced-mode line-ending fixture correction

**Evidence 1 — current source inspection plus explicitly historical Windows executions, `83410653c7d722868a5001d2ef71e6692ec012b3`.**

NamespacedImportTests.ImportedFragmentsModesAndTraitsAreQualifiedWhereverTheImportedFileNamesThem expects escaped LF mode text. StormPrettyPrinter.PrintModeDecl still calls StringBuilder.AppendLine for braces; it is Environment.NewLine, not checkout CR bytes, that causes Windows CRLF. Canonical 226 records deterministic Windows full/focused failures with Storm unchanged at 83410653. This audit did not execute a Windows test.

Contract references:

- `CLAUDE.md`: zero warnings and truthful verification
- `.kiro/specs/inline-rule-prefix/requirements.md`: Requirement 10 unchanged grammar behavior; preserve capture/qualified semantics
- `.kiro/specs/mist-compiler-architecture/requirements.md`: qualified import meaning remains faithful

Implementation references:

- `Source/Storm.SourceGenerator/Test/Storm.SourceGenerator.Tests/NamespacedImportTests.cs`: ImportedFragmentsModesAndTraitsAreQualifiedWhereverTheImportedFileNamesThem
- `Source/Storm.SourceGenerator/Frontend/StormPrettyPrinter.cs`: PrintModeDecl AppendLine versus other explicit LF output

Acceptance:

- Revalidate 226 on current dependency main; if not already fixed/owned, complete the bounded comparison/printer newline correction in a coherent additional commit. Preserve every qualified fragment/mode/trait content assertion; normalize only the agreed text newline dimension or produce consistent canonical printer newlines.
- LF and CRLF input fixtures give the same structural qualified declarations; output comparison is platform-neutral. Do not hide name/capture/ordering differences, normalize literal token contents indiscriminately, or try core.autocrlf/gitattributes alone as the root fix because AppendLine depends on the host.
- Record the current-source reasoning and actual executed environment results; historical Windows sightings stay historical until a Windows run is actually performed. This is incidental to the same dependency owner, not optional AST scope or a separate dispatch.

### Reproducing the generator defect

Save the complete valid grammar above in a temporary `.storm` file. Use the existing `GeneratedParserFidelityTests` helper at the pinned revision to run validation, lowering, parser/AST emission and generated C# diagnostics; retain all diagnostics before testing parse/capture behavior. The complete invalid grammar is a separate prefix-refusal control. No planning-machine harness is required. New regression tests must use the owning grammar/IR or existing bounded generated-artifact seam described below, rather than rebuilding Mist for each grammar.

## Independence and ownership

- This is the sole worker of this selected batch changing/publishing Storm and the Mist gitlink. All other workers preserve its newer compatible pin when integrating; no independent older pin rollback.
- 34/35/36/37 consume compatible current Mist AST contracts. 38 preserves those public names/property types/origins, owns necessary narrow regeneration/consumer adaptations and verifies them against its final pin. It requires no unknown-storage, proof or operator API from another branch.
- No cross-packet agreement is needed on a new AST representation: optional generated API redesigns are excluded; accepted nested-inline public semantics and capture ownership remain the governing contract.

Own nested prefix verification/recursive lowering, coherent emission frames/subtype/capture identity and narrowly necessary generated-node consumers. Preserve every already supported nonnested grammar/API shape, including capture ordering fixes already landed.

This packet has no dependency on another workstream. As its parent session, you own every included task and its internal dependencies. Determine the execution order, decide which work to perform yourself or dispatch to subagents, and integrate their changes into your primary implementation. Resolve internal conflicts and verify the combined result; subagents return contributions to you rather than independently publishing partial work to the shared target. Use current repository contracts. Do not hand internal ordering, coordination or integration to the user, the planning session or another packet. Complete and close out every included item; a representative subset or a collection of subagent reports is not completion.

Allow narrowly necessary adjacent edits, including compatible bound metadata, consumers, generated artifacts and user/API/spec documentation needed to finish this outcome. Keep changes coherent and preserve already landed behavior. Shared files require narrow integration, not another packet's unpublished branch, service, test helper or design decision.

Excluded scope:

- No 199 new delimited-list syntax,200 scalar-alias removal,203 forwarding-only hand-built builder behavior or 204 mixed token/node AST union representation; those are optional/policy/API features. No inline 148 FIRST-k warning/block acceptance policy change.
- No parser-engine replacement, public AST migration delegated to another owner, unsupported grammar expansion, generic callable/operator work 37 or slicing 36.
- 226 is included only as bounded related incidental work after checking current/active ownership; no broad test/formatting cleanup inventory.

## Incidental fixes

If you uncover additional issues with clear expected behavior that are straightforward to fix and verify without user input, solve them in additional commits. This includes small fixes outside your primary files or objective; do not merely surface them as extras or make the user dispatch another task. Add appropriate verification and update actual documentation/backlog entries where relevant. Keep each coherent incidental fix separately committed and include it in the same landing workflow.

Avoid duplicating work already landed or assigned to an active workstream. Defer only when the finding requires a genuine user decision, substantial new work, unavailable access/authorization, or verification you cannot complete; state the concrete reason. Ordinary implementation choices and arbitrary file boundaries do not require user input. Finish the primary task and do not turn incidental cleanup into an open-ended campaign.

## Verification and closeout

Use hand-built grammar syntax/IR at Tier 1 prefix/name/frame decisions and Tier 2 emitter/capture chains; metadata parsing belongs to Storm’s own domain and should remain within its cost. Extend the existing bounded generated-parser fidelity seam for valid nested emitted C# and parse/capture/parent/origin/memo agreement. Do not add one full consumer compilation per nested permutation. Verify Mist generated API/AST origin/capture compatibility, full root-required build/format/regression and the published final pin.

Existing verification anchors (inspect their actual helpers/costs before extending):

- `Source/Storm.SourceGenerator/Test/Storm.SourceGenerator.Tests/InlineBlockParserTests.cs`: existing nested syntax cases
- `Source/Storm.SourceGenerator/Test/Storm.SourceGenerator.Tests/InlinePrefixVerificationTests.cs`: prefix relation decisions
- `Source/Storm.SourceGenerator/Test/Storm.SourceGenerator.Tests/InlineBlockParserEmitterTests.cs`: hand-built IR emitter slices
- `Source/Storm.SourceGenerator/Test/Storm.SourceGenerator.Tests/GeneratedParserFidelityTests.cs`: bounded emitted-parser artifact/capture/memo seam
- `Source/Storm.SourceGenerator/Test/Storm.SourceGenerator.Tests/StormTraitInlineBlockPropagationPropertyTests.cs`: trait retention controls
- `Source/Storm.SourceGenerator/Test/Storm.SourceGenerator.Tests/NamespacedImportTests.cs`: 226 exact fixture
- `Test/Mist.Tests/Parsing/GeneratedAstFidelityTests.cs`: consumer canonical child, parent and origin compatibility

Focused commands:

Storm checkout (initialized Source/Storm.SourceGenerator):

```bash
dotnet test Storm.slnx --filter "FullyQualifiedName~InlinePrefixVerification|FullyQualifiedName~InlineBlockParserEmitter|FullyQualifiedName~GeneratedParserFidelity|FullyQualifiedName~NamespacedImport|FullyQualifiedName~StormTraitInlineBlockPropagation"
```

Storm checkout:

```bash
dotnet build Storm.slnx
```

Storm checkout:

```bash
dotnet format Storm.slnx --verify-no-changes
```

Mist checkout:

```bash
tools/test-ledger/run.sh Mist.slnx --filter "FullyQualifiedName~GeneratedAst|FullyQualifiedName~ParserAst|FullyQualifiedName~NodeOrigin"
```

each repository:

```bash
git diff --check
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

Before the first Storm publication, run the unfiltered dependency regression in its checkout, in addition to its build/format gates. Preserve complete failure output and use its applicable failure-recording policy. The later Mist ledger includes Storm tests and verifies the combined consumer pin; it does not replace this pre-publication dependency checkpoint.

```bash
dotnet test Storm.slnx
```

Update `MIST-101`, `MIST-226` in the actual `backlog/issues.yaml` as part of this change. Record implemented behavior, supporting tests/evidence and any precise remaining limitation. Preserve historical evidence and other workers' updates; keep edits to owning records narrow. Close only completed portions and move fully discharged records to the archive with their stable IDs. Do not merely return proposed closeout text or create a new tracking system. Update necessary contract/API/book documentation and generated artifacts in their owning files.

## Commit and land

You are authorized to commit and publish this task directly to remote `main` without another confirmation. Do not create pull requests. Commit implementation, appropriate tests/generated artifacts, required documentation and actual closeout updates together or in coherent task commits. Fetch and incorporate the latest target branch, resolve conflicts while preserving other landed work, and perform the required verification on the integrated result. Use a normal, non-force push. Work from your own isolated checkout; do not reset or commandeer another session's working tree.

Other workers may land concurrently. If publication is rejected because the target advanced, fetch, reconcile, rerun verification affected by the new combined result, and retry. Keep shared-register edits narrow. You own routine integration; do not hand it to the user or assume a central integrator. Do not force-push the target, bypass protections, silently accept failures or mark unfinished work complete. Preserve newer compatible Storm pins already incorporated by main.

Finish by fetching and confirming that the intended changes, additional incidental fixes and actual closeout are present on remote `main`. Report landed commit IDs, actual verification results, entries updated, completed incidental fixes and any remaining limitations. A local commit or pushed task branch alone is not completion.

If unavailable credentials or branch protection prevents landing, complete all unaffected work, preserve the result durably, and report the exact blocker and smallest required user action. Do not switch to a PR workflow. If a genuine unresolved product decision prevents implementation, identify it precisely and retain the affected item as open. Do not repeat an unchanged failed attempt indefinitely or claim success without verification.

Own both repositories. Land verified Storm implementation and any coherent MIST-226 incidental correction directly on its authoritative remote `main` first. Fetch and confirm the actual landed, remotely reachable commit, including any resulting rebase revision. Pin Mist to that actual commit, preserve newer dependency work already incorporated, validate consumer generation/AST-origin/capture compatibility and run the required Mist gates against the final pin. Land its code/tests/documentation/tracking changes normally on Mist `main`. Never publish a consumer referencing a dependency commit available only locally or on an unmerged temporary branch. Verify and report both remote results.
