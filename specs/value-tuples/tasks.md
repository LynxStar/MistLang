# Implementation Plan: Value Tuples

## Overview

This plan implements ValueTuple support across the Mist compiler pipeline. Tasks follow the pipeline order: grammar → parser (auto-generated) → semantic model → type checker → pretty printer → C# backend → tests. Each task builds incrementally on the previous, ensuring no orphaned code. All property-based tests use FsCheck.Xunit v3.1.0 in-process (no process spawning).

## Tasks

- [x] 1. Add tuple grammar rules to Mist.storm
  - [x] 1.1 Add TupleTypeElement rule and TupleTypeRef alternative to TypeReference
    - Add `rule TupleTypeElement = type:TypeReference name:IDENTIFIER?;`
    - Add `TupleTypeRef` alternative to `TypeReference` rule: `LEFT_PARENTHESIS first:TupleTypeElement COMMA rest:TupleTypeElement (COMMA additional:TupleTypeElement)* RIGHT_PARENTHESIS as TupleTypeRef`
    - The `first COMMA rest` structure enforces minimum arity of 2
    - _Requirements: 1.1, 1.2, 7.3, 7.4, 8.1_
  - [x] 1.2 Add TupleLiteralElement rule and TupleLiteralExpression alternative to Primary
    - Add `rule TupleLiteralElement` with `NamedTupleLiteralElement` (`name:IDENTIFIER COLON value:Expression`) and `UnnamedTupleLiteralElement` (`value:Expression`) alternatives
    - Add `TupleLiteralExpression` alternative to `Primary` rule, placed **before** `ParenthesizedExpression` for PEG-style disambiguation
    - _Requirements: 2.1, 2.2, 7.1, 7.2, 8.2_
  - [x] 1.3 Add tuple deconstruction alternatives to VariableDeclarationStatement
    - Add `VarTupleDeconstruction` alternative: `VAR LEFT_PARENTHESIS firstName:IDENTIFIER (COMMA restNames:IDENTIFIER)* RIGHT_PARENTHESIS EQUAL initializer:Expression SEMICOLON`
    - Add `TupleDeconstructionElement` rule and `TypedTupleDeconstruction` alternative
    - Place deconstruction alternatives before existing `InferredVariableDeclaration` for correct disambiguation
    - _Requirements: 4.1, 4.2, 7.5_

- [x] 2. Verify parser generation and build
  - Build the solution to trigger Storm source generator, producing the auto-generated parser with new tuple AST node types
  - Verify the build succeeds with zero warnings
  - Confirm generated AST node types exist: `TupleTypeRefTypeReferenceNode`, `TupleTypeElementNode`, `TupleLiteralExpressionPrimaryNode`, `NamedTupleLiteralElementTupleLiteralElementNode`, `UnnamedTupleLiteralElementTupleLiteralElementNode`, `VarTupleDeconstructionVariableDeclarationStatementNode`, `TypedTupleDeconstructionVariableDeclarationStatementNode`, `TupleDeconstructionElementNode`
  - _Requirements: 1.1, 2.1, 4.1_

- [x] 3. Checkpoint - Ensure grammar builds cleanly
  - Ensure all tests pass, ask the user if questions arise.

- [x] 4. Implement TupleType in the semantic model
  - [x] 4.1 Create TupleType.cs in Source/Mist/Semantics/Model/Types/
    - Implement `IType` with `TypeKind.Struct`
    - Store `IReadOnlyList<IType> ElementTypes` and `IReadOnlyList<string?>? ElementNames`
    - Build `DisplayName` as `(T1, T2)` for unnamed or `(T1 name1, T2 name2)` for named elements
    - Add `Arity` convenience property
    - Follow the `ArrayType` pattern as a structural type marker
    - _Requirements: 5.1, 5.2, 3.1_
  - [x] 4.2 Write property test for TupleType DisplayName and Kind (Property 2)
    - **Property 2: TupleType DisplayName and Kind invariant**
    - Create `TupleTypeGenerator` in `Test/Mist.Tests/Generators/` that generates `TupleType` instances with random element types (primitives, nested tuples) and optional names
    - Verify `DisplayName` matches `(T1, T2, ..., Tn)` or `(T1 name1, T2 name2, ..., Tn nameN)` format
    - Verify `Kind` is always `TypeKind.Struct`
    - Verify `Arity` equals `ElementTypes.Count`
    - **Validates: Requirements 5.1**

- [x] 5. Integrate TupleType into SemanticModelBuilder
  - [x] 5.1 Resolve TupleTypeRef AST nodes to TupleType instances
    - In `SemanticModelBuilder`, handle `TupleTypeRefTypeReferenceNode` by resolving each element's type reference recursively and collecting optional names
    - Construct `TupleType` from resolved element types and names
    - _Requirements: 1.4, 5.1, 5.2, 8.3_
  - [x] 5.2 Handle TupleLiteralExpression in the semantic model
    - Infer the type of each element expression in a tuple literal
    - Construct a `TupleType` from inferred element types and any explicit names
    - _Requirements: 2.3, 8.2_
  - [x] 5.3 Handle tuple deconstruction statements in the semantic model
    - Create local variable symbols for each deconstruction target
    - For `var` deconstructions, defer type inference to the type checker
    - For typed deconstructions, resolve each element's declared type
    - _Requirements: 4.1, 4.2, 4.5_

- [x] 6. Implement type checker support for tuples
  - [x] 6.1 Add tuple assignability logic to TypeCheckerAnalyzer
    - Two `TupleType` instances are assignable if same arity and each element type is assignable (name erasure)
    - _Requirements: 5.3, 5.4, 2.4_
  - [x] 6.2 Add tuple element access resolution
    - Resolve `tuple.ItemN` positional access (1-indexed) to the element type at position N
    - Resolve `tuple.name` named access to the element type of the matching named item
    - Report `MIST_TC_0063 (historical)` diagnostic for undefined member access
    - _Requirements: 3.1, 3.2, 3.3, 3.4, 3.5_
  - [x] 6.3 Add tuple deconstruction validation
    - Verify RHS has a `TupleType` (report `MIST_TC_0064 (historical)` if not)
    - Verify deconstruction target count matches tuple arity (report `MIST_TC_0061 (historical)` on mismatch)
    - For typed deconstructions, verify each element type is assignable (report `MIST_TC_0062 (historical)` on mismatch)
    - For `var` deconstructions, infer each target's type from the corresponding element type
    - _Requirements: 4.3, 4.4, 4.5, 4.6_
  - [x] 6.4 Write property test for tuple assignability with name erasure (Property 3)
    - **Property 3: Tuple assignability with name erasure**
    - Create `TupleTypePairGenerator` that generates pairs of `TupleType` with same/different types and names
    - Verify same-type pairs are assignable regardless of name differences
    - Verify different-arity or different-element-type pairs are not assignable
    - **Validates: Requirements 2.4, 4.3, 4.4, 5.3, 5.4**
  - [x] 6.5 Write property test for tuple element access resolution (Property 4)
    - **Property 4: Tuple element access resolution**
    - Generate `TupleType` instances with arity N, verify `ItemK` (1 ≤ K ≤ N) resolves to correct element type
    - Verify named access resolves to same type as corresponding positional access
    - **Validates: Requirements 3.1, 3.2, 3.3, 3.4**
  - [x] 6.6 Write property test for invalid tuple member access diagnostic (Property 5)
    - **Property 5: Invalid tuple member access produces diagnostic**
    - Generate `TupleType` + invalid member name (not `Item1`..`ItemN`, not a declared name)
    - Verify the type checker reports an undefined member diagnostic
    - **Validates: Requirements 3.5**

- [x] 7. Checkpoint - Ensure semantic model and type checker tests pass
  - Ensure all tests pass, ask the user if questions arise.

- [x] 8. Add tuple support to the pretty printer
  - [x] 8.1 Handle TupleTypeRef in MistPrettyPrinter
    - Emit `(T1, T2)` for unnamed elements, `(T1 name1, T2 name2)` for named elements
    - Handle nested tuple types recursively
    - _Requirements: 1.5, 8.1_
  - [x] 8.2 Handle TupleLiteralExpression in MistPrettyPrinter
    - Emit `(expr1, expr2)` for unnamed elements, `(name1: expr1, name2: expr2)` for named elements
    - Handle nested tuple literals recursively
    - _Requirements: 2.5, 8.2_
  - [x] 8.3 Handle tuple deconstruction statements in MistPrettyPrinter
    - Emit `var (a, b) = expr;` for var deconstructions
    - Emit `(int a, string b) = expr;` for typed deconstructions
    - _Requirements: 4.7_
  - [x] 8.4 Write property test for tuple syntax round-trip (Property 1)
    - **Property 1: Tuple syntax round-trip**
    - Create `MistTupleGenerator` in `Test/Mist.Tests/Generators/` that generates Mist source strings with tuple types, literals, deconstructions (including nested tuples, named elements, mixed named/unnamed)
    - Parse → pretty-print → parse → compare ASTs for equivalence
    - **Validates: Requirements 1.1, 1.2, 1.5, 1.6, 2.1, 2.2, 2.5, 2.6, 4.1, 4.2, 4.7, 4.8, 7.2, 7.4, 8.1, 8.2**

- [x] 9. Add tuple support to the C# backend
  - [x] 9.1 Add TupleType case to CSharpTypeMap.GetTypeName()
    - Add `TupleType t => EmitTupleTypeName(t, isValueContext)` case to the switch expression
    - `EmitTupleTypeName` produces `(int, string)` for unnamed, `(int x, string y)` for named
    - Each element type is recursively mapped through `GetTypeName()`
    - Place the case before the generic `ClassType`/`StructType` fallbacks
    - _Requirements: 6.1, 6.2, 8.4_
  - [x] 9.2 Write property test for C# type name mapping (Property 6)
    - **Property 6: C# type name mapping for TupleType**
    - Generate `TupleType` instances (including nested tuples) using `TupleTypeGenerator`
    - Verify `CSharpTypeMap.GetTypeName()` produces correct C# tuple type syntax
    - Verify element types are recursively mapped, unnamed elements are bare types, named elements are `type name` pairs
    - **Validates: Requirements 6.1, 6.2, 8.4**
  - [x] 9.3 Emit tuple literals in CSharpSourceWriter
    - Handle `TupleLiteralExpressionPrimaryNode` to emit `(expr1, expr2)` or `(name1: expr1, name2: expr2)`
    - Handle nested tuple literals recursively
    - _Requirements: 6.3, 6.4, 8.4_
  - [x] 9.4 Emit tuple type references in CSharpSourceWriter
    - Handle `TupleTypeRefTypeReferenceNode` to emit C# tuple type syntax
    - _Requirements: 6.1, 6.2_
  - [x] 9.5 Emit tuple deconstruction statements in CSharpSourceWriter
    - Handle `VarTupleDeconstructionVariableDeclarationStatementNode` to emit `var (a, b) = expr;`
    - Handle `TypedTupleDeconstructionVariableDeclarationStatementNode` to emit `(int a, string b) = expr;`
    - _Requirements: 6.6, 6.7_
  - [x] 9.6 Emit tuple element access in CSharpSourceWriter
    - Ensure `tuple.Item1` and `tuple.name` member access passes through correctly (same syntax as C#)
    - _Requirements: 6.5_

- [x] 10. Checkpoint - Ensure C# backend tests pass
  - Ensure all tests pass, ask the user if questions arise.

- [x] 11. Write unit tests for disambiguation and diagnostics
  - [x] 11.1 Write unit tests for parser disambiguation
    - `ParenthesizedExpressionNotTuple`: verify `(42)` parses as `ParenthesizedExpression`
    - `SingleTypeNotTupleType`: verify `(int)` in type position doesn't parse as tuple type
    - `VarDeconstructionDisambiguation`: verify `var (a, b) = expr;` vs `var x = (1, 2);`
    - _Requirements: 7.1, 7.3, 7.5_
  - [x] 11.2 Write unit tests for type checker diagnostics
    - `TupleDeconstructionArityMismatch`: verify diagnostic when targets ≠ tuple arity
    - `TupleDeconstructionNonTupleRhs`: verify diagnostic when RHS is not a tuple
    - _Requirements: 4.6_
  - [x] 11.3 Write unit tests for C# emitter output
    - `EmitterTupleMemberAccess`: verify `tuple.Item1` and `tuple.name` emit correctly
    - `EmitterVarDeconstruction`: verify `var (a, b) = expr;` emits correctly
    - `EmitterTypedDeconstruction`: verify `(int a, string b) = expr;` emits correctly
    - _Requirements: 6.5, 6.6, 6.7_

- [x] 12. Final checkpoint - Ensure all tests pass
  - Ensure all tests pass, ask the user if questions arise.

## Notes

- Tasks marked with `*` are optional and can be skipped for faster MVP
- Each task references specific requirements for traceability
- Checkpoints ensure incremental validation
- Property tests validate universal correctness properties from the design document
- Unit tests validate specific examples, edge cases, and diagnostics
- All tests operate in-process using the parser, pretty printer, semantic model, type checker, and CSharpTypeMap directly — no process spawning
- The parser (MistParser.cs) is auto-generated from the Storm grammar; task 2 verifies generation succeeds
- FsCheck.Xunit v3.1.0 is already a dependency in the test project
