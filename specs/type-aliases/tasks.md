# Implementation Plan: Type Aliases

## Overview

This plan implements type aliases for the Mist language across the full compiler pipeline. Each task builds incrementally — grammar first, then AST generation, pretty-printing, semantic model, type checker, C# transpiler, and editor support. Tests are interleaved close to the code they validate.

## Tasks

- [x] 1. Add `type` keyword and grammar rule
  - [x] 1.1 Add `TYPE` keyword token to `Source/Mist/Grammar/keywords.storm`
    - Add `token TYPE = "type";` to the keyword list
    - _Requirements: 1.1, 1.2, 1.3, 7.4_
  - [x] 1.2 Add `TypeAliasDeclaration` rule to `Source/Mist/Grammar/Mist.storm`
    - Add rule: `rule TypeAliasDeclaration = TYPE name:IDENTIFIER typeParams:TypeParameterList? EQUAL type:TypeReference SEMICOLON;`
    - Add `typeAliasDeclaration:TypeAliasDeclaration as TypeAliasItem` variant to `TopLevelItem`
    - Add `typeAliasDeclaration:TypeAliasDeclaration as NamespaceTypeAliasItem` variant to `NamespaceBodyItem`
    - _Requirements: 7.1, 7.2, 7.3_

- [x] 2. Rebuild generated AST nodes
  - [x] 2.1 Build the project to trigger Storm source generator
    - Run `dotnet build Source/Mist/Mist.csproj` to regenerate the lexer and parser from the updated grammar
    - Verify that `TypeAliasDeclarationNode`, `TypeAliasItemTopLevelItemNode`, and `NamespaceTypeAliasItemNamespaceBodyItemNode` are generated
    - Fix any build errors from the grammar changes
    - _Requirements: 2.1, 2.2, 2.3_

- [x] 3. Pretty printer support
  - [x] 3.1 Add type alias emission to `Source/Mist/Parsing/MistPrettyPrinter.cs`
    - Add `EmitTypeAliasDeclaration(TypeAliasDeclarationNode)` method that emits `type {name}{typeParams} = {typeExpr};`
    - Handle `TypeAliasItemTopLevelItemNode` in `EmitCompilationUnit`
    - Handle `NamespaceTypeAliasItemNamespaceBodyItemNode` in `EmitNamespaceBodyItem`
    - _Requirements: 3.1, 3.2, 3.3, 3.4_
  - [x] 3.2 Write property test for pretty-print round-trip
    - **Property 2: Pretty-print round-trip**
    - Create `Test/Mist.Tests/Parsing/TypeAliasRoundTripPropertyTests.cs`
    - Generate random valid type alias source strings (simple, generic, union targets)
    - Verify `print(parse(print(parse(s)))) == print(parse(s))` (idempotence)
    - **Validates: Requirements 3.1, 3.2, 3.3, 3.4**

- [x] 4. Checkpoint - Verify parsing and pretty-printing
  - Ensure all tests pass, ask the user if questions arise.

- [x] 5. Semantic model changes
  - [x] 5.1 Add `TypeAlias` to `SymbolKind` enum in `Source/Mist/Semantics/Contracts/ISymbol.cs`
    - Add `TypeAlias` variant to the `SymbolKind` enum
    - _Requirements: 4.1_
  - [x] 5.2 Create `TypeAliasType` in `Source/Mist/Semantics/Model/Types/TypeAliasType.cs`
    - Implement `IType` interface with `Name`, `UnderlyingType`, `TypeParameters` properties
    - `DisplayName` returns `Name`; `Kind` delegates to `UnderlyingType.Kind`
    - _Requirements: 4.1, 4.2, 5.1_
  - [x] 5.3 Register type aliases in `Source/Mist/Semantics/Model/SemanticModelBuilder.cs`
    - **Pass 1**: Register `TypeAliasDeclarationNode` as a `Symbol` with `SymbolKind.TypeAlias` in the enclosing scope; store AST node reference and type parameters
    - **Pass 2**: Resolve the target `TypeReference` to an `IType`; construct `TypeAliasType`; detect cycles using a visited-set (report `MIST_SB_0060 (historical)` on cycle, assign `ErrorType`)
    - Report `MIST_SB_0061 (historical)` for duplicate alias names in scope
    - Report `MIST_SB_0062 (historical)` for unresolved types in alias target
    - _Requirements: 4.1, 4.2, 4.3, 4.4, 5.4_
  - [x] 5.4 Write property test for semantic model registration
    - **Property 3: Semantic model registration**
    - Create `Test/Mist.Tests/Semantics/TypeAliasRegistrationPropertyTests.cs`
    - Generate random type alias declarations (with and without type parameters)
    - Build semantic model using `SharedDriver`; verify scope contains alias symbol with `SymbolKind.TypeAlias` and correct type parameters
    - **Validates: Requirements 4.1, 4.2**
  - [x] 5.5 Write unit tests for semantic model diagnostics
    - Create `Test/Mist.Tests/Semantics/TypeAliasDiagnosticTests.cs`
    - Test duplicate alias name produces `MIST_SB_0061 (historical)`
    - Test unresolved type in alias target produces `MIST_SB_0062 (historical)`
    - Test direct cycle (`type A = B; type B = A;`) produces `MIST_SB_0060 (historical)`
    - Test indirect cycle (`type A = B; type B = C; type C = A;`) produces `MIST_SB_0060 (historical)`
    - **Validates: Requirements 4.3, 4.4, 5.4**

- [x] 6. Checkpoint - Verify semantic model
  - Ensure all tests pass, ask the user if questions arise.

- [x] 7. Type checker changes
  - [x] 7.1 Implement transparent alias resolution in `Source/Mist/Analysis/Builtin/TypeCheckerAnalyzer.cs`
    - When resolving a type reference that names a `SymbolKind.TypeAlias` symbol, retrieve `TypeAliasType.UnderlyingType`
    - For generic aliases with type arguments: verify argument count matches parameter count (report `MIST_SB_0063 (historical)` on mismatch), substitute type arguments into the underlying type
    - Return the resolved `IType` (fully transparent — no alias wrapper remains)
    - _Requirements: 5.1, 5.2, 5.3_
  - [x] 7.2 Write property test for type resolution transparency
    - **Property 4: Type resolution transparency**
    - Create `Test/Mist.Tests/Analysis/TypeAliasResolutionPropertyTests.cs`
    - Generate programs with type aliases and equivalent programs using the underlying type directly
    - Build semantic models for both; compare resolved types for equality
    - **Validates: Requirements 5.1, 5.2**
  - [x] 7.3 Write unit tests for type argument mismatch
    - Create `Test/Mist.Tests/Analysis/TypeAliasArgumentMismatchTests.cs`
    - Test generic alias used with wrong number of type arguments produces `MIST_SB_0063 (historical)`
    - **Validates: Requirements 5.3**

- [x] 8. Checkpoint - Verify type checking
  - Ensure all tests pass, ask the user if questions arise.

- [x] 9. C# transpiler changes
  - [x] 9.1 Skip alias declarations in `Source/Mist/Backends/CSharpTranspiler/CSharpEmitter.cs`
    - In the compilation unit emission logic, skip `TypeAliasItemTopLevelItemNode` and `NamespaceTypeAliasItemNamespaceBodyItemNode` variants (no C# output for alias declarations)
    - _Requirements: 6.1, 6.4_
  - [x] 9.2 Ensure `CSharpTypeMap.GetTypeName()` handles `TypeAliasType` in `Source/Mist/Backends/CSharpTranspiler/CSharpTypeMap.cs`
    - If a `TypeAliasType` reaches the type map (defensive), resolve to `UnderlyingType` and delegate to existing logic
    - Simple aliases inline the resolved C# type; union aliases use `UnionHelperGenerator.GetHelperTypeName()`; generic aliases inline the fully substituted type
    - _Requirements: 6.1, 6.2, 6.3, 6.4_
  - [x] 9.3 Write property test for transpilation correctness
    - **Property 5: Transpilation correctness**
    - Create `Test/Mist.Tests/Backend/TypeAliasEmissionPropertyTests.cs`
    - Generate valid Mist programs with type alias declarations and usages (simple, generic, union variants)
    - Compile in-process using `SharedDriver`; verify zero diagnostics and valid C# output
    - Use **batched approach** (1 compile, 1 run) per workspace rules if runtime verification is needed
    - **Validates: Requirements 6.1, 6.2, 6.3, 6.4**

- [x] 10. Editor syntax highlighting
  - [x] 10.1 Update TextMate grammar in `Editor/mist-syntax-highlighting/syntaxes/mist.tmLanguage.json`
    - Add `type` to the keyword declaration pattern: `\\b(namespace|using|class|struct|interface|trait|extension|type)\\b`
    - Add a declaration-name pattern for type alias declarations: `\\b(type)\\s+([a-zA-Z_]\\w*)` with captures for keyword and type entity name
    - _Requirements: 8.1, 8.2_

- [x] 11. Add type alias generator for tests
  - [x] 11.1 Create `Test/Mist.Tests/Generators/MistTypeAliasGenerator.cs`
    - Implement FsCheck `Arbitrary` for generating random valid type alias source strings
    - Support simple aliases, generic aliases (1–5 type parameters), and union target types (2–5 constituents)
    - Reuse existing `MistTypeGenerator` patterns for type reference generation
    - _Requirements: 2.1, 2.2, 2.3_
  - [x] 11.2 Write property test for parsing correctness
    - **Property 1: Parsing produces correct AST structure**
    - Create `Test/Mist.Tests/Parsing/TypeAliasParserPropertyTests.cs`
    - Generate random valid type alias source strings using the generator from 11.1
    - Parse and verify `TypeAliasDeclarationNode` fields (`Name`, `TypeParams`, `Type`) match input
    - **Validates: Requirements 2.1, 2.2, 2.3**

- [x] 12. Final checkpoint - Ensure all tests pass
  - Ensure all tests pass, ask the user if questions arise.

## Notes

- Tasks marked with `*` are optional and can be skipped for faster MVP
- The design uses C# throughout (this is a C# compiler project), so all implementation is in C#
- Property tests use FsCheck 3.1.0 (already in the test project)
- Process spawning follows the batched approach per workspace rules (max 1 compile + 1 run per test class)
- Each task references specific requirements for traceability
- Checkpoints ensure incremental validation at natural boundaries
- The `SharedDriver` pattern is used for in-process compilation in tests
