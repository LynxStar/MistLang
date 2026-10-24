# Requirements Document

## Introduction

This document specifies the requirements for expanding the Refinement Comparison Engine in the Mist compiler. The engine already proves basic implication relationships between refinement predicates (e.g., `>= 21` implies `>= 18`). This spec extends it to handle additional patterns — constant equality, inequality, disjunction (`or`), negation (`not`), and cross-operator implications — making the engine capable of proving more relationships without requiring explicit pattern matching from the developer.

The comparison engine is used in two contexts today:

1. **Implicit widening between refinement types**: When assigning a variable of one refinement type to a destination of another (e.g., `DrinkingAge` → `AdultAge`), the engine determines if the assignment is safe.
2. **Interface/trait constraint satisfaction**: When validating that an implementation's adhoc refinement constraint is not stronger than what the interface/trait requires.

Expanding the engine's proving power means more assignments are accepted implicitly and more constraint relationships are validated correctly — without changing the overall architecture.

Note: The parser already produces `ConstantPatternPrimaryPatternNode` for bare constant patterns (e.g., `int 7`), `NotPatternUnaryPatternNode` for not patterns (e.g., `int not 7`), and `OrPatternNode` for disjunction patterns (e.g., `int 7 or 8`). The `Evaluate` method already handles these correctly. The gap is in the `Implies` method which currently only extracts relational constraints.

## Glossary

- **Refinement_Comparison_Engine**: The compile-time logic (currently in `RefinementPredicate.Implies()`) that determines whether one refinement predicate logically implies another.
- **Refinement_Predicate**: The pattern-match expression attached to a refinement type definition that constrains valid values (e.g., `>= 18`, `>= 0 and <= 100`).
- **Relational_Constraint**: A single operator-value pair extracted from a predicate (e.g., `>= 18` is `(GreaterThanOrEqual, 18)`).
- **Base_Type**: The underlying type from which a refinement type is derived (e.g., `int` is the base type of `type AdultAge = int >= 18;`).
- **Implication**: Source predicate implies target predicate when every value satisfying the source also satisfies the target.
- **Or_Pattern**: A disjunction pattern listing alternative constant values (e.g., `7 or 8`). Represented in the AST as an `OrPatternNode` containing multiple `AndPatternNode` alternatives.
- **Not_Pattern**: A negation pattern excluding a specific value (e.g., `not 7`). Represented in the AST as a `NotPatternUnaryPatternNode` wrapping a `ConstantPatternPrimaryPatternNode`.

## Requirements

### Requirement 1: Equality Pattern Support

**User Story:** As a Mist developer, I want the comparison engine to understand `== literal` patterns, so that a refinement like `== 7` is recognized as implying `>= 0` or `<= 10` without explicit pattern matching.

#### Acceptance Criteria

1. THE Refinement_Comparison_Engine SHALL treat a constant equality pattern (`== literal`) as equivalent to `>= literal and <= literal` for implication purposes.
2. `== a` SHALL imply `>= b` when `a >= b`.
3. `== a` SHALL imply `<= b` when `a <= b`.
4. `== a` SHALL imply `> b` when `a > b`.
5. `== a` SHALL imply `< b` when `a < b`.
6. `== a` SHALL imply `== a` (identity).
7. `== a` SHALL NOT imply `== b` when `a != b`.

### Requirement 2: Inequality Pattern Support

**User Story:** As a Mist developer, I want the comparison engine to understand `!= literal` patterns, so that identity implications and equality-to-inequality relationships are handled correctly.

#### Acceptance Criteria

1. `!= a` SHALL imply `!= a` (identity).
2. `== a` SHALL imply `!= b` when `a != b` (a value that is exactly `a` is necessarily not `b`).
3. `!= a` SHALL NOT imply any relational bound (`>=`, `<=`, `>`, `<`). Knowing a value is not `a` does not constrain it to any range.
4. `!= a` SHALL NOT imply `== b` for any `b`. Knowing a value is not `a` does not prove it equals something specific.
5. No relational bound (`>=`, `<=`, `>`, `<`) SHALL imply `!= a` unless the bound's range provably excludes `a`. Specifically: `> a` SHALL imply `!= a`; `< a` SHALL imply `!= a`; `>= a` SHALL NOT imply `!= a`; `<= a` SHALL NOT imply `!= a`.

### Requirement 3: Cross-Operator Implication

**User Story:** As a Mist developer, I want the comparison engine to prove implications across mixed relational operators (e.g., `> 5` implies `>= 5`), so that more subset relationships are recognized without explicit casting.

#### Acceptance Criteria

1. `>= a` SHALL imply `> b` when `a > b`.
2. `<= a` SHALL imply `< b` when `a < b`.
3. `> a` SHALL imply `> b` when `a >= b`.
4. `< a` SHALL imply `< b` when `a <= b`.

### Requirement 4: Unsupported Pattern Rejection

**User Story:** As a Mist developer, I want the comparison engine to return false (not crash) for patterns it cannot reason about, so that unsupported patterns fall back to requiring explicit pattern matching.

#### Acceptance Criteria

1. THE Refinement_Comparison_Engine SHALL return false when the predicate contains property patterns.
2. THE Refinement_Comparison_Engine SHALL return false when the predicate contains regex patterns.
3. THE Refinement_Comparison_Engine SHALL NOT attempt arithmetic reasoning (e.g., will not prove `>= x + 1` implies `>= x`).
4. THE Refinement_Comparison_Engine SHALL NOT attempt cross-method reasoning.

### Requirement 5: Multi-Numeric-Type Support

**User Story:** As a Mist developer, I want the comparison engine to work across all integer-like types, so that refinements on `short`, `byte`, `char`, etc. are compared correctly.

#### Acceptance Criteria

1. THE Refinement_Comparison_Engine SHALL extract integer literal constant values from relational pattern operands supporting `int`, `long`, `short`, `byte`, `sbyte`, `ushort`, `uint`, and `char` types by converting them to a common 64-bit integer representation for comparison.
2. THE Refinement_Comparison_Engine SHALL only attempt implication proofs when both predicates share the same Base_Type. IF the Base_Types differ, THEN it SHALL return false.

### Requirement 6: Sample Application

**User Story:** As a Mist developer, I want a comprehensive sample application demonstrating the expanded comparison engine, so that I can see every supported implication pattern in one place and verify correctness.

#### Acceptance Criteria

1. A sample project SHALL exist at `samples/RefinementComparison/Program.mist`.
2. The sample SHALL demonstrate every implication pattern defined in the preceding requirements, including but not limited to: equality implying relational bounds, inequality identity, cross-operator implications, compound predicates with mixed operators, multi-numeric-type comparisons, or-pattern implications, and not-pattern implications.
3. Each demonstrated permutation SHALL print labeled output to stdout so correctness is observable.
4. `samples/AllFeatures/Program.mist` SHALL call `RefinementComparison.Program.RunDemo()` to include the feature in the full-suite sample run.
5. The sample SHALL compile and run without errors using the Mist compiler.

### Requirement 7: Or Pattern Implication Support

**User Story:** As a Mist developer, I want the comparison engine to reason about `or` (disjunction) patterns, so that refinement types like `int 7 or 8` can participate in subset assignability checks.

#### Acceptance Criteria

1. `== a` SHALL imply `a or b` (a single value is a member of a set containing it).
2. `a or b` SHALL imply `a or b` (identity).
3. `a or b` SHALL imply `a or b or c` (a subset of alternatives implies a superset of alternatives).
4. `a or b` SHALL NOT imply `== a` (a disjunction does not prove a specific single value).
5. WHEN the target is an Or_Pattern, THE Refinement_Comparison_Engine SHALL check whether the source implies at least one alternative in the target.
6. WHEN the source is an Or_Pattern, THE Refinement_Comparison_Engine SHALL check that every alternative in the source individually implies the target.
7. THE Refinement_Comparison_Engine SHALL NOT attempt range inference from or-pattern alternatives (e.g., `7 or 8` SHALL NOT be treated as implying `>= 7 and <= 8`).

### Requirement 8: Not Pattern Implication Support

**User Story:** As a Mist developer, I want the comparison engine to reason about `not` (negation) patterns in implication checks, so that refinement types like `int not 7` can participate in subset assignability.

#### Acceptance Criteria

1. `not a` SHALL imply `not a` (identity).
2. `== a` SHALL imply `not b` when `a != b` (a value that is exactly `a` is necessarily not `b`). WHEN `a == b`, the implication SHALL NOT apply (`== a` does not imply `not a`).
3. `not a` SHALL NOT imply any relational bound (`>=`, `<=`, `>`, `<`). Knowing a value is not `a` does not constrain it to any range.
4. `not a` SHALL NOT imply `== b` for any `b`. Knowing a value is not `a` does not prove it equals something specific.
5. `> a` SHALL imply `not a` (all values strictly greater than `a` exclude `a` itself).
6. `< a` SHALL imply `not a` (all values strictly less than `a` exclude `a` itself).
7. `>= a` SHALL NOT imply `not a` (the range includes `a` itself).
8. `<= a` SHALL NOT imply `not a` (the range includes `a` itself).
