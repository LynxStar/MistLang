# 33 — Statement and initialization validity

Complete this bundle independently, including verification, actual tracking updates, commits and publication. Repository: `https://github.com/LynxStar/MistLang.git`; target: remote `main`. Suggested model tier: **Sol/Opus**, effort: **high**. The hardest obligation is associating each collection Add and each return/write with its real target and preserving failure ownership and flow facts across nested initializers. Existing selected-call/prover seams are sufficient; no new proof domains or runtime guards are required. These portable labels guide the same task and do not require a separate reviewer or model change.

## Starting point

Planning baseline: `0428515879b2d0245c996a5db98a676e441b9cbe` (2026-10-11). Compiler/source probes and the fresh solution build used `b1e556fb9d9528367f7de6bd7d71a13bd5c28aad`; intervening commits only reconcile canonical tracking evidence/contracts, with no source or test changes. Storm pin: `83410653c7d722868a5001d2ef71e6692ec012b3`. The build passed with zero warnings/errors. No fresh full-suite pass is claimed. Evidence below distinguishes executed probes, current source inspection and leads needing validation. Previous packets 17–25 have landed; revalidate current main rather than repeating those implementations.

Start from current remote main in an isolated checkout, retaining your existing isolated checkout and completed work when continuing. Follow the repository’s applicable instructions. Initialize with `git submodule update --init --recursive` and verify checked-out commits match current main’s recorded pins. Revalidate each item on current main; if already fixed, verify acceptance and complete any real closeout gap without duplicate code. Do not read or port retired classic implementation from Git history. Spec task documents are implemented and excluded from work discovery.

## Outcome and included obligations

Judge writes, returns and initializer entries against the actual storage/callee/entry-point contracts, so invalid source is refused by Mist and accepted unit/TLS/collection paths remain valid.

### MIST-005 — Void body returns and top-level exit-code returns

**Current evidence:**

executed CLI probe
<scratch-project>/out/Probe/Program.cs: error MIST_CS_ERROR: Since 'Program.Main()' returns void, a return keyword must not be followed by an object expression

Observed exit code: 1.

```mist
using Standard; namespace Probe; public class Program { public static void Main() { return 1; } }
```
executed CLI probe

Observed exit code: 0.

```mist
using Standard; namespace Probe; public class Program{public static void Main(){return void;}}
```
executed CLI probe

Observed exit code: 0.

```mist
using Standard; return 0;
```

This probe requires this manifest override:

```toml
name = "Probe"
namespace_root = "Probe"
entry_point = "top-level"
```
executed CLI probe
<scratch-project>/out/Probe/Program.cs: error MIST_CS_ERROR: Cannot implicitly convert type 'string' to 'int'

Observed exit code: 1.

```mist
using Standard; return "bad";
```

This probe requires this manifest override:

```toml
name = "Probe"
namespace_root = "Probe"
entry_point = "top-level"
```

**Contract references:**

- `.kiro/specs/void-unit-type/requirements.md` — Requirement 3.4 refuses non-unit return value; 3.2/3.3/3.5 preserve unit returns
- `.kiro/specs/top-level-statements/requirements.md` — Requirement 7.1 integer exit code, 7.2 bare return zero, 7.3 noninteger type error

**Owning source:**

- `Source/Mist/ProgramAnalysis/Rules/AssignabilityAnalyzer.cs` — ReturnStatement case deliberately skips void targets
- `Source/Mist/ProgramModel/Construction/Binding/StatementBinder.cs` — BindReturn targets actual body return contract
- `Source/Mist/ProgramModel/Construction/Types/TypeBuilder.cs` — SynthesizeTopLevelProgram currently records void ReturnType
- `Source/Mist/ProgramAnalysis/EntryPointSelection.cs` — synthetic TLS identity
- `Source/Mist/Backends/CSharpTranspiler/Emitter/BodyEmitter.cs` — EmitReturn and unit value evaluation

**Existing regression seams:**

- `Test/Mist.Tests/ProgramAnalysis/AssignabilitySiteTests.cs` — A SlotTheAnalyzerDoesNotOwnRemainsSilent void-return row; assess actual ownership
- `Test/Mist.Tests/ProgramAnalysis/AssignabilityRefusalTests.cs` — source-owned conversion refusals
- `Test/Mist.Tests/Parsing/TopLevelStatementParsingTests.cs` — existing TLS grammar controls

**Acceptance and controls:**

- void Main(){return 1;} gets one source diagnostic at the returned value before backend compilation; apply the same rule to supported void method/local-function/lambda/accessor/operator body forms where their actual result contract is void.
- Actual unit-typed expressions remain accepted; return; and implicit fallthrough remain valid for void methods, with side effects evaluated once. The current Standard spelling is Void.Instance, not the old spec example Void.Void; preserve current supported singleton/literal behavior and do not add an illegal same-name member.
- TLS return 0 and other integer expressions remain accepted and set the process exit code; bare TLS return exits zero. TLS return "bad" gets one owning source type error rather than Roslyn. Distinguish TLS from ordinary void bodies in the model/analysis contract.
- Failed returned expressions keep their existing root cause, and unrelated failures still report. Keep async completion/unit behavior and nonvoid missing-return/DA controls intact.

### MIST-222 — Non-storage assignment targets

**Current evidence:**

executed CLI probe
<scratch-project>/out/Probe/Program.cs: error MIST_CS_ERROR: The left-hand side of an assignment must be a variable, property or indexer

Observed exit code: 1.

```mist
using Standard; namespace Probe; public class Program { public static void Main() { 7=3; } }
```

**Contract references:**

- `.kiro/specs/property-initializers/requirements.md` — Assignable_Member and existing writable storage definition
- `.kiro/specs/ref-parameters/requirements.md` — Requirement 3.3 explicitly distinguishes storage from literals/results; ordinary assignment admits settable properties additionally
- `Source/Mist/ProgramModel/CLAUDE.md` — resolved expression graph and source-anchored judgment

**Owning source:**

- `Source/Mist/ProgramModel/Construction/Binding/Expressions/ExpressionBinder.cs` — assignment branch constructs AssignmentExpression for any target
- `Source/Mist/ProgramAnalysis/Rules/AssignabilityAnalyzer.cs` — AssignmentExpression value checking
- `Source/Mist/ProgramAnalysis/StorageLocations.cs` — existing storage classification
- `Source/Mist/ProgramAnalysis/Rules/AccessorAnalyzer.cs` — property/indexer setter and access ownership
- `Source/Mist/ProgramModel/Model/Expressions/AssignmentExpression.cs` — target/value/result graph

**Existing regression seams:**

- `Test/Mist.Tests/ProgramAnalysis/AssignabilityDecisionTests.cs` — assignment conversion controls
- `Test/Mist.Tests/ProgramAnalysis/AssignabilitySiteTests.cs` — actual source spans
- `Test/Mist.Tests/Semantics/ContextualExpressionContractAssignmentTests.cs` — landed assignment result typing

**Acceptance and controls:**

- 7=3 and dependent parameter N=3 receive one source non-assignable-target diagnostic at the target; enumerate supported expression kinds instead of only these two examples.
- Calls, temporaries, type values, constants and other non-storage results cannot be assigned. Local/field storage, supported backing-field references, writable properties/indexers/ref-return storage retain their existing validity and distinct setter/readonly/accessibility refusals.
- Preserve ordinary assignment result type and ??= narrowing already delivered by 18; do not redesign the assignment representation or suppress unfinished ErrorType cases. Retain target/value children so independent failures remain visible.

### MIST-004 — Collection initializer Add refinement obligations

**Current evidence:**

executed CLI probe
accepted

Observed exit code: 0.

```mist
using Standard; using System.Collections; namespace Probe; public class Bag:IEnumerable { public IEnumerator GetEnumerator(){return new ArrayList().GetEnumerator();} public void Add(int value) where value > 0 {} } public class Program { public static void Main(){ var bag=new Bag(){-1}; Console.WriteLine("accepted"); } }
```

**Contract references:**

- `.kiro/specs/adhoc-refinement-constraints/requirements.md` — Requirement 6 all selected parameter refinements proved at call site; introduction rejects non-provable arguments without runtime guards
- `.kiro/specs/refinement-types/requirements.md` — existing argument/refinement admission contracts
- `Source/Mist/Grammar/Mist.storm` — CollectionElementAssignment grammar is existing supported syntax

**Owning source:**

- `Source/Mist/ProgramModel/Construction/Binding/Expressions/ObjectInitializerBinder.cs` — AddElement retains selected Add, converted value and null TargetType
- `Source/Mist/ProgramModel/Model/Expressions/NewExpression.cs` — ObjectInitializerAssignment retained member/value metadata
- `Source/Mist/ProgramAnalysis/Rules/AssignabilityAnalyzer.cs` — CheckConstruction only judges initializer TargetType
- `Source/Mist/ProgramAnalysis/Rules/RefinementAnalyzer.cs` — Proving NewExpression branch and NeedArguments
- `Source/Mist/ProgramAnalysis/Rules/RefinementContracts.cs` — selected parameter/callee proof contracts

**Existing regression seams:**

- `Test/Mist.Tests/ProgramAnalysis/RefinementSiteTests.cs` — refinement refusal at actual argument site
- `Test/Mist.Tests/ProgramAnalysis/RefinementContractRefusalTests.cs` — parameter predicate controls
- `Test/Mist.Tests/ProgramAnalysis/FlattenedArgumentRefinementTests.cs` — closed argument/parameter correspondence
- `Test/Mist.Tests/Semantics/InitializerTargetBindingTests.cs` — initializer binding fragments

**Acceptance and controls:**

- The valid IEnumerable Bag with Add(int value) where value>0 refuses collection element -1 at that element; +1 and a locally proven positive value are accepted. An unknown/non-provable value receives the established proof diagnostic; no new runtime guard is emitted.
- Carry selected Add parameter/callee identity and closed targets to the existing analysis/prover path. Cover named refined parameter types, where predicates, overload selection and constructed generic owners; retain ordinary incompatible-type and failed-overload diagnostics.
- Judge source order and each element separately without re-resolving Add in analysis. An invalid constructor must not hide independent initializer errors, and failed element/target must not create derivative proof errors.
- Keep ordinary named object assignments, collection evaluation order and the selected Add emission contract; do not add new collection initializer grammar, multi-element Add tuples, iterable protocols or generic inference policy.

**Evidence limits:**

- Fresh CLI fixture implements IEnumerable/GetEnumerator through System.Collections.ArrayList, so accepted output is not the old inconclusive non-enumerable Bag fixture.

### MIST-083 — Duplicate initializer names with nested failure controls

**Current evidence:**

executed CLI probe
<scratch-project>/out/Probe/Program.cs: error MIST_CS_ERROR: Duplicate initialization of member 'Value'

Observed exit code: 1.

```mist
using Standard; namespace Probe; public class Box{public int Value {get;set;}} public class Program{public static void Main(){var box=new Box(){Value=1,Value=2};}}
```
executed CLI probe
<scratch-project>/Program.mist(1,213): error MIST_PM_UNRESOLVED_MEMBER: 'Inner' has no member named 'Missing'.

Observed exit code: 1.

```mist
using Standard; namespace Probe; public class Inner{public int Value {get;set;}} public class Box{public Inner Child {get;set;}} public class Program{public static void Main(){var box=new Box(){Child=new Inner(){Missing=1}};}}
```

**Contract references:**

- `.kiro/specs/property-initializers/requirements.md` — Requirement 6 duplicate assignments, 3 member existence, 4 conversion and 5 writable/access checks

**Owning source:**

- `Source/Mist/ProgramModel/Construction/Binding/Expressions/ObjectInitializerBinder.cs` — Bind, AddNamed, target resolution and ordered initializer entries
- `Source/Mist/ProgramModel/Model/Expressions/NewExpression.cs` — retained initializer nodes
- `Source/Mist/ProgramAnalysis/Rules/AccessorAnalyzer.cs` — delivered setter availability checks
- `Source/Mist/ProgramAnalysis/Rules/AssignabilityAnalyzer.cs` — CheckConstruction

**Existing regression seams:**

- `Test/Mist.Tests/Semantics/InitializerTargetBindingTests.cs` — initializer lookup/access/type behavior
- `Test/Mist.Tests/Parsing/PropertyInitializerRoundTripPropertyTests.cs` — existing grammar round-trip

**Acceptance and controls:**

- new Box(){Value=1,Value=2} gets one source duplicate-assignment diagnostic at the second Value with owning source span; a duplicate initializer no longer reaches Roslyn. Duplicate detection applies per initializer block, not across separately constructed nested objects.
- Nested Child=new Inner(){Missing=1} already reports one frontend unresolved-member; preserve it as a control. Preserve supported nested object creation, valid separate same-name members in separate blocks, private/readonly/no-setter/type failures and static/not-storage diagnostics already delivered in18.
- Unknown or positively failed receiver/target keeps its owning failure and retained value subtree. Do not expand a historical blanket PI-family omission into reimplementation of completed checks or C# nested member-initializer syntax not in the grammar.

## Scope and independence

Own ordinary assignment target validity, source-valued return/TLS result contracts, per-block initializer duplicates and the missing selected Add parameter-proof discharge. Necessary compatible metadata, flow walking, diagnostics and lowering adjustments are included; use established storage/accessor and refinement decisions.

- 26/27 produce resolved names and callables. 33 consumes existing selected Add/Method/Parameter and bound value metadata without changing lookup precedence, overload algorithms, operator declarations or visible extension carrier type arguments.
- 29 owns hidden witness forwarding. 33 may retain an Add selection and its normal closed parameter targets using existing substitution fields; it does not alter hidden signatures or invent a shared API.
- Lifetime and erased-value workers own cleanup/runtime representations. 33 preserves node children, expression result contracts and evaluation order and must not redesign drop plans or optional/union carrier shapes.
- Shared AssignabilityAnalyzer/RefinementAnalyzer/new-expression data changes are local to these included proof/judgment sites; all internal ordering and metadata adjustments belong to this owner.


**Excluded work and decisions:**

- No new proof solver domains, guard policy, collection protocols or user slicing (029), unsupported nested initializer syntax, general operator/ref lowering, or witness registry redesign.
- No blanket failure suppression and no reimplementation of landed initializer access/static-target or assignment-result rules.
- The historical proposal to refuse integer TLS returns is explicitly rejected by current Requirement7; integer exit-code behavior is acceptance, not missing refusal.

## Focused verification

Initial focused commands (extend filters to your new classes; these do not replace final gates):

- `tools/test-ledger/run.sh Mist.slnx --filter "FullyQualifiedName~SourceStatementInitializationContract|FullyQualifiedName~Assignability|FullyQualifiedName~InitializerTargetBinding|FullyQualifiedName~RefinementSite|FullyQualifiedName~RefinementContractRefusal|FullyQualifiedName~FlattenedArgumentRefinement|FullyQualifiedName~TopLevelStatement|FullyQualifiedName~ContextualExpressionContractAssignment"`
- `dotnet format Mist.slnx --verify-no-changes --include Source/Mist/ProgramModel/ Source/Mist/ProgramAnalysis/ Source/Mist/Backends/CSharpTranspiler/Emitter/BodyEmitter.cs Test/Mist.Tests/`
- `git diff --check`

To reproduce inline cases, create a fresh scratch project containing `Program.mist` and this `mist.toml`, unless the case explicitly supplies an entry-point override:

```toml
name = "Probe"
namespace_root = "Probe"
```

After building current main, run `dotnet run --project Source/Mist.CLI --no-build -- run <scratch-project> --quiet` from the repository. For generated-consumer fidelity, also run `dotnet build <scratch-project>/out/Probe.csproj --no-incremental` and inspect every diagnostic. Scratch projects and generated `out/` remain untracked. Complete source snippets are reproductions; smaller prose examples are component inputs to construct in the existing narrow seams.

Owning canonical records: `MIST-005`, `MIST-222`, `MIST-004`, `MIST-083`. Each included bounded obligation requires its own verified disposition; preserve excluded or unverified residue rather than claiming the entire record fixed from one representative case.

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
