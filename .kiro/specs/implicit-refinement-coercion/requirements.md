# Requirements Document

## Introduction

This document specifies the requirements for adding implicit refinement coercion and flow-based refinement claims to the Mist programming language. Currently, the refinement type system requires explicit pattern matching to convert between refinement types, even when the compiler could statically prove that one refinement is a subset or superset of another. This feature improves ergonomics by allowing the compiler to perform implicit coercions when provably safe, and by tracking refinement "claims" through control flow so that variables accumulate known constraints without explicit annotation.

The feature introduces three interconnected capabilities:

1. **Implicit widening coercion for value types**: When a value type with a narrower refinement is passed to a destination expecting a wider refinement, the compiler allows the assignment without explicit casting. This is safe because value types are copied — the wider variable can mutate independently without violating the original narrow instance's invariant.

2. **Implicit narrowing coercion for reference types**: When a reference type with a wider refinement is passed to a destination expecting a narrower refinement, the compiler allows the assignment. This is safe because all mutable operations on the reference will be constrained by the stricter pattern — the reference cannot escape the narrower contract.

3. **Flow analysis for refinement claims**: The compiler tracks "claims" (known refinements) through control flow. Assignments, conditionals, and copy semantics propagate claims so that variables accumulate provable constraints without explicit pattern matching.

These capabilities are gated by ownership-aware reasoning: the compiler only performs flow analysis when it can locally reason about a variable's state (owned, borrowed, frozen, or unrestricted value types). Unrestricted reference types cannot be safely reasoned about and fall back to normal compile-time error rules.

**Key design principles:**
- Flow analysis is simplistic — no complex arithmetic or inter-procedural reasoning
- Claims are built from origin to the point of narrowing/widening
- New assignments create new claims (old claims are replaced)
- Copy semantics propagate claims at the point of copy
- The refinement comparison engine powers both implicit coercion decisions and claim checking

## Glossary

- **Compiler**: The Mist compiler pipeline, including lexing, parsing, semantic model construction, analysis, and C# code emission.
- **Type_Checker**: The analysis component (`TypeCheckerAnalyzer`) that validates type rules against the semantic model.
- **Refinement_Type**: A type alias whose definition includes a pattern-match predicate that constrains the valid values of the base type. Defined via `type AdultAge = int >= 18;`.
- **Refinement_Predicate**: The pattern-match expression attached to a type alias definition that specifies which values of the base type are valid for the refined type.
- **Base_Type**: The underlying type from which a Refinement_Type is derived (e.g., `int` is the base type of `type AdultAge = int >= 18;`).
- **Refinement_Claim**: A compiler-internal assertion about the known refinement constraints on a variable at a specific point in control flow. Claims are not user-visible syntax — they are inferred by the compiler.
- **Claim_Context**: The set of active Refinement_Claims for all variables in scope at a given program point. The Claim_Context is updated by assignments, conditionals, and scope boundaries.
- **Widening_Coercion**: An implicit conversion from a narrower refinement type to a wider refinement type (the source set is a subset of the destination set). Always safe for value types because there is only ever one reference to track (copy semantics). For reference types, only safe into a readonly context (Frozen or ReadonlyRef) because a writable wider destination could mutate the value outside the source's narrow contract.
- **Narrowing_Coercion**: An implicit conversion from a wider refinement type to a narrower refinement type (the destination set is a subset of the source set). Safe for any type when the destination is Locally_Reasonable, because all mutations through the destination are constrained by the stricter pattern.
- **Refinement_Comparison_Engine**: The compile-time component that determines subset/superset relationships between two refinement predicates. Powers both implicit coercion decisions and flow analysis claim checking.
- **Ownership_Tier**: The ownership annotation on a variable (Unrestricted, ReadonlyRef, Frozen, Borrowed, Owned) as defined in the ownership system.
- **Locally_Reasonable**: A variable whose state the compiler can reason about within the current function scope. Includes owned, borrowed, frozen variables and unrestricted value types (local value types are effectively owned). Excludes ReadonlyRef (another holder may mutate) and unrestricted reference types.
- **Value_Type**: A struct type in Mist. Passed by copy. Local value type variables are effectively owned regardless of ownership annotation.
- **Reference_Type**: A class type in Mist. Passed by reference. Ownership tier determines whether the compiler can reason about mutation.

## Requirements

### Requirement 1: Refinement Comparison Engine

**User Story:** As a Mist developer, I want the compiler to determine subset/superset relationships between refinement predicates at compile time, so that implicit coercions and flow analysis can be decided automatically.

#### Acceptance Criteria

1. THE Refinement_Comparison_Engine SHALL determine whether one Refinement_Predicate implies another Refinement_Predicate by returning a boolean result: true if every value satisfying the source predicate also satisfies the target predicate, false otherwise.
2. THE Refinement_Comparison_Engine SHALL only attempt implication proofs when both predicates share the same Base_Type. IF the Base_Types differ, THEN THE Refinement_Comparison_Engine SHALL return false.
3. THE Refinement_Comparison_Engine SHALL prove implication for simple relational predicates with integer literal constants by comparing bounds (e.g., `>= 21` implies `>= 18` because 21 >= 18; `<= 5` implies `<= 10` because 5 <= 10).
4. THE Refinement_Comparison_Engine SHALL prove implication for compound `and` predicates by verifying that for each constraint in the target predicate, at least one constraint in the source predicate implies it (e.g., `>= 10 and <= 20` implies `>= 0 and <= 100` because 10 >= 0 and 20 <= 100).
5. THE Refinement_Comparison_Engine SHALL prove implication across mixed relational operators using the following rules: `> a` implies `>= b` when a >= b; `< a` implies `<= b` when a <= b; `>= a` implies `> b` when a > b; `<= a` implies `< b` when a < b.
6. THE Refinement_Comparison_Engine SHALL handle constant equality patterns (`== literal`) by treating them as equivalent to `>= literal and <= literal` for implication purposes (e.g., `== 7` implies `>= 0` because 7 >= 0; `== 7` implies `<= 10` because 7 <= 10).
7. THE Refinement_Comparison_Engine SHALL handle inequality patterns (`!= literal`) as a distinct constraint. `!= a` does NOT imply any relational bound. However, `== a` implies `!= b` when a ≠ b. `!= a` implies `!= a` (identity).
8. THE Refinement_Comparison_Engine SHALL return false when the predicate contains patterns that are not direct relational comparisons or constant equality/inequality patterns against integer literal constants, including property patterns, `not` patterns (other than `!=`), `or` patterns, and regex patterns.
9. THE Refinement_Comparison_Engine SHALL NOT attempt arithmetic reasoning (e.g., the engine will not prove that `>= x + 1` implies `>= x`).
10. THE Refinement_Comparison_Engine SHALL NOT attempt cross-method reasoning (e.g., the engine will not track return value constraints across function boundaries).
11. IF the Refinement_Comparison_Engine returns false for an assignment from one Refinement_Type to another Refinement_Type that shares the same Base_Type, THEN THE Type_Checker SHALL emit a compile-time error indicating that the conversion requires an explicit `is` pattern-match expression.
12. THE Refinement_Comparison_Engine SHALL extract integer literal constant values from relational pattern operands supporting int, long, short, byte, sbyte, ushort, uint, and char types by converting them to a common 64-bit integer representation for comparison.

### Requirement 2: Implicit Widening Coercion

**User Story:** As a Mist developer, I want to assign a variable with a narrow refinement to a destination with a wider refinement without explicit casting, so that I avoid boilerplate pattern matching when the compiler can prove safety.

#### Acceptance Criteria

1. WHEN a value-typed variable with Refinement_Type `A` is assigned to a destination of Refinement_Type `B`, and the Refinement_Comparison_Engine proves that `A` is a subset of `B`, THE Type_Checker SHALL accept the assignment without requiring explicit pattern matching.
2. WHEN a value-typed argument with Refinement_Type `A` is passed to a parameter of Refinement_Type `B`, and the Refinement_Comparison_Engine proves that `A` is a subset of `B`, THE Type_Checker SHALL accept the call without requiring explicit pattern matching.
3. WHEN a reference-typed variable with Refinement_Type `A` is assigned to a destination of Refinement_Type `B`, and the Refinement_Comparison_Engine proves that `A` is a subset of `B`, THE Type_Checker SHALL accept the assignment ONLY IF the destination is in a readonly context (Frozen or ReadonlyRef ownership tier).
4. IF a reference-typed variable with Refinement_Type `A` is widened to a destination of Refinement_Type `B` and the destination is NOT in a readonly context (i.e., it is Owned, Borrowed, or Unrestricted), THEN THE Type_Checker SHALL emit a diagnostic error because the wider destination could write values that violate the source's narrow contract.
5. WHEN the Refinement_Comparison_Engine cannot prove the subset relationship for a widening assignment, THE Type_Checker SHALL emit a diagnostic error requiring explicit pattern matching.
6. WHEN a value-typed variable with Refinement_Type `A` is assigned to a destination of the Base_Type (no refinement), THE Type_Checker SHALL accept the assignment unconditionally (widening to base is always safe for value types). WHEN a reference-typed variable with Refinement_Type `A` is assigned to a destination of the Base_Type, THE Type_Checker SHALL accept the assignment only if the destination is in a readonly context (Frozen or ReadonlyRef).
7. WHEN a value-typed expression with Refinement_Type `A` is returned from a function whose return type is Refinement_Type `B`, and the Refinement_Comparison_Engine proves that `A` is a subset of `B`, THE Type_Checker SHALL accept the return without requiring explicit pattern matching.

### Requirement 3: Implicit Narrowing Coercion

**User Story:** As a Mist developer, I want to pass a variable with a wider refinement to a destination expecting a narrower refinement without explicit casting, so that I avoid boilerplate when the stricter contract is safe for all mutations.

#### Acceptance Criteria

1. WHEN a variable with Refinement_Type `A` is assigned to a destination of Refinement_Type `B`, and the Refinement_Comparison_Engine proves that `B` is a subset of `A`, THE Type_Checker SHALL accept the assignment without requiring explicit pattern matching, regardless of whether the source is a value type or reference type.
2. WHEN an argument with Refinement_Type `A` is passed to a parameter of Refinement_Type `B`, and the Refinement_Comparison_Engine proves that `B` is a subset of `A`, THE Type_Checker SHALL accept the call without requiring explicit pattern matching.
3. THE Type_Checker SHALL only perform implicit narrowing coercion when the destination variable is Locally_Reasonable (owned, borrowed, or frozen). IF the destination is an unrestricted reference type, THEN THE Type_Checker SHALL NOT perform implicit narrowing and SHALL emit a diagnostic error.
4. WHEN a parameter in the callee signature has an ownership annotation of owned, borrowed, or frozen, THE Type_Checker SHALL consider that parameter Locally_Reasonable for the purpose of implicit narrowing coercion at the call site.
5. WHEN the Refinement_Comparison_Engine cannot prove the superset relationship for a narrowing assignment, THE Type_Checker SHALL emit a diagnostic error requiring explicit pattern matching.
6. WHEN an implicit narrowing coercion is accepted, THE Type_Checker SHALL treat the destination variable as carrying Refinement_Type `B` for all subsequent type checking and flow analysis within the destination's scope.
7. WHEN a value-typed variable is implicitly narrowed, THE Type_Checker SHALL accept the narrowing because the destination receives a copy and all mutations on the copy are constrained by the narrower contract.

### Requirement 4: Flow Analysis for Refinement Claims

**User Story:** As a Mist developer, I want the compiler to track known refinement constraints through control flow, so that I can pass variables to refinement-typed parameters without explicit pattern matching when the compiler can infer the constraint.

#### Acceptance Criteria

1. WHEN a variable is assigned a compile-time constant value (e.g., `var i = 7;`), THE Type_Checker SHALL establish a Refinement_Claim on that variable reflecting the exact value (e.g., `int == 7`).
2. WHEN a variable is assigned from another variable that has an active Refinement_Claim, THE Type_Checker SHALL propagate the claim to the destination variable at the point of assignment (e.g., `var j = i;` where `i` has claim `int == 7` gives `j` the claim `int == 7`).
3. WHEN a variable is reassigned, THE Type_Checker SHALL replace the previous Refinement_Claim with a new claim derived from the assigned expression. The old claim is discarded.
4. WHEN a variable is assigned from a function call, THE Type_Checker SHALL derive the claim from the function's return type. IF the return type is a Refinement_Type, the claim is that refinement. IF the return type is the Base_Type with no refinement, no claim is established.
5. WHEN a variable with an active Refinement_Claim is passed to a parameter of a Refinement_Type, and the Refinement_Comparison_Engine proves the claim satisfies the parameter's refinement, THE Type_Checker SHALL accept the call without requiring explicit pattern matching.
6. WHEN a variable with an active Refinement_Claim is assigned to a destination of a Refinement_Type, and the Refinement_Comparison_Engine proves the claim satisfies the destination's refinement, THE Type_Checker SHALL accept the assignment without requiring explicit pattern matching.
7. IF a variable with an active Refinement_Claim is passed to a parameter of a Refinement_Type, and the Refinement_Comparison_Engine cannot prove the claim satisfies the parameter's refinement, THEN THE Type_Checker SHALL emit a diagnostic error.
8. THE Type_Checker SHALL only perform flow analysis on variables that are Locally_Reasonable.
9. IF the assigned expression is not a compile-time constant, a variable with an active claim, or a function call with a Refinement_Type return type, THEN THE Type_Checker SHALL NOT establish a claim on the variable (the expression is opaque).

### Requirement 5: Conditional Refinement Claims

**User Story:** As a Mist developer, I want the compiler to narrow a variable's refinement claim based on conditional checks, so that inside an if-block the variable can be used where a matching refinement is required.

#### Acceptance Criteria

1. WHEN an `if` condition is a simple comparison of the form `variable relational_op literal` or `literal relational_op variable`, where `relational_op` is one of `<`, `<=`, `>`, `>=`, `==`, or `!=`, THE Type_Checker SHALL establish a Refinement_Claim on that variable within the then-branch reflecting the condition (e.g., `if (i < 100)` establishes claim `int < 100`).
2. WHEN a variable already has an active Refinement_Claim and enters a conditional that further constrains it, THE Type_Checker SHALL combine the existing claim with the conditional constraint using logical `and` within the then-branch, and SHALL use the combined claim for refinement checking within that branch.
3. WHEN the else-branch is entered, THE Type_Checker SHALL establish the negation of the condition as a Refinement_Claim on the variable within the else-branch, using the standard negation mapping: `<` becomes `>=`, `<=` becomes `>`, `>` becomes `<=`, `>=` becomes `<`, `==` becomes `!=`, `!=` becomes `==`.
4. WHEN a variable is reassigned within a conditional branch, THE Type_Checker SHALL replace the conditional claim with the new assignment's claim for subsequent statements in that branch.
5. THE Type_Checker SHALL NOT propagate conditional claims beyond the scope of the conditional branch (claims are scoped to the branch they are established in).
6. IF an `if` condition is not a simple comparison of the supported form (e.g., it contains boolean operators `&&`/`||`, method calls, or compares two variables), THEN THE Type_Checker SHALL NOT establish a conditional Refinement_Claim and SHALL leave the existing Claim_Context unchanged within both branches.

### Requirement 6: Pattern Matching Refinement Claims

**User Story:** As a Mist developer, I want the compiler to narrow a variable's refinement claim based on pattern matching, so that inside a matched branch the variable carries the proven constraint.

#### Acceptance Criteria

1. WHEN an `if` condition uses a pattern match of the form `variable is pattern` (e.g., `if (i is >= 0 and <= 50)`), THE Type_Checker SHALL establish a Refinement_Claim on that variable within the then-branch reflecting the matched pattern.
2. WHEN a variable already has an active Refinement_Claim and enters a pattern match that further constrains it, THE Type_Checker SHALL combine the existing claim with the pattern constraint using logical `and` within the then-branch.
3. WHEN the else-branch of a pattern match is entered, THE Type_Checker SHALL establish the negation of the pattern as a Refinement_Claim on the variable within the else-branch, where negation follows the same rules as conditional negation (e.g., `is >= 0 and <= 50` negates to `is < 0 or > 50`).
4. IF the pattern is a compound `and` pattern (e.g., `is >= 0 and <= 50`), THE Type_Checker SHALL append each individual constraint from the pattern to the variable's Claim_Context within the then-branch.
5. IF the pattern contains structures the Refinement_Comparison_Engine cannot reason about (e.g., property patterns, type patterns), THEN THE Type_Checker SHALL NOT establish a Refinement_Claim from the pattern and SHALL leave the existing Claim_Context unchanged.
6. WHEN a pattern match is used in a `match`/`switch` expression, THE Type_Checker SHALL establish the matched pattern as a Refinement_Claim on the scrutinee variable within each arm's body.

### Requirement 7: Ownership-Aware Flow Analysis Gating

**User Story:** As a Mist developer, I want the compiler to only perform flow analysis when it can safely reason about a variable's state, so that unsound reasoning about shared mutable state is prevented.

#### Acceptance Criteria

1. WHEN a variable has Ownership_Tier `Owned`, THE Type_Checker SHALL perform flow analysis and maintain Refinement_Claims for that variable.
2. WHEN a variable has Ownership_Tier `Borrowed`, THE Type_Checker SHALL perform flow analysis and maintain Refinement_Claims for that variable.
3. WHEN a variable has Ownership_Tier `Frozen`, THE Type_Checker SHALL perform flow analysis and maintain Refinement_Claims for that variable.
4. WHEN a variable has Ownership_Tier `ReadonlyRef`, THE Type_Checker SHALL NOT perform flow analysis and SHALL NOT maintain Refinement_Claims for that variable because another holder of the mutable reference may mutate the value concurrently.
5. WHEN a variable is an unrestricted value type (local struct variable), THE Type_Checker SHALL perform flow analysis and maintain Refinement_Claims for that variable because local value types use copy semantics and are effectively owned.
6. WHEN a variable is an unrestricted reference type, THE Type_Checker SHALL NOT perform flow analysis and SHALL NOT maintain Refinement_Claims for that variable.
7. IF a developer attempts to use an unrestricted reference-typed variable or a ReadonlyRef variable where a Refinement_Type is expected without explicit pattern matching, THEN THE Type_Checker SHALL emit a diagnostic error indicating that flow analysis is not available and that explicit pattern matching is required to prove the refinement.
8. WHEN a variable that was Locally_Reasonable is passed as a `ref` argument (creating a mutable alias to the storage location), THE Type_Checker SHALL invalidate all Refinement_Claims on that variable for the duration of the ref's lifetime, because the alias may mutate the variable without the caller's knowledge.
9. IF a variable has its Refinement_Claims invalidated due to a `ref` lending and code subsequently uses that variable where a Refinement_Type is expected before the ref lifetime ends, THEN THE Type_Checker SHALL emit a diagnostic error indicating that the claim was invalidated by the outstanding ref.

### Requirement 8: Claim Propagation Through Copy Semantics

**User Story:** As a Mist developer, I want refinement claims to propagate when I copy a value-typed variable, so that the copy inherits the known constraints at the point of copy.

#### Acceptance Criteria

1. WHEN a value-typed variable with an active Refinement_Claim is copied to another variable via direct variable reference assignment (e.g., `var j = i;`), THE Type_Checker SHALL establish on the destination variable an identical Refinement_Claim to the source variable's claim at the point of copy.
2. WHEN a value-typed variable with an active Refinement_Claim is passed as an argument to a value-type parameter whose type is a Refinement_Type, THE Type_Checker SHALL invoke the Refinement_Comparison_Engine to determine whether the claim satisfies the parameter's Refinement_Predicate, and SHALL accept the call without explicit pattern matching if satisfaction is proven.
3. IF a value-typed variable with an active Refinement_Claim is passed as an argument to a value-type parameter whose Refinement_Type is not satisfied by the claim, THEN THE Type_Checker SHALL emit a diagnostic error indicating the claim does not satisfy the parameter's refinement.
4. WHEN the source variable's claim changes after the copy (e.g., `i` is reassigned), THE Type_Checker SHALL NOT retroactively modify the destination variable's claim. The destination retains the claim from the point of copy.
5. WHEN a value-typed variable whose declared type is a Refinement_Type is copied to another variable, THE Type_Checker SHALL propagate the more specific of the declared refinement or the active flow claim as the destination's claim.
6. WHEN a value-typed variable without an active Refinement_Claim is copied, THE Type_Checker SHALL NOT establish a claim on the destination (no claim to propagate).

### Requirement 9: Claim Invalidation Rules

**User Story:** As a Mist developer, I want clear rules about when refinement claims are invalidated, so that I understand when the compiler can no longer reason about a variable's constraints.

#### Acceptance Criteria

1. WHEN a variable is reassigned, THE Type_Checker SHALL invalidate the previous Refinement_Claim and establish a new claim based on the new assignment. IF the assigned expression has no derivable refinement (e.g., a call returning an unrefined Base_Type, or an opaque expression), THEN THE Type_Checker SHALL leave the variable with no active claim.
2. WHEN a variable is passed to a `ref` parameter (mutable borrow), THE Type_Checker SHALL invalidate the Refinement_Claim on that variable from the point of the call until the borrow scope ends.
3. WHEN a mutable borrow scope ends, THE Type_Checker SHALL NOT restore the previous claim. The variable has no claim until a new assignment or conditional establishes one.
4. WHEN control flow merges after a conditional (e.g., after an if/else), THE Type_Checker SHALL discard branch-specific claims. Only claims that are structurally identical (same predicate operator and operand) in all merging branches survive the merge. IF any branch has no claim on the variable (including branches where the variable was reassigned to an unrefined value), THEN THE Type_Checker SHALL discard the claim at the merge point.
5. WHEN a variable is captured by a closure (whether read-only or mutable capture), THE Type_Checker SHALL invalidate all Refinement_Claims on that variable at the point of capture.
6. WHEN a variable is assigned within a loop body, THE Type_Checker SHALL invalidate any Refinement_Claim established before the loop on that variable for the entire loop body and all program points after the loop. The compiler cannot determine which iteration is executing, so pre-loop claims are unreliable inside the loop. Claims established by assignments within the loop body apply only to subsequent statements within the same iteration.
7. IF a conditional has a then-branch but no else-branch (bare `if` without `else`), THEN THE Type_Checker SHALL discard the branch-specific claim at the merge point after the `if` because the implicit else-branch carries the pre-condition claim, and the then-branch may carry a different claim.

### Requirement 10: Error Diagnostics for Implicit Coercion

**User Story:** As a Mist developer, I want clear error messages when implicit coercion fails, so that I understand why the compiler cannot prove safety and what I need to do instead.

#### Acceptance Criteria

1. WHEN implicit widening coercion fails for a value type because the Refinement_Comparison_Engine cannot prove the subset relationship, THE Compiler SHALL emit a diagnostic error that identifies the source type, the target type, the source predicate, the target predicate, and suggests using pattern matching to validate at runtime.
2. WHEN implicit narrowing coercion fails for a reference type because the Refinement_Comparison_Engine cannot prove the superset relationship, THE Compiler SHALL emit a diagnostic error that identifies the source type, the target type, the target predicate, the source predicate, and suggests using pattern matching to validate at runtime.
3. WHEN flow analysis is unavailable for an unrestricted reference type and the variable is used where a Refinement_Type is expected, THE Compiler SHALL emit a diagnostic error that identifies the variable name and suggests using an ownership annotation (owned, borrowed, or frozen) or explicit pattern matching.
4. WHEN a variable with an active Refinement_Claim is passed to a parameter of a Refinement_Type or assigned to a destination of a Refinement_Type, and the Refinement_Comparison_Engine cannot prove the claim satisfies the target refinement, THE Compiler SHALL emit a diagnostic error that identifies the variable name, the current claim description, the target predicate, the target type, and suggests using a conditional check or pattern matching to establish the required constraint.
5. IF a variable whose Refinement_Claim was invalidated by a mutable borrow is subsequently used where a Refinement_Type is expected (passed to a Refinement_Type parameter or assigned to a Refinement_Type destination), THEN THE Compiler SHALL emit a diagnostic error that identifies the variable name, the location where the mutable borrow invalidated the claim, and suggests re-establishing the claim via assignment or conditional check.
6. THE Compiler SHALL include the source file path, line number, and column number of the offending expression in every diagnostic error emitted under criteria 1 through 5.

### Requirement 11: C# Emission for Implicit Coercions

**User Story:** As a Mist developer, I want implicit coercions to produce correct C# code without runtime overhead, so that the ergonomic improvement does not come at a performance cost.

#### Acceptance Criteria

1. WHEN an implicit widening coercion is accepted by the Type_Checker, THE Emitter SHALL emit a direct assignment in C# (e.g., `target = source;`) with no runtime guard, cast expression, or conditional check inserted by the Emitter for that assignment.
2. WHEN an implicit narrowing coercion is accepted by the Type_Checker, THE Emitter SHALL emit a direct assignment in C# (e.g., `target = source;`) with no runtime guard, cast expression, or conditional check inserted by the Emitter for that assignment.
3. WHEN a flow analysis claim allows a variable to be passed to a refinement-typed parameter, THE Emitter SHALL emit the method call with the variable passed directly as the argument and no runtime refinement predicate guard emitted for that parameter at the call site.
4. THE Emitter SHALL NOT emit any `if`-condition guards, `ArgumentException` throws, or other runtime validation code for assignments or calls that were accepted via implicit coercion or flow analysis claims.
5. THE Emitter SHALL continue to emit runtime refinement guards (predicate-checking `if` statements that throw `ArgumentException`) for method parameters whose arguments were not statically proven by the Type_Checker via implicit coercion or flow analysis claims.
6. WHEN the emitted C# code from an implicit coercion is compiled by the C# compiler, THE emitted assignment SHALL produce identical IL to a hand-written direct assignment between the same C# types (no additional method calls, boxing, or branching instructions).

### Requirement 12: Sample Application

**User Story:** As a Mist developer, I want a comprehensive sample application demonstrating implicit refinement coercion and flow analysis, so that I can see every supported usage pattern in one place and verify correctness.

#### Acceptance Criteria

1. A sample project SHALL exist at `samples/ImplicitRefinementCoercion/Program.mist` with an accompanying `samples/ImplicitRefinementCoercion/mist.toml` whose `name` field is `"ImplicitRefinementCoercion"`.
2. The sample SHALL demonstrate implicit widening coercion for value types (e.g., assigning a `TenTwenty` to a `ZeroHundred` where `TenTwenty` is `int >= 10 and <= 20` and `ZeroHundred` is `int >= 0 and <= 100`) and print a labeled line to stdout confirming the assignment succeeded.
3. The sample SHALL demonstrate implicit narrowing coercion for reference types (e.g., passing a wider-refined class reference to a narrower-refined parameter) and print a labeled line to stdout confirming the parameter received the value.
4. The sample SHALL demonstrate flow analysis establishing a claim from a literal assignment (e.g., `var i = 7;` then passing `i` to a function expecting `int >= 0 and <= 10`) and print a labeled line to stdout confirming the call succeeded.
5. The sample SHALL demonstrate flow analysis propagating a claim through copy (e.g., `var j = i;` inheriting `i`'s claim) and print a labeled line to stdout confirming `j` is accepted where the claim is required.
6. The sample SHALL demonstrate flow analysis establishing a claim from a conditional (e.g., `if (i < 100) { Func(i); }` where `Func` expects `int < 100`) and print a labeled line to stdout confirming the conditional branch accepted the value.
7. The sample SHALL demonstrate claim replacement on reassignment (e.g., `i = Random.NextInt(0, 50);` replacing the previous claim) and print a labeled line to stdout confirming the new claim is in effect.
8. The sample SHALL demonstrate claim invalidation when a variable is passed to a `ref` parameter by showing that after the ref call the variable no longer satisfies the prior claim, and print a labeled line to stdout confirming the invalidation is recognized.
9. The sample SHALL demonstrate ownership-gated flow analysis by showing that an unrestricted (non-owned) reference-type variable does not receive flow claims even after a literal or conditional that would otherwise establish one, and print a labeled line to stdout confirming the distinction.
10. The sample SHALL demonstrate the else-branch receiving the negated claim (e.g., `if (x < 100) { ... } else { /* x >= 100 here */ }`) and print a labeled line to stdout confirming the negated claim is usable in the else branch.
11. The sample SHALL demonstrate combined claims (existing claim + conditional constraint producing a tighter bound) and print a labeled line to stdout confirming the combined claim is accepted.
12. Each demonstrated pattern SHALL print its output prefixed with a unique bracket-enclosed tag (e.g., `[widening-value]`, `[narrowing-ref]`, `[flow-literal]`) so that each permutation is independently identifiable in stdout.
13. The sample SHALL include a `public static void RunDemo()` entry point that executes all demonstrations and produces no fewer than 11 distinct labeled output lines (one per pattern in criteria 2–11, plus at least one for combined claims).
14. `samples/AllFeatures/Program.mist` SHALL call `ImplicitRefinementCoercion.Program.RunDemo()` to include the feature in the full-suite sample run.
15. `samples/AllFeatures/mist.toml` SHALL contain a `[[references]]` entry with `path = "../ImplicitRefinementCoercion"`.
16. The sample SHALL compile and run to completion with exit code 0 using the Mist compiler, producing all expected labeled output lines without compiler errors or runtime exceptions.
