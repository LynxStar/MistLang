# Implementation Plan: Anonymous Types

## Overview

This plan implements anonymous types for the Mist language, following C#'s semantics. The implementation progresses through the compiler pipeline: grammar → parser/AST → pretty printer → semantic model → type checker → emitter → tooling/docs. Each stage builds on the previous, with property-based tests validating correctness at each layer.

## Tasks

- [x] 1. Grammar and AST foundation
  - [x] 1.1 Add anonymous type grammar rules to `Mist.storm`
    - Add `AnonymousTypeExpression` alternative in the `Primary` rule BEFORE `TargetTypedNewExpressionNoArgs`
    - Define `AnonymousPropertyList` rule (one or more `AnonymousProperty` separated by commas, optional trailing comma)
    - Define `AnonymousProperty` rule with `ExplicitAnonymousProperty` (`IDENTIFIER EQUAL Expression`) and `ProjectedAnonymousProperty` (`Postfix`) alternatives
    - Rebuild to generate AST node classes (`AnonymousTypeExpressionPrimaryNode`, `ExplicitAnonymousPropertyAnonymousPropertyNode`, `ProjectedAnonymousPropertyAnonymousPropertyNode`)
    - _Requirements: 6.1, 6.2, 6.3_

  - [x] 1.2 Implement pretty printer support for anonymous type AST nodes
    - Add case in `MistPrettyPrinter.cs` for `AnonymousTypeExpressionPrimaryNode`
    - Format explicit properties as `new { Name = expr }`
    - Format projected properties as `new { variable }` or `new { expr.Member }`
    - _Requirements: 1.6_

  - [x] 1.3 Write property test for anonymous type expression round-trip
    - **Property 1: Anonymous type expression round-trip**
    - Generate valid anonymous type sources (explicit assignments, projections, mixed), parse → pretty-print → re-parse, assert structural AST equivalence
    - **Validates: Requirements 1.1, 1.2, 1.3, 1.4, 1.6, 1.7**

  - [x] 1.4 Write property tests for grammar disambiguation
    - **Property 7: Disambiguation — anonymous content produces AnonymousTypeExpression**
    - **Property 8: Non-anonymous content falls through to target-typed new**
    - Generate sources matching anonymous property syntax, assert `AnonymousTypeExpressionPrimaryNode`; generate non-anonymous `new { ... }` content, assert `TargetTypedNewExpressionNoArgsPrimaryNode`
    - **Validates: Requirements 6.4, 6.5**

- [x] 2. Checkpoint - Ensure grammar and parsing tests pass
  - Ensure all tests pass, ask the user if questions arise.

- [x] 3. Semantic model — `AnonymousType` implementation
  - [x] 3.1 Create `AnonymousType` class in the type system
    - Create `Source/Mist/Semantics/Model/Types/AnonymousType.cs`
    - Implement `IType` interface with `PropertyNames`, `PropertyTypes`, `DisplayName`, `Kind => TypeKind.Class`
    - Implement `StructurallyEquals` method comparing names, types, and order
    - _Requirements: 2.1, 2.2, 2.3, 2.4, 2.6_

  - [x] 3.2 Write property test for AnonymousType construction invariants
    - **Property 2: AnonymousType construction preserves data and DisplayName**
    - Generate N property names and types, construct `AnonymousType`, verify order preservation and `DisplayName` format `{ Name1: T1, Name2: T2 }`
    - **Validates: Requirements 2.1, 2.2**

  - [x] 3.3 Write property test for structural equality
    - **Property 3: Structural equality is determined by names, types, and order**
    - Generate pairs of `AnonymousType` instances (same/different names, types, order), verify `StructurallyEquals` returns correct result
    - **Validates: Requirements 2.3, 2.4**

  - [x] 3.4 Integrate `AnonymousType` into the semantic model builder
    - Add handling in `SemanticModelBuilder.cs` for `AnonymousTypeExpressionPrimaryNode`
    - Resolve expression types for explicit properties, extract projected names from `Postfix` nodes
    - Validate no duplicate property names (emit `MIST_ANON_DUPLICATE_PROP`)
    - Validate projections resolve to identifiers or member accesses (emit `MIST_ANON_NO_PROJECTION_NAME`)
    - Construct `AnonymousType` and register in the type map
    - _Requirements: 2.5, 10.1, 10.2, 10.5_

  - [x] 3.5 Write property test for duplicate property detection
    - **Property 12: Duplicate property names are rejected**
    - Generate anonymous type expressions with duplicate property names, assert diagnostic `MIST_ANON_DUPLICATE_PROP` is reported
    - **Validates: Requirements 10.1**

- [x] 4. Type checker — property access and read-only enforcement
  - [x] 4.1 Implement property access resolution on `AnonymousType`
    - In `TypeCheckerAnalyzer.cs`, when resolving `.PropertyName` on an `AnonymousType`, look up in `PropertyNames` and return corresponding type from `PropertyTypes`
    - Report "undefined member" if property name not found
    - _Requirements: 3.1, 3.2_

  - [x] 4.2 Implement read-only enforcement for anonymous type properties
    - Detect assignments targeting `AnonymousType` properties and emit `MIST_ANON_READONLY`
    - _Requirements: 3.3, 10.4_

  - [x] 4.3 Write property tests for property access and read-only semantics
    - **Property 4: Property access resolution**
    - **Property 5: Anonymous type properties are read-only**
    - Generate types with known properties, assert correct type resolution and read-only rejection
    - **Validates: Requirements 3.1, 3.2, 3.3, 10.4**

- [x] 5. Type checker — assignability, scope restrictions, and equality
  - [x] 5.1 Implement assignability rules for anonymous types
    - Allow assignment between variables with structurally equal `AnonymousType`
    - Allow assignment of `AnonymousType` to `unknown`
    - Infer `AnonymousType` for `var` declarations
    - _Requirements: 4.1, 4.2, 4.6_

  - [x] 5.2 Implement scope restriction diagnostics
    - Report `MIST_ANON_ESCAPE_SIGNATURE` when `AnonymousType` appears as method return/parameter type annotation
    - Report `MIST_ANON_ESCAPE_FIELD` when `AnonymousType` appears as field type annotation
    - Report `MIST_ANON_ESCAPE_GENERIC` when `AnonymousType` appears as generic type argument
    - _Requirements: 4.3, 4.4, 4.5, 4.7, 10.3_

  - [x] 5.3 Implement equality operator validation
    - Allow `==`/`!=` between values of the same `AnonymousType`
    - Report `MIST_ANON_EQUALITY_MISMATCH` for different anonymous types
    - _Requirements: 9.1, 9.3_

  - [x] 5.4 Write property test for same-shape assignability
    - **Property 6: Same-shape anonymous types are assignable**
    - Generate same-shape anonymous type pairs, verify assignment compiles without diagnostics
    - **Validates: Requirements 4.6**

  - [x] 5.5 Write property test for equality operators
    - **Property 11: Equality operators require same-shape anonymous types**
    - Generate same/different shape pairs, verify `==`/`!=` acceptance or rejection
    - **Validates: Requirements 9.1, 9.3**

- [x] 6. Type checker — pattern matching support
  - [x] 6.1 Implement property pattern validation against `AnonymousType`
    - Validate property patterns against `AnonymousType` property names/types
    - Report error for patterns referencing non-existent property names
    - `var` pattern binds with the full `AnonymousType`
    - Allow any property names when scrutinee is `unknown` (defer to runtime)
    - _Requirements: 5.1, 5.2, 5.3, 8.3_

  - [x] 6.2 Write property tests for pattern matching validation
    - **Property 10: Property patterns validate against AnonymousType**
    - **Property 13: Property patterns on unknown accept any property names**
    - Generate property patterns against known/unknown types, verify correct acceptance/rejection
    - **Validates: Requirements 5.1, 5.3, 8.3**

- [x] 7. Checkpoint - Ensure all semantic and type checker tests pass
  - Ensure all tests pass, ask the user if questions arise.

- [x] 8. C# emitter implementation
  - [x] 8.1 Implement emission for `AnonymousTypeExpressionPrimaryNode`
    - Emit `new { Name1 = expr1, Name2 = expr2 }` for explicit properties
    - Emit `new { identifier }` for simple identifier projections
    - Emit `new { expr.Member }` for member access projections
    - _Requirements: 7.1, 7.2, 7.3_

  - [x] 8.2 Implement `var` emission for anonymous type variable declarations
    - When declared type is `AnonymousType`, always emit `var` regardless of source declaration style
    - _Requirements: 7.5_

  - [x] 8.3 Implement emission for property access and pattern matching on anonymous types
    - Emit standard member access `anonValue.Name` for property access
    - Emit C# property patterns directly for pattern matching on anonymous types
    - Handle `unknown` assignment with standard `unknown` → `object` lowering
    - _Requirements: 7.4, 8.1, 8.2, 9.2_

  - [x] 8.4 Write property test for emitter correctness
    - **Property 9: Emitter produces correct C# anonymous type syntax**
    - Generate valid anonymous type expressions with explicit/projected properties, compile and verify emitted C# uses `new { ... }` syntax and `var` declarations
    - Uses batched compilation pattern (1 compile, 1 run, N output lines)
    - **Validates: Requirements 7.1, 7.2, 7.3, 7.5**

- [-] 9. Checkpoint - Ensure emitter tests pass
  - Ensure all tests pass, ask the user if questions arise.

- [ ] 10. Sample application and integration
  - [-] 10.1 Create sample project at `samples/AnonymousTypes/Program.mist`
    - Include the proof code from Requirement 13 verbatim
    - Implement `public static void RunDemo()` entry point
    - Cover all permutations: explicit assignment, variable projection, member access projection, mixed forms, same-shape assignment, assignment to `unknown`, property pattern matching, equality comparison
    - Verify expected stdout output matches the specification exactly
    - _Requirements: 13.1, 13.2, 13.3, 13.5_

  - [x] 10.2 Integrate into `samples/AllFeatures/Program.mist`
    - Add `AnonymousTypes.Program.RunDemo()` call to the AllFeatures orchestrator
    - Verify AllFeatures sample compiles and runs without errors
    - _Requirements: 13.4_

- [x] 11. TextMate grammar update
  - [x] 11.1 Update `mist.tmLanguage.json` for anonymous type highlighting
    - Ensure `new` continues to be recognized as `keyword.other.new.mist`
    - Add pattern to recognize property names before `=` in anonymous type initializers with scope `variable.other.property.mist`
    - Distinguish `=` in anonymous type property assignments from regular assignment
    - _Requirements: 11.1, 11.2, 11.3_

- [x] 12. Book documentation
  - [x] 12.1 Add anonymous types chapter to the language book
    - Create chapter in `book/src/` covering anonymous types
    - Demonstrate creation with explicit assignments, property projections, property access, interaction with `unknown`, and pattern matching
    - Add compiled snippets in `book/snippets/` following Book Snippet Policy
    - Update `book/src/SUMMARY.md` to reference the new chapter
    - _Requirements: 12.1, 12.2, 12.3, 12.4_

- [x] 13. Feature coverage documentation
  - [x] 13.1 Update `CSHARP_FEATURES.md`
    - Add `Anonymous Types` entry marking it as implemented
    - Update the Summary table at the bottom to reflect new implementation counts
    - _Requirements: 14.1, 14.2_

- [x] 14. Final checkpoint - Ensure all tests pass and samples run correctly
  - Ensure all tests pass, ask the user if questions arise.

## Notes

- Tasks marked with `*` are optional and can be skipped for faster MVP
- Each task references specific requirements for traceability
- Checkpoints ensure incremental validation
- Property tests validate universal correctness properties from the design document
- Unit tests validate specific examples and edge cases
- The design uses C# as the implementation language (the Mist compiler is written in C#)
- Property-based tests use FsCheck (already in the project) with 100 iterations minimum
- Process spawning follows the batching policy: generate N scenarios, compile once, run once, compare N output lines
- The proof code in Requirement 13 is immutable — it must appear verbatim in the sample app

## Task Dependency Graph

```json
{
  "waves": [
    { "id": 0, "tasks": ["1.1"] },
    { "id": 1, "tasks": ["1.2", "3.1"] },
    { "id": 2, "tasks": ["1.3", "1.4", "3.2", "3.3"] },
    { "id": 3, "tasks": ["3.4"] },
    { "id": 4, "tasks": ["3.5", "4.1", "4.2"] },
    { "id": 5, "tasks": ["4.3", "5.1", "5.2", "5.3"] },
    { "id": 6, "tasks": ["5.4", "5.5", "6.1"] },
    { "id": 7, "tasks": ["6.2", "8.1", "8.2"] },
    { "id": 8, "tasks": ["8.3"] },
    { "id": 9, "tasks": ["8.4", "11.1"] },
    { "id": 10, "tasks": ["10.1"] },
    { "id": 11, "tasks": ["10.2", "12.1", "13.1"] }
  ]
}
```
