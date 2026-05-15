# Implementation Plan: Array Bounds Flow Analysis

## Overview

This plan implements static array bounds proving for the Mist compiler. The work extends `ArrayType` with length tracking, adds symbolic constraint support to the narrowing infrastructure, implements for-loop induction variable narrowing, and wires arrays into the unified indexer refinement validation path.

## Tasks

- [x] 1. Extend ArrayType with Length tracking
  - [x] 1.1 Add KnownLength and SymbolicLengthId properties to ArrayType
    - Add `KnownLength` (long?) and `SymbolicLengthId` (int?) properties to `ArrayType` in `Source/Mist/Semantics/Model/Types/ArrayType.cs`
    - Add constructors for concrete length (`ArrayType(IType, long)`) and symbolic length (`ArrayType(IType, int symbolicId)`) while preserving the existing single-argument constructor
    - Update `DisplayName` to show `T[N]` when KnownLength is set
    - _Requirements: 1.1, 1.2, 1.3_

- [x] 2. Produce concrete-length ArrayType from SemanticModelBuilder
  - [x] 2.1 Extract literal sizes in array creation expressions
    - In the `ArrayCreationExpressionPrimaryNode` handler, extract the literal integer from `arrayCreation.Size` and pass to the concrete-length constructor
    - In the `ArrayLiteralExpressionPrimaryNode` handler, count elements and pass to the concrete-length constructor
    - In the empty array literal handler, use length 0
    - Non-literal sizes continue using the unknown-length constructor
    - _Requirements: 1.2, 1.3, 1.8_

- [x] 3. Extend ConditionConstraint with symbolic bounds
  - [x] 3.1 Add SymbolicId field and symbolic tautology check
    - Add `int? SymbolicId = null` to the `ConditionConstraint` record struct
    - Extend `ConditionConstraintImplies` with a symbolic tautology check: if both have the same non-null SymbolicId and same Op, return true; if only one is symbolic, return false; otherwise fall through to existing concrete logic
    - _Requirements: 4.1, 4.2, 4.3, 4.4, 4.5_

- [x] 4. Implement for-loop induction variable narrowing
  - [x] 4.1 Add TryInjectForLoopNarrowing method
    - Add `TryInjectForLoopNarrowing` method that extracts the induction variable name and initial literal value from the initializer, extracts the upper bound from the condition (literal, `.Length` on ArrayType with known/symbolic length), checks for reassignment in the body, and returns constraints
    - Call it from `CheckForStatement` to inject constraints before the body check and remove them after
    - _Requirements: 3.1, 3.2, 3.3, 3.4, 3.5, 3.6, 3.7, 3.8, 3.9_

- [x] 5. Implement array indexer refinement validation
  - [x] 5.1 Remove ArrayType guard and add ValidateArrayIndexRefinement
    - Remove the `targetType is not ArrayType` guard in `CheckIndexSuffix`
    - Add `ValidateArrayIndexRefinement` that constructs the ArrayBounds predicate (>= 0 and < KnownLength or < symbolic) and validates the index using the 3-case logic: literal evaluation, narrowing context implication, refinement type implication, or reject with diagnostic
    - _Requirements: 2.1, 2.2, 2.3, 2.4, 5.1, 5.2, 5.3, 5.4_

- [x] 6. Allocate symbolic length IDs for parameter arrays
  - [x] 6.1 Add symbolic length ID counter and parameter array handling
    - Add a `_nextSymbolicLengthId` counter
    - When an unsized array parameter is encountered, allocate a unique symbolic ID
    - Ensure `.Length` access on parameter arrays within for-loop conditions retrieves the SymbolicLengthId to produce symbolic upper-bound constraints
    - _Requirements: 1.4, 1.5, 1.6, 1.7_

- [x] 7. Update existing samples for backward compatibility
  - [x] 7.1 Update samples to use var and add bounds annotations
    - Change `int[] x = new int[N]` to `var x = new int[N]` in Primitives, HelloMist, PatternMatching, and SwitchExpressions samples
    - Update RefParameters, AdhocRefinementConstraints, and Properties/IndexerDemo with appropriate bounds annotations
    - Verify all modified samples compile and produce identical output
    - _Requirements: 6.1, 6.2, 6.3, 6.4, 6.5, 6.6, 6.7, 6.8, 6.9_

- [x] 8. Create ArrayBoundsFlowAnalysis sample application
  - [x] 8.1 Create sample project and integrate with AllFeatures
    - Create `samples/ArrayBoundsFlowAnalysis/Program.mist` and `mist.toml`
    - Demonstrate literal indices on concrete arrays, for-loop with literal bounds, for-loop with `.Length` on concrete arrays, for-loop with `.Length` on unknown-length parameter arrays (symbolic tautology), and nested indexing
    - Add to AllFeatures orchestrator
    - Verify compilation and output
    - _Requirements: 7.1, 7.2, 7.3, 7.4, 7.5, 7.6_

## Notes

- Tasks 1–3 are foundational data model changes that must land first.
- Task 4 (for-loop narrowing) depends on both the ArrayType extension (Task 1) for extracting `.Length` bounds and the symbolic ConditionConstraint (Task 3) for representing them.
- Task 5 (array indexer validation) depends on Tasks 1–4 being complete so that arrays have length info and the narrowing context can prove bounds.
- Task 6 (symbolic ID allocation) can be done in parallel with Task 2 but must be done before Task 4.
- Tasks 7 and 8 (samples) depend on the full compiler pipeline being functional (Tasks 4 and 5).
- The zero-warnings policy applies to every task: verify `dotnet build` produces no warnings after each change.

## Task Dependency Graph

```json
{
  "waves": [
    { "id": 0, "tasks": ["1.1"] },
    { "id": 1, "tasks": ["2.1", "3.1", "6.1"] },
    { "id": 2, "tasks": ["4.1"] },
    { "id": 3, "tasks": ["5.1"] },
    { "id": 4, "tasks": ["7.1", "8.1"] }
  ]
}
```
