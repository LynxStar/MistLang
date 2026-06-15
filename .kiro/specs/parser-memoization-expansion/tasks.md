# Implementation Plan: Parser Memoization Expansion

## Overview

Expands the Storm parser memoization subsystem with two new candidate sources: an automatic multi-caller alternation heuristic and a manual `@memo` annotation. Implementation follows a bottom-up pipeline approach — lexer → parser → syntax → IR → lowering → analysis — with tests at each layer, followed by applying `@memo` to the Mist grammar.

## Tasks

- [x] 1. Lexer: Add Annotation token support
  - [x] 1.1 Add `Annotation` enum value to `StormTokenKind` and implement lexing logic in `StormLexer.Tokenize`
    - Add `Annotation` to `StormTokenKind` enum in `StormLexer.cs`
    - In `Tokenize`, add a branch for `@` before operator dispatch: consume `@` + identifier chars as a single `Annotation` token
    - When `@` is not followed by an identifier-start char, emit `STORM_SYNTAX_ERROR` diagnostic and produce an `Error` token
    - _Requirements: 1.1, 1.2, 1.3_

  - [x] 1.2 Write unit tests for Annotation token lexing
    - Verify `@memo` produces a single `Annotation` token with lexeme `@memo`
    - Verify `@unknown` produces `Annotation` token with lexeme `@unknown`
    - Verify `@` alone (not followed by identifier) produces `Error` token and diagnostic
    - Verify `@123` (digit after @) produces `Error` token and diagnostic
    - Verify annotation token does not interfere with existing token classification
    - _Requirements: 1.1, 1.2, 1.3_

  - [x] 1.3 Write property test for annotation token lexing
    - **Property 1: Annotation token lexing**
    - For any string of form `@` + valid identifier, lexer produces exactly one `Annotation` token with lexeme equal to full input
    - **Validates: Requirements 1.1, 1.3**

- [x] 2. Parser: Recognize @memo annotation on rule declarations
  - [x] 2.1 Implement `ParseAnnotatedDeclaration` in `StormParser.cs`
    - Add `StormTokenKind.Annotation` case to `ParseDeclaration()` switch
    - Implement `ParseAnnotatedDeclaration()` method: consume annotation token, validate lexeme is `@memo`, validate next token is `RuleKeyword`, parse rule with `isMemo: true`
    - Emit error for unrecognized annotations (lexeme != `@memo`)
    - Emit error for `@memo` before `token`, `fragment`, or `hidden` declarations
    - Emit error for duplicate `@memo @memo` on same rule
    - Add `StormTokenKind.Annotation` to `SynchronizeToDeclaration()` recovery set
    - _Requirements: 2.1, 2.2, 2.3, 2.4_

  - [x] 2.2 Write unit tests for @memo parsing
    - Verify `@memo rule X = A;` produces `StormRuleDecl` with `IsMemo = true`
    - Verify `@memo token X = ...;` produces a diagnostic error
    - Verify `@unknown rule X = A;` produces a diagnostic error
    - Verify `@memo @memo rule X = A;` produces a duplicate error
    - Verify parser recovers after annotation errors and parses subsequent declarations
    - _Requirements: 2.1, 2.2, 2.3, 2.4_

  - [x] 2.3 Write property test for unrecognized annotation rejection
    - **Property 2: Unrecognized annotation rejection**
    - For any annotation token whose lexeme is not `@memo`, parser emits a diagnostic error and does not produce a valid declaration node
    - **Validates: Requirements 2.3**

- [x] 3. Syntax and IR: Extend StormRuleDecl and RuleIR with IsMemo property
  - [x] 3.1 Add `IsMemo` parameter to `StormRuleDecl` record in `StormSyntax.cs`
    - Add `bool IsMemo = false` as a defaulted parameter to the `StormRuleDecl` record
    - Ensures all existing call sites remain source-compatible via default value
    - _Requirements: 2.1, 3.2_

  - [x] 3.2 Add `IsMemo` property to `RuleIR` class in `RuleIR.cs`
    - Add `bool IsMemo` property to `RuleIR`
    - Add `bool isMemo = false` parameter to the `RuleIR` constructor
    - _Requirements: 3.2_

  - [x] 3.3 Propagate `IsMemo` in `GrammarIRLowering.Lower()` from `StormRuleDecl` to `RuleIR`
    - In the rule shell creation step of `GrammarIRLowering.Lower()`, read `StormRuleDecl.IsMemo` and pass it to `RuleIR` constructor
    - _Requirements: 3.1_

  - [x] 3.4 Write unit tests for memo annotation propagation through lowering
    - Verify grammar with `@memo rule X = A;` produces `RuleIR` with `IsMemo = true`
    - Verify grammar with `rule X = A;` (no annotation) produces `RuleIR` with `IsMemo = false`
    - Verify multiple rules where only some are annotated propagate correctly
    - _Requirements: 3.1, 3.2_

  - [x] 3.5 Write property test for memo propagation round-trip
    - **Property 3: Memo annotation propagation**
    - For any grammar source with rule declarations, `IsMemo` on each `RuleIR` equals `true` iff the corresponding `StormRuleDecl` had `IsMemo = true`
    - **Validates: Requirements 3.1, 3.2**

- [x] 4. Checkpoint - Ensure all tests pass
  - Ensure all tests pass, ask the user if questions arise.

- [x] 5. MemoizationAnalysisPass: Multi-caller heuristic and candidate union
  - [x] 5.1 Implement `FindMultiCallerCandidates` in `MemoizationAnalysisPass.cs`
    - Add a new static method `FindMultiCallerCandidates(GrammarIR grammar)` returning `HashSet<string>`
    - For each rule with 2+ alternatives, collect rule references per alternative using `CollectRuleRefs`
    - Rules appearing in 2+ distinct alternative ref-sets are multi-caller candidates
    - _Requirements: 4.1, 4.2, 4.3, 4.4_

  - [x] 5.2 Add `STORM_MEMO_IGNORED` diagnostic id and restructure `Run()` to union all candidate sources
    - Add `STORM_MEMO_IGNORED` constant to `StormDiagnosticIds`
    - Add optional `List<StormDiagnostic>? diagnostics` parameter to `MemoizationAnalysisPass.Run()`
    - Restructure `Run()` to: compute cycle candidates (existing), compute multi-caller candidates (new), collect @memo candidates from `RuleIR.IsMemo`, union all three sets, apply `IsSpeculativeSafe` filter
    - Emit `STORM_MEMO_IGNORED` warning when a `@memo`-annotated rule fails `IsSpeculativeSafe`
    - _Requirements: 5.1, 5.2, 5.3, 5.4_

  - [x] 5.3 Write unit tests for multi-caller heuristic
    - Verify a rule referenced in 2+ alternatives of a parent is detected as candidate
    - Verify a rule referenced in only 1 alternative is not detected
    - Verify detection works across multiple parent rules
    - Verify single-alternative parent rules do not produce candidates
    - _Requirements: 4.1, 4.2, 4.3_

  - [x] 5.4 Write unit tests for candidate union and IsSpeculativeSafe filter
    - Verify @memo-annotated rules appear in final plan when speculative-safe
    - Verify @memo-annotated rule that fails IsSpeculativeSafe is excluded with warning diagnostic
    - Verify cycle candidates, multi-caller candidates, and @memo candidates all merge correctly
    - Verify dense 0-based ids in declaration order
    - _Requirements: 5.1, 5.2, 5.3, 5.4_

  - [x] 5.5 Write property test for multi-caller candidate detection
    - **Property 4: Multi-caller candidate detection**
    - For any grammar IR where a rule R is referenced from 2+ distinct alternatives of any parent rule's alternation, R appears in the candidate set
    - **Validates: Requirements 4.1, 4.2**

  - [x] 5.6 Write property test for candidate union with speculative-safety filter
    - **Property 5: Candidate union with speculative-safety filter**
    - Final MemoizationPlan contains exactly those rules in the union of all three candidate sources that also pass IsSpeculativeSafe
    - **Validates: Requirements 5.1, 5.2**

  - [x] 5.7 Write property test for dense declaration-order id assignment
    - **Property 6: Dense declaration-order id assignment**
    - For any non-empty MemoizationPlan, assigned ids are dense 0..N-1 in grammar declaration order
    - **Validates: Requirements 5.4**

- [x] 6. Checkpoint - Ensure all tests pass
  - Ensure all tests pass, ask the user if questions arise.

- [x] 7. Apply @memo annotations to Mist grammar
  - [x] 7.1 Add `@memo` annotations to 7 hot-path rules in `Source/Mist/Grammar/Mist.storm`
    - Add `@memo` annotation before: Expression, Statement, Primary, Postfix, Unary, TypeReference, TopLevelItem
    - _Requirements: 6.1, 6.2_

  - [x] 7.2 Write integration test verifying Mist grammar memoization plan
    - Parse Mist.storm through the full pipeline, verify all 7 annotated rules appear in the MemoizationPlan
    - _Requirements: 6.1, 6.2_

- [x] 8. Integration verification
  - [x] 8.1 Write integration test for correctness preservation and performance target
    - Verify parsing the AllFeatures corpus with expanded memoization produces identical AST to baseline
    - Verify wall-clock time is under 2 seconds for the AllFeatures corpus parse
    - _Requirements: 7.1, 8.1_

  - [x] 8.2 Write property test for memoization transparency
    - **Property 7: Memoization transparency**
    - For any input string, parsing with memoized rule produces identical AST structure and diagnostics as parsing without memoization
    - **Validates: Requirements 7.1, 7.2, 7.3**

- [x] 9. Final checkpoint - Ensure all tests pass
  - Ensure all tests pass, ask the user if questions arise.

## Notes

- Tasks marked with `*` are optional and can be skipped for faster MVP
- Each task references specific requirements for traceability
- Checkpoints ensure incremental validation
- Property tests validate universal correctness properties from the design document
- Unit tests validate specific examples and edge cases
- The implementation language is C# (netstandard2.0 target for the source generator, net8.0 for tests)
- ParserEmitter requires no changes — it already consumes MemoizationPlan correctly
- All existing call sites remain source-compatible through default parameter values

## Task Dependency Graph

```json
{
  "waves": [
    { "id": 0, "tasks": ["1.1"] },
    { "id": 1, "tasks": ["1.2", "1.3", "3.1", "3.2"] },
    { "id": 2, "tasks": ["2.1", "3.3"] },
    { "id": 3, "tasks": ["2.2", "2.3", "3.4", "3.5"] },
    { "id": 4, "tasks": ["5.1"] },
    { "id": 5, "tasks": ["5.2"] },
    { "id": 6, "tasks": ["5.3", "5.4", "5.5", "5.6", "5.7"] },
    { "id": 7, "tasks": ["7.1"] },
    { "id": 8, "tasks": ["7.2", "8.1", "8.2"] }
  ]
}
```
