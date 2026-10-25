# Requirements Document

## Introduction

Mist currently supports decimal integer literals (`42`) and hexadecimal integer literals (`0xFF`), but lacks binary literals and digit separators. C# 7 introduced both binary literals with the `0b` prefix (e.g., `0b1010`) and digit separators using underscores (e.g., `1_000_000`, `0xFF_FF`, `0b1111_0000`), allowing developers to express bit patterns and large numeric values more clearly in source code. This feature adds binary literal support and digit separator support to Mist, following C# semantics for both. Binary literals use the `0b`/`0B` prefix convention. Digit separators allow underscores between digits in all numeric literal types (integer, hex, binary, and float), but not at the start or end of a digit sequence, not adjacent to a prefix (`0x`, `0b`), suffix, or decimal point. Binary literals flow through the same pipeline as hex literals: the grammar defines a new token, the parser produces a dedicated AST node, the semantic model resolves the type using integer suffix rules, the pretty printer reproduces the literal verbatim, and the C# backend emits the literal unchanged (since C# natively supports both features).

## Glossary

- **Binary_Literal**: An integer literal written with a `0b` or `0B` prefix followed by one or more binary digits (`0` or `1`), optionally separated by underscores, with an optional integer suffix (e.g., `0b1010`, `0B1111_0000L`). Follows the same suffix rules as decimal and hex integer literals.
- **Digit_Separator**: An underscore character (`_`) used between digits within a numeric literal to improve readability (e.g., `1_000_000`, `0xFF_FF`, `0b1111_0000`, `3.14_15`). Follows C# 7 semantics: underscores are allowed only between digits, not at the start or end of a digit sequence, not adjacent to a literal prefix (`0x`, `0b`/`0B`), not adjacent to a type suffix, and not adjacent to a decimal point.
- **Grammar**: The Storm grammar definition (`Mist.storm`) that defines the Mist language syntax, including token definitions and parser rules.
- **Lexer**: The Storm-generated lexer that tokenizes Mist source text into a token stream. Wrapping is handled by `MistLexer`.
- **Parser**: The Storm-generated parser that produces the AST from a token stream. Wrapping is handled by `MistParser`.
- **Pretty_Printer**: The `MistPrettyPrinter` that formats a Mist AST back into syntactically valid Mist source text.
- **Semantic_Model_Builder**: The `SemanticModelBuilder` that walks ASTs to build symbols, scopes, types, and expression type mappings.
- **Type_Checker**: The `TypeCheckerAnalyzer` that validates type correctness across the program.
- **CSharp_Emitter**: The `CSharpSourceWriter` that walks the AST and emits C# source files.
- **INT_SUFFIX**: The existing grammar fragment that matches integer type suffixes: `l`, `L`, `u`, `U`, `UL`, `ul`, `Ul`, `uL`, `Lu`, `lU`, `LU`, `lu`.
- **Syntax_Highlighting_Grammar**: The TextMate grammar (`mist.tmLanguage.json`) used for editor syntax highlighting.

## Requirements

### Requirement 1: Grammar — Binary Literal Token

**User Story:** As a Mist developer, I want to write binary literals with a `0b` prefix and optional digit separators, so that I can express bit patterns directly and readably in my source code.

#### Acceptance Criteria

1. THE Grammar SHALL define a `BINARY_LITERAL` token that matches `0` followed by `b` or `B`, followed by one or more binary digits (`0` or `1`) optionally separated by underscores, followed by an optional INT_SUFFIX.
2. THE Grammar SHALL declare the `BINARY_LITERAL` token before the `INTEGER_LITERAL` token so that `0b1010` matches as a binary literal rather than as `0` followed by `b1010`.
3. WHEN the Lexer encounters a `0b` or `0B` prefix followed by binary digits (optionally separated by underscores), THE Lexer SHALL produce a single `BINARY_LITERAL` token containing the entire literal including prefix, underscores, and optional suffix.
4. IF the Lexer encounters `0b` or `0B` followed by no binary digits, THEN THE Lexer SHALL report a lexer error diagnostic.
5. THE Grammar SHALL allow underscores only between binary digits in a `BINARY_LITERAL`, not immediately after the `0b`/`0B` prefix, not before a suffix, and not as consecutive-only characters with no digits following (e.g., `0b_1010`, `0b1010_`, and `0b1010_L` are invalid; `0b1010_0000` is valid).

### Requirement 2: Grammar — Binary Literal Expression in Parser

**User Story:** As a Mist developer, I want binary literals to be usable in any expression position, so that I can use them the same way as integer and hex literals.

#### Acceptance Criteria

1. THE Grammar SHALL define a `BinaryLiteralExpression` alternative in the `Primary` rule that accepts a `BINARY_LITERAL` token.
2. WHEN the Parser encounters a `BINARY_LITERAL` token in expression position, THE Parser SHALL produce a `BinaryLiteralExpression` AST node containing the parsed token.
3. WHEN a binary literal is used as a variable initializer (e.g., `int mask = 0b1010;`), THE Parser SHALL parse the binary literal as a `BinaryLiteralExpression` in the initializer position.
4. WHEN a binary literal is used as a method argument (e.g., `foo(0b1010)`), THE Parser SHALL parse the binary literal as a `BinaryLiteralExpression` within the argument list.

### Requirement 3: Semantic Model — Binary Literal Type Resolution

**User Story:** As a Mist developer, I want binary literals to have the correct numeric type based on their suffix, so that the type checker validates my code correctly.

#### Acceptance Criteria

1. WHEN the Semantic_Model_Builder encounters a `BinaryLiteralExpression` AST node with no suffix, THE Semantic_Model_Builder SHALL resolve the expression type as `int`.
2. WHEN the Semantic_Model_Builder encounters a `BinaryLiteralExpression` AST node with an `L` or `l` suffix, THE Semantic_Model_Builder SHALL resolve the expression type as `long`.
3. WHEN the Semantic_Model_Builder encounters a `BinaryLiteralExpression` AST node with a `U` or `u` suffix, THE Semantic_Model_Builder SHALL resolve the expression type as `uint`.
4. WHEN the Semantic_Model_Builder encounters a `BinaryLiteralExpression` AST node with a `UL`, `ul`, `Lu`, or equivalent suffix, THE Semantic_Model_Builder SHALL resolve the expression type as `ulong`.
5. THE Semantic_Model_Builder SHALL use the same `ResolveIntegerLiteralType` method for binary literals as for decimal and hex literals, ensuring consistent suffix handling.

### Requirement 4: C# Backend — Binary Literal Emission

**User Story:** As a Mist developer, I want binary literals to compile to valid C# code, so that my Mist programs run correctly on .NET.

#### Acceptance Criteria

1. WHEN the CSharp_Emitter encounters a `BinaryLiteralExpression` AST node, THE CSharp_Emitter SHALL emit the binary literal lexeme verbatim (e.g., `0b1010` emits as `0b1010`).
2. WHEN the CSharp_Emitter encounters a `BinaryLiteralExpression` with a suffix (e.g., `0b1010L`), THE CSharp_Emitter SHALL emit the full lexeme including the suffix.

### Requirement 5: Pretty Printer — Binary Literal Round-Trip

**User Story:** As a Mist tooling developer, I want the pretty printer to faithfully reproduce binary literals including digit separators, so that parse-print-parse round-trips are idempotent.

#### Acceptance Criteria

1. WHEN the Pretty_Printer encounters a `BinaryLiteralExpression` AST node, THE Pretty_Printer SHALL emit the binary literal lexeme verbatim, preserving the original prefix case (`0b` or `0B`), digit separators, and suffix.
2. FOR ALL valid Mist source texts containing binary literal expressions, parsing then printing then parsing SHALL produce an equivalent AST (round-trip property).

### Requirement 6: Syntax Highlighting — Binary Literals and Digit Separators

**User Story:** As a Mist developer using an editor, I want binary literals and digit separators in all numeric literals to be highlighted as numeric constants, so that they are visually consistent with existing literal highlighting.

#### Acceptance Criteria

1. THE Syntax_Highlighting_Grammar SHALL recognize binary literals (`0b` or `0B` followed by binary digits optionally separated by underscores, with optional suffix) and apply the `constant.numeric.binary.mist` scope.
2. THE Syntax_Highlighting_Grammar SHALL declare the binary literal pattern before the integer literal pattern so that `0b1010` is highlighted as a binary literal rather than as an integer.
3. THE Syntax_Highlighting_Grammar SHALL update the hex literal pattern to recognize underscores between hex digits (e.g., `0xFF_FF`) and continue applying the `constant.numeric.hex.mist` scope.
4. THE Syntax_Highlighting_Grammar SHALL update the integer literal pattern to recognize underscores between decimal digits (e.g., `1_000_000`) and continue applying the `constant.numeric.integer.mist` scope.
5. THE Syntax_Highlighting_Grammar SHALL update the float literal pattern to recognize underscores between decimal digits in both the integer and fractional parts (e.g., `1_000.50_25f`) and continue applying the `constant.numeric.float.mist` scope.

### Requirement 7: Feature Tracking — CSHARP_FEATURES.md Update

**User Story:** As a project maintainer, I want the feature tracking document to reflect that binary literals and digit separators are implemented, so that the project status is accurate.

#### Acceptance Criteria

1. WHEN binary literals are fully implemented, THE `CSHARP_FEATURES.md` document SHALL update the "Binary literals (`0b1010`)" row from 🔲 to ✅.
2. WHEN digit separators are fully implemented, THE `CSHARP_FEATURES.md` document SHALL update the "Digit separators (`1_000_000`)" row from 🔲 to ✅.

### Requirement 8: Grammar — Digit Separators in Numeric Literals

**User Story:** As a Mist developer, I want to use underscores as digit separators in all numeric literal types, so that I can write large or complex numbers more readably (e.g., `1_000_000`, `0xFF_FF_FF`, `3.14_15f`).

#### Acceptance Criteria

1. THE Grammar SHALL update the `INTEGER_LITERAL` token to allow underscores between decimal digits (e.g., `1_000_000` matches as a single `INTEGER_LITERAL` token).
2. THE Grammar SHALL update the `HEX_LITERAL` token to allow underscores between hex digits (e.g., `0xFF_FF` matches as a single `HEX_LITERAL` token).
3. THE Grammar SHALL update the `FLOAT_LITERAL` token to allow underscores between decimal digits in both the integer part and the fractional part (e.g., `1_000.50_25f` matches as a single `FLOAT_LITERAL` token).
4. THE Grammar SHALL prohibit underscores immediately after the `0x`/`0X` prefix in hex literals (e.g., `0x_FF` is invalid).
5. THE Grammar SHALL prohibit underscores immediately before a type suffix in all numeric literals (e.g., `100_L`, `0xFF_UL`, `1.0_f` are invalid).
6. THE Grammar SHALL prohibit underscores immediately adjacent to the decimal point in float literals (e.g., `1_.0` and `1._0` are invalid).
7. THE Grammar SHALL prohibit underscores at the start or end of a digit sequence in all numeric literals (e.g., `_100` is an identifier, `100_` is invalid).
8. THE Grammar SHALL prohibit consecutive underscores with no intervening digit in all numeric literals (e.g., `1__000` is invalid).
9. WHEN the Lexer encounters a numeric literal with valid digit separators, THE Lexer SHALL produce the appropriate token type (`INTEGER_LITERAL`, `HEX_LITERAL`, `FLOAT_LITERAL`, or `BINARY_LITERAL`) containing the full lexeme including underscores.
10. THE Semantic_Model_Builder SHALL interpret numeric literals with digit separators identically to the same literal without separators (e.g., `1_000` has the same value and type as `1000`).
11. THE CSharp_Emitter SHALL emit numeric literals with digit separators verbatim, preserving the underscores in the C# output.
12. THE Pretty_Printer SHALL emit numeric literals with digit separators verbatim, preserving the underscores in the output.
13. FOR ALL valid Mist source texts containing numeric literals with digit separators, parsing then printing then parsing SHALL produce an equivalent AST (round-trip property).
