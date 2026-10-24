# Implementation Plan: Binary Literals and Digit Separators

## Overview

This plan implements binary literal support (`0b1010`) and digit separator support (`1_000_000`) for the Mist compiler, following C# 7 semantics. The implementation proceeds grammar-first (since the lexer and parser are Storm-generated), then hand-written code changes (semantic model, C# emitter, pretty printer), then syntax highlighting, then tests, then documentation. Each task builds incrementally on the previous one so there is no orphaned code.

## Tasks

- [x] 1. Update Storm grammar with binary literal token and digit separator support
  - [x] 1.1 Add `BINARY_DIGIT` fragment and `BINARY_LITERAL` token to `Source/Mist/Grammar/Mist.storm`
    - Add `fragment BINARY_DIGIT = \`[01]\`;` in the Fragments section after `ESCAPE_SEQUENCE`
    - Add `token BINARY_LITERAL = "0" \`[bB]\` BINARY_DIGIT ('_' BINARY_DIGIT)* INT_SUFFIX?;` in the Literal Tokens section, declared before `INTEGER_LITERAL` (after `HEX_LITERAL`)
    - _Requirements: 1.1, 1.2, 1.5_

  - [x] 1.2 Update existing numeric literal tokens with digit separator support
    - Update `HEX_LITERAL` to: `"0" \`[xX]\` HEX_DIGIT ('_' HEX_DIGIT)* INT_SUFFIX?;`
    - Update `INTEGER_LITERAL` to: `\`[0-9]\` ('_' \`[0-9]\`)* INT_SUFFIX?;`
    - Update `FLOAT_LITERAL` to: `\`[0-9]\` ('_' \`[0-9]\`)* "." \`[0-9]\` ('_' \`[0-9]\`)* FLOAT_SUFFIX? | \`[0-9]\` ('_' \`[0-9]\`)* FLOAT_SUFFIX;`
    - _Requirements: 8.1, 8.2, 8.3, 8.4, 8.5, 8.6, 8.7, 8.8_

  - [x] 1.3 Add `BinaryLiteralExpression` alternative to the `Primary` parser rule
    - Add `| binaryLiteral:BINARY_LITERAL as BinaryLiteralExpression` after `HexLiteralExpression` and before `FloatLiteralExpression` in the `Primary` rule
    - _Requirements: 2.1, 2.2, 2.3, 2.4_

- [x] 2. Checkpoint — Build the solution and verify the Storm source generator produces the new AST node
  - Run `dotnet build` on the solution and ensure zero warnings
  - Verify that `BinaryLiteralExpressionPrimaryNode` is generated with a `BinaryLiteral` token property
  - Ensure all tests pass, ask the user if questions arise.

- [x] 3. Add binary literal support to hand-written compiler pipeline code
  - [x] 3.1 Add `BinaryLiteralExpressionPrimaryNode` case to `SemanticModelBuilder.cs`
    - In the primary expression type resolution switch (around line 7052), add a new case delegating to `ResolveIntegerLiteralType(binLit.BinaryLiteral.Lexeme, fileScope, symbolTable)`, following the existing `HexLiteralExpressionPrimaryNode` pattern
    - _Requirements: 3.1, 3.2, 3.3, 3.4, 3.5, 8.10_

  - [x] 3.2 Add `BinaryLiteralExpressionPrimaryNode` case to `CSharpEmitter.cs`
    - In the `EmitPrimary` switch (around line 2331), add a new case returning `binLit.BinaryLiteral.Lexeme`, following the existing `HexLiteralExpressionPrimaryNode` pattern
    - _Requirements: 4.1, 4.2, 8.11_

  - [x] 3.3 Add `BinaryLiteralExpressionPrimaryNode` case to `MistPrettyPrinter.cs`
    - In the `EmitPrimary` switch (around line 1161), add a new case returning `binLit.BinaryLiteral.Lexeme`, following the existing `HexLiteralExpressionPrimaryNode` pattern
    - _Requirements: 5.1, 5.2, 8.12, 8.13_

- [x] 4. Checkpoint — Build and verify zero warnings with all pipeline changes
  - Run `dotnet build` on the solution and ensure zero warnings
  - Ensure all existing tests pass, ask the user if questions arise.

- [x] 5. Update syntax highlighting for binary literals and digit separators
  - [x] 5.1 Add binary literal pattern and update numeric patterns in `Editor/mist-syntax-highlighting/syntaxes/mist.tmLanguage.json`
    - Add new binary literal pattern with scope `constant.numeric.binary.mist` and match `\\b0[bB][01](?:_?[01])*([lLuU]|UL|ul|Ul|uL|Lu|lU|LU|lu)?\\b` as the first entry in the `literals.patterns` array (before hex)
    - Update hex pattern to `\\b0[xX][0-9a-fA-F](?:_?[0-9a-fA-F])*([lLuU]|UL|ul|Ul|uL|Lu|lU|LU|lu)?\\b`
    - Update float pattern to `\\b[0-9](?:_?[0-9])*\\.[0-9](?:_?[0-9])*[fFdDmM]?\\b|\\b[0-9](?:_?[0-9])*[fFdDmM]\\b`
    - Update integer pattern to `\\b[0-9](?:_?[0-9])*([lLuU]|UL|ul|Ul|uL|Lu|lU|LU|lu)?\\b`
    - Ensure pattern ordering: binary → hex → float → integer → boolean
    - _Requirements: 6.1, 6.2, 6.3, 6.4, 6.5_

- [x] 6. Write lexer unit tests and property tests for binary literals and digit separators
  - [x] 6.1 Write unit tests for binary literal and digit separator tokenization
    - Create `Test/Mist.Tests/Lexing/BinaryLiteralTokenTests.cs`
    - Test that `0b1010` lexes as a single `BINARY_LITERAL` token (not `INTEGER_LITERAL`) — Req 1.2
    - Test binary literals with both `0b` and `0B` prefixes — Req 1.1
    - Test binary literals with suffixes (`0b1010L`, `0b1010u`, `0b1010UL`) — Req 1.1
    - Test binary literals with digit separators (`0b1111_0000`) — Req 1.3, 1.5
    - Test integer literals with digit separators (`1_000_000`) — Req 8.1
    - Test hex literals with digit separators (`0xFF_FF`) — Req 8.2
    - Test float literals with digit separators (`1_000.50_25f`) — Req 8.3
    - Test invalid separator placement produces multiple tokens (not a single literal): `0b_1010`, `0x_FF`, `1__000`, `100_L`, `1_.0`, `1._0` — Req 1.4, 8.4, 8.5, 8.6, 8.7, 8.8
    - Test that `0b` alone (no digits) does not produce a `BINARY_LITERAL` token — Req 1.4
    - _Requirements: 1.1, 1.2, 1.3, 1.4, 1.5, 8.1, 8.2, 8.3, 8.4, 8.5, 8.6, 8.7, 8.8, 8.9_

  - [x] 6.2 Write property test for binary literal tokenization (Property 1)
    - Create `Test/Mist.Tests/Lexing/BinaryLiteralTokenPropertyTests.cs`
    - **Property 1: Valid binary literals tokenize as BINARY_LITERAL**
    - Generate random valid binary literal strings (0b/0B prefix, one or more binary digits with optional single underscores between digits, optional INT_SUFFIX)
    - Lex via `MistLexer.Lex()` in-process, verify single `BINARY_LITERAL` token with matching lexeme
    - No process spawning — use in-process lexer directly
    - **Validates: Requirements 1.1, 1.3, 1.5**

  - [x] 6.3 Write property test for digit separator tokenization (Property 2)
    - Create `Test/Mist.Tests/Lexing/DigitSeparatorTokenPropertyTests.cs`
    - **Property 2: Valid numeric literals with digit separators tokenize correctly**
    - Generate random valid numeric literals with separators for all four types (integer, hex, float, binary)
    - Lex via `MistLexer.Lex()` in-process, verify correct token kind and matching lexeme
    - No process spawning — use in-process lexer directly
    - **Validates: Requirements 8.1, 8.2, 8.3, 8.9**

- [x] 7. Write semantic model and emitter tests
  - [x] 7.1 Write property test for binary literal type resolution (Property 3)
    - Create `Test/Mist.Tests/Semantics/BinaryLiteralTypeResolutionPropertyTests.cs`
    - **Property 3: Binary literal type resolution follows suffix rules**
    - Generate binary literals with all valid suffix combinations, build a Mist program `class C { static void Main() { var x = <literal>; } }`, compile in-process via `CompilerDriver.Compile()`, inspect the semantic model's expression type map
    - Verify: no suffix → `int`, L/l → `long`, U/u → `uint`, UL-variants → `ulong`
    - No process spawning — compile in-process only
    - **Validates: Requirements 3.1, 3.2, 3.3, 3.4, 3.5**

  - [x] 7.2 Write property test for digit separator type invariance (Property 4)
    - Create `Test/Mist.Tests/Semantics/DigitSeparatorTypeInvariancePropertyTests.cs`
    - **Property 4: Digit separators do not affect type resolution**
    - Generate numeric literals with and without separators (same digits, same suffix), compile both in-process, compare resolved types
    - No process spawning — compile in-process only
    - **Validates: Requirements 8.10**

  - [x] 7.3 Write unit tests for binary literal expression contexts
    - Create `Test/Mist.Tests/Semantics/BinaryLiteralExpressionTests.cs`
    - Test binary literal as variable initializer: `int mask = 0b1010;` compiles without diagnostics — Req 2.3
    - Test binary literal as method argument: `foo(0b1010)` compiles without diagnostics — Req 2.4
    - Test binary literal in arithmetic expression: `0b1010 + 0b0101` compiles without diagnostics
    - Use in-process `CompilerDriver.Compile()` — no process spawning
    - _Requirements: 2.3, 2.4, 3.1_

- [x] 8. Checkpoint — Run all tests and verify zero failures
  - Run `dotnet test` on the full test suite
  - Ensure all tests pass, ask the user if questions arise.

- [x] 9. Write pretty printer and emitter round-trip tests
  - [x] 9.1 Write property test for pretty printer round-trip (Property 5)
    - Create `Test/Mist.Tests/Parsing/BinaryLiteralRoundTripPropertyTests.cs`
    - **Property 5: Pretty printer round-trip for numeric literals**
    - Generate Mist source strings containing numeric literals (binary, hex, integer, float, with/without separators), parse → print → parse → print, compare both print outputs
    - Use in-process `MistLexer`/`MistParser`/`MistPrettyPrinter` — no process spawning
    - **Validates: Requirements 5.2, 8.13**

  - [x] 9.2 Write property test for verbatim emission (Property 6)
    - Create `Test/Mist.Tests/Backend/BinaryLiteralVerbatimEmissionPropertyTests.cs`
    - **Property 6: Verbatim emission of numeric literals**
    - Generate Mist programs with numeric literals (binary, hex, integer, float, with/without separators and suffixes), compile in-process, verify the C# emitter output contains the literal lexeme unchanged, and verify the pretty printer output contains the literal lexeme unchanged
    - Use in-process `CompilerDriver.Compile()` and `MistPrettyPrinter.Print()` — no process spawning
    - **Validates: Requirements 4.1, 4.2, 5.1, 8.11, 8.12**

- [x] 10. Update feature tracking documentation
  - [x] 10.1 Update `CSHARP_FEATURES.md` with binary literals and digit separators
    - Change `Binary literals (\`0b1010\`)` row from 🔲 to ✅, add note "C# 7; with digit separators"
    - Change `Digit separators (\`1_000_000\`)` row from 🔲 to ✅, add note "C# 7; in all numeric literal types"
    - _Requirements: 7.1, 7.2_

- [x] 11. Final checkpoint — Ensure all tests pass
  - Run `dotnet test` on the full test suite
  - Ensure all tests pass, ask the user if questions arise.

## Notes

- Tasks marked with `*` are optional and can be skipped for faster MVP
- Each task references specific requirements for traceability
- Checkpoints ensure incremental validation
- Property tests validate universal correctness properties from the design document
- All tests are in-process only (no process spawning) as specified in the design — lexer tests use `MistLexer.Lex()`, semantic tests use `CompilerDriver.Compile()`, pretty printer tests use `MistPrettyPrinter.Print()`
- Grammar changes come first because the Storm source generator must produce `BinaryLiteralExpressionPrimaryNode` before hand-written code can reference it
- The grammar enforces digit separator placement rules at the token level — no separate validation pass needed
