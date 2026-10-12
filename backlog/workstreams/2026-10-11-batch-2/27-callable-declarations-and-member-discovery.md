# 27 — Callable declarations and member discovery

Complete this bundle independently, including verification, actual tracking updates, commits and publication. Repository: `https://github.com/LynxStar/MistLang.git`; target: remote `main`. Suggested model tier: **Sol/Opus**, effort: **high**. Signature equality, diagnostic ownership and closed extension carrier identity cross declaration analysis, lookup and C# lowering. The work is finite and uses existing bound Method/BlockClosing metadata, not a new inference or witness API. These portable labels guide the same task and do not require a separate reviewer or model change.

## Starting point

Planning baseline: `0428515879b2d0245c996a5db98a676e441b9cbe` (2026-10-11). Compiler/source probes and the fresh solution build used `b1e556fb9d9528367f7de6bd7d71a13bd5c28aad`; intervening commits only reconcile canonical tracking evidence/contracts, with no source or test changes. Storm pin: `83410653c7d722868a5001d2ef71e6692ec012b3`. The build passed with zero warnings/errors. No fresh full-suite pass is claimed. Evidence below distinguishes executed probes, current source inspection and leads needing validation. Previous packets 17–25 have landed; revalidate current main rather than repeating those implementations.

Start from current remote main in an isolated checkout, retaining your existing isolated checkout and completed work when continuing. Follow the repository’s applicable instructions. Initialize with `git submodule update --init --recursive` and verify checked-out commits match current main’s recorded pins. Revalidate each item on current main; if already fixed, verify acceptance and complete any real closeout gap without duplicate code. Do not read or port retired classic implementation from Git history. Spec task documents are implemented and excluded from work discovery.

## Outcome and included obligations

Give declared callables a valid source signature/modifier contract and keep supported universal/static extension members reachable with the exact selected method and closed receiver-block type arguments through emission.

### MIST-007 — Duplicate operator and conversion signatures

**Current evidence:**

executed CLI probe
<scratch-project>/out/Probe/Program.cs: error MIST_CS_ERROR: Type 'Money' already defines a member called 'op_Addition' with the same parameter types

Observed exit code: 1.

```mist
using Standard; namespace Probe; public struct Money { public static Money operator +(Money a,Money b) { return a; } public static Money operator +(Money a,Money b) { return b; } } public class Program { public static void Main() {} }
```
executed CLI probe
<scratch-project>/out/Probe/Program.cs: error MIST_CS_ERROR: Duplicate user-defined conversion in type 'Money'

Observed exit code: 1.

```mist
using Standard; namespace Probe; public struct Money { public static implicit operator int(Money a){return 1;} public static implicit operator int(Money a){return 2;} } public class Program{public static void Main(){}}
```

**Contract references:**

- `.kiro/specs/standard-library/requirements.md` — Requirement 4.6 operators registered by symbol and parameter types
- `.kiro/specs/method-overloading/requirements.md` — Requirement 1.2/1.4 duplicate parameter signatures; return types do not create overloads
- `.kiro/specs/cast-operators/requirements.md` — Requirement 6.5 duplicate same-kind source/target cast pair

**Owning source:**

- `Source/Mist/ProgramAnalysis/Rules/DeclarationShapeAnalyzer.cs` — CheckOperators, CheckSignatures, DeclaredMembers, CheckRepeats
- `Source/Mist/ProgramModel/Construction/Members/OperatorBuilder.cs` — registered Operator entities
- `Source/Mist/ProgramModel/Construction/Members/CastOperatorBuilder.cs` — registered CastOperator entities

**Existing regression seams:**

- `Test/Mist.Tests/ProgramAnalysis/DeclarationShapeRefusalTests.cs` — existing owning signature refusals
- `Test/Mist.Tests/ProgramAnalysis/DeclarationShapeDecisionTests.cs` — deliberate admissions
- `Test/Mist.Tests/ProgramAnalysis/CastDecisionTests.cs` — conversion controls
- `Test/Mist.Tests/Backend/OperatorEmissionTests.cs` — supported declaration lowering

**Acceptance and controls:**

- Duplicate operators with the same operator symbol/parameter signature receive one source duplicate-signature finding at the later declaration, with both declarations identifiable, before backend compilation. Different return types alone do not create valid operator overloads.
- Duplicate cast operators of the same kind and same source-target pair receive the specified source finding; distinct supported source-target pairs remain admitted. Do not invent a blanket ban on every implicit/explicit pair from this same-kind contract.
- Preserve legitimate unary/binary overload distinction, differing parameter types, complement-pair checks, declaring-type operand checks, accessibility and intrinsic controls. Positively failed parameter types must not create cascaded duplicates.

### MIST-026 — Operator modifier legality

**Current evidence:**

executed CLI probe

Observed exit code: 0.

```mist
using Standard; namespace Probe; public struct Money { public static sealed Money operator +(Money a,Money b) { return a; } } public class Program{public static void Main(){}}
```

**Contract references:**

- `.kiro/specs/standard-library/requirements.md` — Requirement 4.2 public static operator form; Requirements 4.10/5 intrinsic/ordinary member distinctions
- `.kiro/specs/purity/requirements.md` — existing pure callable effects, if applicable; preserve Standard declarations

**Owning source:**

- `Source/Mist/ProgramModel/Construction/Members/DeclarationModifierPlacement.cs` — Refusals and OperatorDeclarationNode placement
- `Source/Mist/ProgramModel/Construction/Members/OperatorBuilder.cs` — existing missing static/async checks
- `Source/Mist/ProgramAnalysis/Rules/DeclarationShapeAnalyzer.cs` — CheckOperators publicness and complements

**Existing regression seams:**

- `Test/Mist.Tests/Construction/DeclarationModifierPlacementTests.cs` — direct syntax/modifier decisions
- `Test/Mist.Tests/Construction/ModifierPlacementBuildTests.cs` — registration refusals
- `Test/Mist.Tests/Parsing/OperatorAndIntrinsicGrammarTests.cs` — existing operator/intrinsic shapes

**Acceptance and controls:**

- Source-refuse unsupported sealed/readonly/owned/affine operator declaration modifiers; verify the exact modifier cases admitted by the grammar rather than relying on the single sealed probe. Anchor each owner at the modifier/declaration.
- Preserve accepted public static operators, existing pure effect modifiers and Standard intrinsic public pure static operators. Do not mechanically treat the words public/static as excluding already specified intrinsic/purity metadata.
- Existing missing-static, async and ref-return refusals remain singular; this task does not decide ref operator parameter lowering or static abstract/conformance operator support.

### MIST-034 — Universal parameterless ToString alongside overloads

**Current evidence:**

executed CLI probe
<scratch-project>/Program.mist(1,192): error MIST_PM_NO_APPLICABLE_OVERLOAD: There is no overload of method 'ToString' accepting 0 argument(s). Declared arities: 1.

Observed exit code: 1.

```mist
using Standard; namespace Probe; public class Temp { public string ToString(string format) { return format; } } public class Program { public static void Main() { Console.WriteLine(new Temp().ToString()); } }
```
executed CLI probe
<scratch-project>/Program.mist(1,239): error MIST_PM_NO_APPLICABLE_OVERLOAD: There is no overload of method 'ToString' accepting 0 argument(s). Declared arities: 2.

Observed exit code: 1.

```mist
using Standard; using System; namespace Probe; public class Temp:IFormattable{public string ToString(string format,IFormatProvider provider){return "formatted";}} public class Program{public static void Main(){Console.WriteLine(new Temp().ToString());}}
```

**Contract references:**

- `.kiro/specs/extension-methods/requirements.md` — Requirements 9 universal candidates and 23.4/23.5 parameterless ToString on any value
- `Source/Mist/ProgramModel/CLAUDE.md` — binding owns method selection and bound identity is authoritative

**Owning source:**

- `Source/Mist/ProgramModel/Construction/Binding/MemberResolution.cs` — Resolve ordinary member/extension candidates
- `Source/Mist/ProgramModel/Construction/Binding/ExtensionLookup.cs` — Find and universal receiver applicability
- `Source/Mist/ProgramModel/Construction/Binding/Expressions/PostfixBinder.cs` — BindMemberAccess, BindInvocation
- `Source/Mist/Backends/CSharpTranspiler/Lowering/ExtensionCalls.cs` — object-shape ToString exception
- `Source/Mist/Backends/CSharpTranspiler/Lowering/InvariantCulture.cs` — existing formatting behavior

**Existing regression seams:**

- `Test/Mist.Tests/Semantics/ExtensionLookupTests.cs` — own member versus universal extension lookup
- `Test/Mist.Tests/Backend/InvariantCultureEmissionTests.cs` — existing invariant text controls

**Acceptance and controls:**

- The complete plain Temp.ToString(string) and IFormattable Temp.ToString(string,IFormatProvider) fixtures retain a callable zero-argument surface; their explicit formatting overloads stay callable at their declared arities.
- Preserve a user-declared zero-argument ToString implementation and existing universal/invariant-culture formatting semantics on classes, primitives and unknown.
- Do not change general own-member-versus-extension lookup precedence under unrelated names or choose the unresolved CLR-shadowing policy. This is the specified universal zero-argument contract.

### MIST-066 — Static extension members in generic blocks

**Current evidence:**

executed CLI probe
<scratch-project>/Program.mist(1,228): error MIST_PM_METHOD_GROUP_NOT_CONVERTED: 'Make' names a method group, which is only valid where it is invoked or where a function type is expected whose signature one of its overloads has.

Observed exit code: 1.

```mist
using Standard; namespace Probe; public class Animal {} public class Box<T> {} <T> extension(Box<T> self) { public static int Make<U>(U item) { return 1; } } public class Program { public static void Main() { Console.WriteLine(Box<Animal>.Make<int>(1)); } }
```
executed CLI probe
<scratch-project>/out/Probe/Program.cs: error MIST_CS_ERROR: The type arguments for method 'BoxExtensions_Program_0.Make<T>(int)' cannot be inferred from the usage. Try specifying the type arguments explicitly.

Observed exit code: 1.

```mist
using Standard; namespace Probe; public class Box<T>{} <T> extension(Box<T> self){ public static int Make(int item){return 1;} } public class Program{public static void Main(){Console.WriteLine(Box<int>.Make(1));}}
```

**Contract references:**

- `.kiro/specs/extension-methods/requirements.md` — Requirements 12 block closing, 13 static member lookup, 16 generic emission, 19 static carrier invocation
- `.kiro/specs/generics-type-arguments/requirements.md` — Requirement 4 explicit generic method calls
- `Source/Mist/ProgramModel/CLAUDE.md` — resolved overload and closed selections are authoritative

**Owning source:**

- `Source/Mist/ProgramModel/Construction/Binding/Expressions/PostfixBinder.cs` — generic type member access, generic invocation and postfix call routing
- `Source/Mist/ProgramModel/Construction/Binding/ExtensionLookup.cs` — Find static members and Closed block arguments
- `Source/Mist/ProgramModel/Construction/Binding/ExtensionSpecialization.cs` — Close, ClosedBlock/BlockClosing and SourceDeclaration
- `Source/Mist/Backends/CSharpTranspiler/Lowering/ExtensionCalls.cs` — carrier selection
- `Source/Mist/Backends/CSharpTranspiler/Emitter/BodyEmitter.cs` — static extension invocation argument spelling
- `Source/Mist/Backends/CSharpTranspiler/Emitter/ExtensionBlockEmitter.cs` — carrier generic parameters

**Existing regression seams:**

- `Test/Mist.Tests/Backend/StaticExtensionCarrierCallTests.cs` — hand-built promoted/generic static carrier controls
- `Test/Mist.Tests/Backend/ExtensionCarrierEmissionTests.cs` — closed block and selected-carrier preservation
- `Test/Mist.Tests/Construction/ExtensionSpecializationTests.cs` — closure identities
- `Test/Mist.Tests/Semantics/ExtensionLookupTests.cs` — static visibility and scope

**Acceptance and controls:**

- Box<Animal>.Make<int>(1) over <T> extension(Box<T>) with static Make<U>(U) binds/invokes exactly the selected callable, instead of settling the named method group before its invocation.
- Box<int>.Make(1) over a generic block and a nongeneric method emits the actual carrier Make<int>(1); no block type argument is dropped just because the method has no own type parameters.
- Cover nongeneric receiver blocks, explicit/inferred method arguments, two distinct receiver instantiations, namespace visibility, ordinary own static member shadowing, and rejection of receiver-instance access from a static member.
- Use existing selected Method/SourceDeclaration/ClosedBlock/BlockClosing metadata through the whole path; preserve async promotion and existing hidden argument ordering. Do not require packet 29 to invent an API or redo its witness signatures.

## Scope and independence

Own operator/cast source legality, the bounded universal ToString candidate surface, static extension call routing and explicit block/method generic argument preservation. Necessary compatible adjacent lookup, diagnostic and carrier emission changes are included. No operator conformance/discovery policy expansion.

- 26 supplies the represented receiver and qualified names. 27 consumes an already resolved named type; every 066 fixture uses Box<T> named spelling and can finish without primitive keyword static lookup.
- 29 owns hidden context/default/constructor witness forwarding. 27 owns visible closed block/method type arguments and selected callable identity using existing metadata, and preserves existing hidden metadata without changing its public contract.
- 33 owns finished-body assignment/return/initializer judgments; operator declarations and ordinary callable lookup do not require its diagnostics or initializer graph changes.
- Common DeclarationShapeAnalyzer, PostfixBinder and BodyEmitter changes may create textual conflicts, but obligations lie in different arms/helpers and introduce no shared unpublished representation. Each worker integrates narrow edits against current main.


**Excluded work and decisions:**

- `MIST-058, MIST-060`: Current source omissions do not establish accepted operator/static abstract slots or operator discovery from extension/conformance contracts. Keep open pending explicit language contract; do not invent these features.
- `MIST-079`: Fresh ref operator parameter declaration/invocation still reaches Roslyn, but ref-parameters Requirement 1.2 is a parser acceptance clause while its call/semantic clauses describe methods; an operator lowering/call-site ref contract was not established. Do not silently convert this into either support or blanket parameter refusal.
- `MIST-069`: Fresh string.Contains direct/group fixture executes CLR True/True; it does not demonstrate Mist selected the extension. Preserve bound-callee controls but do not schedule a CLR-shadowing group policy from this probe.
- `MIST-089`: Flat and tuple function targets both refuse current Count<params TArgs>, but the flat-call convention versus one declared tuple signature is not sufficiently specified for a new method-group adapter contract in this audit. Ordinary generic groups are already supported and not rescheduled.

## Focused verification

Initial focused commands (extend filters to your new classes; these do not replace final gates):

- `tools/test-ledger/run.sh Mist.slnx --filter "FullyQualifiedName~CallableDeclarationContract|FullyQualifiedName~DeclarationModifierPlacement|FullyQualifiedName~DeclarationShape|FullyQualifiedName~OperatorEmission|FullyQualifiedName~CastDecision|FullyQualifiedName~ExtensionLookup|FullyQualifiedName~ExtensionSpecialization|FullyQualifiedName~StaticExtensionCarrierCall|FullyQualifiedName~ExtensionCarrierEmission|FullyQualifiedName~InvariantCulture"`
- `dotnet format Mist.slnx --verify-no-changes --include Source/Mist/ProgramModel/ Source/Mist/ProgramAnalysis/Rules/DeclarationShapeAnalyzer.cs Source/Mist/Backends/CSharpTranspiler/ Test/Mist.Tests/`
- `git diff --check`

To reproduce inline cases, create a fresh scratch project containing `Program.mist` and this `mist.toml`, unless the case explicitly supplies an entry-point override:

```toml
name = "Probe"
namespace_root = "Probe"
```

After building current main, run `dotnet run --project Source/Mist.CLI --no-build -- run <scratch-project> --quiet` from the repository. For generated-consumer fidelity, also run `dotnet build <scratch-project>/out/Probe.csproj --no-incremental` and inspect every diagnostic. Scratch projects and generated `out/` remain untracked. Complete source snippets are reproductions; smaller prose examples are component inputs to construct in the existing narrow seams.

Owning canonical records: `MIST-007`, `MIST-026`, `MIST-034`, `MIST-066`. Each included bounded obligation requires its own verified disposition; preserve excluded or unverified residue rather than claiming the entire record fixed from one representative case.

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
