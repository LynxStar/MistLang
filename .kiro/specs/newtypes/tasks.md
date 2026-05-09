# Implementation Plan: Newtypes

## Overview

Implement basic newtypes — `type Name = new SourceType;` — that create nominally distinct clones of all members from the source type. The implementation spans the full compiler pipeline: grammar, pretty printer, semantic model, type checker, C# transpiler, TextMate grammar, and a sample application. Property-based tests validate correctness properties using FsCheck with the batched compilation pattern.

## Tasks

- [x] 1. Grammar rule and pretty printer
  - [x] 1.1 Add `NewtypeDeclaration` rule to Storm grammar
    - Add `rule NewtypeDeclaration = TYPE name:IDENTIFIER EQUAL NEW sourceType:TypeReference SEMICOLON;` to `Source/Mist/Grammar/Mist.storm`
    - Add `newtypeDeclaration:NewtypeDeclaration as NewtypeItem` variant to `TopLevelItem`
    - Add `newtypeDeclaration:NewtypeDeclaration as NamespaceNewtypeItem` variant to `NamespaceBodyItem`
    - Run the Storm source generator to produce `NewtypeDeclarationNode` AST class
    - _Requirements: 6.1, 6.2, 6.3_

  - [x] 1.2 Add pretty printer support for newtype declarations
    - Add `EmitNewtypeDeclaration(NewtypeDeclarationNode)` method to `MistPrettyPrinter`
    - Emit single-line format: `type {name} = new {source_type};`
    - Wire into `EmitCompilationUnit` and `EmitNamespaceBodyItem` via pattern matching on the new variants
    - _Requirements: 4.1, 4.2_

- [x] 2. Semantic model types and member cloning
  - [x] 2.1 Add `SymbolKind.Newtype` and `NewtypeType` class
    - Add `Newtype` variant to `SymbolKind` enum
    - Create `NewtypeType` class implementing `IType` with `Name`, `SourceType`, and `MemberScope` properties
    - _Requirements: 3.1_

  - [x] 2.2 Implement `RegisterNewtypeDeclaration` in SemanticModelBuilder (Pass 1)
    - Register the newtype name as a symbol with `SymbolKind.Newtype` in the enclosing scope
    - Check for duplicate names and report `MIST_DUPLICATE_SYMBOL` diagnostic
    - Wire into the Pass 1 dispatch for `TopLevelItem` and `NamespaceBodyItem`
    - _Requirements: 3.1, 3.3_

  - [x] 2.3 Implement `ResolveNewtypeDeclaration` with member cloning (Pass 2)
    - Resolve the source type reference; report `MIST_UNRESOLVED_TYPE` if not found and assign `ErrorType`
    - Create `NewtypeType` instance and clone all members (fields, properties, methods, constructors, operators, cast operators, indexers) from the source type's scope into the newtype's member scope
    - Update constructor names to the newtype name
    - Set `symbol.DeclaredType` to the new `NewtypeType`
    - Wire into the Pass 2 dispatch for `TopLevelItem` and `NamespaceBodyItem`
    - _Requirements: 3.2, 3.4_

- [x] 3. Checkpoint - Ensure all tests pass
  - Ensure all tests pass, ask the user if questions arise.

- [x] 4. Type checker updates
  - [x] 4.1 Enforce nominal distinctness in type checker
    - Update `IsDirectlyAssignable` to reject newtype↔source assignments (both directions)
    - Reject cross-newtype assignments (two newtypes sharing the same source)
    - Report `MIST_TYPE_MISMATCH` diagnostic with appropriate message for each case
    - _Requirements: 2.1, 2.3, 2.4, 2.5_

  - [x] 4.2 Allow explicit casts between newtype and source type
    - Update `CheckCastExpression` to permit explicit casts from newtype to source and source to newtype
    - Ensure member access on newtype instances resolves against the newtype's own cloned member scope
    - _Requirements: 2.1, 2.2_

- [x] 5. C# transpiler
  - [x] 5.1 Implement `EmitNewtypeDeclaration` with three strategies
    - Strategy 1 (class source): emit a C# class with the newtype name containing all cloned members
    - Strategy 2 (struct source): emit a C# struct with the newtype name containing all cloned members
    - Strategy 3 (primitive source): emit a C# readonly struct wrapping the primitive value with explicit conversion operators in both directions
    - Wire into the transpiler's top-level and namespace-body dispatch
    - _Requirements: 5.1, 5.2, 5.3, 5.4_

- [x] 6. Checkpoint - Ensure all tests pass
  - Ensure all tests pass, ask the user if questions arise.

- [x] 7. TextMate grammar and sample application
  - [x] 7.1 Update TextMate grammar for newtype syntax highlighting
    - Add pattern to recognize `new` after `=` in type declaration context with scope `keyword.operator.new.mist`
    - Ensure the identifier after `type` in a newtype declaration gets scope `entity.name.type.newtype.mist`
    - Add the pattern before the existing type alias pattern so it matches first
    - _Requirements: 7.1, 7.2_

  - [x] 7.2 Create sample application at `samples/Newtypes/`
    - Create `samples/Newtypes/mist.toml` project file
    - Create `samples/Newtypes/Program.mist` demonstrating: newtype from primitive (int, string, bool), newtype from struct, newtype from class, member access on newtype instances, explicit casting between newtype and source type
    - Each pattern preceded by `--- <Pattern Name> ---` header, results in `key=value` format
    - Add `public static void RunDemo()` entry point
    - _Requirements: 8.1, 8.2, 8.3, 8.5_

  - [x] 7.3 Create baseline and integrate with AllFeatures
    - Compile and run the sample, capture output to `samples/__baselines__/Newtypes.txt`
    - Add `Newtypes.Program.RunDemo()` call to `samples/AllFeatures/Program.mist`
    - Verify zero exit code and no diagnostics
    - _Requirements: 8.4, 8.5, 8.6_

- [x] 8. Checkpoint - Ensure all tests pass
  - Ensure all tests pass, ask the user if questions arise.

- [x] 9. Property-based tests (parsing and pretty-printing)
  - [x] 9.1 Write property test: parsing produces correct AST structure (Property 1)
    - **Property 1: Parsing produces correct AST structure**
    - Generate random valid identifiers and type references, construct `type {id} = new {typeRef};`, parse, and verify the NewtypeDeclaration node has correct Name and SourceType
    - Create `MistNewtypeGenerator` in `Test/Mist.Tests/Generators/` for generating newtype source text
    - **Validates: Requirements 1.1**

  - [x] 9.2 Write property test: missing semicolon produces diagnostic (Property 2)
    - **Property 2: Missing semicolon produces diagnostic**
    - Generate valid newtype declarations, remove trailing semicolon, parse, and verify at least one diagnostic error is produced
    - **Validates: Requirements 1.3**

  - [x] 9.3 Write property test: pretty-printer round-trip (Property 8)
    - **Property 8: Pretty-printer round-trip**
    - Generate valid newtype declarations, parse → pretty-print → parse → pretty-print, verify both pretty-print outputs are identical
    - **Validates: Requirements 4.1, 4.2**

- [x] 10. Property-based tests (semantic model)
  - [x] 10.1 Write property test: symbol registration with correct kind (Property 6)
    - **Property 6: Symbol registration with correct kind**
    - Generate programs with newtype declarations, build semantic model in-process via shared `CompilerDriver`, verify symbol table contains a symbol with `SymbolKind.Newtype` for each declared name
    - **Validates: Requirements 3.1**

  - [x] 10.2 Write property test: member cloning completeness (Property 7)
    - **Property 7: Member cloning completeness**
    - Generate source types with varying member counts (fields, properties, methods, constructors, operators, indexers), declare a newtype from each, build semantic model in-process, verify the newtype's member scope contains exactly the same number of members as the source
    - **Validates: Requirements 3.2**

- [x] 11. Property-based tests (type checker — batched compilation)
  - [x] 11.1 Write property test: nominal distinctness (Property 3)
    - **Property 3: Nominal distinctness (bidirectional non-assignability)**
    - Generate scenarios with source→newtype and newtype→source assignments without casts, batch into a single program, compile in-process via shared `CompilerDriver`, verify each scenario produces a type mismatch diagnostic
    - Uses batched pattern: 1 compile, N diagnostic checks
    - **Validates: Requirements 2.1, 2.3, 2.4**

  - [x] 11.2 Write property test: cross-newtype non-assignability (Property 4)
    - **Property 4: Cross-newtype non-assignability**
    - Generate pairs of newtypes sharing the same source type, attempt cross-assignment, compile in-process, verify type mismatch diagnostic for each
    - Uses batched pattern: 1 compile, N diagnostic checks
    - **Validates: Requirements 2.5**

  - [x] 11.3 Write property test: member access resolves on cloned set (Property 5)
    - **Property 5: Member access resolves on cloned set**
    - Generate source types with accessible members, declare newtypes, access members on newtype instances, compile in-process, verify no "member not found" diagnostics
    - Uses batched pattern: 1 compile, N diagnostic checks
    - **Validates: Requirements 2.2**

- [x] 12. Property-based tests (transpiler — batched compilation)
  - [x] 12.1 Write property test: transpilation preserves type category (Property 9)
    - **Property 9: Transpilation preserves type category**
    - Generate newtypes from classes and structs, transpile in-process, verify the emitted C# uses `class` for class sources and `struct` for struct sources
    - **Validates: Requirements 5.1, 5.2**

  - [x] 12.2 Write property test: transpiled C# compiles without errors (Property 10)
    - **Property 10: Transpiled C# compiles without errors**
    - Generate valid newtype declarations (primitives, structs, classes), transpile in-process, batch all C# outputs into a single compilation, spawn `dotnet build` once, verify zero exit code
    - Uses batched pattern: 1 process spawn for C# compilation
    - **Validates: Requirements 5.4**

- [x] 13. Final checkpoint - Ensure all tests pass
  - Ensure all tests pass, ask the user if questions arise.

## Notes

- Tasks marked with `*` are optional and can be skipped for faster MVP
- Each task references specific requirements for traceability
- Checkpoints ensure incremental validation
- Property tests validate universal correctness properties from the design document
- All compilation-based property tests use the **batched pattern** (1 compile, N checks) per the test process spawning policy
- Semantic model tests use in-process `CompilerDriver` — no process spawning needed
- The Storm source generator automatically produces AST node classes from grammar rules — no hand-written parser code needed
- The `NEW` keyword token already exists in the grammar (used in `ObjectCreationExpression`) — no lexer changes needed

## Task Dependency Graph

```json
{
  "waves": [
    { "id": 0, "tasks": ["1.1"] },
    { "id": 1, "tasks": ["1.2", "2.1"] },
    { "id": 2, "tasks": ["2.2"] },
    { "id": 3, "tasks": ["2.3"] },
    { "id": 4, "tasks": ["4.1", "4.2"] },
    { "id": 5, "tasks": ["5.1"] },
    { "id": 6, "tasks": ["7.1", "7.2"] },
    { "id": 7, "tasks": ["7.3"] },
    { "id": 8, "tasks": ["9.1", "9.2", "9.3"] },
    { "id": 9, "tasks": ["10.1", "10.2"] },
    { "id": 10, "tasks": ["11.1", "11.2", "11.3"] },
    { "id": 11, "tasks": ["12.1", "12.2"] }
  ]
}
```
