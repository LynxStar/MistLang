# Implementation Plan: Type Literals

## Overview

Add type literal expressions to the Mist language, allowing type names to be used directly as expressions that evaluate to `Type`. Implementation proceeds bottom-up through the compiler pipeline: grammar → type system → semantic model → type checker → C# backend → pretty printer → syntax highlighting → sample app.

## Tasks

- [x] 1. Add TypeLiteralExpression to the grammar and create TypeType
  - [x] 1.1 Add TypeLiteralExpression alternative to the Primary rule in Mist.storm
    - In `Source/Mist/Grammar/Mist.storm`, add `| typeRef:TypeReference as TypeLiteralExpression` as the last alternative in the `Primary` rule (after `IdentifierExpression`)
    - This placement ensures primitive keywords match via `TypeLiteralExpression` while user-defined names match `IdentifierExpression` first
    - _Requirements: 1.1, 1.2, 1.3, 1.4, 1.5, 2.1_

  - [x] 1.2 Create the TypeType singleton class
    - Create `Source/Mist/Semantics/Model/Types/TypeType.cs` following the same singleton pattern as `StringType`, `VoidType`, etc.
    - `DisplayName` returns `"Type"`, `Kind` returns `TypeKind.Class` (since `System.Type` is a reference type)
    - _Requirements: 3.1, 3.5_

  - [x] 1.3 Write property test: Primitive type keywords produce TypeLiteralExpression (Property 1)
    - **Property 1: Primitive type keywords in expression position produce TypeLiteralExpression**
    - For any primitive type keyword in any expression position, the parser SHALL produce a `TypeLiteralExpression` AST node containing the corresponding `PrimitiveTypeRef`
    - **Validates: Requirements 1.2, 2.2, 2.3, 2.4, 2.5**

  - [x] 1.4 Write property test: Array type literals include array rank (Property 2)
    - **Property 2: Array type literals in expression position include array rank**
    - For any valid base type followed by `[]` in expression position, the parser SHALL produce a `TypeLiteralExpression` with array rank set
    - **Validates: Requirements 1.4**

  - [x] 1.5 Write property test: Type annotation positions unaffected (Property 3)
    - **Property 3: Type annotation positions are unaffected by type literal grammar**
    - For any type name in annotation position (parameter, field, return type, typed variable declaration), the parser SHALL produce a `TypeReference` node, NOT a `TypeLiteralExpression`
    - **Validates: Requirements 1.5**

- [x] 2. Checkpoint
  - Rebuild the Storm-generated parser from the updated grammar. Ensure the project compiles and existing tests pass. Ask the user if questions arise.

- [x] 3. Implement semantic model and type checker support
  - [x] 3.1 Add TypeLiteralExpression handling in SemanticModelBuilder
    - In `Source/Mist/Semantics/Model/SemanticModelBuilder.cs`, add a case for `TypeLiteralExpressionPrimaryNode` in the `ResolvePrimary` method
    - Call `ResolveTypeReference` on the node's `TypeRef` to validate the referenced type (emits `MIST_UNDEFINED_TYPE` if unresolved)
    - Record `TypeType.Instance` as the expression type in the expression type map
    - _Requirements: 3.2, 3.3, 3.4, 4.1, 4.2, 4.3_

  - [x] 3.2 Add TypeLiteralExpression handling in TypeCheckerAnalyzer
    - In `Source/Mist/Analysis/Builtin/TypeCheckerAnalyzer.cs`, add a case for `TypeLiteralExpressionPrimaryNode` in `CheckPrimaryNode`
    - Type literals are always valid primaries; type mismatch checking is handled by existing assignability checks at usage sites
    - _Requirements: 4.4_

  - [x] 3.3 Write property test: All type literals have TypeType expression type (Property 4)
    - **Property 4: All type literal expressions have TypeType as their expression type**
    - For any valid `TypeLiteralExpression`, the semantic model SHALL record `TypeType` as the expression type; `var` declarations initialized with a type literal SHALL infer `TypeType`
    - **Validates: Requirements 3.2, 3.3**

  - [x] 3.4 Write property test: Variable names shadow type names (Property 5)
    - **Property 5: Variable names shadow type names in expression position**
    - When an identifier is both a type name and a local variable name, expression position SHALL resolve to the variable
    - **Validates: Requirements 2.6**

  - [x] 3.5 Write property test: Unresolved type literals produce diagnostics (Property 6)
    - **Property 6: Unresolved type literals produce diagnostics**
    - A type name in `TypeLiteralExpression` that cannot be resolved SHALL emit `MIST_UNDEFINED_TYPE`
    - **Validates: Requirements 4.1**

  - [x] 3.6 Write property test: Type mismatch for type literal assigned to non-Type variable (Property 7)
    - **Property 7: Type mismatch when assigning type literal to non-Type variable**
    - `D x = T;` where D is not `Type` SHALL produce `MIST_TC_0001`
    - **Validates: Requirements 4.4**

- [x] 4. Checkpoint
  - Ensure all tests pass, ask the user if questions arise.

- [x] 5. Implement C# backend support
  - [x] 5.1 Add TypeLiteralExpression handling in CSharpEmitter
    - In `Source/Mist/Backends/CSharpTranspiler/CSharpEmitter.cs`, add a case for `TypeLiteralExpressionPrimaryNode` in `EmitPrimary` within `CSharpSourceWriter`
    - Emit `typeof({EmitTypeReference(typeLiteral.TypeRef)})` wrapping the existing `EmitTypeReference` output
    - _Requirements: 5.1, 5.2, 5.3, 5.4_

  - [x] 5.2 Add TypeType mapping in CSharpTypeMap
    - In `Source/Mist/Backends/CSharpTranspiler/CSharpTypeMap.cs`, add `TypeType => "System.Type"` to the `GetTypeName` switch expression
    - This ensures `Type foo` in Mist emits `System.Type foo` in C#
    - _Requirements: 5.5_

  - [x] 5.3 Write property test: C# emitter wraps type literals in typeof() (Property 8)
    - **Property 8: C# emitter wraps type literals in typeof()**
    - For any `TypeLiteralExpression`, the C# emitter SHALL produce `typeof(<type_reference>)`
    - **Validates: Requirements 5.1, 5.3, 5.4**

  - [x] 5.4 Write unit tests for C# backend
    - Test `CSharpTypeMap.GetTypeName(TypeType.Instance)` returns `"System.Type"`
    - Test `typeof(MyClass)` emission for user-defined types
    - Test `var foo = typeof(int);` emission for inferred declarations
    - _Requirements: 5.2, 5.5_

- [x] 6. Implement pretty printer and syntax highlighting
  - [x] 6.1 Add TypeLiteralExpression handling in MistPrettyPrinter
    - In `Source/Mist/Parsing/MistPrettyPrinter.cs`, add a case for `TypeLiteralExpressionPrimaryNode` in `EmitPrimary`
    - Reuse the existing `EmitTypeReference(typeLiteral.TypeRef)` method
    - _Requirements: 6.1, 6.2, 6.3_

  - [x] 6.2 Add Type keyword to TextMate syntax highlighting grammar
    - In `Editor/mist-syntax-highlighting/syntaxes/mist.tmLanguage.json`, add a new pattern in the `types` repository: `{ "name": "storage.type.metatype.mist", "match": "\\bType\\b" }`
    - _Requirements: 7.1, 7.2_

  - [x] 6.3 Write property test: Pretty printer round-trip for type literals (Property 9)
    - **Property 9: Pretty printer round-trip for type literals**
    - For any valid Mist source with type literals, parse → print → parse SHALL produce an equivalent AST
    - **Validates: Requirements 6.1, 6.2, 6.3, 6.4**

- [x] 7. Create sample application
  - [x] 7.1 Create the TypeLiterals sample program
    - Create `samples/TypeLiterals/Program.mist` demonstrating primitive type literals (`var intType = int;`), array type literals (`var intArrayType = int[];`), explicit `Type` annotation (`Type floatType = float;`), void type literal, and passing type literals as method arguments
    - _Requirements: 8.1, 8.2_

- [x] 8. Final checkpoint
  - Ensure all tests pass and the sample program compiles successfully through the Mist compiler. Ask the user if questions arise.

## Notes

- Tasks marked with `*` are optional and can be skipped for faster MVP
- Each task references specific requirements for traceability
- The grammar change (1.1) must be followed by a Storm parser rebuild (checkpoint 2) before semantic model work begins
- Property tests validate universal correctness properties from the design document using FsCheck
- The C# class is named `TypeType` (not `MistType`), following the existing convention: `StringType`, `VoidType`, `PrimitiveType`, etc.
