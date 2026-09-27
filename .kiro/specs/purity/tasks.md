# Purity Implementation Tasks

The work extends the isolated v2 compiler; classic parses the new keywords through the shared grammar and ignores them. Syntax and model first, then the analysis phase over that model, then the sample, book and registers.

- [x] 1. Syntax and model.
  - Reserve `pure` and `local`; add the modifier, lambda modifier and `pure` function type productions; rename the two test variables named `local`.
  - Record `IsPure` and `IsLocal` on `MemberModifiers`, with `pure` implying `readonly`; inherit both from a type per Requirement 15; report the conflict, info and placement diagnostics.
  - Carry `IsPure` on `FunctionType` through interning, substitution, display, resolution and conversions; erase it in overload signatures.
  - Print every new spelling; add the keywords to the TextMate grammar and both book highlighters.
  - Pin each with parse-print-parse tests, model probes and binder refusal cases.
- [x] 2. Inference and the judge.
  - `Origins`: what a scope owns, holds frozen, or reaches from outside.
  - `PurityJudge`: one walk per scope kind yielding findings for every row of the design's table.
  - `PurityInference`: the whole-program greatest fixpoint over every body, bodiless members by declaration, abstract members by their implementations, pure and local readonly gated by `ReadonlyInference`.
- [x] 3. The analyzer and the amendments.
  - `PurityAnalyzer` over declared promises, pure lambdas, pure function conversions, field initializers of defaultable types and default entries.
  - `ReadonlyInference` and `ReadonlyAnalyzer` admit writes to what the context owns and calls to local members on it.
  - `StableMembers` admits a pure getter.
  - Declare Standard's promises.
  - Pin every judge row and every diagnostic with analyzer probes and refusal cases; measure every banked sample.
- [ ] 4. Sample, book and registers.
  - `samples/Purity`, composed into `AllFeaturesV2` as V2-ONLY with its output reviewed against the program.
  - `book/src/advanced/purity.md` and its snippets.
  - Analysis register order 13, emitter register evidence, the default-value-synthesis task closed, and this spec's status.
- [ ] 5. Verification.
  - Zero-warning build, formatter, full ledger, classic `AllFeatures`, both registers valid YAML.

## Verification Record (2026-09-27)

Tasks 1 to 3 are done. Task 4 is done except the book chapter: `book/src/advanced/purity.md` and its snippets were being written when this record was made, and are not verified here. Task 5 waits on the final ledger.

- **Syntax and model** (task 1). Tier 3: `PuritySyntaxTests` (53), `AccessorPromiseSyntaxTests` (14), `PurityModifierTests` (51), `PureFunctionTypeTests` (11), `AccessorPromiseTests` (8) and `PurityBindingRefusalTests` (5). Tier 1: `PureFunctionTypeDecisionTests` (3). Also at Tier 3, each class saying why: `ParamsLambdaBindingTests` (2), `DefaultableInitializerEmissionTests` (4), `ReadonlyEmissionDecisionTests` (1) and `StandardListPurityTests` (3), which reads the real `Standard/Experimental/List.mist`.
- **Judge, inference and analyzer** (tasks 2 and 3). Tier 2, over hand-built fragments (`PurityFragments`, `PuritySpecFragments`, `PurityJudgeFragments`, `PuritySoundnessFragments`, `ReadonlyContextFragments`, `AccessorPromiseFragments`, `PureFunctionTypeFragments` and their partials): `PurityJudgeTests` (144), `PurityRefusalTests` (53), `PuritySiteTests` (13), `PuritySoundnessTests` (12), `PuritySoundnessRoundTwoTests` (36), `AccessorPromiseRefusalTests` (23), `ReadonlyContextGapTests` (16), `ReadonlyOwnedWritesTests` (12), `DefaultConsistencyTests` (19), `OwnedReturnProvenanceTests` (8), `StablePureGetterTests` (9), `PurityInferenceTests` (20), `PurityContractTests` (11), `PureFunctionConversionTests` (23) and `PureFunctionTypeRefusalTests` (3).
- **Tier migration** (commit 0141cb26). Every refusal case the branch had written, and every probe row that judged a rule, became a Tier 2 row or a Tier 1 call of the decision. The six rows main pinned that this spec flips were rewritten to shapes that still refuse, and the flipped shapes are pinned as admissions beside them. These are the five predicate getters Requirement 12.2 admits and the indexer set on a fresh local that Requirement 11.1 admits. Six one-line mutations of the rules each failed named rows. Before the migration, a purity check needed the refusal corpus, which took 6 m 1 s. After it, the purity filter ran 3,519 tests in 10 s.
- **Adversarial rounds** (commit 41f9a02f). Three rounds of independent agents wrote programs the analysis admitted but should refuse. They reported 31, 50 and 44 of these holes, 125 in all. 112 were closed, each with a Tier 2 row in `PurityRoundThreeTests`, `PurityRoundFourTests` or `PurityRoundFiveTests`. Of the 13 left open, 11 were about the current culture, which culture-invariant emission closes. The other 2 are decisions, and the analysis register records them together with the hunt, which has not converged. After round five the purity filter passed 3,670 of 3,670 and the baselines passed 5 of 5.
- **Culture-invariant emission** (Requirement 13.2, decision A, 2026-09-27; uncommitted when this was recorded). Tier 3: `InvariantCultureEmissionTests` (18). Tier 4: `CultureInvariantEmissionTests` compiles once and runs under `tr_TR.UTF-8` and `de_DE.UTF-8`. `AllFeaturesV2` prints output byte-identical to its baseline under `tr_TR`, `de_DE` and `sv_SE`. The build has zero warnings and the formatter is clean.
- **Sample** (Requirement 17; uncommitted when this was recorded). `samples/Purity` is composed into `AllFeaturesV2` as V2-ONLY, and its 23 lines were read against the program. Classic stops with `MIST_UNDEFINED_TYPE` on `List` and `MIST_UNDEFINED_IDENTIFIER` on the local function. On v2 it compiles with no diagnostic of any severity, with and without `--quiet`. Results: baselines 5 of 5, purity filter 3,670 of 3,670, `SpecProofCodeMatchesSampleTest` 2 of 2. Seventeen one-line mutations across twelve of its demonstrations were each run by hand to the refusal expected; the emitter register's `Purity` entry lists them. The `[culture]` lines were checked by running the built sample under `tr_TR.UTF-8` and `de_DE.UTF-8`.
- **Ledgers.** In ledger 20260927-035235, 10,047 of 10,049 tests passed. The two failures were classic-side stdlib tests reading `Standard/Experimental`, fixed by commit 0a535fca. In ledger 20260927-091615, all 10,210 tests passed: 9,434 compiler, 68 CLI and 708 source generator, with the tests taking 151 s.
- Every build reported zero warnings, and `dotnet format --verify-no-changes` was clean at each step. Every test class the branch adds declares exactly one Tier trait. Both registers parse as valid YAML.
- **Final ledger: pending.**
