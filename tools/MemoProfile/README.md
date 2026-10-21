# MemoProfile

A measurement-only harness for the Storm-generated Mist parser's packrat memoization (MIST-194).
It changes nothing in production: it builds parser variants beside the real one, proves they parse
identically, and times them under a fixed protocol. Results live under `results/`, one directory per
recorded run, each pinned to the Mist and Storm commits it measured. Those files are the tool's output
unedited, which is why `results/.gitattributes` exempts them from whitespace checks.

## Run it

```bash
dotnet build tools/MemoProfile -c Release
```

```bash
dotnet tools/MemoProfile/bin/Release/net10.0/MemoProfile.dll all --out tools/MemoProfile/results/<name>
```

Build `Source/Mist` first if you want the source check against MSBuild's own generated parser
(`Source/Mist/obj/*/net10.0/generated/...`); the tool reports it as skipped when that output is
absent. Run nothing else heavy on the machine meanwhile: the run log records the load average, the
total CPU of other processes and the count of `dotnet`/`VBCSCompiler` processes at start and end, so
competing load is visible, but it is not removed. A full run takes about five minutes.

## Variants

| Variant | Plan | How it is produced |
|---|---|---|
| `mist-core` | production | The parser compiled into `Mist.Core` by MSBuild. The anchor: every other variant is checked against it. |
| `production` | automatic analysis plus `@memo` | `StormIncrementalGenerator` run through a Roslyn `GeneratorDriver` over `Source/Mist/Grammar/*.storm` as committed, then compiled in memory. |
| `auto-only` | automatic analysis alone | The same generator over the grammar with its `@memo` lines removed. This is the only supported way to see automatic coverage alone: there is no switch that disables the analysis, and removing `@memo` does not remove automatic coverage. |
| `explicit-only` | the `@memo` rules alone | **Experimental.** Not producible through any supported path. `production`'s generated files with `Mist.Parser.g.cs` replaced by Storm's own `ParserEmitter.Emit` over the same lowered grammar and a plan of the annotated rules. |
| `no-memo` | empty | **Experimental**, as `explicit-only`, with `MemoizationPlan.Empty`. |

What differs between variants is shown, not assumed. `sources.txt` checks that the runtime, AST and
lexer files are byte-identical across every generated variant; that `ParserEmitter.Emit` over the
analysed production plan reproduces the generator's production parser byte for byte (so the
experimental variants differ from `production` only in the plan); that each parser's distinct
`MemoKey` ids match its plan; that the harness's production parser equals MSBuild's; and it lists
the lines the memo machinery adds. `plans.txt` lists every plan's rules.

All in-memory variants are compiled by Roslyn with `OptimizationLevel.Release` against the BCL only,
with the generator's default node-origin type (`object`; Mist.Core uses `NodeOrigin`, which the parser
file never mentions), plus one adapter class (`ParserVariant.AdapterSource`) that is identical in every
variant.

## Workload

Every `.mist` file under `samples/` and `Source/Standard/` (excluding `out/`, `bin/` and `obj/`),
ordinal-sorted by path, plus deterministic invalid inputs derived from them with no random seed: the
file at every index divisible by 8 truncated at half its length, and the file at every index 4 mod 8
with its first `)` removed. `workload.csv` records each input's SHA-256, bytes and token count.

Inputs are lexed once by Mist.Core's generated lexer and passed through `MistParser`'s own keyword
reclassification (called by reflection), so every variant parses exactly the token stream production
parses. Lexing and token-stream construction are setup and are excluded from warm timings.

## Equivalence before speed

Before any timing, every input is parsed once by every variant and compared with `mist-core`: whether
a tree was produced, a SHA-256 over the tree's node types, spans and hidden-token counts, the parser's
final position, and the exact diagnostic list (line, column, message). Any mismatch stops the run
before timing. `equivalence.csv` holds every row. The same pass reads each variant's memo table after
each parse and sums its entries per rule into `memo-entries.csv`, the attribution of memo work.

## Protocol

Fixed in `Protocol.cs` before the recorded run:

- **Warm.** 3 discarded warm-up passes per variant, then a Williams design over the five variants
  (10 rounds; each variant in each slot position twice and after every other variant twice), 2 passes
  per slot: 20 recorded passes per variant. A pass parses the whole workload once. Before each pass a
  forced, compacting full GC. Each pass records wall time, process CPU time, bytes allocated on the
  parsing thread, gen0/1/2 collections and GC pause time; each input's time per pass feeds
  `warm-inputs.csv` medians. Budget: 10 minutes, after which rounds stop and the log says so.
- **Cold.** 6 fresh processes per variant, ordered by the same design's rows. Each records runtime
  start-up, generation (generator driver and, for experimental variants, the extra emit), in-memory
  compile and load, workload lexing, the first pass over the workload (JIT included) and a second pass.
  `mist-core` has no generation or compile step: its cold cost is start-up and JIT alone. Budget:
  10 minutes.

Ratios are taken against `production` per round, so they compare passes run under the same
conditions; the run log reports their median and range.

## Limits

- One machine, one run. The environment block records what it was.
- CPU attribution is by input (`warm-inputs.csv`) and by memoized rule (`memo-entries.csv`); there is
  no method-level sampling profile, since `dotnet-trace` is not part of the repository's toolchain.
- The memo table records entries, not hits; a hit count would need an instrumented parser.
- In-memory compilation is not MSBuild compilation. `mist-core` beside `production` measures that gap.

## Results

### `results/2026-10-21-036112c1`

Measured Mist `036112c1` (harness commit; the parser sources are those of `0ec674a9`), Storm
`83410653`, on .NET 10.0.3 (SDK 10.0.103), macOS 26.6.2, Arm64, Apple M5 Pro (18 cores, 24 GB),
workstation GC, tiered compilation on its default, every assembly JIT-optimized. Workload: 244 inputs
(195 files, 49 derived invalid), 858,186 bytes, 176,746 tokens. The machine was shared: load average
10.9 at start and 15.3 at end, with 20 to 21 other `dotnet` processes running. Wall and CPU time agree
within 2% on every variant's median, so the parsing thread was rarely descheduled, but the ranges carry
that load. One calibration run with four variants preceded this one and is not reported. It ran
before `explicit-only` was added and before the design was fixed at its current shape.

**Plans.** The grammar lowers to 137 rules, 7 of them `@memo`. Automatic analysis alone selects 84,
including 6 of the 7 annotated rules. The annotations add exactly one rule, `TopLevelItem`, so
production memoizes 85.

**Equivalence.** 0 mismatches over 244 inputs and 5 variants: identical tree fingerprints, stop
positions and diagnostics. The generated parser reports no diagnostics on these inputs. On 34 invalid
inputs it stops before the end of input and returns a partial tree, which Mist's wrapper then reports.
All 210 others reach the end.

**Warm**, 20 passes per variant over the whole workload:

| Variant | Wall ms, median [range] | Per-round ratio to production, median [range] | Alloc MB/pass | GC pause ms, median | gen0/gen1 |
|---|---|---|---|---|---|
| mist-core | 163.4 [154.7..299.1] | 1.001 [0.913..1.065] | 485.3 | 93.6 | 52/14 |
| production | 163.0 [154.1..272.1] | 1 | 485.3 | 92.9 | 52/14 |
| auto-only | 178.8 [155.4..285.2] | 1.033 [0.930..1.192] | 485.3 | 103.6 | 52/14 |
| explicit-only (experimental) | 115.3 [97.7..192.8] | 0.645 [0.557..0.809] | 633.5 | 47.6 | 75/9 |
| no-memo (experimental) | 3262.3 [3004.2..4372.4] | 19.5 [16.0..21.8] | 23,559 | 614.9 | 2816/14 |

**Cold**, 6 fresh processes per variant, medians [range] in ms: `production` generation 548 [523..798],
in-memory compile and load 1448 [1321..2470], first pass with JIT 503 [464..725], second pass 454
[347..641]. `mist-core` has no generation or compile step: first pass 492 [438..642], second 365
[312..502]. `auto-only` matches `production` within its range. `explicit-only`'s first pass is 379
[330..432] and `no-memo`'s is 5995 [5252..7435]. Generation and compile cost the same for every plan.
They are build-time costs, since MSBuild runs the generator, and no Mist run pays them.

**Attribution.** Production fills 702,704 memo entries per pass over the workload. They spread almost
evenly down the expression precedence chain: `PostfixSuffix`, `Unary`, `Primary`, `Postfix`, `Range`,
`Multiplicative` and the others below `Expression` each take 21,000 to 25,000 entries, about one per
token position. `explicit-only` fills 118,955. GC pauses are 57% of production's warm wall time and 41%
of `explicit-only`'s. The per-input medians in `warm-inputs.csv` show where the aggregate comes from:

- `auto-only` is within 0.8x to 1.2x of production on every input.
- `explicit-only` is faster than 0.8x on 45 inputs and more than 2x slower on 26, up to 49x
  (`Source/Standard/Bootstrap/SByte.mist`, 65 us to 3,227 us), 24x on a derived invalid input and 16x
  on `samples/VoidType/Result.mist`.
- `no-memo` is up to 206x slower on one file (`samples/SizedSequencePatterns/Program.mist`, 3.9 ms
  to 808 ms).

### Conclusion

No tuning of automatic coverage is warranted from this profile.

- Removing the `@memo` annotations is not a meaningful variant. It drops one rule, and the 3% median
  difference sits inside a 0.93 to 1.19 range of per-round ratios on a loaded machine.
- The automatic plan does cost real time. GC is more than half of warm parse time, and
  `explicit-only` allocates 30% more yet pauses half as long, which points at what the memo tables
  keep alive. This profile does not measure retention, so that remains an inference. Yet the only cheaper plan measured, the 7 annotated
  rules, buys a 35% aggregate saving by accepting slowdowns of up to 49x on individual files.
  Memoization itself is plainly needed: none at all is 19x slower overall and 206x on one file.
  Coverage is buying protection against backtracking blow-ups, and a smaller plan would have to show
  it keeps that protection on every input, not only on the aggregate.
- Whatever cuts the parser's allocation, about 2.7 KB per token in production (3.6 KB under
  `explicit-only`), would attack the
  GC share without changing coverage. That is a different change from coverage tuning, and it is
  recorded as an unverified lead on MIST-194 rather than pursued here.
