# 26 — Qualified types and static type lookup

Complete this bundle independently, including verification, actual tracking updates, commits and publication. Repository: `https://github.com/LynxStar/MistLang.git`; target: remote `main`. Suggested model tier: **Sol/Opus**, effort: **high**. The hard part is keeping grammar capture/origin shapes, arity-aware resolution, qualified lookup and type-value/static-member disambiguation consistent across consumers. No new scalar-domain or type-expression policy is required. These portable labels guide the same task and do not require a separate reviewer or model change.

## Starting point

Planning baseline: `0428515879b2d0245c996a5db98a676e441b9cbe` (2026-10-11). Compiler/source probes and the fresh solution build used `b1e556fb9d9528367f7de6bd7d71a13bd5c28aad`; intervening commits only reconcile canonical tracking evidence/contracts, with no source or test changes. Storm pin: `83410653c7d722868a5001d2ef71e6692ec012b3`. The build passed with zero warnings/errors. No fresh full-suite pass is claimed. Evidence below distinguishes executed probes, current source inspection and leads needing validation. Previous packets 17–25 have landed; revalidate current main rather than repeating those implementations.

Start from current remote main in an isolated checkout, retaining your existing isolated checkout and completed work when continuing. Follow the repository’s applicable instructions. Initialize with `git submodule update --init --recursive` and verify checked-out commits match current main’s recorded pins. Revalidate each item on current main; if already fixed, verify acceptance and complete any real closeout gap without duplicate code. Do not read or port retired classic implementation from Git history. Spec task documents are implemented and excluded from work discovery.

## Outcome and included obligations

Make namespace-qualified nominal inheritance and named dependent domains resolve consistently with existing type positions, and make supported static members reachable through primitive keyword spellings without losing first-class type values.

### MIST-045 — Qualified nominal bases

**Current evidence:**

executed CLI probe
<scratch-project>/Program.mist(1,55): error MIST_PARSE_INCOMPLETE: Parser did not consume all input. Unparseable content begins at line 1.

Observed exit code: 1.

```mist
using Standard; namespace Probe; public class Base {} public class Derived:Probe.Base {} public class Program { public static void Main() {} }
```

**Contract references:**

- `.kiro/specs/generics-type-arguments/requirements.md` — Requirement 3 named qualified type references and Requirement 8 generic inheritance
- `.kiro/specs/nested-scoping/requirements.md` — qualified and nested namespace type resolution
- `.kiro/specs/struct-inheritance/requirements.md` — Requirements 1/3 base storage and constructed-base identity

**Owning source:**

- `Source/Mist/Grammar/Mist.storm` — BaseTypeRef, BaseList, existing TypeReference/QualifiedIdentifier
- `Source/Mist/ProgramModel/Construction/Resolution/BaseTypeResolver.cs` — ResolveBaseTypeRef, TryResolveConcreteBase, ResolveAbstractBase
- `Source/Mist/ProgramModel/Construction/Resolution/ScopeResolver.cs` — ResolveGenericDefinition, ResolveNestedGeneric, type argument construction
- `Source/Mist/ProgramModel/Construction/Types/TypeBuilder.cs` — base and nested declaration consumers
- `Source/Mist/Parsing/MistPrettyPrinter.cs` — base-list printing consumers; locate current partial implementation

**Existing regression seams:**

- `Test/Mist.Tests/Semantics/NestedTypeResolutionTests.cs` — qualified nominal lookup controls
- `Test/Mist.Tests/Backend/NestedGenericTypeEmissionTests.cs` — constructed enclosing type spelling

**Acceptance and controls:**

- Probe.Derived : Probe.Base parses, resolves the actual nominal Base and emits a compilable relationship. Exercise namespace-qualified generic class/struct and abstract bases with existing supported substitution rules.
- Unqualified bases, imported aliases, arity mismatches, unknown/inaccessible types and source spans retain their owning diagnostics. Existing nested type references retain identity.
- Keep the existing permitted base categories: arrays, unions, primitive/invalid kinds, sealed bases, cycles and multiple concrete bases remain refused. A full TypeReference grammar merger is an implementation option, not scope expansion.
- Update every affected generated-AST reader/pretty-printer/origin consumer and verify regeneration with the current compatible Storm pin; no dependency publication or new syntax unrelated to qualification.

### MIST-046 — Qualified named dependent scalar domains

**Current evidence:**

executed CLI probe
<scratch-project>/Program.mist(1,57): error MIST_PARSE_INCOMPLETE: Parser did not consume all input. Unparseable content begins at line 1.

Observed exit code: 1.

```mist
using Standard; namespace Probe; type Positive=int > 0; public class Box<N:Probe.Positive>{} public class Program { public static void Main() {} }
```

**Contract references:**

- `.kiro/specs/dependent-types/requirements.md` — named/scalar dependent value domains and closed type identity
- `.kiro/specs/adhoc-refinement-constraints/requirements.md` — named refinement domains and dependent constraint validation

**Owning source:**

- `Source/Mist/Grammar/Mist.storm` — NamedConstraintDependentTypeParameter, TypeParameter
- `Source/Mist/ProgramModel/Construction/Types/TypeParameterPopulator.cs` — named.ConstraintName to TypeParameter.DomainTypeName
- `Source/Mist/ProgramModel/Construction/Resolution/TypeParameterDomainResolver.cs` — Resolve, CarryableDomains, Underlying
- `Source/Mist/ProgramModel/Construction/Resolution/ScopeResolver.cs` — existing qualified type resolution

**Existing regression seams:**

- `Test/Mist.Tests/Semantics/DependentValueLookupTests.cs` — dependent projections and scalar domains
- `Test/Mist.Tests/Parsing/AdhocRefinementRoundTripPropertyTests.cs` — existing parameter/refinement syntax printing

**Acceptance and controls:**

- Box<N:Probe.Positive> over type Positive=int > 0 parses and resolves the same domain as the unqualified alias; a valid closed value is accepted and an invalid value is refused at its owning site.
- Exercise namespace collisions, a nested namespace, imported unqualified aliases and exact unknown/inaccessible domain diagnostics without silently leaving a qualified unresolved name as an ordinary type parameter.
- Keep all existing CarryableDomains restrictions and refinement transparency: this does not allow float/string/arbitrary object domains, arbitrary parameter annotations, or alter generic entailment.

### MIST-098 — Supported static lookup through keyword type spelling

**Current evidence:**

executed CLI probe
<scratch-project>/Program.mist(1,163): error MIST_PM_UNRESOLVED_MEMBER: 'Type' has no member named 'Meaning'.

Observed exit code: 1.

```mist
using Standard; namespace Probe; extension(int self){public static int Meaning(){return 42;}} public class Program{public static void Main(){Console.WriteLine(int.Meaning());}}
```
executed CLI probe
42

Observed exit code: 0.

```mist
using Standard; namespace Probe; extension(int self){public static int Meaning(){return 42;}} public class Program{public static void Main(){Console.WriteLine(Int32.Meaning());}}
```

**Contract references:**

- `.kiro/specs/extension-methods/requirements.md` — Requirement 13 static extension lookup through a type name
- `.kiro/specs/type-literals/requirements.md` — Requirements 2/4/5 primitive type expressions remain Type values
- `CLAUDE.md` — Mist language facts: type names as values and reflected string.Name

**Owning source:**

- `Source/Mist/ProgramModel/Construction/Binding/Expressions/ExpressionBinder.cs` — BindTypeLiteral
- `Source/Mist/ProgramModel/Construction/Binding/Expressions/PostfixBinder.cs` — member receiver classification, ResolveMember/BindMemberAccess
- `Source/Mist/ProgramModel/Construction/Binding/MemberResolution.cs` — static visibility and extension lookup
- `Source/Mist/ProgramModel/Construction/Binding/ExtensionLookup.cs` — Find with StaticOnly

**Existing regression seams:**

- `Test/Mist.Tests/Semantics/ExtensionLookupTests.cs` — type-specific static lookup controls
- `Test/Mist.Tests/Parsing/TypeLiteralParserPropertyTests.cs` — keyword type literal grammar
- `Test/Mist.Tests/Parsing/TypeLiteralPrettyPrinterPropertyTests.cs` — type literal round-trip

**Acceptance and controls:**

- Given extension(int self){public static int Meaning(){return 42;}}, int.Meaning() and Int32.Meaning() bind the same selected member and execute 42. The member is genuinely declared; do not reuse string.IsNullOrEmpty as a fixture because Standard.String does not establish that target.
- Keep var t=int as a first-class Type value and its reflected properties such as t.Name. Retain existing primitive keyword reflection/typeof emission controls.
- Static context must not expose instance members or instance extension receivers, and unknown supported-name misses remain source anchored. Classify the syntactic type-member route narrowly; do not reinterpret every Type-valued local as its represented static type.

## Scope and independence

Own Mist grammar captures, dependent-domain name storage/resolution, base resolution, keyword type-member routing and necessary printing/generated-AST consumers. Generated output and origin tests are verified against the recorded Storm pin; preserve its current public API and do not change Storm.

- Packet 27 owns callable overload/candidate membership and static extension block/method closure after a type receiver is resolved. Packet 26 owns only supplying the correct represented receiver and static visibility. Both can use existing MemberResolution/ExtensionLookup contracts independently; no new shared API is required.
- Packet 29 owns hidden context/default/constructor witness forwarding. Qualified base/domain lookup consumes existing resolver/substitution APIs and does not alter witnesses, constraints or dependent identity policy.
- Packet 33 owns assignment/return/initializer judgments on bound values; qualified name/receiver production does not change storage validity or collection Add checking.
- Shared grammar generated files and PostfixBinder edits are ordinary narrow integration/rebase work. Each packet must retain all previously landed 17–25 behavior.

**Already-working controls:**

- MIST-084: Not missing work: current ordinary nested alias and newtype fixtures print 2 and 3. Keep these positive controls when changing qualified paths; do not add a blanket nested-alias/newtype refusal.

**Excluded work and decisions:**

- No new scalar domain categories, arbitrary base shape permission, global CLR metadata expansion, general namespace expression redesign, or Storm publication/pin ownership.
- No rescheduling 17–25; preserve dependent projection and constructor-constraint behavior already landed.

## Focused verification

Initial focused commands (extend filters to your new classes; these do not replace final gates):

- `tools/test-ledger/run.sh Mist.slnx --filter "FullyQualifiedName~QualifiedTypeDomain|FullyQualifiedName~NestedTypeResolution|FullyQualifiedName~DependentValueLookup|FullyQualifiedName~ExtensionLookup|FullyQualifiedName~TypeLiteral|FullyQualifiedName~AdhocRefinement"`
- `dotnet format Mist.slnx --verify-no-changes --include Source/Mist/Grammar/ Source/Mist/ProgramModel/ Source/Mist/Parsing/ Test/Mist.Tests/`
- `git diff --check`

To reproduce inline cases, create a fresh scratch project containing `Program.mist` and this `mist.toml`, unless the case explicitly supplies an entry-point override:

```toml
name = "Probe"
namespace_root = "Probe"
```

After building current main, run `dotnet run --project Source/Mist.CLI --no-build -- run <scratch-project> --quiet` from the repository. For generated-consumer fidelity, also run `dotnet build <scratch-project>/out/Probe.csproj --no-incremental` and inspect every diagnostic. Scratch projects and generated `out/` remain untracked. Complete source snippets are reproductions; smaller prose examples are component inputs to construct in the existing narrow seams.

Owning canonical records: `MIST-045`, `MIST-046`, `MIST-098`. Each included bounded obligation requires its own verified disposition; preserve excluded or unverified residue rather than claiming the entire record fixed from one representative case.

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
