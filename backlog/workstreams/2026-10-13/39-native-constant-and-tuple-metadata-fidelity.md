# 39 — Native constants and tuple metadata fidelity

Complete this task independently, including implementation, verification, tracking updates and landing. Repository: `https://github.com/LynxStar/MistLang.git`. Target: `origin/main`. Suggested model: **Opus, high effort**. This is a sizable metadata-to-semantic-identity bundle. It needs constant kind/value fidelity, target-position argument resolution and metadata tuple cursor extraction across three owner/row shapes. One owner keeps declaration projection and consuming closure consistent without adding source const syntax or changing tuple nominal identity. These are portable user model categories, not API identifiers.

## Outcome and starting point

Native literal constants retain their exact compile-time values and can fill existing dependent scalar positions; tuple names on base, interface and generic constraint signatures reach the same named-member consumers as ordinary CLR signatures.

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

## Contracts and current evidence

- `.kiro/specs/dependent-types/requirements.md`: Compile_Time_Constant includes const declaration references; Requirements 2.4/3.5 named const args and printing
- `.kiro/specs/dependent-types/requirements.md`: Requirement 5.1–3 only literal/const references, with nonconstant source refusal
- `.kiro/specs/value-tuples/requirements.md`: Requirement 3.2/3.4 named item lookup alongside ItemN;5.4 names nonbreaking for assignability
- `.kiro/specs/ambient-type-provider-v2/requirements.md`: Requirement 7.3 assembly/source semantics parity and9.4 native public readonly/const fields
- `.kiro/specs/assembly-type-provider/requirements.md`: Requirement 3.2 type metadata (bases/interfaces),5.4 field projection,7 intrinsic mapping

**Evidence 1 — executed scratch CLI, `c80c36823175514e5448fc2296bb174fbb360ec2`.**

```mist
using Standard;
namespace Probe;
public class Box<N:int>{} public class Program{public static void Main(){Console.WriteLine(Box<System.Threading.Timeout.Infinite>.N);}}
```

Exit 1. Observed:

```text
MIST_PM_UNRESOLVED_TYPE: System.Threading.Timeout.Infinite is treated as a type name, though the SDK member is a public const int -1.
```

**Evidence 2 — source inspection only, `c80c36823175514e5448fc2296bb174fbb360ec2`.**

FieldProjector records IsReadonly=IsInitOnly||IsLiteral but no raw constant value/constant initializer. EnumProjector uses GetRawConstantValue on its separate path. BaseTypeProjector resolves base/interface types without tuple-name cursor; GenericConstraintProjector likewise resolves each constraint without names. No current named-base/interface/constraint consuming fixture has been executed in this audit.

Implementation references:

- `Source/Mist/Backends/CSharpTranspiler/AmbientProvider/FieldProjector.cs`: Project; IsLiteral readonly marker with absent raw value
- `Source/Mist/Backends/CSharpTranspiler/AmbientProvider/EnumProjector.cs`: existing exact raw constant projection
- `Source/Mist/ProgramModel/Model/Members/Field.cs`: constant metadata/value model
- `Source/Mist/ProgramModel/Construction/Resolution/ScopeResolver.cs`: TypeArgumentList/type-reference argument resolution and target generic parameter identity
- `Source/Mist/ProgramModel/Construction/Binding/ConstantConversion.cs`: scalar constant conversion/domain compatibility
- `Source/Mist/ProgramModel/IntegralConstant.cs`: signed/unsigned exact integral representation
- `Source/Mist/Backends/CSharpTranspiler/AmbientProvider/TupleElementNames.cs`: Of/OfNames cursor and Read metadata owner
- `Source/Mist/Backends/CSharpTranspiler/AmbientProvider/BaseTypeProjector.cs`: ProjectBaseType/ProjectInterfaces no tuple cursor
- `Source/Mist/Backends/CSharpTranspiler/AmbientProvider/GenericConstraintProjector.cs`: Project constraint Resolve without tuple names
- `Source/Mist/Backends/CSharpTranspiler/AmbientProvider/AmbientTypeResolver.cs`: Resolve decorated structural types and inbound boundaries

## Scope and acceptance

### Native literal values

A class/struct public native const field retains the exact scalar kind/value: negative int/long, ulong beyond Int64.MaxValue, bool false/true and char. Compare normalized dependent carrier/type identities against literal arguments: Box<Timeout.Infinite> equals Box<-1> and N evaluates -1. Existing enum constant values remain correct.

Static readonly, mutable fields, properties, arbitrary method calls and nonconstant variables are not accepted as dependent compile-time constants. Do not execute native field initializers or use GetValue to fold runtime values; metadata constants are authoritative. Reading GetRawConstantValue from the owning compiled metadata fixture is valid. Null/string/native enum cases follow existing allowed domain contract, not new scalar categories.

### Imported const reference argument consumer

Complete SDK-only portable native_constant source now prints -1. In metadata fixture, Box<Limits.Size>.N prints 3; method/type/alias dependent positions resolve the const by actual member identity and preserve domain conversions, forwarding and get-only projection. Regular type arguments still resolve as types.

A runtime-only reference at dependent argument position receives one owning source constant/type-domain diagnostic, not a silent type-lookup fallthrough. Unknown/inaccessible names retain correct error ownership. No source const declarations/keyword/modifier grammar are introduced; keep MIST-074 open with precise source-const residue.

### Base tuple names

Native NamedBase : PairBase<(int X,int Y)> projects its actual base argument as NamedTuple(X,Y); inherited Pair.X/Pair.Y bind with primitive element types while Pair.Item1/Item2 remain available. Preserve generic base closing, nesting and tuple Rest shape.

Absent names stay structural/unnamed; no aliases fabricate tuple nominal identity. Attribute owner/path mismatch or truncated malformed transform data does not shift names onto neighboring arguments.

### Interface tuple names

INamedPair : IPair<(int Left,int Right)> projects names through the actual interface signature path; inherited Pair.Left/Right lookup works. Cover multiple interfaces/diamond construction with different signature-name cursors and tuple names inside nested generic types.

Inspect the minimal emitted fixture to establish the actual custom-attribute owner and transform layout, which may aggregate relationship signatures. Decode and partition names by each actual base/interface/constraint path; do not mandate per-row attribute storage or blindly apply the same blob to every interface. Missing or malformed data is handled conservatively; ordinary member/delegate names stay unchanged.

### Generic constraint tuple names

Native Constrained<T> where T:IPair<(int Limit,int Count)> projects named tuple args on the actual constraint. In a binder component with the projected open T as input, Pair.Limit/Count resolve through that constraint. Cover method and enclosing type generic parameters, multiple constraints and nested tuples.

Do not guess whether the compiler stored names on the type/generic parameter/constraint row: inspect the minimal emitted fixture and decode the applicable metadata owner. Native kind/default/nominal constraints and unresolved-constraint withholding stay intact.

### Identity and annotation separation

Named and unnamed tuple forms share the same underlying structural identity and existing name-insensitive assignment. Native const value identity is separate from mutable/read-only field status. Existing nullable and generic native slot annotations retain current behavior.

MIST-233 nullable callback-input semantics is explicitly excluded: no runtime-null guard, source wrapping/narrowing policy, warning suppression or model-wide nullable-input change. MIST-232 unconstrained T? annotation and MIST-231 native hiding warnings are not needed for these value/name paths.

## Minimal metadata fixture

Extend the existing small compiled Test/Mist.Tests.InteropFixtures project once, not a per-test Roslyn or process compilation. Hand-build resolver/provider inputs and load only this fixture where the subject is the loader. Field-projector tests may use typeof(Limits) plus stubs; cursor decoding decisions take direct arrays/blobs. Bind projected open constraint T only in a bounded named metadata→binder seam if lower module checks cannot prove it.

```csharp
namespace Plan.NativeMetadata;
public static class Limits
{
    public const int Size = 3;
    public const long Signed = -7;
    public const ulong High = 18446744073709551615UL;
    public const bool Enabled = true;
    public const char Mark = 'x';
    public static readonly int RuntimeSize = 3;
}
public class PairBase<T>
{
    public T Pair;
    public PairBase(T pair) { Pair = pair; }
}
public class NamedBase : PairBase<(int X, int Y)>
{
    public NamedBase() : base((3, 4)) { }
}
public interface IPair<T> { T Pair { get; } }
public interface INamedPair : IPair<(int Left, int Right)> { }
public class NamedPair : INamedPair
{
    public (int Left, int Right) Pair => (5, 6);
}
public class Constrained<T> where T : IPair<(int Limit, int Count)>
{
    public T Item;
    public Constrained(T item) { Item = item; }
}
```

`Test/Mist.Tests.InteropFixtures/Mist.Tests.InteropFixtures.csproj`: existing net10.0, Nullable enable, no extra package/feed needed for test-seam projection

AmbientProviderTestHelper.ProviderOver diagnostic bag and [typeof(Plan.NativeMetadata.NamedBase).Assembly.Location], with RegisterStandardTypes/hand-built primitive scope as needed. Do not load all SDK metadata just to exercise these few facts.

The tuple owner layout and new named-base/interface/constraint consumer fixture have not been executed in this audit; inspect the compiled metadata before choosing extraction paths.

If an external manual CLI consumer is necessary, pack the same minimal fixture as Plan.NativeMetadataFixture1.0.0 once, seed a task-specific package cache with dotnet restore of a scratch seed project using --source <local-feed> --packages <task-cache>, and point only the CLI process at that cache via NUGET_PACKAGES. PackageResolver restores in its own temporary folder, so a NuGet.Config placed only in the Mist scratch folder is not sufficient. No runtime test should pack/restore per case.

Optional manual consumer manifest:

```toml
name="Probe"
namespace_root="Probe"
[packages]
"Plan.NativeMetadataFixture"="1.0.0"
```

Optional complete manual consumer source:

```mist
using Standard;using Plan.NativeMetadata; namespace Probe; public class Box<N:int>{} public class Program{public static void Main(){Console.WriteLine(Box<Limits.Size>.N);var b=new NamedBase();Console.WriteLine(b.Pair.X);INamedPair p=new NamedPair();Console.WriteLine(p.Pair.Left);}}
```

## Independence and ownership

One owner covers native literal projection and existing dependent-argument reference consumption plus tuple name decoration at native base/interface/constraint boundaries. Packet 34 uses member-effect identities, which this packet does not alter; constants remain the same static readonly native fields with added exact literal facts, and tuple decorators keep underlying structural type identity. Packet 37 owns generic operators/inference candidates, not these constant-name/type-argument and metadata decoration sites. Packet 36 consumes ordinary selected slice members; adding decorated tuple names/native literal facts does not change selection/access API or sequence protocols. No new effect/operator/witness API negotiation is required; preserve existing resolver and interner contracts.

This packet has no dependency on another workstream. As its parent session, you own every included task and its internal dependencies. Determine the execution order, decide which work to perform yourself or dispatch to subagents, and integrate their changes into your primary implementation. Resolve internal conflicts and verify the combined result; subagents return contributions to you rather than independently publishing partial work to the shared target. Use current repository contracts. Do not hand internal ordering, coordination or integration to the user, the planning session or another packet. Complete and close out every included item; a representative subset or a collection of subagent reports is not completion.

Allow narrowly necessary adjacent edits, including compatible bound metadata, consumers, generated artifacts and user/API/spec documentation needed to finish this outcome. Keep changes coherent and preserve already landed behavior. Shared files require narrow integration, not another packet's unpublished branch, service, test helper or design decision.

Excluded scope:

- Source const declaration syntax/modifier/evaluation language, arithmetic dependent argument expressions, new scalar domains, arbitrary readonly field folding, native initializer execution.
- Nominal tuple identity, broader CLR metadata/member inventory MIST-090, whole assembly partitioning, callback-input null policy 233, isolated native warning fixes 231/232, generic operators, sequence protocol discovery and unrelated defaults.

## Incidental fixes

If you uncover additional issues with clear expected behavior that are straightforward to fix and verify without user input, solve them in additional commits. This includes small fixes outside your primary files or objective; do not merely surface them as extras or make the user dispatch another task. Add appropriate verification and update actual documentation/backlog entries where relevant. Keep each coherent incidental fix separately committed and include it in the same landing workflow.

Avoid duplicating work already landed or assigned to an active workstream. Defer only when the finding requires a genuine user decision, substantial new work, unavailable access/authorization, or verification you cannot complete; state the concrete reason. Ordinary implementation choices and arbitrary file boundaries do not require user input. Finish the primary task and do not turn incidental cleanup into an open-ended campaign.

## Verification and closeout

Use revised What a Tier May Cost budget. Tier 1 exact scalar/raw-value normalization and tuple-name cursor decisions take direct data; Tier 2 native projector/binder module fragments use hand-built values and stubs; owning metadata-loader phase may use one minimal compiled fixture at its honest tier/cost. Under Tier 4 use only named seams, under 10 seconds and existing boundary budgets; no whole SDK loader/Roslyn/process just to test a member name. Reuse the fixture built by ordinary solution build, never compile/package a fixture in each theory. Any full CLI/package/consumer checks are explicit manual acceptance evidence or existing justified higher-tier bank, not a new process-heavy unit suite. Exactly one runnable Tier trait on each new/migrated test.

Existing verification anchors (inspect their actual helpers/costs before extending):

- `Test/Mist.Tests/Backend/ClrEnumConstantProjectionTests.cs`: direct native integral metadata controls
- `Test/Mist.Tests/Backend/AmbientTypeProviderV2TupleProjectionTests.cs`: current names/nested/Rest cursor controls; audit actual costs rather than copy Tier 2 label
- `Test/Mist.Tests/Backend/AmbientDelegateTupleNameTests.cs`
- `Test/Mist.Tests/Backend/AmbientConstraintProjectionTests.cs`
- `Test/Mist.Tests/Semantics/DependentValueLookupTests.cs`
- `Test/Mist.Tests/Semantics/DependentValueReadTests.cs`
- `Test/Mist.Tests/Backend/DependentValueEmissionTests.cs`
- `Test/Mist.Tests/Semantics/TupleTypeIdentityTests.cs`
- `Test/Mist.Tests.InteropFixtures/TupleSignatures.cs`
- `Test/Mist.Tests.InteropFixtures/ConstraintProjection/ConstraintProjectionFixtures.cs`

Focused commands:

```bash
tools/test-ledger/run.sh Test/Mist.Tests --filter "FullyQualifiedName~ClrEnumConstantProjectionTests|FullyQualifiedName~AmbientTypeProviderV2TupleProjectionTests|FullyQualifiedName~AmbientDelegateTupleNameTests|FullyQualifiedName~AmbientConstraintProjectionTests|FullyQualifiedName~DependentValue|FullyQualifiedName~TupleTypeIdentityTests"
```

```bash
dotnet run --project Source/Mist.CLI --no-build -- run "$packet_probe_dir" --quiet
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

Update `MIST-047`, `MIST-049`, `MIST-074` in the actual `backlog/issues.yaml` as part of this change. Record implemented behavior, supporting tests/evidence and any precise remaining limitation. Preserve historical evidence and other workers' updates; keep edits to owning records narrow. Close only completed portions and move fully discharged records to the archive with their stable IDs. Do not merely return proposed closeout text or create a new tracking system. Update necessary contract/API/book documentation and generated artifacts in their owning files.

MIST-074 includes source-const uncertainty outside this packet: discharge the imported const-reference portion and explicitly retain its separate residue. Fully close MIST-047/049 only after all included scalar and signature paths are verified.

## Commit and land

You are authorized to commit and publish this task directly to remote `main` without another confirmation. Do not create pull requests. Commit implementation, appropriate tests/generated artifacts, required documentation and actual closeout updates together or in coherent task commits. Fetch and incorporate the latest target branch, resolve conflicts while preserving other landed work, and perform the required verification on the integrated result. Use a normal, non-force push. Work from your own isolated checkout; do not reset or commandeer another session's working tree.

Other workers may land concurrently. If publication is rejected because the target advanced, fetch, reconcile, rerun verification affected by the new combined result, and retry. Keep shared-register edits narrow. You own routine integration; do not hand it to the user or assume a central integrator. Do not force-push the target, bypass protections, silently accept failures or mark unfinished work complete. Preserve newer compatible Storm pins already incorporated by main.

Finish by fetching and confirming that the intended changes, additional incidental fixes and actual closeout are present on remote `main`. Report landed commit IDs, actual verification results, entries updated, completed incidental fixes and any remaining limitations. A local commit or pushed task branch alone is not completion.

If unavailable credentials or branch protection prevents landing, complete all unaffected work, preserve the result durably, and report the exact blocker and smallest required user action. Do not switch to a PR workflow. If a genuine unresolved product decision prevents implementation, identify it precisely and retain the affected item as open. Do not repeat an unchanged failed attempt indefinitely or claim success without verification.
