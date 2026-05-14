# Implementation Plan: Refinement Comparison Engine Expansion

## Overview

This plan expands the `RefinementPredicate.Implies()` method to handle constant equality, inequality (not), disjunction (or), and cross-operator implication patterns. The implementation is contained within `RefinementPredicate.cs` — no public API changes are needed. The approach introduces a unified `Constraint` type system, extends the extraction logic to recognize new AST patterns, and expands the pairwise implication matrix. Property-based tests validate correctness using brute-force oracles.

## Tasks

- [x] 1. Introduce unified constraint type system
  - [x] 1.1 Define new constraint types and unified wrapper in `RefinementPredicate.cs`
    - Add `EqualityConstraint(long Value)` record struct
    - Add `NotConstraint(long Value)` record struct
    - Add `OrConstraint(ImmutableArray<long> Values)` record struct
    - Add `ConstraintKind` enum with `Relational`, `Equality`, `Not`, `Or`
    - Add `Constraint` record struct wrapping all constraint types with nullable fields
    - All types are `private` nested types within `RefinementPredicate`
    - _Requirements: 1.1, 2.1, 7.1, 8.1_

  - [x] 1.2 Refactor `ExtractConstraints` to return `List<Constraint>` instead of `List<RelationalConstraint>`
    - Change return type of `ExtractConstraints` from `List<RelationalConstraint>` to `List<Constraint>`
    - Wrap existing relational extraction in `Constraint` with `ConstraintKind.Relational`
    - Update `Implies` method to operate on `List<Constraint>` instead of `List<RelationalConstraint>`
    - Preserve all existing relational-to-relational implication logic via `ConstraintImplies`
    - _Requirements: 1.1, 3.1, 3.2, 3.3, 3.4_

- [x] 2. Checkpoint - Ensure all existing tests pass after refactor
  - Ensure all tests pass, ask the user if questions arise.

- [x] 3. Implement equality constraint extraction and implication
  - [x] 3.1 Extend `ExtractConstraintsFromUnary` to recognize `ConstantPatternPrimaryPatternNode`
    - When a `DirectPatternUnaryPatternNode` contains a `ConstantPatternPrimaryPatternNode`, extract the integer literal value via `TryExtractLongFromExpression`
    - Produce a `Constraint` with `ConstraintKind.Equality` and `EqualityConstraint(value)`
    - _Requirements: 1.1, 5.1_

  - [x] 3.2 Implement equality-to-relational implication rules in the pairwise check
    - `== a` implies `>= b` when `a >= b`
    - `== a` implies `<= b` when `a <= b`
    - `== a` implies `> b` when `a > b`
    - `== a` implies `< b` when `a < b`
    - `== a` implies `== b` when `a == b`
    - `== a` does NOT imply `== b` when `a != b`
    - _Requirements: 1.2, 1.3, 1.4, 1.5, 1.6, 1.7_

  - [x] 3.3 Write property test: Equality-to-relational equivalence (Property 1)
    - **Property 1: Equality-to-relational equivalence**
    - For any integer value `a` and any relational constraint target, `Implies(== a, target)` returns the same result as `Implies(>= a and <= a, target)`
    - Construct AST nodes in-process, no process spawning
    - **Validates: Requirements 1.1, 1.2, 1.3, 1.4, 1.5, 1.6, 1.7**

- [x] 4. Implement not-pattern constraint extraction and implication
  - [x] 4.1 Extend `ExtractConstraintsFromUnary` to recognize `NotPatternUnaryPatternNode`
    - When a `NotPatternUnaryPatternNode` wraps a `ConstantPatternPrimaryPatternNode`, extract the integer literal value
    - Produce a `Constraint` with `ConstraintKind.Not` and `NotConstraint(value)`
    - _Requirements: 2.1, 8.1_

  - [x] 4.2 Implement not-pattern implication rules in the pairwise check
    - `not a` implies `not a` (identity, values match)
    - `== a` implies `not b` when `a != b`
    - `== a` does NOT imply `not b` when `a == b`
    - `> a` implies `not a`; `< a` implies `not a`
    - `>= a` does NOT imply `not a`; `<= a` does NOT imply `not a`
    - `not a` does NOT imply any relational bound
    - `not a` does NOT imply `== b` for any `b`
    - _Requirements: 2.1, 2.2, 2.3, 2.4, 2.5, 8.1, 8.2, 8.3, 8.4, 8.5, 8.6, 8.7, 8.8_

  - [x] 4.3 Write property test: Not-pattern identity and non-implication (Property 2)
    - **Property 2: Not-pattern identity and non-implication**
    - For any integer value `a`: `Implies(not a, not a)` returns true; `Implies(not a, relational)` returns false; `Implies(not a, == b)` returns false
    - Construct AST nodes in-process, no process spawning
    - **Validates: Requirements 2.1, 2.3, 2.4, 8.1, 8.3, 8.4**

  - [x] 4.4 Write property test: Equality implies not-pattern (Property 3)
    - **Property 3: Equality implies not-pattern**
    - For any integer values `a` and `b` where `a != b`: `Implies(== a, not b)` returns true; when `a == b`: returns false
    - Construct AST nodes in-process, no process spawning
    - **Validates: Requirements 2.2, 8.2**

  - [x] 4.5 Write property test: Relational-to-not implication (Property 4)
    - **Property 4: Relational-to-not implication**
    - For any integer value `a`: `Implies(> a, not a)` is true; `Implies(< a, not a)` is true; `Implies(>= a, not a)` is false; `Implies(<= a, not a)` is false
    - Construct AST nodes in-process, no process spawning
    - **Validates: Requirements 2.5, 8.5, 8.6, 8.7, 8.8**

- [x] 5. Implement cross-operator relational implication
  - [x] 5.1 Extend `ConstraintImplies` to handle cross-operator cases
    - `>= a` implies `> b` when `a > b`
    - `<= a` implies `< b` when `a < b`
    - `> a` implies `> b` when `a >= b`
    - `< a` implies `< b` when `a <= b`
    - _Requirements: 3.1, 3.2, 3.3, 3.4_

  - [x] 5.2 Write property test: Cross-operator relational implication (Property 5)
    - **Property 5: Cross-operator relational implication**
    - For any integer values `a` and `b`: verify the four cross-operator rules hold with correct boundary conditions
    - Construct AST nodes in-process, no process spawning
    - **Validates: Requirements 3.1, 3.2, 3.3, 3.4**

- [x] 6. Checkpoint - Ensure all tests pass after equality, not, and cross-operator support
  - Ensure all tests pass, ask the user if questions arise.

- [x] 7. Implement or-pattern constraint extraction and implication
  - [x] 7.1 Extend extraction logic to recognize or-patterns in `AndPatternNode`
    - Detect when the `AndPatternNode` AST structure contains `OrPatternNode` children
    - Extract individual constant values from each or-alternative into an `OrConstraint(ImmutableArray<long> values)`
    - Handle the AST shape where `Left` is a `DirectPatternUnaryPatternNode` with the first constant and additional alternatives appear via or-pattern structure
    - If any alternative is not an integer constant, return empty constraint list (implication returns false)
    - _Requirements: 7.1, 7.2, 7.3_

  - [x] 7.2 Implement or-pattern implication rules in the pairwise check
    - **Target is or-pattern:** `== a` implies `or(values)` when `a ∈ values`; source equality membership check
    - **Target is or-pattern:** `or(A)` implies `or(B)` when `A ⊆ B` (subset check)
    - **Source is or-pattern:** `or(values)` implies relational target when ALL values satisfy the relational constraint
    - **Source is or-pattern:** `or(values)` implies `not b` when ALL values != b
    - `or(values)` does NOT imply `== a` (disjunction doesn't prove single value)
    - Do NOT infer ranges from or-pattern alternatives
    - _Requirements: 7.1, 7.2, 7.3, 7.4, 7.5, 7.6, 7.7_

  - [x] 7.3 Write property test: Or-pattern source requires all alternatives to imply target (Property 6)
    - **Property 6: Or-pattern source requires all alternatives to imply target**
    - For any set of integer values forming an or-pattern source and any relational target, implication holds iff every value satisfies the target
    - Construct AST nodes in-process, no process spawning
    - **Validates: Requirements 7.4, 7.6**

  - [x] 7.4 Write property test: Or-pattern target membership (Property 7)
    - **Property 7: Or-pattern target membership**
    - For any integer value `a` and any set of values `S`, `Implies(== a, or(S))` is true iff `a ∈ S`
    - Construct AST nodes in-process, no process spawning
    - **Validates: Requirements 7.1, 7.5**

  - [x] 7.5 Write property test: Or-pattern subset implication (Property 8)
    - **Property 8: Or-pattern subset implication**
    - For any two sets `A` and `B` as or-patterns, `Implies(A, B)` is true iff `A ⊆ B`
    - Construct AST nodes in-process, no process spawning
    - **Validates: Requirements 7.2, 7.3**

- [x] 8. Checkpoint - Ensure all tests pass after or-pattern support
  - Ensure all tests pass, ask the user if questions arise.

- [x] 9. Implement soundness property test and unsupported pattern unit tests
  - [x] 9.1 Write property test: Soundness — Implies true means brute-force confirms (Property 9)
    - **Property 9: Soundness — Implies true means brute-force confirms**
    - For any pair of predicates from expanded constraint types, when `Implies(source, target)` returns true, then for every integer `v` in a test range, if `source.Evaluate(v)` is true then `target.Evaluate(v)` must also be true
    - Uses the existing `BruteForceImplies` oracle pattern
    - Construct AST nodes in-process, no process spawning
    - **Validates: Requirements 1.1–1.7, 2.1–2.5, 3.1–3.4, 7.1–7.7, 8.1–8.8**

  - [x] 9.2 Write unit tests for unsupported pattern rejection and edge cases
    - Test that property patterns cause `Implies` to return false (not crash)
    - Test that regex patterns cause `Implies` to return false (not crash)
    - Test that arithmetic expressions in pattern operands are not extracted
    - Test or-pattern with non-constant alternatives returns false
    - _Requirements: 4.1, 4.2, 4.3, 7.7_

- [x] 10. Sample application
  - [x] 10.1 Create `samples/RefinementComparison/mist.toml` and `samples/RefinementComparison/Program.mist`
    - Create `mist.toml` with `name = "RefinementComparison"`
    - Implement `Program.mist` with `public static void RunDemo()` demonstrating all implication patterns:
      - Equality implying relational bounds (`== 7` implies `>= 0`, `<= 10`, `> 5`, `< 10`) — label `[eq-implies-relational]`
      - Equality identity (`== 7` implies `== 7`) — label `[eq-identity]`
      - Inequality identity (`not 7` implies `not 7`) — label `[not-identity]`
      - Equality implies inequality (`== 5` implies `not 7`) — label `[eq-implies-not]`
      - Relational implies not (`> 5` implies `not 5`, `< 5` implies `not 5`) — label `[relational-implies-not]`
      - Cross-operator implications (`>= 10` implies `> 5`, `> 5` implies `> 5`) — label `[cross-operator]`
      - Or-pattern membership (`== 7` implies `7 or 8`) — label `[or-membership]`
      - Or-pattern subset (`7 or 8` implies `7 or 8 or 9`) — label `[or-subset]`
      - Or-pattern to relational (`7 or 8` implies `>= 7`, `<= 8`) — label `[or-implies-relational]`
      - Or-pattern to not (`7 or 8` implies `not 5`) — label `[or-implies-not]`
      - Compound predicates with mixed operators — label `[compound-mixed]`
      - Multi-numeric-type comparisons (short, byte) — label `[multi-numeric]`
    - Each permutation prints labeled output to stdout
    - _Requirements: 6.1, 6.2, 6.3, 6.5_

  - [x] 10.2 Integrate into `samples/AllFeatures`
    - Add `RefinementComparison.Program.RunDemo()` call in `samples/AllFeatures/Program.mist`
    - Add `[[references]]` entry with `path = "../RefinementComparison"` in `samples/AllFeatures/mist.toml`
    - _Requirements: 6.4_

- [x] 11. Final checkpoint - Ensure all tests pass and sample compiles
  - Ensure all tests pass, ask the user if questions arise.

## Notes

- Tasks marked with `*` are optional and can be skipped for faster MVP
- Each task references specific requirements for traceability
- Checkpoints ensure incremental validation
- Property tests validate universal correctness properties from the design document
- All property-based tests are **in-process only** — they construct AST nodes directly and call `Implies()` without spawning external processes
- The only process spawning is in the sample app integration test (1 compile + 1 run via AllFeatures orchestrator)
- The existing `RefinementImplicationPropertyTests` class provides the pattern for AST node construction and brute-force oracle; the new tests extend this approach
- The refactor in task 1.2 is the critical structural change — it must preserve all existing relational implication behavior
- No public API changes are needed; the `RefinementPredicate.Implies(RefinementPredicate other)` signature is unchanged
- Zero warnings policy applies: all new code must build with zero warnings

## Task Dependency Graph

```json
{
  "waves": [
    { "id": 0, "tasks": ["1.1"] },
    { "id": 1, "tasks": ["1.2"] },
    { "id": 2, "tasks": ["3.1", "5.1"] },
    { "id": 3, "tasks": ["3.2", "4.1"] },
    { "id": 4, "tasks": ["3.3", "4.2"] },
    { "id": 5, "tasks": ["4.3", "4.4", "4.5", "5.2"] },
    { "id": 6, "tasks": ["7.1"] },
    { "id": 7, "tasks": ["7.2"] },
    { "id": 8, "tasks": ["7.3", "7.4", "7.5"] },
    { "id": 9, "tasks": ["9.1", "9.2"] },
    { "id": 10, "tasks": ["10.1"] },
    { "id": 11, "tasks": ["10.2"] }
  ]
}
```
