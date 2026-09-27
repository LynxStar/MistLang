# Refusal corpus migration

**Completed 2026-09-26.** All 759 original cases and their 764 diagnostic obligations have named
replacement rows at the owning decision or fragment. The verified corpus, its README and
`RefusalCorpusTest` are deleted. Refusal coverage uses hand-built inputs at Tier 1 or Tier 2,
with seven Tier 4 cases for the binding-to-analysis boundary.

The measurements and original migration procedure below explain the tier choices and deletion gates.

Read first: the `## Tests` section of the root `CLAUDE.md` (binding), the testing philosophy
(`.claude/skills/testing-philosophy/SKILL.md`), and `Source/Mist/ProgramAnalysis/CLAUDE.md`,
"Proving a rule". Commit `02f57db9` froze the corpus and records why.

## Verification at completion

- Original corpus plus replacements: ledger `20260926-214517`, 1,017 passed. The original
  diagnostic behavior held against the production refactoring before deletion.
- Grouped mutation evidence: ledger `20260926-221712`, 61 mutations in 47 production files,
  878 expected failures. Every original negative case had all matching refusal rows fail
  (759/759); the five positive exhaustiveness rows were excluded from that negative audit.
  All 47 production files match their restored copies byte for byte.
- Final classified run before deletion: ledger `20260926-225317`, 1,020 passed in a 1s test
  phase. The passing-row audit matched all 759 originals to 829 named rows, including site
  and silence coverage. All 764 original diagnostic IDs and multiplicities are retained.
- Tier audit: every new or migrated test has exactly one effective xUnit Tier trait. The
  classified cases are 443 Tier 1, 570 Tier 2 and seven Tier 4. Tier 1 invokes decisions over
  direct values; Tier 2 walks hand-built model or binder syntax fragments with stub ambient
  dependencies. Six boundary cases run binding into the complete analysis runner; the seventh
  binds a failed struct-local member and checks that definite-assignment analysis stays silent.
  Existing unclassified tests remain unmarked.
- After deletion: build passed with zero warnings and errors in 5.26s; full-solution
  `dotnet format --verify-no-changes --no-restore` passed. Ledger `20260926-225427` ran the
  unfiltered solution with current binaries: **9,491 passed, zero failed, zero skipped**
  (Mist.Tests 8,715, CLI 68, source generator 708), including both AllFeatures baselines.
  Test wall time was **162s (2m42s)**; the main assembly took 161s. Build time is separate.
  `failures.txt` is empty, and no rerun was needed.

The final run used an 18-CPU macOS host; the original 13m08s full-run measurement below used
32 CPUs. These are observed timings, not a controlled comparison on identical hardware.
Ledger evidence is local under `TestResults/ledger/` and ignored by Git; the committed named
replacement tests are the durable coverage record. There is no second per-case checklist.

## Why

`refusals/` and `RefusalCorpusTest` completely ignored the testing philosophy. Each case compiles a
whole program in-process to check what one stage reports: Tier 4–5 machinery for Tier 1–2
invariants. On ledger `20260926-191851` (2026-09-26, 32 cores), the full suite took 13m08s
(including a 33s build). `RefusalCorpusTest` took 697s of the 754s test phase; every other test had
finished by 173s. Without it the suite is about 3½ minutes.

## What was measured

A throwaway test (not committed) compiled 40 cases spread across all 11 directories, exactly as
`RefusalCorpusTest` does:

- **Every compile costs 680–1,120ms, 809ms on average, whatever the case contains.** The case's own
  program is a rounding error; the cost is fixed per compile.
- **The ambient provider is not it.** `AmbientTypeProviderV2Factory.Create` takes about 5ms once
  warm.
- **Not measured:** where the fixed cost goes. The likely candidate is binding and analysing all of
  Standard on every compile, since analysis scope includes Standard. The same cost probably
  inflates every Tier 3 test built over the real Standard sources, which is worth measuring
  separately.

Deleting a case saves about 0.8s of test time.

## Where each claim goes

A corpus case asserts three things. None of them needs a compiled program.

| Claim | Tier | Count |
|---|---|---|
| The rule refuses this input, with this diagnostic id | **Tier 1:** call the rule's decision with inputs built directly | One row per case, about 759 rows |
| The rule reports at the right place | **Tier 2:** the rule's walk over a hand-built model fragment | A few per rule: one per kind of site, not per case |
| Nothing else fires (one mistake, one complaint) | **Tier 4**, the Stage 4→5 seam | 5–10 in total |

For the Tier 4 set, keep `refusals/binding/unresolved-identifier.mist`: on its first run it caught
`Console.WriteLine(neverDeclared)` reporting both the unresolved identifier and a derived
`MIST_PM_UNRESOLVABLE_OVERLOAD`.

Rules that walk a body — `flow-framework`, `ownership-borrow-checking` — take Tier 2 for the first
claim too, over a body or flow graph built by hand, not parsed. Tier 3 is rare, and needs a reason
the lower tiers cannot give.

## Starting point before migration

- **Hand-built types work today.** `new TypeInterner()` and
  `new ClassType("Int32", "Standard", new Scope(null), null, null)`, as in
  `Test/Mist.Tests/Backend/ConstructedTypeInterningV2Tests.cs`. Model types are `internal`;
  `Mist.Tests` has `InternalsVisibleTo`.
- **Some decisions are callable as they stand.** For example,
  `Assignability.Judge(IMistType from, IMistType to, TypeInterner interner, long? integerConstant)`,
  `LiteralPredicates.Decide(IMistType, LiteralValue?)` and `AccessDomains.Admits(owner, assembly,
  accessibility, declaring)`.
- **Some are not.** `RefinementContracts.Judge` takes an `AnalysisContext`. Under "Proving a rule",
  a decision that cannot be called without compiling a program is refactored out into a callable
  function; it is not tested higher up. Expect this cost to be uneven across directories.
- **Ambient stubs.** `Test/Mist.Tests/Helpers/NullAmbientTypeProvider.cs` resolves nothing.
  `ProgramModelProbe` has a private `StandardTypesAmbientProvider`, with copies in
  `BodyBindingIntegrationTests`, `UnknownStorageBanTests` and `TargetTypedNewTests`. A shared one may
  be worth extracting.
- **Tier 1 examples:** `AdmittedTests`, `RealSetTests`, `ConstructedTypeInterningV2Tests`. No Stage 5
  *rule* has a Tier 1 test yet. Even the `*DecisionTests` files parse over real Standard and call
  themselves Tier 3. The pilot below makes the first one.

## Original migration order

Case counts per directory, 759 in total:

1. **Pilot: `assignability-core` (67).** Its decision takes only types. Build the hand-built-type
   helper the other directories will reuse, and set the pattern they copy.
2. **Pure decisions:** `total-conversion-classification` (26), `refinement-proving` (203, which has
   Tier 1 interval tests to build on), `operator-and-positional-checks` (63), `pattern-checking`
   (14), `exhaustiveness-checking` (5), `abstract-conformance-checking` (20).
3. **`declaration-shape-checks` (170).** Its decisions read declarations, so some rows will be Tier 2.
4. **Tier 2:** `flow-framework` (75), `ownership-borrow-checking` (75).
5. **`binding/` (41).** These are the binder's refusals, so they move into the binder's own Tier 1–3
   tests, not Stage 5's.

After the pilot, steps 2–5 are independent of each other and can run in parallel worktrees.

## Procedure, per directory

1. Read every case in the directory. Its `// REFUSE: <ID>` marker names the diagnostic, and the line
   under it is what is refused.
2. Find the rule's decision. If it cannot be called without a model, extract it first.
3. Write a Tier 1 `[Theory]` (Tier 2 where the rule walks a body): **one row per case**, recognisably
   named after the case file, asserting the same diagnostic id. A case's silences, where it has
   them, become rows too. Declare `[Trait("Tier", "1")]` or `[Trait("Tier", "2")]` on each new
   test method, or on a concrete class whose tests all share that tier, following `Test/CLAUDE.md`.
4. Add a few Tier 2 tests for where the rule reports: one per kind of site, tagged
   `[Trait("Tier", "2")]`. Tag the retained Stage 4-to-5 seam tests `[Trait("Tier", "4")]`.
5. Mutation-check the new rows: break the decision and confirm each row fails. The register records
   how, on order 1: restore with a copy plus a touch, and re-run after the final restore, not trusting
   the build's exit code.
6. **Delete each `.mist` file only when its row exists and passes.** The directory emptying is the
   progress record. Do not keep a second checklist.
7. Run the new tests and `RefusalCorpusTest` through `tools/test-ledger/run.sh` (see `Test/CLAUDE.md`).

Follow the code comment budget: a `// Tier N (...)` header, if present, is that file's one comment.
The runnable trait is required; a tier comment is not a substitute. Existing tests outside the
migration may stay unclassified.

## Completed cleanup

- The 759 `.mist` files, corpus README, directories and legacy runner are deleted after their
  replacement rows passed and the saved mutation evidence was audited.
- Root `CLAUDE.md`, `Source/Mist/ProgramAnalysis/CLAUDE.md` and `Test/CLAUDE.md` retain the testing
  rules without instructing contributors to add corpus files. Active emitter-register references
  name the replacement tests; dated historical evidence remains.
- The `rejection-ledger` WITHDRAWN task in `backlog/analysis-v2-capability-progression.yaml` records
  completion and the measured unfiltered suite result.

## Stopgap not needed

The proposed per-directory split of `RefusalCorpusTest` was not implemented. The migration finished
without keeping the whole-program refusal harness alive as a parallel runner.

## Environment notes

- `tools/test-ledger/run.sh` calls Python to write `summary.txt` and `failures.txt`. On Arthur's
  Windows machine Python is not installed, and the Store alias answers instead, so neither file is
  written. Read `test.log` and the trx files in the ledger directory instead.
- The ledger's `env.txt` cannot list other `dotnet` processes under Git Bash (`ps -x` fails), so it
  cannot rule out a concurrent suite skewing timings.
