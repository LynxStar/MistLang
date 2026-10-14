# 37 — Closed operator selection and contracts

Complete this task independently, including implementation, verification, tracking updates and landing. Repository: `https://github.com/LynxStar/MistLang.git`. Target: `origin/main`. Suggested model: **Opus, high effort**. Closed generic owner/base substitutions, candidate ranking and preserving prototype versus call-site identity across the bound graph and effect consumers are substantial reasoning work. Existing substitution/overload machinery bounds the design; the packet does not need Fable-only capacity or an invented operator feature policy. These are portable user model categories, not API identifiers.

## Outcome and starting point

Resolve each supported unary/binary operator once against closed operand/owner signatures, with ordinary overload ranking and source-owned ambiguity, then carry that exact declaration, result and conversion contract through analysis and emission.

Planning baseline: `50f9fd7c0116d87a821d6a7000bff2ea6f35c27a`, 2026-10-13. Source inspection, bounded CLI/generator probes and the clean solution build used `c80c36823175514e5448fc2296bb174fbb360ec2`; the only intervening change confirms evidence on three canonical records. Storm pin and checked remote main: `83410653c7d722868a5001d2ef71e6692ec012b3`. Packets 26–33 have landed and are not reserved or rescheduled. The build passed with zero warnings/errors after using a writable temporary NuGet HTTP cache. No fresh full-suite pass is claimed.

Start from current remote `main` in an isolated task checkout; use your existing isolated checkout and retain completed work when continuing. Follow the repository's applicable instructions. Initialize submodules and verify their checked-out commits match this revision's recorded pins; a successful worktree hook alone does not prove initialization. Revalidate every included issue on current main. If already fixed, verify the acceptance cases and finish any real closeout gap without duplicating implementation. Use grammar and requirements/design as contracts; spec task documents are implemented, and retired classic source is not a reference to read or port from Git history.

For each inline Mist reproduction, build the current solution once, make a separate temporary project, and put that complete source in `Program.mist`:

```bash
packet_probe_dir=$(mktemp -d)
cat > "$packet_probe_dir/mist.toml" <<'TOML'
name = "Probe"
namespace_root = "Probe"
TOML
```

After saving the selected inline source, run from the repository root:

```bash
dotnet run --project Source/Mist.CLI --no-build -- run "$packet_probe_dir" --quiet
```

These are manual audit reproductions, not a template for new process-heavy test matrices. Scratch output is not a deliverable.

## Included obligations and acceptance

### MIST-100 — Closed owner and inherited unary/binary signatures

**Evidence 1 — executed CLI probe, `c80c36823175514e5448fc2296bb174fbb360ec2`.**

```mist
using Standard;namespace Probe;public class Tag<T>{public T Held;public Tag(T held){this.Held=held;} public static T operator +(Tag<T> left,Tag<T> right){return left.Held;}} public class Program{public static void Main(){var t=new Tag<int>(7);Console.WriteLine(t+t);}}
```

Exit 1. Observed:

```text
<scratch-project>/Program.mist(1,262): error MIST_PA_OPERATOR_NOT_APPLICABLE: Operator '+' cannot be applied to operands of type 'Tag<int>' and 'Tag<int>'.
```

**Evidence 2 — executed CLI probe, `c80c36823175514e5448fc2296bb174fbb360ec2`.**

```mist
using Standard;namespace Probe;public class Tag<T>{public T Held;public Tag(T held){this.Held=held;} public static Tag<T> operator -(Tag<T> value){return value;}} public class Program{public static void Main(){var t=new Tag<int>(7);Console.WriteLine((-t).Held);}}
```

Exit 1. Observed:

```text
<scratch-project>/Program.mist(1,252): error MIST_PA_OPERATOR_NOT_APPLICABLE: Operator '-' cannot be applied to operands of type 'Tag<int>' and 'Tag<int>'.
```

**Evidence 3 — executed CLI probe, `c80c36823175514e5448fc2296bb174fbb360ec2`.**

```mist
using Standard;namespace Probe;public class Base<T>{public static int operator -(Base<T> value){return 7;}}public class Derived:Base<int>{}public class Program{public static void Main(){Console.WriteLine(-new Derived());}}
```

Exit 1. Observed:

```text
<scratch-project>/Program.mist(1,205): error MIST_PA_OPERATOR_NOT_APPLICABLE: Operator '-' cannot be applied to operands of type 'Derived' and 'Derived'.
```

OperatorAdmissibility.Operators enumerates a constructed operand definition without applying its owner arguments, and its base loop casts BaseType directly to MemberType, stopping at constructed generic bases. SelectedUnary/Declared compare against open declared parameter types and return open declared result types. OperatorBinder calls selection and result typing separately, then converts operands against the unclosed selected ParametersList.

Contract references:

- `.kiro/specs/standard-library/requirements.md`: Requirement 6.1/6.2 searches operand declarations;6.3 ordinary overload algorithm;6.6 selected return type
- `.kiro/specs/generics-type-arguments/requirements.md`: Requirements 8/9 constructed inheritance and substituted member signatures
- `.kiro/specs/method-overloading/requirements.md`: Requirement 3 applicability and pairwise ranking; Requirement 4 source ambiguity
- `Source/Mist/ProgramModel/CLAUDE.md`: binding owns operator resolution and finished bound selections are authoritative

Implementation references:

- `Source/Mist/ProgramModel/Construction/Binding/OperatorAdmissibility.cs`: ResultType, UnaryResultType, Selected, SelectedUnary, Declared, Operators, Accepts
- `Source/Mist/ProgramModel/Construction/Binding/Expressions/OperatorBinder.cs`: MakeBinary and unary branches
- `Source/Mist/ProgramModel/Construction/Binding/TypeSubstitution.cs`: ForReceiver/ForPath and substitution composition
- `Source/Mist/ProgramModel/Construction/Binding/OverloadResolution.cs`: existing applicability/ranking controls
- `Source/Mist/ProgramModel/Model/Expressions/BinaryExpression.cs`: bound Operator/result/operand graph
- `Source/Mist/ProgramModel/Model/Expressions/UnaryExpression.cs`: bound Operator/result/operand graph

Acceptance:

- Tag<int>+Tag<int> with declared T operator+(Tag<T>,Tag<T>) binds the real declaration, converts against closed Tag<int> parameters, answers int and executes 7; -Tag<int> with Tag<T> return answers Tag<int> and its Held read executes 7. Cover different owner instantiations without cross-contamination.
- A unary operator declared on Base<T> is reachable through Derived:Base<int>, closes through that base path and executes 7. Add two-level constructed base paths and supported binary inherited candidates, preserving accessibility and declaration identity.
- Cover selected closed return types, owner parameters used multiple times, transparent aliases/refinement wrappers, declared conversions and explicit builtin/intrinsic/non-generic controls. Do not infer arbitrary unknown generic operator support or add operator members to new sources such as conformances/extensions.
- Store/use one selection result for declaration, closed parameter/result types and needed conversion metadata; result typing must not repeat independent first-candidate resolution. A positively failed operand keeps its existing cause and retained children; unfinished results remain distinguishable.

### MIST-100 — Ranking, ambiguity and model/emitted agreement

**Evidence 1 — executed CLI probe, `c80c36823175514e5448fc2296bb174fbb360ec2`.**

```mist
using Standard;namespace Probe;public class Left {public static string operator +(Left left,Right right){return "left";}}public class Right{public static string operator +(Left left,Right right){return "right";}}public class Program{public static void Main(){Console.WriteLine(new Left()+new Right());}}
```

Exit 1. Observed:

```text
<scratch-project>/out/Probe/Program.cs: error MIST_CS_ERROR: Operator '+' is ambiguous on operands of type 'Left' and 'Right'
```

**Evidence 2 — executed CLI probe, `c80c36823175514e5448fc2296bb174fbb360ec2`.**

```mist
using Standard;namespace Probe;public struct Money {public static string operator +(Money left,long right){return "wide";}public static int operator +(Money left,int right){return 7;}}public class Program{public static void Main(){Money m=default;var result=m+1;Console.WriteLine(result);}}
```

Exit 1. Observed:

```text
<scratch-project>/out/Probe/Program.cs: error MIST_CS_ERROR: Cannot implicitly convert type 'int' to 'string'
```

**Evidence 3 — executed CLI probe, `c80c36823175514e5448fc2296bb174fbb360ec2`.**

```mist
using Standard;namespace Probe;public struct Money {public static string operator +(Money left,long right){return "wide";}public static string operator +(Money left,int right){return "exact";}} public class Program{public static void Main(){Money m=default;Console.WriteLine(m+1);}}
```

Exit 0. Observed:

```text
exact
```

Declared returns the first applicable declaration from left then right. The same-result exact-versus-wide fixture happens to print exact because C# reselects; it does not prove binder ranking correct. Changing the wide result to string and exact result to int reveals the model/C# disagreement as MIST_CS_ERROR int-to-string.

Contract references:

- `.kiro/specs/standard-library/requirements.md`: Requirement 6.3 ordinary overload algorithm and6.5 ambiguity
- `.kiro/specs/method-overloading/requirements.md`: Requirement 3 exact/converted/specificity rules and4 ambiguity diagnostics

Implementation references:

- `Source/Mist/ProgramModel/Construction/Binding/OperatorAdmissibility.cs`: Declared and SelectedUnary candidate selection
- `Source/Mist/ProgramModel/Construction/Binding/Expressions/OperatorBinder.cs`: closed operand settlement
- `Source/Mist/ProgramAnalysis/Rules/OperatorAnalyzer.cs`: source refusal and failure ownership
- `Source/Mist/Backends/CSharpTranspiler/Emitter/BodyEmitter.cs`: binary/unary emission
- `Source/Mist/Backends/CSharpTranspiler/Lowering/OverloadPinning.cs`: existing selected-callee fidelity principles

Acceptance:

- Equally applicable Left.operator+(Left,Right) and Right.operator+(Left,Right) get one source ambiguity diagnostic naming the competing signatures; no Roslyn ambiguity is the owning complaint. A declaration discovered from both operands is deduplicated by semantic identity rather than creating a false tie.
- In Money+(long)->string and Money+(int)->int, m+1 selects the int candidate, the inferred local is int and executes 7; reverse declaration order and swapped discovery order do not change that outcome. Preserve the same-return exact control without falsely using its output to prove bound identity.
- Exercise exact versus established implicit/constant conversion, strict specificity and a genuinely incomparable signed/unsigned competitor using current method-overload contracts. Preserve numeric bootstrap operand/result behavior and genuine no-applicable diagnostics; do not create an ad-hoc left-operand tiebreak in a true ambiguity.
- Emission runs the selected operator and normal conversions once in source order; the generated expression/result type agrees with the model. Do not rely on target-language selection to repair missing binding decisions.

### MIST-100 — Preserve selected declaration effects and closed consumer contracts

**Evidence 1 — current source inspection, `c80c36823175514e5448fc2296bb174fbb360ec2`.**

Binary/Unary carry Operator declaration; PurityJudge/ReadonlyInference/FormattingCallbacks/ContextCalls already consume that declaration. FormattingCallbacks.ThroughOperands finds a direct constructed owner but cannot substitute a distant constructed base path it does not receive. This is a required propagation/control surface once constructed operators are admitted; no fresh effect-specific CLI probe is claimed.

Contract references:

- `Source/Mist/ProgramModel/CLAUDE.md`: bound selection authoritative
- `.kiro/specs/purity/requirements.md`: operator/call effect judgments follow actual selected declarations
- `.kiro/specs/readonly-contexts/requirements.md`: existing readonly call/formatting promises; preserve current contract

Implementation references:

- `Source/Mist/ProgramAnalysis/PurityJudge.cs`: BinaryExpression/UnaryExpression declared operator cases
- `Source/Mist/ProgramAnalysis/ReadonlyInference.cs`: selected binary/unary callable edges
- `Source/Mist/ProgramAnalysis/FormattingCallbacks.cs`: Uses, ThroughOperands and inherited closing
- `Source/Mist/ProgramAnalysis/ContextCalls.Sites.cs`: operator context call-site recognition

Acceptance:

- Preserve source/prototype identity for effect inference and carry the closed owner/base path to consumers needing concrete parameter/result types. Do not make a synthetic closed operator look like a separate unrelated callable body.
- At hand-built seams, a selected readonly/pure operator retains its promises, a selected mutable/impure operator remains refused where existing caller contracts prohibit it, and formatting of a closed generic operand uses that actual formatter type and correct inherited substitution. Independent operators sharing names must not poison each other.
- Preserve existing context-call and promotion behavior already admitted for operators; do not invent a hidden-witness/async/ref operator ABI or reschedule prior witness work. Necessary compatible consumer metadata edits belong to this owner.
- Closed selection metadata retains the actual source Operator/Method identity and enclosing/base substitution for storage-effect consumers. Add an any-order hand-built control for a generic wrapper invoking a selected T-storage writer versus reader, including renamed/inherited owner parameters. Do not replace the source body with a bodyless synthetic callable or implement packet 34's storage-summary fixed point.

## Independence and ownership

- Fable 34 owns readonly generic storage member filtering and source-to-erased storage semantics. 37 owns static unary/binary operator selection on known operand types and preserves existing member filtering/readonly guarantees; it does not invent readonly views, widen unknown storage, change storage reification or need 34 to publish a new carrier/API. Consume the existing IMistType/TypeSubstitution contract; keep unknown/error admission/refusal behavior under its existing owner.
- 35 owns structural containment proofs; operator selection does not change pattern domains. 36 owns slice/index/length protocol selections and can use unchanged generic substitution/overload APIs. 37 uses existing APIs internally without requiring a new public selection representation for 36.
- 38 alone owns Storm source/publication and Mist gitlink. 37 changes no grammar or generated AST/public dependency API; preserve any newer compatible pin when landing.

Own closed operator candidates/choices, unary/binary result and conversion settlement, source ambiguity ownership, selected-declaration metadata and necessary analysis/emission consumers. Do not reimplement27 duplicate/modifier legality.

This packet has no dependency on another workstream. As its parent session, you own every included task and its internal dependencies. Determine the execution order, decide which work to perform yourself or dispatch to subagents, and integrate their changes into your primary implementation. Resolve internal conflicts and verify the combined result; subagents return contributions to you rather than independently publishing partial work to the shared target. Use current repository contracts. Do not hand internal ordering, coordination or integration to the user, the planning session or another packet. Complete and close out every included item; a representative subset or a collection of subagent reports is not completion.

Allow narrowly necessary adjacent edits, including compatible bound metadata, consumers, generated artifacts and user/API/spec documentation needed to finish this outcome. Keep changes coherent and preserve already landed behavior. Shared files require narrow integration, not another packet's unpublished branch, service, test helper or design decision.

Excluded scope:

- MIST-058/060 operator static-abstract/conformance/extension discovery policy remains unestablished; MIST-079 ref operator parameter lowering/refusal is not selected.
- Do not add089 variadic method-group adapters without a settled declared tuple-slot versus flattened function-signature contract. Do not enlarge081 into a historical variadic parity checklist.
- MIST-096 distant generic struct fixture now prints value; variadic method inside variadic type081 prints 3. Keep as delivered controls or precise historical ledger disposition, not missing primary work.
- No broad builtin operator subsystem rewrite, new operator syntax, shared solver/API migration or backend-only workaround that leaves binding unclosed.

## Incidental fixes

If you uncover additional issues with clear expected behavior that are straightforward to fix and verify without user input, solve them in additional commits. This includes small fixes outside your primary files or objective; do not merely surface them as extras or make the user dispatch another task. Add appropriate verification and update actual documentation/backlog entries where relevant. Keep each coherent incidental fix separately committed and include it in the same landing workflow.

Avoid duplicating work already landed or assigned to an active workstream. Defer only when the finding requires a genuine user decision, substantial new work, unavailable access/authorization, or verification you cannot complete; state the concrete reason. Ordinary implementation choices and arbitrary file boundaries do not require user input. Finish the primary task and do not turn incidental cleanup into an open-ended campaign.

## Verification and closeout

Add Tier 1 closed candidate/ranking/result identity decisions and Tier 2 binder/analyzer/emitter fragments, including source spans and declaration identity. Stub unrelated providers/resolvers. Reuse bounded existing model-to-emitter seam to prove selected contract reaches generated C#; use the complete portable probes only as manual reproduction or existing higher-boundary controls, not as a new full-pipeline unit-test matrix.

Existing verification anchors (inspect their actual helpers/costs before extending):

- `Test/Mist.Tests/ProgramAnalysis/OperatorPositionRefusalTests.cs`: direct operator/position refusals
- `Test/Mist.Tests/ProgramAnalysis/OperatorPositionSiteTests.cs`: hand-built body source ownership
- `Test/Mist.Tests/ProgramAnalysis/OperatorDecisionTests.cs`: legacy component controls; inspect helper cost before choosing tier
- `Test/Mist.Tests/Backend/OperatorEmissionTests.cs`: supported declaration/expression emission controls
- `Test/Mist.Tests/ProgramAnalysis/ReadonlyFormattingClosingTests.cs`: closed formatting judgments
- `Test/Mist.Tests/ProgramAnalysis/FormattingCallbackDecisionTests.cs`: callback identity decisions

Focused commands:

```bash
tools/test-ledger/run.sh Test/Mist.Tests --filter "FullyQualifiedName~ClosedOperator|FullyQualifiedName~OperatorPosition|FullyQualifiedName~OperatorDecision|FullyQualifiedName~OperatorEmission|FullyQualifiedName~ReadonlyFormattingClosing|FullyQualifiedName~FormattingCallback"
```

```bash
dotnet format Mist.slnx --verify-no-changes --include Source/Mist/ProgramModel/ Source/Mist/ProgramAnalysis/ Source/Mist/Backends/CSharpTranspiler/ Test/Mist.Tests/
```

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

Update `MIST-100` in the actual `backlog/issues.yaml` as part of this change. Record implemented behavior, supporting tests/evidence and any precise remaining limitation. Preserve historical evidence and other workers' updates; keep edits to owning records narrow. Close only completed portions and move fully discharged records to the archive with their stable IDs. Do not merely return proposed closeout text or create a new tracking system. Update necessary contract/API/book documentation and generated artifacts in their owning files.

## Commit and land

You are authorized to commit and publish this task directly to remote `main` without another confirmation. Do not create pull requests. Commit implementation, appropriate tests/generated artifacts, required documentation and actual closeout updates together or in coherent task commits. Fetch and incorporate the latest target branch, resolve conflicts while preserving other landed work, and perform the required verification on the integrated result. Use a normal, non-force push. Work from your own isolated checkout; do not reset or commandeer another session's working tree.

Other workers may land concurrently. If publication is rejected because the target advanced, fetch, reconcile, rerun verification affected by the new combined result, and retry. Keep shared-register edits narrow. You own routine integration; do not hand it to the user or assume a central integrator. Do not force-push the target, bypass protections, silently accept failures or mark unfinished work complete. Preserve newer compatible Storm pins already incorporated by main.

Finish by fetching and confirming that the intended changes, additional incidental fixes and actual closeout are present on remote `main`. Report landed commit IDs, actual verification results, entries updated, completed incidental fixes and any remaining limitations. A local commit or pushed task branch alone is not completion.

If unavailable credentials or branch protection prevents landing, complete all unaffected work, preserve the result durably, and report the exact blocker and smallest required user action. Do not switch to a PR workflow. If a genuine unresolved product decision prevents implementation, identify it precisely and retain the affected item as open. Do not repeat an unchanged failed attempt indefinitely or claim success without verification.
