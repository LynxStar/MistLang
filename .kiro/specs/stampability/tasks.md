# Stampability Implementation Tasks

The work extends the isolated v2 compiler. Independent grammar/model representation, structural classification, and specification/book work can proceed in parallel. Analyzer and backend integration follows once those interfaces agree; final verification follows all source changes.

- [x] 1. Record and reconcile the language contract.
  - Specify inference, declaration guarantees, and open generic proof in requirements and design.
  - Supersede definite-assignment's blanket type-parameter-field prohibition.
  - Cross-reference generic constraints and stack-only types.
- [x] 2. Extend shared syntax and v2 representation.
  - Add `stampable` modifier and constraint keyword productions.
  - Preserve the modifier and a distinct keyword constraint in the model.
  - Cover pretty-print round trips and invalid placement diagnostics.
- [x] 3. Extend the shared structural classifier.
  - Substitute own and enclosing receiver arguments and inherited receivers.
  - Include automatic-property and explicit `field` accessor backing storage and ignore static storage.
  - Prove constrained parameters; retain all existing category exclusions.
  - Accept finite nested constructions and terminate recursive expansion.
  - Add focused model probes before changing the classifier.
- [x] 4. Validate declaration guarantees and generic uses.
  - Check every marked declaration even when unused.
  - Check `stampable` at every recorded generic closing, including aliases and forwarding.
  - Reject incompatible constraints and preserve existing constraint semantics.
  - Add analyzer seam cases for each new refusal and accepted proof.
- [x] 5. Integrate backend and allocation behavior.
  - Emit valid `unmanaged` bounds for open generic stack allocation.
  - Reuse the shared proof for bare arrays and stack allocation.
  - Preserve type interning, closing records, length inference, and size evaluation count.
- [x] 6. Bank runtime and book coverage.
  - Add the v2 sample and compose it into `AllFeaturesV2` with its reviewed output.
  - Document inference, modifier guarantees, constraints, and the existing excluded categories.
  - Generate every new snippet's C# with `SnippetEmitter`, compile/run the positive snippets, and synchronize syntax highlighting.
- [x] 7. Complete required checks and evidence.
  - Run the zero-warning build, test ledger, formatter verification, affected samples, and classic `AllFeatures` compatibility check.
  - Inspect failures before rerunning and record any unchanged-test flakes.
  - Update model, analysis, and emitter registers with measured evidence and remaining limitations.

## Verification Record (2026-09-26)

- The initial 71-case probe reproduced 52 failures before integration (`TestResults/ledger/20260926-150759`). The final implementation adds 121 syntax, structural-model, analyzer, and emission cases, plus six refusal fixtures.
- The full solution ledger `TestResults/ledger/20260926-152727` passed all 7,681 tests: 6,907 Mist, 706 Storm, and 68 CLI. Its build completed with zero warnings and zero errors. No unchanged-test flakes occurred.
- Whole-solution formatter verification passed. The standalone `StampableTypes` sample passed, and the full suite verified its composition in `AllFeaturesV2`; computed sizes invoked their helper exactly twice. Existing baseline output outside the new sample is unchanged. Classic `AllFeatures` still builds without diagnostics.
- `SnippetEmitter` generated all three new C# tabs without failures; each new Mist example compiled and ran with the expected output. The affected stack-only snippet pairs were also regenerated successfully. `mdbook build`, both JavaScript syntax checks, include resolution, and highlighting synchronization passed.
- Analysis and emitter registers parse as YAML, and `git diff --check` passes. Arbitrary imported CLR layout proof is recorded as future model work; indirect constraint entailment and classic semantic support remain outside this delivery.

## Main Integration (2026-09-26)

- Implementation commit `870e1d13` was integrated with main's refinement, accessor, tuple, constraint-substitution, extension, and nested-generic changes through `a9dce212`.
- Generic closing records retain the complete sibling, receiver, and enclosing substitution. Open stampable forwarding retains its selective checks. Constructor witnesses retain both declared and closed signatures. Four added integration cases combine stampability with sibling constraints on scalar and pack arguments.
- Focused ledger `TestResults/ledger/20260926-154926` passed all 170 constraint cases with zero build warnings or errors. The earlier integration ledger `20260926-154123` was stopped cleanly after main advanced; it recorded no failures before interruption and is not counted as a full pass.
- The integrated whole-solution build and formatter verification pass. All five affected book snippet pairs regenerate unchanged without failures. The combined baseline preserves the latest main's output outside the two newly banked samples.
- Final integration ledger `TestResults/ledger/20260926-155133` passed all 8,230 tests: 7,454 Mist, 708 Storm, and 68 CLI, with zero failures and a zero-warning, zero-error build. No unchanged-test flakes occurred. Only documentation evidence changed after this run.
