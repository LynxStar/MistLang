# Requirements Document

## Introduction

This document specifies the requirements for adding array literal syntax to the Mist programming language. Currently, Mist supports array creation only via `new T[size]` (which allocates a fixed-size array with default values) and element-by-element assignment. This feature introduces a bracket-delimited literal syntax (`[expr, expr, ...]`) that allows creating arrays pre-populated with values inline.

The design must carefully disambiguate array literals from two existing uses of `[...]` in Mist: the postfix index suffix (`arr[i]`) and list patterns in pattern matching (`[> 0, ..]`).

## Glossary

- **Array_Literal**: A bracket-delimited expression `[expr, expr, ...]` that creates a new array pre-populated with the specified element values.
- **Parser**: The Storm-generated recursive descent parser that produces the Mist AST from a token stream.
- **Type_Checker**: The semantic analysis pass that validates types, infers types for expressions, and reports type errors.
- **CSharp_Emitter**: The backend that transpiles the Mist AST into equivalent C# source code.
- **Index_Suffix**: The existing postfix syntax `target[expr]` used for array element access.
- **List_Pattern**: The existing pattern syntax `[pattern, pattern, ..]` used in `is` expressions and switch arms.
- **Element_Type**: The type of individual elements within an array literal.
- **TextMate_Grammar**: The JSON-based grammar used for syntax highlighting in the VS Code extension.

## Requirements

### Requirement 1: Parse Non-Empty Array Literals

**User Story:** As a Mist developer, I want to write array literals like `[1, 2, 3]`, so that I can create pre-populated arrays without element-by-element assignment.

#### Acceptance Criteria

1. WHEN the Parser encounters a `LEFT_BRACKET` followed by a comma-separated list of one or more expressions and a `RIGHT_BRACKET` in expression position, THE Parser SHALL produce an ArrayLiteralExpression AST node containing the list of element expressions.
2. WHEN the Parser encounters a trailing comma after the last element (e.g. `[1, 2, 3,]`), THE Parser SHALL accept the syntax and produce the same ArrayLiteralExpression node as without the trailing comma.
3. WHEN the Parser encounters nested array literals (e.g. `[[1, 2], [3, 4]]`), THE Parser SHALL produce an ArrayLiteralExpression whose elements are themselves ArrayLiteralExpression nodes.

### Requirement 2: Parse Empty Array Literals

**User Story:** As a Mist developer, I want to write empty array literals using `[]`, so that I can create zero-length arrays concisely.

#### Acceptance Criteria

1. WHEN the Parser encounters `[]` (LEFT_BRACKET immediately followed by RIGHT_BRACKET) in expression position, THE Parser SHALL produce an EmptyArrayLiteralExpression AST node.
2. WHEN an EmptyArrayLiteralExpression is assigned to a variable with an explicit array type annotation (e.g. `int[] e = [];`), THE Semantic_Model_Builder SHALL infer the element type from the target type.
3. WHEN an EmptyArrayLiteralExpression has no target type context (e.g. `var foo = [];`), THE Type_Checker SHALL report a diagnostic error indicating that empty array literals require an explicit element type annotation.
4. WHEN the Parser encounters `type[]` in expression position (e.g. `int[]`), THE Parser SHALL produce a TypeLiteralExpression (referring to the type itself), NOT an empty array literal.

### Requirement 3: Disambiguate Array Literals from Index Suffix

**User Story:** As a Mist developer, I want array literals and index access to coexist without ambiguity, so that the compiler always interprets my code correctly.

#### Acceptance Criteria

1. WHEN a `LEFT_BRACKET` appears as a postfix suffix after a primary expression (e.g. `arr[0]`, `matrix[i]`), THE Parser SHALL parse it as an IndexSuffix, not as an array literal.
2. WHEN a `LEFT_BRACKET` appears at the start of an expression or after an operator where a new expression is expected, THE Parser SHALL parse it as an array literal.
3. THE Parser SHALL resolve the ambiguity by treating `[...]` as an IndexSuffix only when it immediately follows a primary expression that can be indexed, and as an ArrayLiteralExpression in all other expression-start positions.

### Requirement 4: Disambiguate Array Literals from List Patterns

**User Story:** As a Mist developer, I want array literals in expressions and list patterns in pattern matching to coexist, so that the compiler correctly distinguishes between the two contexts.

#### Acceptance Criteria

1. WHILE parsing a pattern context (after `is`, in switch arms), THE Parser SHALL interpret `[...]` as a ListPattern.
2. WHILE parsing an expression context (variable initializers, arguments, return values), THE Parser SHALL interpret `[...]` as an ArrayLiteralExpression.
3. THE Parser SHALL never produce an ArrayLiteralExpression node inside a pattern context, nor a ListPattern node inside an expression context.

### Requirement 5: Infer Element Type for Array Literals

**User Story:** As a Mist developer, I want the compiler to infer the element type of array literals from their contents, so that I can use `var` without redundant type annotations.

#### Acceptance Criteria

1. WHEN all elements in an array literal have the same type, THE Type_Checker SHALL infer the array type as that element type followed by `[]` (e.g. `[1, 2, 3]` infers as `int[]`).
2. WHEN elements in an array literal have differing types that share no common type, THE Type_Checker SHALL report a diagnostic error indicating that the element types are incompatible.
3. WHEN an array literal is assigned to a variable with an explicit array type annotation (e.g. `int[] x = [1, 2, 3]`), THE Type_Checker SHALL validate that each element is assignable to the declared element type.
4. WHEN an empty array literal `[]` is assigned to a variable with an explicit array type annotation (e.g. `int[] e = [];`), THE Type_Checker SHALL infer the array type from the declared element type.
5. WHEN an empty array literal `[]` has no target type context (e.g. `var foo = [];`), THE Type_Checker SHALL report a diagnostic error indicating that empty array literals require an explicit element type annotation.

### Requirement 6: Type-Check Array Literal Elements

**User Story:** As a Mist developer, I want the compiler to validate each element in an array literal, so that type errors are caught at compile time.

#### Acceptance Criteria

1. THE Type_Checker SHALL recursively type-check each element expression within an array literal.
2. IF an element expression contains a type error, THEN THE Type_Checker SHALL report the error with a location pointing to the offending element.
3. IF an array literal contains elements of type `void`, THEN THE Type_Checker SHALL report a diagnostic error indicating that void is not permitted as an array element type.
4. IF an array literal contains elements of type `unknown`, THEN THE Type_Checker SHALL report a diagnostic error indicating that unknown is not permitted in array element storage.

### Requirement 7: Emit C# for Non-Empty Array Literals

**User Story:** As a Mist developer, I want array literals to compile to valid C# code, so that the transpiled output runs correctly on .NET.

#### Acceptance Criteria

1. WHEN the CSharp_Emitter encounters a non-empty ArrayLiteralExpression, THE CSharp_Emitter SHALL emit `new[] { elem1, elem2, ... }` where each element is the emitted form of the corresponding element expression.
2. WHEN the CSharp_Emitter encounters a non-empty ArrayLiteralExpression assigned to a variable with an explicit array type, THE CSharp_Emitter SHALL emit `new type[] { elem1, elem2, ... }` using the declared element type.
3. WHEN the CSharp_Emitter encounters nested array literals, THE CSharp_Emitter SHALL emit nested `new[] { ... }` expressions.

### Requirement 8: Emit C# for Empty Array Literals

**User Story:** As a Mist developer, I want empty array literals to compile to valid C# code, so that zero-length arrays work correctly.

#### Acceptance Criteria

1. WHEN the CSharp_Emitter encounters an EmptyArrayLiteralExpression with a known target element type T (from variable annotation), THE CSharp_Emitter SHALL emit `Array.Empty<T>()`.

### Requirement 9: Pretty-Print Array Literals

**User Story:** As a Mist developer, I want the pretty printer to faithfully reproduce array literal syntax, so that round-trip formatting preserves my code.

#### Acceptance Criteria

1. WHEN the Pretty_Printer encounters an ArrayLiteralExpression, THE Pretty_Printer SHALL emit `[elem1, elem2, ...]` with each element separated by a comma and a space.
2. WHEN the Pretty_Printer encounters an EmptyArrayLiteralExpression, THE Pretty_Printer SHALL emit `[]`.
3. FOR ALL valid array literal expressions, parsing then pretty-printing then parsing SHALL produce an equivalent AST (round-trip property).

### Requirement 10: Syntax Highlighting for Array Literals

**User Story:** As a Mist developer, I want array literals to be highlighted correctly in my editor, so that the bracket delimiters and element expressions are visually distinct.

#### Acceptance Criteria

1. WHEN the TextMate_Grammar encounters array literal brackets in expression context, THE TextMate_Grammar SHALL apply punctuation scope to the `[` and `]` delimiters.
2. WHEN the TextMate_Grammar encounters element expressions within array literal brackets, THE TextMate_Grammar SHALL apply the standard expression highlighting rules to each element.
3. WHEN the TextMate_Grammar encounters commas separating array literal elements, THE TextMate_Grammar SHALL apply punctuation separator scope to each comma.

### Requirement 11: Array Literal in Various Expression Positions

**User Story:** As a Mist developer, I want to use array literals anywhere an expression is valid, so that I have full flexibility in how I construct arrays.

#### Acceptance Criteria

1. THE Parser SHALL accept array literals as variable initializers (e.g. `var x = [1, 2, 3];`).
2. THE Parser SHALL accept array literals as method arguments (e.g. `Foo([1, 2, 3])`).
3. THE Parser SHALL accept array literals as return values (e.g. `return [1, 2, 3];`).
4. THE Parser SHALL accept array literals as elements of other array literals (e.g. `[[1], [2]]`).
5. THE Parser SHALL accept array literals as the right-hand side of assignment expressions.
