# Controlled rework evidence

MIST-1204; run `617c7d90-a646-4c68-a479-66e7dfba41f9`, implementation attempt 4, Session `c6e148b9-7c68-46bb-9c8a-a01fbe94449c`. This addresses `pilot-controlled-rework`, the authorized workflow fault injection from review2 attempt 3. It adds fresh evidence without changing the retained implementation or claiming an independent review.

Verified baseline: `d3768f0b42459f8d840108d84d207679c3fd66ef`. Tested candidate source checkpoint: `128619af46dc8b85347b8f5ecce05bf83db2d24b`. The final Session report identifies the evidence-only descendant committed for handoff. Storm gitlink and clean checkout: `86dfd33895ba15ec7a3187e00340b236e666ab19`. Environment: Darwin arm64, .NET SDK 10.0.113.

Before measurement, `git fetch origin refs/heads/codex/recurring-work-pilot-20261104` returned the verified baseline. `git merge-base --is-ancestor FETCH_HEAD HEAD` exited 0; divergence was `0 3`. The retained candidate already incorporates the destination. Initialized the required Storm submodule with `git submodule update --init Source/Storm.SourceGenerator`.

## Fresh measurements

Every row rebuilds and selects the same explicit 19-case cohort, including all unclassified cases:

```sh
tools/test-ledger/run.sh Test/Mist.Tests --filter 'FullyQualifiedName~Mist.Tests.Semantics.ScopeResolverAmbiguityTests'
```

| Assertion and compiler state | Ledger under `TestResults/ledger/` | Passed | Failed |
| --- | --- | ---: | ---: |
| Original helper, normal compiler | `20261104-094821` | 19 | 0 |
| Original helper, diagnostic suppressed | `20261104-094934` | 19 | 0 |
| Corrected helper, identical suppression | `20261104-095003` | 15 | 4 expected |
| Corrected helper, restored compiler | `20261104-095056` | 19 | 0 |

The additional decisive source-mutation rerun fails exactly the four helper callers: simple-name, partial-match, arity-bearing and projected ambiguity. Each reports `Got ErrorType with 0 diagnostics.` All 15 controls pass. Both normal cohorts have identical identities, no failures and no skips. All four builds have zero warnings/errors. Summaries, complete failures, build results and TRX counters/inventories were inspected; full raw logs are retained. These deterministic mutation failures are not flaky-test sightings.

The mutation replaces only the anchored `AmbiguousType` reporting statement in `ImportingScope.cs` with `_ = (node, diagnostics);`. ErrorType propagation stays intact. Each mutation restores the exact production bytes in `finally`. Before/after production SHA-256: `abeb99cc1168d37e193c03fb8a0a218665085c76e8bb2f91a8714279076f8f11`; both mutated hashes: `1bdf2133c35489234bff8547a0bd5bc7be0db2b16a71cc050f5d350a370299a3`. The baseline test file is temporarily loaded from the verified baseline and restored in an outer `finally`. Baseline/candidate test SHA-256: `dc4d78c9aabf249f9f3f57321eda7dac69119ead02a5e36023cb9ccb708507f6` / `d5bf853e4d935098cb07bf5c5599fff10081ce56a8885eaa7dc6d17eba5496f2`.

Both baseline and restored-candidate standalone recovery probes pass source and projected cases: node-less ambiguity returns ErrorType with no diagnostic; its anchored counterpart returns ErrorType and Error-level MIST0299 and passes the actual helper. These probes add no xUnit test methods.

## Scope and reproduction

Current source inspection confirms the bounded correction is justified. `ProgramModelDiagnostics.AmbiguousType` declares fixed Error-level MIST0299; `SeverityPolicy` preserves that severity and `DiagnosticBag` applies it on admission. `ImportingScope.LookupTier` reports it for anchored collisions and returns ErrorType, which `ScopeResolver.ResolveNamedType` propagates. Inferred-namespaces Requirement 7.3 and assembly-type-provider Requirement 8.4 require the ambiguity error. Node-less recovery deliberately omits it and is outside the helper's anchored contract.

Examined the complete ambiguity fixture, its four callers and controls, `NullAmbientTypeProvider`, `BuilderContext`, `ImportingScope`, `Scope`, `FileScope`, `AssemblyScope`, `ScopeResolver.ResolveNamedType`, the governing descriptor/severity/admission code, the two requirements sections, retained reproduction/recovery probes, ledger runner and root/scoped testing instructions. No classic source was read.

The exact source audit reconstructs the entire permitted test file from baseline bytes: one helper predicate correction and four method-level Tier 2 labels. The callers construct inputs directly, use a stub ambient provider and exercise only the resolver's internal chain. Four cases change from unclassified to Tier 2; the other 15 remain unclassified. All identities/counts, other assertions, fixture code and comments remain unchanged. Production, specifications, samples and sample baselines match the baseline exactly. This rework changes evidence files only.

The executed orchestration is `TestResults/assert-ambiguous-rework/run.py`; its SHA-256 is saved in [rework-checks.json](rework-checks.json). Reproduce using the Python sequence in [implementation.md](implementation.md#reproduction), replacing `implementation-checks.json` with `rework-checks.json` and `TestResults/assert-ambiguous-implementation` with `TestResults/assert-ambiguous-rework`. Run with `PYTHONDONTWRITEBYTECODE=1`. It executes baseline, baseline recovery, baseline mutation, candidate mutation, restored candidate, candidate recovery, exact scope audit, solution build and format verification sequentially. It asserts expected counts, failure identities, identical inventories, zero build warnings and exact restoration.

`rework-checks.json` retains commands, source/submodule revisions, classifications, inventories, counts, restoration hashes and raw SHA-256 values. Raw output, mutation patches, restoration records, provenance and orchestration remain under `TestResults/assert-ambiguous-rework/`; ledger artifacts remain at the four paths above, relative to this Session worktree.

After restoration, `dotnet build Mist.slnx` exits 0 with zero warnings/errors and `dotnet format Mist.slnx --verify-no-changes` exits 0 without diagnostics. Raw output is `TestResults/assert-ambiguous-rework/{build,format}.log`. `git diff --check` passes. All saved raw checksums are revalidated before commit. The final evidence-only checkpoint retains exactly the tested source, with a clean worktree attested in the Session report.

No broad suite or performance measurement is claimed; the saved policy has no performance metric. No new test, unrelated assertion, compiler fix, specification/sample edit, suppression or C# comment is added. Independent review and publication remain separate workflow stages. The controlled rejection invalidates earlier approvals; three fresh independent passing reviews are required before deterministic delivery to `codex/recurring-work-pilot-20261104`. Nothing is published during this implementation stage.
