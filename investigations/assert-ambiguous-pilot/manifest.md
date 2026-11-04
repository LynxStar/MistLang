# AssertAmbiguous acceptance pilot

Current MIST-1204 implementation evidence is in [implementation.md](implementation.md) and `implementation-checks.json`. The investigation record below belongs to its original Session.

MIST-1203; run `ff10fc06-b79c-4e50-96e6-8249ff159427`; investigation Session `7fa9e8b2-cf20-41f0-80df-b0eb7a21e8eb`. Stable finding: `scope-ambiguity-assertion`, with `existingTaskKey: null`. This is the assigned acceptance pilot, not a newly discovered backlog obligation.

Original HEAD, recorded before edits: `d3768f0b42459f8d840108d84d207679c3fd66ef`. Initialized Storm gitlink and checkout: `86dfd33895ba15ec7a3187e00340b236e666ab19`. Environment: macOS arm64, SDK 10.0.113. Read-only fetch of `refs/heads/codex/recurring-work-pilot-20261104` returned the original source revision; the divergence count was `0 0`, so the destination was already integrated before verification. Provenance commands and output are retained in `TestResults/assert-ambiguous-pilot/provenance.json`.

## Supported proposal

Require `ErrorType` together with effective Error-level MIST0299 in the existing helper. Its four direct callers supply diagnostic nodes. `specs/inferred-namespaces/requirements.md` Requirement 7.3 requires the ambiguity error; `specs/assembly-type-provider/requirements.md` Requirement 8.4 specifies Error severity for projected ambiguity. `ProgramModelDiagnostics.AmbiguousType` declares fixed Error-level MIST0299. `ImportingScope.LookupTier` reports it on anchored collisions and returns ErrorType; `ScopeResolver.ResolveNamedType` propagates the type. The old disjunction lets loss of either outcome evade the assertion. The executed mutation specifically proves the missing-diagnostic false positive.

The only test-source changes are that predicate and method-level Tier 2 attributes on:

- `TwoImportedNamespaces_SameTypeName_ReportsAmbiguity`
- `ThreeImports_TwoHaveSameName_ReportsAmbiguity`
- `TwoImportedNamespaces_SameArityBearingName_ReportsAmbiguity`
- `TwoProjectedImports_SameTypeName_StillReportsAmbiguity`

These exercise one module's resolution chain over directly constructed models/scopes, with `NullAmbientTypeProvider`. There is no parsing, real ambient loading, Roslyn, file I/O or process execution inside the tests. Classification delta: four unclassified cases become Tier 2; the other 15 remain unclassified. Selection uses the class name, never a Tier filter. No case is added, removed or renamed. All other assertions, fixture code, comments and failure messages remain byte-for-byte identical. `reproduce.py scope` mechanically reconstructs the entire permitted source delta from the baseline and requires exact equality.

## Audit and deliberate nonchanges

Examined current paths:

- `Test/Mist.Tests/Semantics/ScopeResolverAmbiguityTests.cs`: whole class, four helper callers, 15 controls and every local fixture helper.
- `Test/Mist.Tests/Helpers/NullAmbientTypeProvider.cs`; `Source/Mist/ProgramModel/Construction/BuilderContext.cs`: stub dependency and in-memory setup.
- `Source/Mist/ProgramModel/Model/Scopes/{ImportingScope,Scope,FileScope,AssemblyScope,LookupRequest,LookupResult}.cs`: import deduplication, identity, source/projected preference, scope precedence and node-less recovery.
- `Source/Mist/ProgramModel/Model/ProgramModel.cs`: type registration and namespace/name lookup.
- `Source/Mist/ProgramModel/Construction/Resolution/{ScopeResolver,ScopeResolver.Failure}.cs`: named-type propagation and diagnostic reporting.
- `Source/Mist/Common/Diagnostics/Descriptors/ProgramModelDiagnostics.cs`, `MistDiagnostic.cs`, `DiagnosticBag.cs`, `DiagnosticBagExtensions.cs`, `SeverityPolicy.cs`: descriptor, reporting and effective severity.
- The inferred-namespaces, assembly-type-provider and global-usings requirements; root/scoped instructions, testing-philosophy skill, test project and test-ledger runner. An adjacent contextual-expression ambiguity test was read for diagnostic ownership; it is outside the focused cohort and unchanged.

The prepared prior-examinations list and assigned task findings are empty. Read the retained MIST-1199 audit at `a16ee5bbd92fa36dfb2477065ac938c8769e1535` and stopped-run manifest at `6eb5a6b696775d74852141dbd7878be1d3482d5b` as leads. Their counts were independently rerun here; none supplies present proof or an existingTaskKey. The historical rejected missing-coverage premise does not justify additional tests. The stopped report's task linkage and treatment of future workflow gates are not retained.

The existing class comment's loose unit-test description, ImportingScope's stale GlobalScope reference, and cross-level shadowing explanation remain unchanged. Global-usings Requirement 4.5 describes a separate cross-level policy mismatch; this proposal neither decides nor changes that policy. Same-level anchored collisions have their own unambiguous contract. No classic source was read.

Duplicate-import, single-match, source/projected preference, file/global/namespace/local precedence and lexical lookup assertions remain intact. Node-less recovery deliberately reports no diagnostic and is outside this helper's anchored contract. No compiler fix, specifications, samples, baselines, exact diagnostic cardinality, slot text, ordering, unrelated assertion strengthening, or new C# comment is included.

## Reproduction and actual outcomes

The fixed cohort consists of the 19 existing tests selected by:

```sh
tools/test-ledger/run.sh Test/Mist.Tests --filter 'FullyQualifiedName~Mist.Tests.Semantics.ScopeResolverAmbiguityTests'
```

`reproduce.py` runs that ledger command with a rebuild every time. Mutation modes replace only the anchored MIST0299 reporting statement in `ImportingScope.cs` with `_ = (node, diagnostics);`. This keeps both parameter reads, introduces no warning, and leaves ErrorType propagation unchanged. Each mutation saves its patch and restores exact production bytes in `finally`, before recording/asserting results. The script verifies 19 cases, identical identities, expected failures, no skipped cases, and zero build warnings. Do not run builds concurrently in this worktree.

| Phase | Raw ledger | Passed | Failed |
| --- | --- | ---: | ---: |
| Original helper | `TestResults/ledger/20261104-085254` | 19 | 0 |
| Original helper, required diagnostic suppressed | `TestResults/ledger/20261104-085437` | 19 | 0 |
| Corrected helper, identical suppression | `TestResults/ledger/20261104-085542` | 15 | 4 expected |
| Corrected helper, restored source and fresh build | `TestResults/ledger/20261104-085650` | 19 | 0 |

All four mutation failures are the direct helper callers and report `Got ErrorType with 0 diagnostics.` The other 15 cases pass under mutation. Summaries, complete failure files, build outputs and TRX were inspected. Both suppressed source hashes are `1bdf2133c35489234bff8547a0bd5bc7be0db2b16a71cc050f5d350a370299a3`; both restore `abeb99cc1168d37e193c03fb8a0a218665085c76e8bb2f91a8714279076f8f11`. Baseline/candidate test-source SHA-256 values are respectively `dc4d78c9aabf249f9f3f57321eda7dac69119ead02a5e36023cb9ccb708507f6` and `d5bf853e4d935098cb07bf5c5599fff10081ce56a8885eaa7dc6d17eba5496f2`.

Standalone `Recovery.cs.txt` invokes the actual private fixture helpers and assertion through reflection, without injecting xUnit methods. Before and after the correction it checks both source and projected collisions: node-less lookup returns ErrorType with no diagnostic; its anchored counterpart returns ErrorType with error-level MIST0299 and passes the actual helper. Each phase passes both probes. Raw output: `TestResults/assert-ambiguous-pilot/{baseline,candidate}-recovery.txt`.

From the original helper, the executed sequence was:

```sh
python3 investigations/assert-ambiguous-pilot/reproduce.py recovery baseline
python3 investigations/assert-ambiguous-pilot/reproduce.py baseline-mutation
# Apply the saved predicate correction and four Tier attributes.
python3 investigations/assert-ambiguous-pilot/reproduce.py candidate-mutation
python3 investigations/assert-ambiguous-pilot/reproduce.py candidate
python3 investigations/assert-ambiguous-pilot/reproduce.py recovery candidate
python3 investigations/assert-ambiguous-pilot/reproduce.py scope
python3 investigations/assert-ambiguous-pilot/reproduce.py verify
```

To repeat the baseline mutation from the candidate, temporarily load only the original test file, restoring it even on failure:

```python
import pathlib, subprocess
p = pathlib.Path('Test/Mist.Tests/Semantics/ScopeResolverAmbiguityTests.cs')
saved = p.read_bytes()
try:
    p.write_bytes(subprocess.check_output(['git', 'show', 'd3768f0b42459f8d840108d84d207679c3fd66ef:' + str(p)]))
    subprocess.run(['python3', 'investigations/assert-ambiguous-pilot/reproduce.py', 'baseline-mutation'], check=True)
finally:
    p.write_bytes(saved)
```

Then repeat `candidate-mutation`, `candidate`, recovery and verification to rebuild the restored candidate. Recovery generates its console project only under ignored TestResults. It is an investigation probe at the same module scope, not a new runnable test case. The Python orchestrator's builds/processes are outside the tests.

`checks.json` indexes commands, source/submodule revisions, classifications, full 19-case inventories, exact counts, restoration hashes and SHA-256 for all ledger files and other raw evidence. Raw results stay at the named paths in this Session checkout; reproducible source/scripts and this concise manifest are retained in local Git. The candidate source checkpoint is `7749b875303f7252abfaac8fb9d981fc58adb385`; later evidence-only commits preserve its exact tested source. The structured Session report supplies the final clean HEAD and its final cohort ledger.

After restoration, `dotnet build Mist.slnx` exits 0 with zero warnings/errors (`TestResults/assert-ambiguous-pilot/build.log`). `dotnet format Mist.slnx --verify-no-changes` exits 0 with no diagnostics (`TestResults/assert-ambiguous-pilot/format.log`). `git diff --check` passes. The final clean-checkpoint cohort uses `LEDGER_NO_BUILD=1` only with these current, restored binaries; no source changes follow verification.

## Limits and handoff

Coverage is Complete for this bounded investigation. No broad test sweep or sample execution was performed. No performance improvement is asserted; the saved policy has no performance metric. Intentional mutation failures are deterministic and do not constitute flaky-test sightings.

Independent challenge is the next workflow stage, not part of this investigation's execution authority. The challenger must independently repeat decisive evidence, seek invalid assumptions, preserve `scope-ambiguity-assertion` and `existingTaskKey: null`, and assess the actual baseline/candidate technical criteria. Future reviews/publication are separate workflow gates, not technical failures of this candidate. The controlled rejection applies only to the specified later review2 attempt. No push, merge, PR, task mutation or delivery occurred here.
