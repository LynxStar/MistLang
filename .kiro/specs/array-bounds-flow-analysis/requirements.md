# Requirements Document

## Introduction

The Mist compiler must statically prove that array index expressions satisfy the `ArrayBounds` refinement constraint (`>= 0 and < Length`) defined on `Standard.Array<TValue, Length>`. This feature introduces **inferred refinement types** for for-loop induction variables and extends `ArrayType` to carry the dependent `Length` parameter. The compiler infers a refinement type for the loop variable based on the loop structure, then uses standard refinement-to-refinement implication checking when the variable is used as an array index. For arrays with unknown Length (e.g., `int[]` parameters), the solver proves implication by recognizing that the same unknown Length value appears on both sides of the constraint.

## Glossary

- **Type_Checker**: The type checking analysis pass (`TypeCheckerAnalyzer`) that validates type correctness, refinement constraints, and flow narrowing.
- **Refinement_Solver**: The component (`RefinementSolver` / `RefinementPredicate`) that proves logical implication between refinement predicates and condition constraints.
- **Narrowing_Context**: The dictionary (`_narrowingContext`) mapping variable names to lists of `ConditionConstraint` values representing proven relational bounds within a scope.
- **ArrayBounds**: The refinement type alias `ZeroOrPositiveInt and < Length` defined on `Standard.Array`, requiring an index to be `>= 0` and `< Length`.
- **Induction_Variable**: A loop counter variable declared in a for-loop initializer whose refinement type can be inferred from the loop initializer, condition, and body analysis.
- **Inferred_Refinement_Type**: A refinement type assigned to a variable based on static analysis of its usage context (e.g., loop bounds), rather than an explicit type annotation.
- **Condition_Constraint**: A relational fact (operator + value, or operator + symbolic reference) proven about a variable within a given scope, stored in the Narrowing_Context.
- **Array_Length_Parameter**: The dependent `Length` type parameter on `Standard.Array<TValue, Length>` that represents the array's size at the type level. Concrete when the size is known (from literals or `new T[N]`), unknown when the size is unspecified (from `T[]` parameter declarations).
- **Symbolic_Bound**: An upper-bound constraint that references an unknown Length parameter rather than a concrete integer. Two symbolic bounds are equal when they reference the same unknown Length identity.

## Requirements

### Requirement 1: ArrayType Dependent Length Parameter

**User Story:** As a Mist compiler developer, I want `ArrayType` to carry the dependent `Length` type parameter, so that the refinement system can resolve `ArrayBounds` constraints against concrete or symbolic array sizes.

#### Acceptance Criteria

1. THE Type_Checker SHALL extend `ArrayType` to carry a Length value that is concrete when the array size is statically known and symbolic when unknown, where each distinct unknown-Length source (e.g., each unsized parameter declaration) produces a unique symbolic identity.
2. WHEN an array is created with `new T[N]` where `N` is an integer literal, THE Type_Checker SHALL record the Length as `N` in the resulting `ArrayType`.
3. WHEN an array is created with array literal syntax (e.g., `[1, 2, 3]`), THE Type_Checker SHALL record the Length as the number of elements in the resulting `ArrayType`.
4. WHEN an array type appears in a parameter declaration without a size (e.g., `int[] items`), THE Type_Checker SHALL represent the Length as a unique symbolic identity in the `ArrayType`, such that two references to the same parameter's Length resolve to the same identity.
5. WHEN an array type appears in a parameter declaration with a dependent size (e.g., `int[7] items`), THE Type_Checker SHALL represent the Length as the specified concrete value in the `ArrayType`.
6. WHEN `.Length` is accessed on an array with a concrete Length, THE Type_Checker SHALL resolve it to the concrete integer value.
7. WHEN `.Length` is accessed on an array with a symbolic Length, THE Type_Checker SHALL resolve it to the symbolic Length identity of that array, preserving the reference for use in refinement implication checks.
8. WHEN an array is created with `new T[N]` where `N` is not an integer literal, THE Type_Checker SHALL represent the Length as unknown in the resulting `ArrayType`.

### Requirement 2: Unified Indexer Refinement Validation

**User Story:** As a Mist developer, I want array indexing to use the same refinement validation as custom indexers, so that there are no special cases or bypasses for array types.

#### Acceptance Criteria

1. THE Type_Checker SHALL NOT have any special-case bypass that skips refinement validation for `ArrayType` targets; the indexer parameter refinement validation path SHALL be invoked for `ArrayType` targets under the same conditions as for `ClassType`, `StructType`, and `ConstructedType` targets.
2. WHEN an index expression is applied to any type with an indexer (including arrays), THE Type_Checker SHALL validate the index argument against the indexer parameter's refinement constraint by applying, in order: (a) literal evaluation if the argument is a compile-time constant, (b) refinement implication if the argument has a `RefinementType`, (c) narrowing context lookup if the argument has proven `ConditionConstraint` bounds in scope.
3. IF the index argument fails all three validation checks (literal evaluation, refinement implication, and narrowing context proof), THEN THE Type_Checker SHALL emit a diagnostic indicating that the argument cannot be proven to satisfy the indexer's refinement constraint.
4. THE Type_Checker SHALL emit the same diagnostic codes for refinement validation failures on `ArrayType` indexers as it does for custom indexers on user-defined types.

### Requirement 3: For-Loop Induction Variable Refinement Inference

**User Story:** As a Mist developer, I want the compiler to infer a refinement type for for-loop induction variables based on the loop structure, so that using the loop variable as an array index works through standard refinement checking.

#### Acceptance Criteria

1. WHEN a for-loop has the pattern `for (int i = <init>; i < <bound>; ...)` where `<init>` is a literal, THE Type_Checker SHALL infer a lower-bound constraint `>= <init>` for the Induction_Variable.
2. WHEN a for-loop condition uses the `<` operator (e.g., `i < <bound>`) and the upper bound resolves to a concrete integer (literal value or a refinement type with a known bound), THE Type_Checker SHALL infer an upper-bound constraint `< <bound>` for the Induction_Variable.
3. WHEN a for-loop condition uses the `<=` operator (e.g., `i <= <bound>`) and the upper bound resolves to a concrete integer, THE Type_Checker SHALL infer an upper-bound constraint `<= <bound>` for the Induction_Variable.
4. WHEN the for-loop condition upper bound resolves to an unknown Length parameter (e.g., `items.Length` where `items` is `Array<T, unknown>`), THE Type_Checker SHALL infer a Symbolic_Bound constraint referencing that specific unknown Length identity, preserving the comparison operator (`<` or `<=`) from the condition.
5. WHEN the for-loop condition upper bound is a variable with a refinement type (e.g., `type Under100 = int < 100`), THE Type_Checker SHALL infer the upper-bound constraint from the refinement type's bounds.
6. IF the for-loop condition does not match a recognized pattern (`i < <bound>` or `i <= <bound>` where `<bound>` resolves to a concrete integer, a symbolic Length, or a refinement-typed variable), THEN THE Type_Checker SHALL skip refinement inference for that loop and not emit a diagnostic.
7. THE Type_Checker SHALL treat the Induction_Variable as reassigned if the loop body contains any direct assignment (`i = ...`), compound assignment (`i += ...`, `i -= ...`), or prefix/postfix increment/decrement (`i++`, `i--`, `++i`, `--i`) targeting that variable.
8. IF the Induction_Variable is reassigned within the loop body (as defined in criterion 7), THEN THE Type_Checker SHALL NOT apply the inferred refinement type to that variable.
9. WHEN the loop body scope ends, THE Type_Checker SHALL remove the Induction_Variable's inferred refinement from the Narrowing_Context.

### Requirement 4: Refinement Implication with Dependent Type Variables

**User Story:** As a Mist compiler developer, I want the refinement solver to recognize tautologies between constraints that reference the same dependent type parameter instance, so that refinement types sharing the same generic parameter can imply each other.

#### Acceptance Criteria

1. WHEN a refinement predicate constraint references a dependent type variable (e.g., `< Length` where `Length` is a generic dependent type parameter), THE Refinement_Solver SHALL represent that constraint as a symbolic constraint that preserves a reference to the originating DependentTypeParameterType instance, distinct from concrete integer constraints.
2. WHEN checking implication between two symbolic constraints that reference the same DependentTypeParameterType instance and use the same relational operator (e.g., `< X` implies `< X`, or `>= Y` implies `>= Y`), THE Refinement_Solver SHALL accept the implication as a tautology.
3. WHEN checking implication between two symbolic constraints that reference the same DependentTypeParameterType instance but use different relational operators (e.g., `< X` does not imply `<= X` by identity alone), THE Refinement_Solver SHALL NOT accept the implication as a tautology.
4. WHEN checking implication between two symbolic constraints that reference different DependentTypeParameterType instances (even if they share the same parameter name), THE Refinement_Solver SHALL NOT accept the implication by identity alone.
5. THE Refinement_Solver SHALL apply this tautology rule uniformly to all refinement types regardless of their origin, not only to array-related or inferred refinement types.

### Requirement 5: Nested Array Indexing

**User Story:** As a Mist developer, I want to use nested array indexing (e.g., `d[i][j]`) where each index is independently proven safe through its own inferred refinement, so that multi-dimensional array patterns compile correctly.

#### Acceptance Criteria

1. WHEN a nested index expression `arr[i][j]` is encountered, THE Type_Checker SHALL first resolve the type of `arr[i]` to obtain the inner array type, then validate `i` against the outer array's `ArrayBounds` and `j` against the inner array's `ArrayBounds` as separate, independent validation steps.
2. WHEN the inner array has a concrete Length (resolved from the outer array's element type), THE Type_Checker SHALL use that concrete Length for validating the inner index.
3. WHEN the inner array has an unknown Length (resolved from the outer array's element type), THE Type_Checker SHALL use the symbolic Length identity of the inner array for validating the inner index via the same symbolic tautology rule defined in Requirement 4.
4. WHEN an index expression is nested beyond two levels (e.g., `arr[i][j][k]`), THE Type_Checker SHALL apply the same per-level type resolution and independent bounds validation recursively for each successive index.

### Requirement 6: Backward Compatibility

**User Story:** As a Mist developer, I want all existing sample applications to continue compiling after this change, with only targeted modifications to adopt dependent type annotations where array access was previously unproven.

#### Acceptance Criteria

1. THE Type_Checker SHALL compile the `ArrayLiterals` sample (for-loop variables, nested indexing, array literals) without modification and without emitting any diagnostic with severity Error whose ID relates to array bounds validation (e.g., refinement constraint failures on index arguments).
2. WHEN the `Primitives` sample is modified to change `int[] nums = new int[6]` and `int[] haystack = new int[5]` to use `var` so the compiler infers the concrete Length from the initializer, THE Type_Checker SHALL compile the modified sample without errors and the sample SHALL produce output identical to its stored baseline.
3. WHEN the `HelloMist` sample is modified to change `int[] numbers = new int[5]` to use `var` so the compiler infers the concrete Length from the initializer, THE Type_Checker SHALL compile the modified sample without errors and the sample SHALL produce output identical to its stored baseline.
4. WHEN the `Properties` sample is modified to make `IndexerDemo` a generic class with a dependent type parameter for size, using `int[Size].ArrayBounds` as the indexer parameter type, THE Type_Checker SHALL compile the modified sample without errors and the sample SHALL produce output identical to its stored baseline.
5. WHEN the `RefParameters` sample is modified to use a sized array field (`int[3] Items`) and `int[3].ArrayBounds` as the `GetItemRef` parameter type, THE Type_Checker SHALL compile the modified sample without errors and the sample SHALL produce output identical to its stored baseline.
6. WHEN the `AdhocRefinementConstraints` sample is modified to change `SafeList._items` to `int[5]` and update the indexer constraint to `int[5].ArrayBounds` (or equivalent `where index >= 0 and < 5`), THE Type_Checker SHALL compile the modified sample without errors and the sample SHALL produce output identical to its stored baseline.
7. WHEN the `PatternMatching` sample is modified to change `int[]` declarations with `new int[N]` initializers to use `var` so the compiler infers the concrete Length, THE Type_Checker SHALL compile the modified sample without errors and the sample SHALL produce output identical to its stored baseline.
8. WHEN the `SwitchExpressions` sample is modified to change `int[]` declarations with `new int[N]` initializers to use `var` so the compiler infers the concrete Length, THE Type_Checker SHALL compile the modified sample without errors and the sample SHALL produce output identical to its stored baseline.
9. WHEN all sample modifications described in criteria 1–8 are applied, THE `AllFeatures` integration test SHALL pass, confirming that the full-suite sample run compiles, executes, and matches its stored baseline.

### Requirement 7: Sample Application

**User Story:** As a Mist developer, I want a comprehensive sample application demonstrating array bounds flow analysis, so that I can see every supported usage pattern in one place and verify correctness.

#### Acceptance Criteria

1. A sample project SHALL exist at `samples/ArrayBoundsFlowAnalysis/Program.mist` with a corresponding `mist.toml` declaring the project name `ArrayBoundsFlowAnalysis`.
2. The sample SHALL demonstrate each of the following bounds-proving patterns: integer literal indices on concrete-Length arrays, for-loop induction variables with literal upper bounds, for-loop induction variables with `.Length` bounds on concrete-Length arrays, for-loop induction variables with `.Length` bounds on unknown-Length parameter arrays (symbolic tautology), nested array indexing with inferred bounds (e.g., `arr[i][j]`), indexer parameters using `ArrayBounds` type references (e.g., `int[N].ArrayBounds`), and dependent-typed array parameters (e.g., `int[5] items`).
3. Each demonstrated pattern SHALL print a section header line to stdout (e.g., `--- Pattern Name ---`) followed by the computed output values, so that correctness is observable by inspecting stdout.
4. The sample SHALL expose a `public static void RunDemo()` method that exercises all demonstrated patterns when called.
5. `samples/AllFeatures/Program.mist` SHALL call `ArrayBoundsFlowAnalysis.Program.RunDemo()` and `samples/AllFeatures/mist.toml` SHALL include a `[[references]]` entry with `path = "../ArrayBoundsFlowAnalysis"`.
6. The sample SHALL compile and run without errors or warnings using the Mist compiler.
