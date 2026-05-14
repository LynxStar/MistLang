# Requirements Document

## Introduction

This document specifies the requirements for expanding the Refinement Comparison Engine in the Mist compiler. The engine already proves basic implication relationships between refinement predicates (e.g., `>= 21` implies `>= 18`). This spec extends it to handle additional patterns — constant equality, inequality, and cross-operator implications — making the engine capable of proving more relationships without requiring explicit pattern matching from the developer.

The comparison engine is used in two contexts today:

1. **Implicit widening between refinement types**: When assigning a variable of one refinement type to a destination of another (e.g., `DrinkingAge` → `AdultAge`), the engine determines if the assignment is safe.
2. **Interface/trait constraint satisfaction**: When validating that an implementation's adhoc refinement constraint is not stronger than what the interface/trait requires.

Expanding the engine's proving power means more assignments are accepted implicitly and more constraint relationships are validated correctly — without changing the overall architecture.

## Glossary

- **Refinement_Comparison_Engine**: The compile-time logic (currently in `RefinementPredicate.Implies()`) that determines whether one refinement predicate logically implies another.
- **Refinement_Predicate**: The pattern-match expression attached to a refinement type definition that constrains valid values (e.g., `>= 18`, `>= 0 and <= 100`).
- **Relational_Constraint**: A single operator-value pair extracted from a predicate (e.g., `>= 18` is `(GreaterThanOrEqual, 18)`).
- **Base_Type**: The underlying type from which a refinement type is derived (e.g., `int` is the base type of `type AdultAge = int >= 18;`).
- **Implication**: Source predicate implies target predicate when every value satisfying the source also satisfies the target.

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
2. THE Refinement_Comparison_Engine SHALL return false when the predicate contains `or` patterns.
3. THE Refinement_Comparison_Engine SHALL return false when the predicate contains regex patterns.
4. THE Refinement_Comparison_Engine SHALL return false when the predicate contains `not` patterns other than `!= literal`.
5. THE Refinement_Comparison_Engine SHALL NOT attempt arithmetic reasoning (e.g., will not prove `>= x + 1` implies `>= x`).
6. THE Refinement_Comparison_Engine SHALL NOT attempt cross-method reasoning.

### Requirement 5: Multi-Numeric-Type Support

**User Story:** As a Mist developer, I want the comparison engine to work across all integer-like types, so that refinements on `short`, `byte`, `char`, etc. are compared correctly.

#### Acceptance Criteria

1. THE Refinement_Comparison_Engine SHALL extract integer literal constant values from relational pattern operands supporting `int`, `long`, `short`, `byte`, `sbyte`, `ushort`, `uint`, and `char` types by converting them to a common 64-bit integer representation for comparison.
2. THE Refinement_Comparison_Engine SHALL only attempt implication proofs when both predicates share the same Base_Type. IF the Base_Types differ, THEN it SHALL return false.

### Requirement 6: Sample Application

**User Story:** As a Mist developer, I want a comprehensive sample application demonstrating the expanded comparison engine, so that I can see every supported implication pattern in one place and verify correctness.

#### Acceptance Criteria

1. A sample project SHALL exist at `samples/RefinementComparison/Program.mist`.
2. The sample SHALL demonstrate every implication pattern defined in the preceding requirements, including but not limited to: equality implying relational bounds, inequality identity, cross-operator implications, compound predicates with mixed operators, and multi-numeric-type comparisons.
3. Each demonstrated permutation SHALL print labeled output to stdout so correctness is observable.
4. `samples/AllFeatures/Program.mist` SHALL call `RefinementComparison.Program.RunDemo()` to include the feature in the full-suite sample run.
5. The sample SHALL compile and run without errors using the Mist compiler.
