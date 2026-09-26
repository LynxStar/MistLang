# Purity Implementation Tasks

The work extends the isolated v2 compiler; classic parses the new keywords through the shared grammar and ignores them. Syntax and model first, then the analysis phase over that model, then the sample, book and registers.

- [ ] 1. Syntax and model.
  - Reserve `pure` and `local`; add the modifier, lambda modifier and `pure` function type productions; rename the two test variables named `local`.
  - Record `IsPure` and `IsLocal` on `MemberModifiers`, with `pure` implying `readonly`; inherit both from a type per Requirement 15; report the conflict, info and placement diagnostics.
  - Carry `IsPure` on `FunctionType` through interning, substitution, display, resolution and conversions; erase it in overload signatures.
  - Print every new spelling; add the keywords to the TextMate grammar and both book highlighters.
  - Pin each with parse-print-parse tests, model probes and binder refusal cases.
- [ ] 2. Inference and the judge.
  - `Origins`: what a scope owns, holds frozen, or reaches from outside.
  - `PurityJudge`: one walk per scope kind yielding findings for every row of the design's table.
  - `PurityInference`: the whole-program greatest fixpoint over every body, bodiless members by declaration, abstract members by their implementations, pure and local readonly gated by `ReadonlyInference`.
- [ ] 3. The analyzer and the amendments.
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
