# 25 — Storm grammar semantics

Complete this independent bundle, including verification, actual backlog updates, commits and publication. Repository: `https://github.com/LynxStar/MistLang.git`. Target: remote `main`. Suggested model tier: **Sol/Opus**, effort: **high**. Lexer token priority versus literal matching and qualified grammar identity affect IR, generated parser/AST names, memoization and validation. One owner must finish the authoritative dependency and consumer pin. The accepted contracts avoid the deferred inline policy decision. These portable labels are guidance, not API identifiers or a requirement to launch a separate review.

## Starting point

Planning baseline: `7a14a1a7648c30c40b0e83da932255558dea64de` (2026-10-11). Executed compiler probes and the clean solution build used `8390675bd27f8e683c50664a94b4bc728daa7007`; the intervening published changes are publishing scripts/README and four backlog evidence corrections, with no compiler or test changes. Storm pin: `b4e5853ccca45623e59fcdb9ee0afd6e90e836ce`, also verified as Storm remote main during planning. The solution build passed with zero warnings/errors; no fresh full-suite pass is claimed. Each item below distinguishes executed behavior from source inspection or an unverified lead.

Start from current remote main in an isolated task checkout; retain your existing isolated checkout and completed work when continuing. Follow the repository’s applicable instructions. Run `git submodule update --init --recursive` and verify checked-out commits match the revision’s recorded pins; a setup hook alone is not evidence. Revalidate every item against current main. If already fixed, verify acceptance and finish any real tracking gap without duplicate implementation. Do not read or port retired classic implementation from Git history. `.kiro/specs/**/tasks.md` documents are implemented and excluded from work discovery.

## Outcome and included obligations

Make supported Storm literal rules and namespaced imports parse and emit according to their contracts, and give proved ordinary unreachable alternatives the required error severity, while preserving Mist consumer APIs and the explicitly separate inline reachability policy.

### MIST-050

A string literal in a rule must match its spelling even when the winning default-mode lexer token is a regex/class/fragment pattern that also admits it. Preserve maximal munch and token declaration-order tie breaking.

**Current evidence:** Source inspection at current Storm pin: GrammarIRLowering.DeclaredTokenSpelling only recognizes StringLiteralIR or InlineFragmentIR exact spelling. The synthetic token is emitted after declared pattern tokens, so token IDENT=[a-z]+; rule Word="if"; cannot match the winning IDENT token. This audit did not independently execute a generated grammar; current code and existing RuleLiteralTokenReuseTests pin the exact-spelling-only behavior.

**Owning source and existing seams:**

- Source/Storm.SourceGenerator/IR/GrammarIRLowering.cs::DeclaredTokenSpelling, Spells, synthetic literal lowering
- Source/Storm.SourceGenerator/Emit/ParserEmitter.cs::literal/token match emission
- Source/Storm.SourceGenerator/Emit/LexerEmitter.cs::maximal munch and declaration order
- Source/Storm.SourceGenerator/Test/Storm.SourceGenerator.Tests/RuleLiteralTokenReuseTests.cs

**Acceptance and controls:**

- Generated Word parser accepts if under an earlier identifier regex/class token, including an inline fragment case, with normal token origin/span.
- It refuses iff/iffy or any other winning token whose full spelling is not if; it does not split a longer maximal-munch token to force the literal.
- Named exact token reuse, duplicate spelling/order, non-default modes and existing hidden-token refusal remain correct.
- The lexer selects the same token kind as before under declaration-order/maximal-munch rules; literal recognition must not reprioritize tokens or steal generic identifier parsing.

### MIST-051

Preserve namespaced declaration identity and local lookup context through resolution, lowering, parser/AST naming, memoization and validation.

**Current evidence:** Source inspection at current pin: StormGrammar.LookupByName searches root unqualified declarations first and has no declaration owner context; GrammarIRLowering builds qualified RuleIR names; ParserEmitter interpolates rule.Name into Parse/_Parse identifiers and node names. Qualified names therefore become illegal C# identifiers and imported local homonyms may resolve to the root rule. Historical tracker observed invalid generated parser output; no current generated-grammar execution is claimed here.

**Owning source and existing seams:**

- Source/Storm.SourceGenerator/Resolve/StormGrammar.cs::LookupByName, NamespacedDeclarations
- Source/Storm.SourceGenerator/Resolve/StormImportResolver.cs
- Source/Storm.SourceGenerator/IR/GrammarIRLowering.cs::ruleShells, ruleOrder, rule reference lowering
- Source/Storm.SourceGenerator/Emit/ParserEmitter.cs::entry/memoized method names
- Source/Storm.SourceGenerator/Emit/AstEmitter.cs::generated node names
- Source/Storm.SourceGenerator/Validate/LeftRecursionPass.cs
- Source/Storm.SourceGenerator/Test/Storm.SourceGenerator.Tests/StormImportResolverTests.cs

**Acceptance and controls:**

- An imported grammar uses its own unqualified Foo internally even when root defines another Foo; root accesses the imported declaration by alias.Foo as specified.
- Root and imported homonyms, nested namespaced imports and two aliases of one resolved file keep distinct reachable qualified identities without accidental root duplication.
- Generated parser entry methods/AST classes/memoization identities are valid and collision-free; qualified display/lookup names remain faithful in diagnostics.
- Unnamespaced imports, duplicate-name diagnostics with both sites, unresolved paths, cycles and import-relative path resolution remain correct.
- Namespaced left-recursion/undefined-reference checks inspect the resolved imported rule graph, while root grammar generated public APIs remain stable.

### MIST-052

Make proved ordinary ordered-choice unreachable alternatives report Error and block generator output. Leave the inline-specific Warning and FIRST-k policy for MIST-148.

**Current evidence:** Current ReachabilityPass ordinary diagnostic branch at line 52 uses StormDiagnosticSeverity.Warning; mist-compiler-architecture Requirement7.2 demands Error and build failure. Inline-rule-prefix Requirement5.3 specifically demands a warning and remains a separate unresolved policy.

**Owning source and existing seams:**

- Source/Storm.SourceGenerator/Validate/ReachabilityPass.cs::ordinary and inline branches
- Source/Storm.SourceGenerator/Validate/AlternativeShadowing.cs::proof and conservative cutoffs
- Source/Storm.SourceGenerator/Generator/StormIncrementalGenerator.cs::error handling
- Source/Storm.SourceGenerator/Test/Storm.SourceGenerator.Tests/AlternativeShadowingTests.cs
- Source/Storm.SourceGenerator/Test/Storm.SourceGenerator.Tests/InlineBlockReachabilityTests.cs

**Acceptance and controls:**

- A proved ordinary duplicate/shadowed alternative reports STORM0006 Error at the owning rule/alternative and the generator refuses publication of invalid output.
- A reachable common-prefix/backtracking alternative and a case beyond the conservative proof limit receive no invented proof.
- Existing inline-tail/block warnings retain their specified severity/acceptance policy; do not close MIST-148.
- Update affected existing tests by ownership rather than globally changing every STORM0006 expectation.

## Contracts and implementation

- .kiro/specs/mist-compiler-architecture/requirements.md::Requirement4.4 hidden tokens,4.6 maximal munch,4.7 declaration-order ties; Requirement5 ordered choice and public rule entry points; Requirement6.1–9 imports/qualified visibility/order/path/cycles; Requirement7.2 ordinary unreachable Error
- .kiro/specs/inline-rule-prefix/requirements.md::Requirement5.3 inline warning remains unchanged; capture ownership acceptance10 remains unchanged
- .kiro/specs/storm-anonymous-groups/requirements.md and design.md::per-alternative canonical capture ownership, generated public shape and child order

Own the complete Storm source/IR/resolution/generation/validation change, tests and necessary dependency documentation. Own publication to StormLang main, then update Mist to the published remotely reachable final commit and verify consumer generation/build against that pin. No unpublished dependency or consumer-only patch counts as completion.

**Independence:**

- This is the sole packet that changes or publishes Storm and the Mist gitlink. All other packets consume its compatible generated API and must not pin an older revision when they land.
- Preserve root Mist lexer/runtime token priorities and existing AST public class/property names/types; regenerate and compare the consumer surface. Other binder packets may alter frontend helpers, but require no new Storm API.
- If narrow generated internals change, this owner verifies all affected Mist consumer seams against its final pin; it does not hand integration to a second packet or the user.

**Excluded work:**

- MIST-148 inline FIRST-k/block proof product-policy changes are excluded; inline warnings remain specified.
- No delimited-list feature, forwarding-only hand-built AST redesign, nested-inline AST redesign, removed scalar aliases or broad new grammar syntax.
- No token reprioritization, broad parser engine replacement, separate consumer API migration packet or planner-owned integration.

## Focused verification

- Tier 1 IR/lookup/name allocation/proof-severity decisions, Tier 2 direct parser-emitter/resolver fragments and diagnostics. Use existing in-memory import fixtures.
- Use the existing generated-parser fidelity seam for literal/import behavior that cannot be established by emitted-text/unit checks; keep cases bounded and classified by their true infrastructure.
- Run Storm focused filters, full Storm suite/format gates, then Mist generation/AST-origin/capture compatibility checks and required Mist combined regression against the published pin. Record actual commands, revisions and results, not old test counts.

**Initial focused commands** (extend filters to include your new test classes; these do not replace final gates):

- `dotnet build Storm.slnx` — from the Storm checkout
- `dotnet test Storm.slnx --no-build --filter "FullyQualifiedName~RuleLiteralTokenReuse|FullyQualifiedName~StormImportResolver|FullyQualifiedName~AlternativeShadowing|FullyQualifiedName~InlineBlockReachability|FullyQualifiedName~GeneratedParserFidelity"` — from Storm
- `dotnet test Storm.slnx --no-build` — from Storm
- `dotnet format Storm.slnx --verify-no-changes` — from Storm
- `tools/test-ledger/run.sh Mist.slnx --filter "FullyQualifiedName~GeneratedAstFidelity|FullyQualifiedName~ParserAst|FullyQualifiedName~NodeOrigin"` — from Mist
- `git diff --check` — run in both repositories

For inline source probes, create a fresh scratch project with this `mist.toml` and one reproduction as `Program.mist`:

```toml
name = "Probe"
namespace_root = "Probe"
```

After a current solution build, run `dotnet run --project Source/Mist.CLI --no-build -- run <scratch-project> --quiet` from the Mist repository. Scratch directories and generated `out/` are not tracked deliverables. For artifact fidelity, also build that scratch project’s `out/Probe.csproj` and inspect all diagnostics. Snippets inside prose are component cases to construct in existing test seams, not claims that an incomplete fragment is a runnable program.

Storm commands run in the initialized `Source/Storm.SourceGenerator` repository. Its authoritative remote is `https://github.com/LynxStar/StormLang.git`, target `main`. Its source-inspection leads must first be reproduced with generated-grammar tests at current dependency main; failed hypotheses receive evidence-based closeout rather than invented defects.

Tracking records owned by this packet: `MIST-050`, `MIST-051`, `MIST-052`.

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

## Dependency and consumer landing

Own both repositories. You are authorized to publish Storm directly to its authoritative remote main under the same normal-push/no-PR agreement. Land and verify the complete Storm change first. Pin Mist to the actual landed, remotely reachable final commit, including any resulting rebase commit, preserving newer dependency work already incorporated by Mist. Validate consumer generation, AST/origin/capture compatibility, build and combined regression against the final pin, then land Mist code/tests/documentation/tracking. Never publish a consumer referencing a dependency commit available only locally or on an unmerged temporary branch. Verify both remote results and report both commits. This is the sole packet owning the gitlink; other workers must preserve its newer compatible pin when integrating.
