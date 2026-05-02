# Implementation Plan: Cast Operators

## Overview

Add implicit and explicit cast operators to the Mist programming language compiler. The implementation proceeds bottom-up through the compiler pipeline: grammar and keywords first, then parsing and pretty-printing, semantic model construction (cast operator registration, Pass 1 validation, Pass 2 resolution), type checker extensions (declaration validation, implicit cast resolution, explicit cast expression validation), C# emitter extensions (cast operator declarations, cast expressions, implicit cast emission at assignment/argument/return sites), syntax highlighting, documentation, and a sample application. Each task builds on the previous ones so that the compiler remains buildable at every step.

## Tasks

- [x] 1. Add `IMPLICIT`, `EXPLICIT` keywords and grammar rules
  - [x] 1.1 Add keyword tokens and grammar rules to `keywords.storm` and `Mist.storm`
    - Add `token IMPLICIT = "implicit";` to `Source/Mist/Grammar/keywords.storm`
    - Add `token EXPLICIT = "explicit";` to `Source/Mist/Grammar/keywords.storm`
    - Add `CastOperatorDeclaration` rule with modifiers, cast kind, `OPERATOR`, target type reference, left parenthesis, parameter list, right parenthesis, and operator body
    - Add `CastOperatorKind` rule with two variants: `ImplicitCast` (using `IMPLICIT`) and `ExplicitCast` (using `EXPLICIT`)
    - Add `CastOperatorMember` variant to the `MemberDeclaration` rule alongside existing member variants
    - Add `CastExpression` variant to the `Unary` rule: `LEFT_PARENTHESIS targetType:TypeReference RIGHT_PARENTHESIS operand:Unary as CastExpression`
    - Ensure `CastExpression` is ordered before `PostfixExpression` in the `Unary` rule so the parser tries the cast interpretation first
    - Reuse the existing `OperatorBody` rule (with `OperatorBlockBody` and `OperatorNoBody` variants) for cast operator bodies
    - _Requirements: 1.1, 1.2, 1.3, 2.1, 2.2, 2.3, 2.4, 2.5, 3.1, 3.2, 3.3, 3.4_

  - [x] 1.2 Verify the project builds with the new grammar
    - Run `dotnet build` to confirm the Storm source generator produces the new AST node types (`CastOperatorDeclarationNode`, `CastOperatorKindNode`, `ImplicitCastCastOperatorKindNode`, `ExplicitCastCastOperatorKindNode`, `CastOperatorMemberMemberDeclarationNode`, `CastExpressionUnaryNode`)
    - _Requirements: 2.1, 2.4, 3.3_

- [x] 2. Add pretty-printer support for cast operators and cast expressions
  - [x] 2.1 Add cast operator and cast expression formatting to `MistPrettyPrinter`
    - Add `CastOperatorMemberMemberDeclarationNode` case to `EmitMemberDeclaration`
    - Implement `EmitCastOperatorDeclaration` to format `{modifiers}implicit operator TargetType(SourceType param) { body }` or `{modifiers}explicit operator TargetType(SourceType param) { body }` with proper indentation
    - Handle body-less (semicolon-terminated) cast operator declarations consistent with existing `OperatorNoBody` formatting
    - Add `CastExpressionUnaryNode` case to `EmitUnary` to format `(TargetType)expression` with no space between closing parenthesis and operand
    - _Requirements: 4.1, 4.2, 4.3, 4.4, 4.5_

  - [x] 2.2 Write property test for cast operator and cast expression round-trip
    - **Property 1: Cast operator and cast expression round-trip**
    - Create `Test/Mist.Tests/Generators/MistCastOperatorGenerator.cs` with generators for random cast operator declarations (varying implicit/explicit kinds, target types, parameter types, block bodies, semicolon-terminated bodies) and cast expressions (varying target types and operand expressions)
    - Create `Test/Mist.Tests/Parsing/CastOperatorRoundTripPropertyTests.cs`
    - **Validates: Requirements 2.1, 2.2, 2.3, 2.4, 2.5, 3.1, 3.2, 3.3, 4.1, 4.2, 4.3, 4.4, 4.5**

- [x] 3. Register cast operator symbols in the semantic model
  - [x] 3.1 Implement cast operator registration in `SemanticModelBuilder` Pass 1
    - Add `CastOperatorMemberMemberDeclarationNode` case to `RegisterMemberDeclaration`
    - Implement `RegisterCastOperatorDeclaration` to create a `SymbolKind.Method` symbol with encoded name `operator_{kind}_{targetType}` (e.g., `operator_implicit_Weight`, `operator_explicit_string`)
    - Register the symbol in the enclosing type's member scope
    - Set `isIntrinsic` based on the `intrinsic` modifier, and `hasBody` based on the body variant
    - _Requirements: 5.1, 5.2, 5.3_

  - [x] 3.2 Implement `Pass1e_ValidateCastOperatorShape` validation sub-pass
    - Add new validation sub-pass alongside existing `Pass1e_ValidateOperatorShape`
    - Validate parameter count is exactly one; emit `MIST_SB_0040` if not
    - Validate `public static` modifiers are present; emit `MIST_SB_0041` if missing
    - Validate cast operator is declared inside a class or struct; emit `MIST_SB_0042` if not
    - Detect name-based duplicate cast operators (same kind + same target type name); emit `MIST_SB_0043`
    - _Requirements: 6.1, 6.3, 6.4, 6.5_

  - [x] 3.3 Write property test for cast operator symbol registration
    - **Property 2: Cast operator symbol registration**
    - Create `Test/Mist.Tests/Semantics/CastOperatorRegistrationPropertyTests.cs`
    - **Validates: Requirements 5.1, 5.2, 5.3, 5.4**

  - [x] 3.4 Write property test for cast operator parameter count validation
    - **Property 4: Cast operator parameter count validation**
    - Create `Test/Mist.Tests/Semantics/CastOperatorValidationPropertyTests.cs`
    - **Validates: Requirements 6.3, 6.4**

- [x] 4. Implement Pass 2 resolution for cast operators and cast expressions
  - [x] 4.1 Resolve cast operator declarations in Pass 2
    - Add `CastOperatorMemberMemberDeclarationNode` case to `ResolveMemberDeclaration`
    - Implement `ResolveCastOperatorDeclaration` to resolve the target type reference to an `IType` and set it as the symbol's `DeclaredType` (return type)
    - Register the single parameter as a `SymbolKind.Parameter` symbol within the cast operator's scope
    - Resolve the body block using the same logic as `ResolveOperatorDeclaration`
    - _Requirements: 5.1, 5.4_

  - [x] 4.2 Resolve cast expressions in Pass 2
    - Add `CastExpressionUnaryNode` case to `ResolveUnary`
    - Implement `ResolveCastExpression` to resolve the target type reference to an `IType`, resolve the operand expression, and record the target type as the expression type
    - _Requirements: 8.1, 8.2_

- [x] 5. Checkpoint — Ensure all tests pass
  - Run `dotnet test` and verify all existing and new tests pass. Ask the user if questions arise.

- [x] 6. Extend type checker for cast operator validation and resolution
  - [x] 6.1 Validate cast operator declarations in the type checker
    - Detect when the declaring node is a `CastOperatorDeclarationNode` in `CheckMethodBody`
    - Validate enclosing type constraint: either source parameter type or target type must be the enclosing type; emit `MIST_TC_0040` if violated
    - Validate duplicate cast operators after type resolution (same kind + same resolved source-target type pair); emit `MIST_TC_0041`
    - _Requirements: 6.2, 6.5, 6.6, 6.7_

  - [x] 6.2 Write property test for enclosing type constraint validation
    - **Property 3: Cast operator enclosing type constraint validation**
    - Create or extend `Test/Mist.Tests/Analysis/CastOperatorValidationPropertyTests.cs`
    - **Validates: Requirements 6.2, 13.3**

  - [x] 6.3 Extend `IsAssignable` with implicit cast operator fallback
    - Add `TryFindImplicitCast` helper that searches both source type and target type member scopes for `operator_implicit_{targetDisplayName}` symbols
    - Add `TryResolveImplicitCast` companion method that returns the resolved symbol (or null) for recording on the AST node
    - Add implicit cast operator search as the last fallback in `IsAssignable`, after identity, subtype, interface, trait, and numeric widening checks
    - At assignment/argument/return sites, call `TryResolveImplicitCast` to both check assignability and record the resolved operator via `RecordResolvedOperator`
    - Emit `MIST_TC_0044` when multiple implicit cast operators are applicable (ambiguous)
    - _Requirements: 7.1, 7.2, 7.3, 7.4, 7.5, 7.6, 7.7, 14.1, 14.2, 14.3_

  - [x] 6.4 Write property test for implicit cast resolution
    - **Property 5: Implicit cast resolution at assignment, argument, and return sites**
    - Create `Test/Mist.Tests/Analysis/ImplicitCastResolutionPropertyTests.cs`
    - **Validates: Requirements 7.1, 7.2, 7.3, 7.4, 7.5, 7.6**

  - [x] 6.5 Implement explicit cast expression validation in the type checker
    - Add `CastExpressionUnaryNode` case to `CheckUnaryNode`
    - Implement `CheckCastExpression`: resolve target type, get compile-time type of operand
    - If operand type is already assignable to target type (identity/subtype), accept the cast without requiring an operator
    - Search source type and target type members for `operator_explicit_{target}` or `operator_implicit_{target}` symbols
    - Do NOT search base types or derived types — only operators declared directly on the compile-time type and the target type
    - Record resolved operator via `RecordResolvedOperator` and set expression type to target type
    - Emit `MIST_TC_0042` when no cast operator is found
    - Emit `MIST_TC_0043` when multiple operators are applicable (ambiguous)
    - _Requirements: 8.1, 8.2, 8.3, 8.4, 8.5, 8.6, 8.7, 8.8, 8.9, 9.1, 9.2, 9.3, 9.4_

  - [x] 6.6 Write property test for explicit cast expression compile-time safety
    - **Property 6: Explicit cast expression compile-time safety**
    - Create `Test/Mist.Tests/Analysis/ExplicitCastSafetyPropertyTests.cs`
    - **Validates: Requirements 8.1, 8.2, 8.3, 8.4, 8.5, 8.6, 8.7, 8.8, 8.9, 9.1, 9.2, 9.3**

  - [x] 6.7 Write property test for direct assignability precedence
    - **Property 8: Direct assignability takes precedence over implicit casts**
    - Create `Test/Mist.Tests/Analysis/CastOperatorPriorityPropertyTests.cs`
    - **Validates: Requirements 14.1, 14.2, 14.3**

  - [x] 6.8 Write property test for cast operator and arithmetic operator coexistence
    - **Property 9: Cast operators coexist with arithmetic operators**
    - Create `Test/Mist.Tests/Analysis/CastOperatorCoexistencePropertyTests.cs`
    - **Validates: Requirements 14.4, 14.5**

- [x] 7. Checkpoint — Ensure all tests pass
  - Run `dotnet test` and verify all existing and new tests pass. Ask the user if questions arise.

- [x] 8. Extend C# emitter for cast operator code generation
  - [x] 8.1 Emit cast operator declarations
    - Add `CastOperatorMemberMemberDeclarationNode` case to `EmitMemberDeclaration`
    - Implement `EmitCastOperatorDeclaration` to emit `public static implicit operator TargetType(SourceType param) { body }` or `public static explicit operator TargetType(SourceType param) { body }`
    - Handle intrinsic cast operators by looking up emission templates from `IntrinsicEmissionRegistry`
    - _Requirements: 10.1, 10.2, 10.3, 10.4_

  - [x] 8.2 Emit cast expressions
    - Add `CastExpressionUnaryNode` case to `EmitUnary`
    - Implement `EmitCastExpression`: for user-defined cast operators, emit as static method call (`TypeName.op_Implicit(expr)` or `TypeName.op_Explicit(expr)`); for intrinsic operators, look up emission template; for identity/subtype casts (no resolved operator), emit standard C# cast `((TargetType)expr)`
    - _Requirements: 11.1, 11.2, 11.3, 11.4_

  - [x] 8.3 Emit implicit casts at assignment, argument, and return sites
    - In `EmitAssignment`, check for a resolved implicit cast operator on the assignment node; if present, wrap the RHS in a static method call (`TypeName.op_Implicit(expr)`)
    - In `EmitArgumentList`, check each argument for a resolved implicit cast operator; if present, wrap the argument
    - In `EmitReturnStatement`, check for a resolved implicit cast operator on the return node; if present, wrap the return expression
    - _Requirements: 12.1, 12.2, 12.3_

  - [x] 8.4 Add `TryGetCastOperator` to `IntrinsicEmissionRegistry`
    - Add `TryGetCastOperator(string declaringType, string targetType)` method to `IIntrinsicEmissionRegistry`
    - Implement lookup by key `"{declaringType}.operator_implicit_{targetType}"` or `"{declaringType}.operator_explicit_{targetType}"`
    - Initially no intrinsic cast operators are registered — the stdlib can add numeric conversions in a future iteration
    - _Requirements: 10.4, 11.2_

  - [x] 8.5 Write property test for cast operator C# emission correctness
    - **Property 7: Cast operator C# emission correctness**
    - Create `Test/Mist.Tests/Backend/CastOperatorEmissionPropertyTests.cs`
    - **Validates: Requirements 10.1, 10.2, 10.3, 11.1, 11.3, 11.4, 12.1, 12.2, 12.3**

- [x] 9. Checkpoint — Ensure all tests pass
  - Run `dotnet test` and verify all existing and new tests pass. Ask the user if questions arise.

- [x] 10. Update syntax highlighting and documentation
  - [x] 10.1 Update TextMate grammar for `implicit` and `explicit` keywords
    - Add `implicit` and `explicit` to the `storage.modifier.mist` pattern in `Editor/mist-syntax-highlighting/syntaxes/mist.tmLanguage.json` alongside `public`, `private`, `static`, and `intrinsic`
    - _Requirements: 15.1, 15.2_

  - [x] 10.2 Update `CSHARP_FEATURES.md`
    - Update the `Implicit/explicit conversion operators` entry in the `Other Members` section from 🔲 to ✅
    - Update the `Cast expressions ((T)expr)` entry in the `Other Expressions` section from 🔲 to ✅
    - Update the `Explicit cast validation` entry in the `Type Checking & Diagnostics` section from 🔲 to ✅
    - Update the `Summary` table at the bottom to reflect the new implementation counts
    - _Requirements: 16.1, 16.2, 16.3, 16.4_

- [x] 11. Create cast operator sample application
  - [x] 11.1 Create `samples/CastOperators/` with Mist source files
    - Demonstrate declaring an implicit cast operator on a type
    - Demonstrate declaring an explicit cast operator on a type
    - Demonstrate an implicit cast being applied automatically during assignment
    - Demonstrate an explicit cast using `(TargetType)expression` syntax
    - Demonstrate the compile-time safety feature with a commented-out example showing that casting through a base-typed variable produces a compile error
    - Include a `static void Main` entry point that exercises all demonstrated cast operator features and prints observable output via `Console.WriteLine`
    - _Requirements: 17.1, 17.2, 17.3, 17.4, 17.5, 17.6, 17.7, 17.8_

- [x] 12. Final checkpoint — Ensure all tests pass
  - Run `dotnet test` and verify all existing and new tests pass. Ask the user if questions arise.

## Notes

- Tasks marked with `*` are optional and can be skipped for faster MVP
- Each task references specific requirements for traceability
- Checkpoints ensure incremental validation
- Property tests validate universal correctness properties from the design document
- Unit tests validate specific examples and edge cases
- The implementation language is C#, matching the existing codebase and design document
- The Storm source generator auto-generates AST node types from grammar changes — a successful build after task 1 confirms the new nodes are available
- Cast operators reuse the existing `OperatorBody` grammar rule to avoid duplicating body handling logic
- Cast operator symbols use encoded names (`operator_implicit_Weight`, `operator_explicit_string`) for O(1) lookup, mirroring the existing arithmetic operator naming convention (`operator+`, `operator-`)
- The `TryResolveImplicitCast` companion method enables both assignability checking and resolved operator recording in a single pass
- Implicit cast operators are the lowest-priority assignability check, ensuring they never shadow inheritance, interface, trait, or numeric widening relationships
- The `IntrinsicEmissionRegistry` extension is forward-looking — no intrinsic cast operators are registered initially, but the infrastructure supports stdlib numeric conversions in a future iteration
