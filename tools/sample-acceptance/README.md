# Sample acceptance harness

A standalone acceptance runner over the whole sample suite. It is the Stage 0 seed
harness authorized by MIST-1071: it runs today's C#-hosted compiler over every
sample, captures a reviewed baseline, and records the generation and environment it
ran in, so a later Mist-hosted compiler can be judged against the same manifest and
expectations without editing the runner. It does not claim self-hosting or
generation parity; the Mist-authored generations remain MIST-1071/MIST-1073.

## What it guarantees

- **Nothing disappears.** `run.py` reconciles the committed `manifest.toml` against
  live discovery (`samples/**/mist.toml` plus the C# projects reached through their
  `[references]`) before any sample runs. A newly added, removed, duplicated or
  unclassified project fails the run instead of silently vanishing.
- **An aggregate pass cannot hide a standalone failure.** Every runnable sample is
  run on its own and keeps its own result and logs.
- **No failure is ever blessed.** A compiler warning/info/error, a nonzero exit, a
  stdout mismatch or a missing expectation is a failure, never a new baseline.

## The manifest and the roles

`manifest.toml` is the authoritative roster: one entry per sample project and per C#
companion, each with a role. It is executable acceptance input, not a work ledger.

| Role | How it is exercised | Expectation |
|---|---|---|
| `orchestrator` | `mist run` of AllFeaturesV2 | reuses `samples/__baselines__/AllFeaturesV2.out` (never duplicated) |
| `executable` | standalone `mist run` | `expectations/<name>.out`, compared exactly |
| `generator-consumer` | `mist run`, driving a source generator through the generator protocol | `expectations/<name>.out` |
| `msbuild-consumer` | `mist run` of a sub-project that references a C# project (builds MSBuild) | `expectations/<name>.out`, substring match |
| `ef-consumer` | the manual EF demonstration; needs network + `dotnet-ef` | run only with `--include-network` |
| `test-library` | `mist test` | `expectations/<name>.tally` (regex lines) |
| `library` | built/exercised through its consumer; not run directly | — |
| `composed-library` | composed into AllFeaturesV2 and exercised through its `RunDemo()`; has no standalone entry point, so it is not run on its own | — |
| `source-generator` | exercised through its consumer; not a zero-arg demo | — |
| `csharp-companion` | a `.csproj` the compiler builds while compiling its consumer | — |

## Running it

```sh
# Build the compiler once (the harness does not build it):
dotnet build Mist.slnx

# Reconcile and list the roster without running anything:
python3 tools/sample-acceptance/run.py --list

# A full seed capture-and-compare against the current C# host:
python3 tools/sample-acceptance/run.py --generation csharp-host-g0

# Point it at any other compiler (a built binary, or a command line):
python3 tools/sample-acceptance/run.py --compiler /path/to/mist --generation mist-host-g1
python3 tools/sample-acceptance/run.py --compiler "dotnet run --project Source/Mist.CLI --no-build --"

# Also run the network/tool-dependent EfCore demonstration:
python3 tools/sample-acceptance/run.py --include-network
```

With no `--compiler`, the runner uses the `mist` binary under
`Source/Mist.CLI/bin/{Release,Debug}/net10.0/<rid>/` if present.

Each run writes to `--out` (default `_runs/<stamp>/`, gitignored): `report.json` with
the provenance block and every per-project result, and `logs/<name>.stdout|stderr`.
The provenance block records the Mist revision, the Storm submodule pin, the compiler
command and its `version` output, the .NET SDK, the runtime identifier and the OS.

Exit code is 0 only when reconciliation holds and every executed project passed.

## Seeding or refreshing expectations

`--capture` writes `expectations/<name>.out` from the run instead of comparing. It
refuses to touch `samples/` baselines (the AllFeaturesV2 baseline is always reused,
never rewritten), and a captured sample that emitted a diagnostic or exited nonzero
is still reported as a failure. Review captured expectations before committing them:
for samples that AllFeaturesV2 composes, each one's standalone output appears as a
contiguous block of the reviewed aggregate baseline, in orchestrator order. The
runner checks this automatically (`--check-aggregate`, also folded into every run's
report), so an aggregate pass and a standalone pass cannot disagree.

Two kinds of composed sample are not block-cross-checked against the baseline, and
each run lists them explicitly:

- `composed-library` samples (`ClrRefReadonly`, `ExtensionMethods`, `PatternMatching`,
  `SizedSequencePatterns`, `Tuples`, `TypeAliases`, `TypeUnions`) have a `RunDemo()`
  but no standalone entry point, so `mist run` on them is `MIST0108` by design. They
  are exercised only through AllFeaturesV2; the Tier 6 aggregate is their contract.
- `TopLevelStatements` carries `aggregate_check = false`: its standalone run executes
  the top-level-statement entry point, while the orchestrator calls its separate
  `RunDemo()` disambiguation demo, so its standalone expectation is reviewed against
  its own program rather than a baseline slice.

## The bounded self-test

```sh
python3 tools/sample-acceptance/run.py selftest
```

`selftest.py` drives the pure verdict functions with hand-built inputs to prove the
four detections — manifest omission, a diagnostic, a nonzero exit and an output
mismatch — without spawning a compiler. It is deliberately not an xUnit test and must
not grow into one process per sample; the single Tier 6 aggregate check stays in the
suite.
