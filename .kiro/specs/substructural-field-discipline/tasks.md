# Implementation Plan: Substructural Field Discipline

## Overview

This plan implements the corrected substructural field discipline for the Mist compiler. It replaces the binary "is the container owned?" check with a discipline-compatibility matrix, adds substructural constraint syntax for generic type parameters, enforces constraint satisfaction at instantiation sites, removes the obsolete `MIST_TC_0110` diagnostic, and adds documentation and samples.

The implementation proceeds in layers: grammar → semantic model → analysis (DropAnalyzer matrix + TypeChecker instantiation checks) → MIST_TC_0110 removal → sample/book/tests.

## Tasks

- [x] 1. Grammar and semantic model infrastructure
  - [x] 1.1 Add `LinearConstraint`, `AffineConstraint`, `RelevantConstraint` alternatives to the `Constraint` rule in `Source/Mist/Grammar/Mist.storm`
    - Add three new alternatives to the existing `Constraint` rule: `| LINEAR as LinearConstraint`, `| AFFINE as AffineConstraint`, `| RELEVANT as RelevantConstraint`
    - The `LINEAR`, `AFFINE`, and `RELEVANT` tokens already exist in `keywords.storm`
    - Run the Storm source generator to produce the new node types: `LinearConstraintConstraintNode`, `AffineConstraintConstraintNode`, `RelevantConstraintConstraintNode`
    - _Requirements: 3.1_

  - [x] 1.2 Add `HasLinearConstraint`, `HasAffineConstraint`, `HasRelevantConstraint` boolean properties to `TypeParameterConstraints` in `Source/Mist/Semantics/Model/Types/TypeParameterConstraints.cs`
    - Add three new `bool` properties with `{ get; init; }` following the existing pattern (`HasReadonlyConstraint`, `HasMutableConstraint`, `HasOwnedConstraint`, etc.)
    - _Requirements: 3.2_

  - [x] 1.3 Update `ResolveConstraintClause` in `Source/Mist/Semantics/Model/SemanticModelBuilder.cs` to handle the new constraint node types
    - Add three new cases to the constraint resolution switch: `LinearConstraintConstraintNode` → sets `hasLinearConstraint = true`, `AffineConstraintConstraintNode` → sets `hasAffineConstraint = true`, `RelevantConstraintConstraintNode` → sets `hasRelevantConstraint = true`
    - Wire the local booleans into the `TypeParameterConstraints` initialization
    - Add validation in constraint combination logic: if more than one substructural constraint is present on the same type parameter, emit a diagnostic error
    - Ensure combining one substructural constraint with non-substructural constraints (class, struct, interface, new(), owned, readonly, mutable, default) is permitted
    - _Requirements: 3.1, 3.2, 3.3, 3.4_

- [x] 2. Checkpoint — Grammar and semantic model
  - Ensure the project builds cleanly with zero warnings. Ensure all existing tests pass. Ask the user if questions arise.

- [x] 3. DropAnalyzer discipline-compatibility matrix
  - [x] 3.1 Rewrite `ValidateFieldContainment` in `Source/Mist/Analysis/Builtin/DropAnalyzer.cs` to use the discipline-compatibility matrix
    - Replace the binary `IsOwnedType` check with the full matrix logic
    - Add `GetContainerDiscipline` helper: returns the container's discipline; for `owned` containers with no substructural modifier returns `None` (unrestricted rules)
    - Add `GetEffectiveFieldDiscipline` helper: for concrete types resolves through `ConstructedType`; for `TypeParameterType` reads constraint booleans to determine effective discipline
    - Add `IsFieldCompatible(SubstructuralDiscipline container, SubstructuralDiscipline field)` pure function implementing the matrix: linear holds anything; affine holds only affine + unrestricted; relevant holds only relevant + unrestricted; unrestricted/owned holds only unrestricted
    - Handle `TypeParameterType` fields: if the container is substructural and the type parameter has no substructural constraint, emit the "unconstrained type parameter" diagnostic
    - If the type parameter has a constraint, use the constraint's discipline as the field's effective discipline and apply the matrix
    - Each incompatible field emits its own independent diagnostic (no short-circuit)
    - _Requirements: 1.1, 1.2, 1.3, 1.4, 1.5, 1.6, 1.7, 4.1, 4.2, 4.3, 4.7_

  - [x] 3.2 Implement the three diagnostic message templates for field incompatibility
    - Affine container + linear/relevant field: "Affine type '{ContainerName}' cannot contain {field_modifier} field '{FieldName}'. The container may never be dropped, violating the field's requirement of at least one drop."
    - Relevant container + affine/linear field: "Relevant type '{ContainerName}' cannot contain {field_modifier} field '{FieldName}'. The container may be dropped multiple times, violating the field's at-most-once drop requirement."
    - Unrestricted/owned container + any substructural field: "Type '{ContainerName}' cannot contain {field_modifier} field '{FieldName}'. The container has no substructural discipline and cannot guarantee the field's drop requirements."
    - Retain the `MIST_DROP_FIELD_NOT_OWNED` diagnostic ID for all three message variants
    - _Requirements: 2.1, 2.2, 2.3, 2.4_

  - [x] 3.3 Implement the unconstrained type parameter diagnostic messages
    - Linear container: "Linear type '{ContainerName}' contains field '{FieldName}' of unconstrained type parameter '{T}'. Add a substructural constraint (e.g., 'where {T} : linear') to ensure the field's discipline is compatible."
    - Affine container: "Affine type '{ContainerName}' contains field '{FieldName}' of unconstrained type parameter '{T}'. Add a substructural constraint (e.g., 'where {T} : affine') to ensure the field's discipline is compatible."
    - Relevant container: "Relevant type '{ContainerName}' contains field '{FieldName}' of unconstrained type parameter '{T}'. Add a substructural constraint (e.g., 'where {T} : relevant') to ensure the field's discipline is compatible."
    - All use the `MIST_DROP_FIELD_NOT_OWNED` diagnostic ID
    - _Requirements: 4.4, 4.5, 4.6_

- [x] 4. Checkpoint — DropAnalyzer matrix
  - Ensure the project builds cleanly. Ensure existing tests that relied on the old "is the container owned?" logic are updated or pass with the new matrix behavior. Ask the user if questions arise.

- [x] 5. Instantiation-site constraint satisfaction in TypeCheckerAnalyzer
  - [x] 5.1 Add `ValidateSubstructuralConstraints` method to `Source/Mist/Analysis/Builtin/TypeCheckerAnalyzer.cs`
    - Implement the constraint satisfaction lattice: linear satisfies {linear, affine, relevant}; affine satisfies only {affine}; relevant satisfies only {relevant}; unrestricted satisfies nothing
    - Add `SatisfiesConstraint(SubstructuralDiscipline argument, SubstructuralDiscipline constraint)` helper
    - Add `GetArgumentDiscipline(IType arg)` helper that resolves the discipline for concrete types and type parameter types (using their own constraints for type-parameter-to-type-parameter forwarding)
    - Suppress the diagnostic when the type argument resolves to `ErrorType` to avoid cascading errors
    - _Requirements: 5.1, 5.2, 5.3, 5.5, 5.6, 6.1, 6.2, 6.3, 6.4, 6.5, 6.6, 6.7, 6.8_

  - [x] 5.2 Wire `ValidateSubstructuralConstraints` into all instantiation sites
    - Call the validation at: `new` expressions, base class specifications, field declarations with generic types, method call type arguments, and variable declarations with explicit type arguments
    - Use the existing instantiation-site visiting pattern already in `TypeCheckerAnalyzer`
    - _Requirements: 5.4_

- [x] 6. Remove MIST_TC_0110 diagnostic
  - [x] 6.1 Delete the `MIST_TC_0110` Task storage restriction checks from `Source/Mist/Analysis/Builtin/TypeCheckerAnalyzer.cs`
    - Remove the `TaskStorageRestriction` diagnostic ID declaration
    - Remove the field validation section around lines 3349-3360 that rejects `Task<T>` in fields
    - Remove the property validation section around lines 3420-3430 that rejects `Task<T>` in properties
    - Remove the `IsTaskLikeType` helper if it is exclusively used by these checks (check all call sites first)
    - Remove the section header comment "Task<T> storage restriction (MIST_TC_0110)" around line 9463
    - After removal, `Task<T>` (relevant) and `ValueTask<T>` (linear) are handled by the DropAnalyzer's compatibility matrix
    - _Requirements: 9.1, 9.2, 9.3, 9.4, 9.5, 9.6, 9.9_

  - [x] 6.2 Update `TaskStorageRestrictionDiagnosticTests.cs` and `TaskStorageRestrictionPropertyTests.cs` in `Test/Mist.Tests/Analysis/`
    - Update test expectations: Task in relevant/linear containers → zero diagnostics; Task in affine/unrestricted → discipline-compatibility diagnostic from Requirement 2 (not MIST_TC_0110); ValueTask in linear → zero diagnostics; ValueTask in affine/relevant/unrestricted → discipline-compatibility diagnostic
    - Verify that the specific `MIST_TC_0110` diagnostic ID no longer appears in any output
    - _Requirements: 9.7, 9.8_

- [x] 7. Checkpoint — Full compiler pipeline
  - Ensure the project builds cleanly with zero warnings. Ensure all existing test suites pass (including updated TaskStorageRestriction tests). Ask the user if questions arise.

- [x] 8. Property-based tests
  - [x] 8.1 Write property test for discipline-compatibility matrix (Property 1)
    - Create `Test/Mist.Tests/Analysis/DisciplineCompatibilityMatrixPropertyTests.cs`
    - Generate random (container discipline, field discipline) pairs using FsCheck
    - Build a single batched Mist source with all scenarios as separate type declarations
    - Compile once in-process via `CompilerDriver`
    - Assert each scenario produces zero `MIST_DROP_FIELD_NOT_OWNED` diagnostics iff the matrix permits, exactly one diagnostic otherwise with correct message content
    - Zero process spawning — all compilation in-process
    - **Property 1: Discipline-Compatibility Matrix**
    - **Validates: Requirements 1.1, 1.2, 1.3, 1.4, 1.5, 1.7, 2.1, 2.2, 2.3, 2.4**

  - [x] 8.2 Write property test for substructural constraint parsing (Property 2)
    - Create `Test/Mist.Tests/Analysis/SubstructuralConstraintParsingPropertyTests.cs`
    - Generate random valid type declarations with `where T : {discipline}` clauses
    - Compile in-process, inspect the resulting `TypeParameterType.Constraints` booleans
    - Assert the corresponding `Has{Discipline}Constraint` is `true` and the other two are `false`
    - **Property 2: Substructural Constraint Parsing**
    - **Validates: Requirements 3.1, 3.2**

  - [x] 8.3 Write property test for constraint combination validation (Property 3)
    - Create `Test/Mist.Tests/Analysis/ConstraintCombinationPropertyTests.cs`
    - Generate `where` clauses with multiple substructural constraints → expect diagnostic
    - Generate `where` clauses with one substructural + non-substructural constraints → expect no substructural-combination error
    - **Property 3: Constraint Combination Validation**
    - **Validates: Requirements 3.3, 3.4**

  - [x] 8.4 Write property test for generic field containment (Property 4)
    - Create `Test/Mist.Tests/Analysis/GenericFieldContainmentPropertyTests.cs`
    - Generate substructural containers with type parameter fields: unconstrained → expect diagnostic; constrained → apply matrix
    - **Property 4: Generic Field Containment**
    - **Validates: Requirements 4.1, 4.2, 4.3, 4.4, 4.5, 4.6, 4.7**

  - [x] 8.5 Write property test for constraint satisfaction lattice (Property 5)
    - Create `Test/Mist.Tests/Analysis/ConstraintSatisfactionLatticePropertyTests.cs`
    - Generate random (argument discipline, constraint discipline) pairs at instantiation sites
    - Assert acceptance iff the lattice permits (linear satisfies all; affine satisfies affine only; relevant satisfies relevant only; unrestricted satisfies nothing)
    - **Property 5: Constraint Satisfaction Lattice**
    - **Validates: Requirements 5.1, 5.2, 5.3, 5.5, 6.1, 6.2, 6.3, 6.4, 6.5, 6.6, 6.7, 6.8**

- [x] 9. Sample application
  - [x] 9.1 Create `samples/SubstructuralFieldDiscipline/mist.toml` project file and `samples/SubstructuralFieldDiscipline/Program.mist` with the proof code from Requirement 7
    - Copy the proof code verbatim from the requirements document (it is immutable per the Sample Proof Code Policy)
    - Create the `mist.toml` project file with appropriate references (Standard library)
    - _Requirements: 7.1, 7.2, 7.3, 7.5_

  - [x] 9.2 Update `samples/AllFeatures/Program.mist` to call `SubstructuralFieldDiscipline.Program.RunDemo()` and update `samples/AllFeatures/mist.toml` to include the reference
    - Add `SubstructuralFieldDiscipline = { path = "../SubstructuralFieldDiscipline" }` to `mist.toml`
    - Add the `RunDemo()` call in the appropriate location in `Program.mist`
    - _Requirements: 7.4_

- [x] 10. Book documentation
  - [x] 10.1 Create book section `book/src/advanced/substructural-field-discipline.md` covering the discipline-compatibility matrix, drop guarantee reasoning, generic substructural constraints, and constraint satisfaction subtyping
    - Include at least 4 compiled code snippets demonstrating key concepts
    - Create corresponding `.mist` snippet files in `book/snippets/`
    - Use `{{#include}}` references in the tabbed format per the Book Snippet Policy
    - _Requirements: 8.1, 8.2, 8.3_

  - [x] 10.2 Update `book/src/SUMMARY.md` to add the new section after the "Drop Semantics" entry in the Advanced Features part
    - _Requirements: 8.4_

- [x] 11. Final checkpoint
  - Compile the sample application (`samples/SubstructuralFieldDiscipline`) with zero diagnostics and zero warnings. Run it and verify the output matches the expected output from Requirement 7 exactly. Ensure `samples/AllFeatures` still compiles and runs cleanly. Ensure all tests pass. Ask the user if questions arise.

## Notes

- Tasks marked with `*` are optional and can be skipped for faster MVP
- Each task references specific requirements for traceability
- Checkpoints ensure incremental validation
- Property tests validate universal correctness properties using in-process `CompilerDriver` compilation (zero process spawning per the Test Process Spawning Policy)
- The proof code in the sample app is immutable — the compiler must be made to compile it, not vice versa
- The design uses C# throughout (the Mist compiler is a C# project)

## Task Dependency Graph

```json
{
  "waves": [
    { "id": 0, "tasks": ["1.1", "1.2"] },
    { "id": 1, "tasks": ["1.3"] },
    { "id": 2, "tasks": ["3.1"] },
    { "id": 3, "tasks": ["3.2", "3.3"] },
    { "id": 4, "tasks": ["5.1", "6.1"] },
    { "id": 5, "tasks": ["5.2", "6.2"] },
    { "id": 6, "tasks": ["8.1", "8.2", "8.3", "8.4", "8.5"] },
    { "id": 7, "tasks": ["9.1", "10.1"] },
    { "id": 8, "tasks": ["9.2", "10.2"] }
  ]
}
```
