# 41 — Generic self construction

Complete this task independently, including implementation, verification, actual tracking updates and landing. Repository: `https://github.com/LynxStar/MistLang.git`. Target: `origin/main`. Suggested model: **Fable, high effort**, using the user's remaining subscription window. This is also Opus-capable; no Fable-only capability is claimed. A bounded generic receiver correction with careful identity and substitution controls. No frontier-only complexity is claimed; correct self construction must cover ordinary, nested and pack owners without changing selected operator/call/effect contracts.

## Outcome and starting point

Inside an instance body, this carries the innermost declaring type applied to its own type parameters and the enclosing construction. Ordinary and pack-generic calls, conversions, member access, method groups and selected operators consume that same type without reconstructing or replacing source symbols.

Planning baseline: `4b7b5cc5a3acf0e4f826685e10ed7b25292fb837`, 2026-10-15. Current source inspection, bounded CLI reproductions and a zero-warning/error solution build used `0f41d2b8ea4a4576a8a5fedc28ad091f645c7618`; the intervening change only adds current evidence to MIST-054/057/068. Storm pin: `83410653c7d722868a5001d2ef71e6692ec012b3`. No fresh full-suite pass is claimed. Packets 34, 36 and 37 are landed. Packets 35, 38 and 39 are reserved and must not be duplicated.

Start from current remote `main` in an isolated task checkout, reusing an existing checkout or Git worktree/object database where available. Preserve completed work when continuing. Follow the repository's applicable instructions. Initialize submodules and verify their checked-out commits match this revision's recorded pins. Revalidate every included issue on latest main. If already fixed, verify acceptance and finish any real closeout gap without duplicating implementation. Spec task documents are implemented; use requirements/design and grammar as contracts. Do not read or derive code from retired classic source in Git history.

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

## Contracts and current evidence

- `.kiro/specs/generics-type-arguments/requirements.md`: Requirements 6, 7, 9.1-9.4, 11.1/11.3/11.4/11.5, 12; A generic body resolves its declaring parameters; constructed identity and member substitution remain consistent through calls, inherited conversions and emitted type references.
- `.kiro/specs/variadics/requirements.md`: Requirements 3.1, 4.3-4.4, 5, 6.8, 7, 12.1/12.6-12.8; A pack is one dependent Type[] slot. The open self construction must preserve that slot, not turn the domain Type[] or a type parameter object into the wrong runtime generic argument. Empty and nonempty pack specializations remain distinct.
- `.kiro/specs/class-inheritance/requirements.md`: Requirement 4.5; generic inheritance requirements take precedence for constructed bases; this accesses inherited members through the same receiver/base path as other instances.
- `.kiro/specs/abstract-type-unification/requirements.md`: Requirement 15.4-15.5; Default bodies resolve this against the declared abstract hierarchy; include as a bounded receiver-typing control, not DIM redesign.

- `Source/Mist/ProgramModel/Model/Scopes/TypeScope.cs`: TypeScope.LookupSelf; Returns ReceiverResult(memberType) for this, using the declaration definition rather than its self construction.
- `Source/Mist/ProgramModel/Construction/Binding/Expressions/ExpressionBinder.cs`: BindThis; Copies receiver.Type directly into ThisExpression; ordinary call conversion and operator applicability see the bare receiver.
- `Source/Mist/ProgramModel/Model/Types/ConstructedType.cs`: ConstructedType.Enclosing; Nested non-generic definitions under a generic enclosing owner are constructions too; enclosing substitution participates in identity.
- `Source/Mist/ProgramModel/Construction/Binding/TypeSubstitution.cs`: ForReceiver; ForPath; ForMethod; Existing maps key actual TypeParameter objects and compose enclosing/base paths. Preserve this contract and source Method identities.
- `Source/Mist/Backends/CSharpTranspiler/Lowering/RespelledGroups.cs`: OpenOwner; Backend has an ordinary generic open-owner spelling helper; it is a consumer reference, not authority for canonical pack argument representation.
- `Source/Mist/ProgramModel/Model/Scopes/ReceiverScope.cs`: LookupSelf; Receivers intentionally supplied by surrounding lowering/construction already keep their type; do not replace them with nominal declaring type.

### Reproduction — self-method

```mist
using Standard;namespace Probe;public class Tag<T>{public T Held;public Tag(T held){this.Held=held;}public static Tag<T> Same(Tag<T> other){return other;}public Tag<T> Me(){return Same(this);}}public class Program{public static void Main(){Console.WriteLine(new Tag<int>(7).Me().Held);}}
```

Observed exit 1:

```text
<scratch-project>: error MIST_PA_NO_IMPLICIT_CONVERSION: Cannot implicitly convert type 'Tag' to 'Tag<T>'.
```

### Reproduction — self-pack

```mist
using Standard;namespace Probe;public class Bundle<params Ts>{public Bundle(params Ts args){} public static Bundle<Ts...> Same(Bundle<Ts...> value){return value;}public Bundle<Ts...> Me(){return Same(this);}}public class Program{public static void Main(){new Bundle<int,string>(7,"v").Me();Console.WriteLine("ok");}}
```

Observed exit 1:

```text
<scratch-project>: error MIST_PA_NO_IMPLICIT_CONVERSION: Cannot implicitly convert type 'Bundle' to 'Bundle<[Ts...]>'.
```

### Reproduction — self-nested-valid

```mist
using Standard;namespace Probe;public class Outer<T>{public class Inner<U>{public static Inner<U> Same(Inner<U> value){return value;}public Inner<U> Me(){return Same(this);}}public Inner<string> Make(){return new Inner<string>();}}public class Program{public static void Main(){new Outer<int>().Make().Me();Console.WriteLine("ok");}}
```

Observed exit 1:

```text
<scratch-project>: error MIST_PA_NO_IMPLICIT_CONVERSION: Cannot implicitly convert type 'Inner' to 'Inner<U>'.
```

### Reproduction — self-typed-control

```mist
using Standard;namespace Probe;public class Tag<T>{public T Held;public Tag(T held){this.Held=held;}public static Tag<T> Same(Tag<T> other){return other;}public Tag<T> Me(Tag<T> typed){return Same(typed);}}public class Program{public static void Main(){var t=new Tag<int>(7);Console.WriteLine(t.Me(t).Held);}}
```

Observed exit 0:

```text
7
```

### Reproduction — self-inherited

```mist
using Standard;namespace Probe;public class Base<T>{public static Base<T> Same(Base<T> value){return value;}}public class Child<T>:Base<(T,int)>{public Base<(T,int)> Me(){return Same(this);}}public class Program{public static void Main(){new Child<string>().Me();Console.WriteLine("ok");}}
```

Observed exit 0:

```text
ok
```

Ordinary, variadic and generic nested self mismatches are current executed failures. Typed ordinary receiver and reordered/nested constructed-base receiver controls currently succeed. Generic struct/default-body/clone/extension and method-group cases are bounded regression controls grounded in current contracts, not claimed failing reproductions.

## Scope and acceptance

- Ordinary Tag<T>.Me returning Same(this) compiles and the fresh baseline reproduction prints 7 after the fix. Returning this directly and assigning it to a same-self local must agree with passing an already typed Tag<T> parameter.
- Bundle<params Ts>.Me returning Same(this) compiles for empty, one, two and at least nine argument widths; the existing two-element reproduction prints ok. Preserve canonical symbolic pack slots until specialization rather than flattening the model into emission-width parameters.
- The executed generic nested fixture using Outer<T>.Make() and Inner<U>.Me() must print ok. For a generic nested type and for a non-generic nested type under Outer<T>, this has its own definition with the correct enclosing construction; it never answers as the enclosing outer instance. Distinct owners with ordinal-zero parameters remain distinct, including method/type parameters with the same name.
- Preserve the executed passing Child<T>:Base<(T,int)> inherited-call control, which prints ok already. For generic class and struct receiver conversions through an existing constructed base, this and an explicitly typed self parameter produce equivalent substituted parameter/result types and keep the same source selected Method or Operator identity. Use reordered/nested base arguments as a control, without rewriting ancestor conversion lowering.
- Explicit this.member access, unqualified existing member access, instance method group conversion, property/accessor bodies and generic abstract default bodies agree on the receiver type where already admitted. Constructor bodies use the same instance contract; static methods, static initializers and outside-type this retain their current refusals.
- Keep implicit operators/conversions and effect ownership unchanged: this+this in Tag<T> may become applicable because its operands are correctly typed, but the existing selected closed operator, source identity, ambiguity/ranking and effect summaries are consumed unchanged.
- Exercise explicitly supplied ReceiverScope (extension/newtype body receiver) as regression control; generic clone receiver identity must not revert to its source definition. Do not expand cloning or extension inference policies.

Implementation references:

- `Source/Mist/ProgramModel/Model/Scopes/TypeScope.cs`
- `Source/Mist/ProgramModel/Model/Scopes/ConstructionScope.cs`
- `Source/Mist/ProgramModel/Model/Types/ConstructedType.cs`
- `Source/Mist/ProgramModel/Construction/Binding/Expressions/ExpressionBinder.cs`
- `Source/Mist/ProgramModel/Construction/Binding/TypeSubstitution.cs`

Change other frontend consumers only where a demonstrated this-self mismatch survives correct receiver construction. Avoid duplicate open-owner recipes in backend consumers; any reuse must preserve layer boundaries and the immutable finished model.

Do not manufacture replacement Methods, Operators or TypeParameters to satisfy conversions. Preserve the source declaration, actual closed type argument list and enclosing/base substitution path.

## Independence and ownership

Own generic instance-expression self construction, including enclosing and symbolic pack slots. Preserve supplied ReceiverScope types and source Method/Operator/TypeParameter identity. Landed 34/36/37 keep ownership/effects, slicing and operator ranking; consume them unchanged. Reserved 35/38/39 own proof, generator and metadata domains. Packet 42 owns zero-argument eligibility/default storage/implicit packed argument realization. Shared ExpressionBinder edits are receiver binding versus SelectConstructor, with narrow normal integration and no new cross-packet API.

This packet has no dependency on another workstream. As its parent session, you own every included task and its internal dependencies. Determine the execution order, decide which work to perform yourself or dispatch to subagents, and integrate their changes into your primary implementation. Resolve internal conflicts and verify the combined result; subagents return contributions to you rather than independently publishing partial work to the shared target. Use current repository contracts. Do not hand internal ordering, coordination or integration to the user, the planning session or another packet. Complete and close out every included item; a representative subset or a collection of subagent reports is not completion.

Allow narrowly necessary adjacent edits, including compatible bound metadata, consumers, generated artifacts and user/API/spec documentation needed to finish this outcome. Keep changes coherent and preserve already landed behavior. Shared files require narrow integration, not another packet's unpublished branch, service, test helper or design decision.

Excluded scope:

- MIST-057 implicit chain eligibility/materialization
- MIST-059 tuple pattern closure validation and all containment proof work
- MIST-081 blanket residual list: no extension tuple-pack receiver inference, base-list spread implementation, constructor-chain spread redesign or method group calling-convention policy
- MIST-096 ancestor emission rewrite: current bounded nested-argument distant ancestor control passes
- MIST-089 method-group pack convention; MIST-058/060 operator slot/discovery policy
- No CLI lifecycle, Storm generator, unknown effect, slicing, operator ranking, or defaults refactor

## Incidental fixes

If you uncover additional issues with clear expected behavior that are straightforward to fix and verify without user input, solve them in additional commits. This includes small fixes outside your primary files or objective; do not merely surface them as extras or make the user dispatch another task. Add appropriate verification and update actual documentation/backlog entries where relevant. Keep each coherent incidental fix separately committed and include it in the same landing workflow.

Avoid duplicating work already landed or assigned to an active workstream. Defer only when the finding requires a genuine user decision, substantial new work, unavailable access/authorization, or verification you cannot complete; state the concrete reason. Ordinary implementation choices and arbitrary file boundaries do not require user input. Finish the primary task and do not turn incidental cleanup into an open-ended campaign.

## Verification and closeout

Use direct scope/self-construction inputs and identity comparisons at Tier 1–2. Use stubbed binder component inputs only for the domain being tested, and an existing bounded self-construction-to-emission seam where necessary. Pack widths are semantic shape cases, not permission to compile a separate program at every width. Preserve existing inherited/member/operator/effect controls.

Existing verification anchors (inspect actual helpers/costs before extending):

- `Test/Mist.Tests/Semantics/ConstructedGenericMemberAccessTests.cs`: Constructed receiver identity and member substitution; extend only at appropriate named seam.
- `Test/Mist.Tests/Semantics/NestedTypeResolutionTests.cs`: Lexical enclosing parameter identity and nested own-receiver controls.
- `Test/Mist.Tests/Semantics/InheritedGenericMemberTests.cs`: Existing substituted base member paths.
- `Test/Mist.Tests/Semantics/InheritedGenericMethodTests.cs`: Selected source method identity plus substitution.
- `Test/Mist.Tests/Semantics/PackForwardingTests.cs`: Symbolic same-pack forwarding and negative destination controls.
- `Test/Mist.Tests/Backend/NestedGenericTypeEmissionTests.cs`: Bound receiver-to-emitted enclosing construction seam.
- `Test/Mist.Tests/Backend/PackForwardingEmissionTests.cs`: Existing specialization consumers must keep their metadata.
- `Test/Mist.Tests/ProgramAnalysis/ThisLendInferenceTests.cs`: Existing this-based receiver/effect ownership control, not a request to change effects.

Focused commands, after building changed code:

```bash
tools/test-ledger/run.sh Test/Mist.Tests --filter 'FullyQualifiedName~ConstructedGenericMemberAccessTests|FullyQualifiedName~NestedTypeResolutionTests|FullyQualifiedName~InheritedGenericMemberTests|FullyQualifiedName~InheritedGenericMethodTests|FullyQualifiedName~PackForwardingTests|FullyQualifiedName~NestedGenericTypeEmissionTests|FullyQualifiedName~PackForwardingEmissionTests|FullyQualifiedName~ThisLendInferenceTests'
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

Update `MIST-240`, `MIST-081` in the actual `backlog/issues.yaml`. Record implemented behavior, tests/evidence and precise remaining limitations. Keep changes narrow; preserve historical evidence, stable IDs and other workers' updates. Move fully discharged records to the archive and retain genuinely unfinished residue. Do not merely return proposed closeout text or create another tracking system. Update necessary user/API/spec documentation and generated artifacts in their owning files.

MIST-240 is the owning defect. On MIST-081, record only a precisely demonstrated overlapping self-receiver route or a cross-reference to MIST-240; retain its other unverified variadic forms. Do not claim the whole residual checklist is complete.

## Commit and land

You are authorized to commit and publish this task directly to remote `main` without another confirmation. Do not create pull requests. Commit implementation, appropriate tests/generated artifacts, required documentation and actual closeout updates together or in coherent task commits. Fetch and incorporate the latest target branch, resolve conflicts while preserving other landed work, and perform the required verification on the integrated result. Use a normal, non-force push. Work from your own isolated checkout; do not reset or commandeer another session's working tree.

Other workers may land concurrently. If publication is rejected because the target advanced, fetch, reconcile, rerun verification affected by the new combined result, and retry. Keep shared-register edits narrow. You own routine integration; do not hand it to the user or assume a central integrator. Do not force-push the target, bypass protections, silently accept failures or mark unfinished work complete. Preserve newer compatible Storm pins already incorporated by main.

Finish by fetching and confirming that the intended changes, additional incidental fixes and actual closeout are present on remote `main`. Report landed commit IDs, actual verification results, entries updated, completed incidental fixes and any remaining limitations. A local commit or pushed task branch alone is not completion.

If unavailable credentials or branch protection prevents landing, complete all unaffected work, preserve the result durably, and report the exact blocker and smallest required user action. Do not switch to a PR workflow. If a genuine unresolved product decision prevents implementation, identify it precisely and retain the affected item as open. Do not repeat an unchanged failed attempt indefinitely or claim success without verification.
