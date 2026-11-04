# Anchored ambiguity implementation evidence

MIST-1204 acceptance pilot; run `617c7d90-a646-4c68-a479-66e7dfba41f9`, implementation Session `9c37b746-8250-42bc-82a1-8eefdb322ba7`. This records implementation verification, not an independent review or a new backlog finding. No publication is authorized at this stage.

Verified baseline: `d3768f0b42459f8d840108d84d207679c3fd66ef`. Retained candidate code checkpoint: `12455dfcd753322a2ff0859f84b3341ad35ae905`; the implementation handoff identifies its final evidence-only descendant. Storm gitlink and initialized checkout: `86dfd33895ba15ec7a3187e00340b236e666ab19`. SDK: 10.0.113, Darwin arm64. Read-only fetch of `refs/heads/codex/recurring-work-pilot-20261104` returned the verified baseline; `git merge-base --is-ancestor FETCH_HEAD HEAD` succeeded and divergence was `0 2`. The retained proposal already integrates the current destination, so no merge or rebase was necessary before measurement.

## Bounded correction and source investigation

The retained correction is justified: `AssertAmbiguous` requires `ErrorType` AND an effective Error-level `MIST0299`. All four callers pass a diagnostic node. `ImportingScope.LookupTier` reports that diagnostic for anchored same-level collisions and returns `ErrorType`; `ScopeResolver.ResolveNamedType` propagates that result. `ProgramModelDiagnostics.AmbiguousType` declares fixed Error severity, preserved by `SeverityPolicy` and diagnostic admission. Inferred-namespaces Requirement 7.3 and assembly-type-provider Requirement 8.4 require the ambiguity error. Node-less recovery intentionally omits the diagnostic and does not use this helper.

The only executable-source delta from the baseline is the helper predicate and missing method-level `[Trait("Tier", "2")]` on its four existing direct callers. The tests construct their models, scopes and syntax nodes directly, use `NullAmbientTypeProvider`, and exercise only the resolver's internal chain. Tier 2 is appropriate. Four previously unclassified cases become Tier 2; the other 15 stay unclassified. Selection uses the class name, never a Tier filter. All 19 identities and counts, every other assertion, and every fixture/helper byte outside the permitted delta are preserved. The `scope` check reconstructs the entire allowed test file from the baseline and compares exact bytes.

Examined paths: the complete `ScopeResolverAmbiguityTests.cs` class and fixture helpers; `Helpers/NullAmbientTypeProvider.cs`; `ProgramModel/Construction/BuilderContext.cs`; `Construction/Resolution/ScopeResolver.cs` and `ScopeResolver.Failure.cs`; `Model/Scopes/{ImportingScope,Scope,FileScope,AssemblyScope,LookupRequest,LookupResult}.cs`; diagnostic `Descriptors/ProgramModelDiagnostics.cs`, `DiagnosticBag.cs`, `DiagnosticBagExtensions.cs`, `DiagnosticFactory.cs`, `MistDiagnostic.cs`, and `SeverityPolicy.cs`; the two requirements sections above; root/scoped instructions, testing-philosophy skill, test project, retained reproduction/recovery probes and test-ledger runner.

Exclusions: no compiler fixes, specifications, samples, baselines, new/removed/renamed test cases, unrelated assertions, cardinality or message requirements, cross-level policy decisions, or added C# comments. No classic implementation was inspected. No broad suite or performance claim is made; the saved performance metric is null. Historical evidence remains historical; all decisive results below are fresh for this Session.

## Reproduction

The explicit cohort, including all otherwise-unclassified controls, is:

```sh
tools/test-ledger/run.sh Test/Mist.Tests --filter 'FullyQualifiedName~Mist.Tests.Semantics.ScopeResolverAmbiguityTests'
```

Each of the four measurements rebuilds. The baseline measurement temporarily loads the exact test file from the verified baseline; every other compiler/test-source file already matches that baseline. Both mutations replace only the anchored reporting statement in `ImportingScope.cs` with `_ = (node, diagnostics);`, leaving `ErrorType` propagation intact and preserving parameter reads. Production bytes are restored in `finally` after each mutation, and the candidate test bytes are restored in an outer `finally` after baseline checks. The retained script verifies the 19 identities, exact expected failure set, counts, zero build warnings, and restoration hashes. The standalone recovery probe adds no xUnit methods and checks source/projected node-less and anchored counterparts against the actual helper.

The exact orchestration is saved as ignored raw `TestResults/assert-ambiguous-implementation/run.py`. To reproduce from a candidate checkout, run the following equivalent Python with `PYTHONDONTWRITEBYTECODE=1`:

```python
import importlib.util, pathlib, subprocess
root = pathlib.Path.cwd()
spec = importlib.util.spec_from_file_location('pilot', root / 'investigations/assert-ambiguous-pilot/reproduce.py')
p = importlib.util.module_from_spec(spec)
spec.loader.exec_module(p)
p.REPORT = root / 'investigations/assert-ambiguous-pilot/implementation-checks.json'
p.RAW = root / 'TestResults/assert-ambiguous-implementation'
saved = p.TEST.read_bytes()
try:
    p.TEST.write_bytes(subprocess.check_output(['git', 'show', 'd3768f0b42459f8d840108d84d207679c3fd66ef:' + str(p.TEST.relative_to(root))]))
    p.run('baseline')
    p.recovery('baseline')
    p.run('baseline-mutation')
finally:
    p.TEST.write_bytes(saved)
p.run('candidate-mutation')
p.run('candidate')
p.recovery('candidate')
p.scope()
p.verify()
assert p.TEST.read_bytes() == saved
```

`implementation-checks.json` records commands, revisions, source hashes, complete case inventories and classifications, exact counts, ledger paths, restoration, and raw SHA-256 values. Raw paths are relative to this isolated Session checkout. Provenance, the orchestration, source audit and mutation patches/restoration records are retained under `TestResults/assert-ambiguous-implementation/`; ledger logs and TRX remain in the named ledger directories.

## Observed results

| Source and assertion | Ledger | Passed | Failed |
| --- | --- | ---: | ---: |
| Original helper, normal compiler | `TestResults/ledger/20261104-092027` | 19 | 0 |
| Original helper, diagnostic suppressed | `TestResults/ledger/20261104-092141` | 19 | 0 |
| Corrected helper, identical suppression | `TestResults/ledger/20261104-092215` | 15 | 4 expected |
| Corrected helper, restored compiler | `TestResults/ledger/20261104-092309` | 19 | 0 |

The four expected failures are exactly the helper callers (simple-name, partial-match, arity-bearing and projected ambiguity), each with `Got ErrorType with 0 diagnostics.` All other controls pass under mutation. Complete summaries, failures, build logs and TRX were inspected. Both normal runs contain the same 19 identities with no skips. Source/projected node-less recovery and anchored counterparts pass both before and after the correction (two probe cases per phase).

Both mutation source SHA-256 values are `1bdf2133c35489234bff8547a0bd5bc7be0db2b16a71cc050f5d350a370299a3`; each restores `abeb99cc1168d37e193c03fb8a0a218665085c76e8bb2f91a8714279076f8f11`. Candidate test-source SHA-256 is `d5bf853e4d935098cb07bf5c5599fff10081ce56a8885eaa7dc6d17eba5496f2`; baseline is `dc4d78c9aabf249f9f3f57321eda7dac69119ead02a5e36023cb9ccb708507f6`. Production, specifications and samples match the baseline exactly. Deliberate mutation failures are deterministic, not flaky-test sightings.

`dotnet build Mist.slnx` exits 0 with zero warnings/errors; `dotnet format Mist.slnx --verify-no-changes` exits 0 without diagnostics. Raw output is `TestResults/assert-ambiguous-implementation/{build,format}.log`. `git diff --check` passes. A final read-only destination fetch still returns `d3768f0b42459f8d840108d84d207679c3fd66ef`. No executable-source changes follow the restored candidate run; the final evidence-only commit and clean worktree are attested in the structured handoff.

## Review handoff

Technical criteria pass for this bounded candidate. This is solely the implementation stage: independent reviews and deterministic publication remain separate workflow gates. Apply the captured controlled rejection only to the first review2 at run attempt 3; it requests one further saved suppression rerun with restoration, without alleging a product defect. After that rework, all three reviews must pass afresh before delivery. No push, PR, task mutation or independent-review approval occurs in this Session.
