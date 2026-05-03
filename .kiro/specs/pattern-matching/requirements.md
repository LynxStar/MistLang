# Requirements Document

## Introduction

This document specifies the requirements for adding C#-style pattern matching to the Mist programming language via `is` expressions. Pattern matching enables developers to test values against shapes (types, constants, structures) and extract data in a concise, declarative style. The feature supports a rich set of pattern kinds including type patterns, constant patterns, var patterns, discard patterns, property patterns, relational patterns, logical combinators, and list patterns.

Mist intentionally excludes `null` from its type system, so null-related patterns (null constant pattern, null checks) are omitted. Positional patterns (tuple deconstruction) are deferred because Mist does not yet support tuples or `Deconstruct` methods. Slice patterns (`..`) within list patterns are included as they operate on arrays, which Mist already supports.

The implementation touches every stage of the compiler pipeline: new keywords and grammar rules in Storm, AST nodes from the parser, pretty-printer support for round-trip fidelity, semantic model construction for pattern bindings and type resolution, type-checker validation of pattern correctness, and C# code emission that lowers Mist `is` expressions to equivalent C# pattern matching constructs.

## Glossary

- **Compiler**: The Mist compiler pipeline, including lexing, parsing, semantic model construction, analysis, and C# code emission.
- **Grammar**: The Storm grammar definition that specifies the syntax of the Mist language (`Mist.storm` and `keywords.storm`).
- **Parser**: The component that reads Mist source tokens and produces an abstract syntax tree (AST) conforming to the Grammar.
- **Pretty_Printer**: The component (`MistPrettyPrinter`) that formats a Mist AST back into valid Mist source text.
- **Semantic_Model_Builder**: The component (`SemanticModelBuilder`) that constructs the semantic model from the AST in two passes: Pass 1 registers types and members, Pass 2 resolves type references and expressions.
- **Type_Checker**: The analysis component (`TypeCheckerAnalyzer`) that validates type rules against the semantic model.
- **Emitter**: The C# code emission component (`CSharpEmitter` / `CSharpSourceWriter`) that translates the Mist AST into C# source files.
- **Pattern**: A syntactic construct that tests a value against a shape and optionally binds variables to parts of the matched value.
- **Type_Pattern**: A pattern of the form `TypeName identifier` that tests whether a value is of the given type and binds it to the identifier if so.
- **Constant_Pattern**: A pattern that tests whether a value equals a compile-time constant (integer, float, string, char, boolean literal).
- **Var_Pattern**: A pattern of the form `var identifier` that always matches and binds the value to the identifier with its static type.
- **Discard_Pattern**: The pattern `_` that always matches and binds nothing.
- **Property_Pattern**: A pattern of the form `{ PropertyName: subpattern, ... }` optionally preceded by a type name, that tests whether the value's properties match the given subpatterns.
- **Relational_Pattern**: A pattern using a relational operator (`<`, `>`, `<=`, `>=`) followed by a constant expression, testing whether the value satisfies the relation.
- **Logical_Pattern**: A compound pattern using `and`, `or`, or `not` combinators to compose subpatterns.
- **List_Pattern**: A pattern of the form `[element1, element2, ..]` that matches array values by testing each element against its corresponding subpattern, with optional slice (`..`) to match remaining elements.
- **Slice_Pattern**: The `..` element within a List_Pattern that matches zero or more remaining elements.
- **Is_Expression**: An expression of the form `expression is pattern` that evaluates to `bool` and optionally introduces pattern variables into scope.
- **Pattern_Variable**: A variable introduced by a Type_Pattern or Var_Pattern that is in scope within the `then` branch of an `if` statement whose condition contains the `is` expression.

## Requirements

### Requirement 1: Pattern Matching Keywords

**User Story:** As a Mist developer, I want the compiler to recognize `is`, `and`, `or`, and `not` as keywords, so that pattern matching syntax can be parsed.

#### Acceptance Criteria

1. THE Grammar SHALL define an `IS` keyword token with the value `"is"`.
2. THE Grammar SHALL define an `AND` keyword token with the value `"and"`.
3. THE Grammar SHALL define an `OR` keyword token with the value `"or"`.
4. THE Grammar SHALL define a `NOT` keyword token with the value `"not"`.
5. THE Grammar SHALL define a `DOTDOT` token with the value `".."`, declared before the `DOT` token so that maximal-munch selects the longer token.

### Requirement 2: Pattern Grammar Rules

**User Story:** As a Mist developer, I want to write patterns using a consistent grammar, so that I can use type checks, constants, property destructuring, relational comparisons, and logical combinators in pattern matching contexts.

#### Acceptance Criteria

1. THE Grammar SHALL define a `Pattern` rule as the entry point for all pattern syntax, dispatching to `OrPattern`.
2. THE Grammar SHALL define an `OrPattern` rule that matches one or more `AndPattern` alternatives separated by the `OR` keyword.
3. THE Grammar SHALL define an `AndPattern` rule that matches one or more `UnaryPattern` alternatives separated by the `AND` keyword.
4. THE Grammar SHALL define a `UnaryPattern` rule that matches either `NOT` followed by a `PrimaryPattern`, or a `PrimaryPattern` directly.
5. THE Grammar SHALL define a `PrimaryPattern` rule with the following alternatives:
   a. **Type pattern**: a type reference followed by an identifier (e.g., `int i`, `string s`, `MyClass obj`).
   b. **Var pattern**: the `VAR` keyword followed by an identifier (e.g., `var x`).
   c. **Discard pattern**: a standalone underscore `_` token.
   d. **Constant pattern**: a literal expression (integer, float, string, char, boolean).
   e. **Relational pattern**: a relational operator (`<`, `>`, `<=`, `>=`) followed by a constant expression.
   f. **Property pattern**: an optional type reference followed by `{` property subpattern list `}` optionally followed by an identifier for binding.
   g. **List pattern**: `[` comma-separated element patterns `]` where each element is either a Pattern or a Slice_Pattern (`..` optionally followed by an identifier).
   h. **Parenthesized pattern**: `(` Pattern `)` for explicit grouping.
6. THE Grammar SHALL define a `PropertySubpattern` rule as a name identifier, a colon, and a Pattern (e.g., `Name: "John"`).
7. THE Grammar SHALL define a `ListPatternElement` rule that is either a Pattern or a Slice_Pattern (`DOTDOT` optionally followed by an identifier).
8. WHEN a pattern is parsed, THE Parser SHALL produce an AST node that captures the pattern kind and all sub-components (type references, identifiers, subpatterns, operators, constants).

### Requirement 3: Is Expression Grammar and Parsing

**User Story:** As a Mist developer, I want to write `expression is pattern` to test whether a value matches a pattern and optionally bind variables, so that I can use pattern matching in conditional expressions.

#### Acceptance Criteria

1. THE Grammar SHALL define an `IsExpression` as a binary expression with the `IS` keyword between a left-hand expression and a Pattern.
2. THE Grammar SHALL place the `is` operator at the same precedence level as relational operators (`<`, `>`, `<=`, `>=`), so that `x is int i && y > 0` parses as `(x is int i) && (y > 0)`.
3. WHEN an `is` expression is parsed, THE Parser SHALL produce an AST node that captures the left-hand expression and the Pattern.
4. WHEN an `is` expression uses a type pattern with an identifier (e.g., `x is string s`), THE Parser SHALL capture both the type reference and the identifier in the pattern AST node.
5. WHEN an `is` expression uses a constant pattern (e.g., `x is 42`), THE Parser SHALL capture the constant value in the pattern AST node.

### Requirement 4: Pretty-Printer Support for Pattern Matching

**User Story:** As a Mist developer, I want the pretty-printer to correctly format all pattern matching constructs, so that round-trip printing preserves the source structure.

#### Acceptance Criteria

1. THE Pretty_Printer SHALL format an `is` expression as `expression is pattern`.
2. THE Pretty_Printer SHALL format a type pattern as `TypeName identifier`.
3. THE Pretty_Printer SHALL format a var pattern as `var identifier`.
4. THE Pretty_Printer SHALL format a discard pattern as `_`.
5. THE Pretty_Printer SHALL format a constant pattern as the literal value.
6. THE Pretty_Printer SHALL format a relational pattern as `operator constant` (e.g., `> 0`, `<= 100`).
7. THE Pretty_Printer SHALL format a property pattern as `{ Name: subpattern, ... }`, optionally preceded by a type name and optionally followed by an identifier.
8. THE Pretty_Printer SHALL format a list pattern as `[element1, element2, ..]`.
9. THE Pretty_Printer SHALL format a slice pattern as `..` optionally followed by an identifier.
10. THE Pretty_Printer SHALL format logical patterns using `and`, `or`, and `not` with appropriate spacing.
11. THE Pretty_Printer SHALL format a parenthesized pattern as `(pattern)`.
12. FOR ALL valid Mist ASTs containing pattern matching constructs, parsing then pretty-printing then parsing SHALL produce an equivalent AST (round-trip property).

### Requirement 5: Semantic Model for Pattern Variables

**User Story:** As a Mist developer, I want pattern variables introduced by type patterns and var patterns to be available in the appropriate scope, so that I can use matched values in subsequent code.

#### Acceptance Criteria

1. WHEN an `is` expression with a type pattern (e.g., `x is string s`) appears in an `if` condition, THE Semantic_Model_Builder SHALL introduce the pattern variable `s` into the scope of the `then` branch.
2. WHEN an `is` expression with a var pattern (e.g., `x is var y`) appears in an `if` condition, THE Semantic_Model_Builder SHALL introduce the pattern variable `y` into the scope of the `then` branch with the static type of the scrutinee.
3. THE Semantic_Model_Builder SHALL register each pattern variable as a symbol with `SymbolKind.LocalVariable` in the appropriate scope.
4. WHEN a discard pattern `_` is used, THE Semantic_Model_Builder SHALL NOT introduce any variable into scope.

### Requirement 6: Type Checker Validation for Is Expressions

**User Story:** As a Mist developer, I want the type checker to validate `is` expressions, so that I get compile-time errors for invalid pattern usage.

#### Acceptance Criteria

1. THE Type_Checker SHALL verify that an `is` expression evaluates to type `bool`.
2. WHEN a type pattern references a type that does not exist, THE Type_Checker SHALL emit a diagnostic error indicating the type is undefined.
3. WHEN a constant pattern uses a value whose type is incompatible with the scrutinee type (e.g., matching a `string` scrutinee against an `int` constant), THE Type_Checker SHALL emit a diagnostic error indicating the type mismatch.
4. WHEN a relational pattern is applied to a scrutinee whose type does not support comparison operators, THE Type_Checker SHALL emit a diagnostic error.
5. WHEN a property pattern references a property name that does not exist on the scrutinee type (or the type specified in the pattern), THE Type_Checker SHALL emit a diagnostic error indicating the property is undefined.
6. WHEN a list pattern is applied to a scrutinee that is not an array type, THE Type_Checker SHALL emit a diagnostic error indicating that list patterns require an array type.
7. WHEN a list pattern has more fixed elements than the array could contain (for fixed-length checks), THE Type_Checker SHALL emit a diagnostic warning.

### Requirement 7: Type Checker Validation for Logical and Relational Patterns

**User Story:** As a Mist developer, I want the type checker to validate compound patterns using `and`, `or`, `not`, and relational operators, so that I get compile-time errors for invalid combinations.

#### Acceptance Criteria

1. WHEN an `or` pattern combines subpatterns that introduce different pattern variables, THE Type_Checker SHALL emit a diagnostic error (all alternatives in an `or` pattern must introduce the same set of variables with the same types).
2. WHEN a `not` pattern contains a subpattern that introduces a pattern variable, THE Type_Checker SHALL emit a diagnostic error (negated patterns cannot introduce variables).
3. WHEN a relational pattern uses a constant whose type is not compatible with the scrutinee type, THE Type_Checker SHALL emit a diagnostic error.
4. THE Type_Checker SHALL validate that `and` patterns combine subpatterns that are individually valid against the scrutinee type.

### Requirement 8: C# Code Emission for Is Expressions

**User Story:** As a Mist developer, I want `is` expressions to be correctly transpiled to C# pattern matching, so that the compiled output behaves as specified.

#### Acceptance Criteria

1. THE Emitter SHALL emit a Mist `is` expression with a type pattern as a C# `is` expression with a type pattern (e.g., `x is string s`).
2. THE Emitter SHALL emit a Mist `is` expression with a constant pattern as a C# `is` expression with a constant pattern (e.g., `x is 42`).
3. THE Emitter SHALL emit a Mist `is` expression with a var pattern as a C# `is` expression with a `var` pattern (e.g., `x is var y`).
4. THE Emitter SHALL emit a Mist `is` expression with a discard pattern as `x is _` in C#.
5. THE Emitter SHALL emit a Mist `is` expression with a property pattern as a C# property pattern (e.g., `x is { Name: "John" }`).
6. THE Emitter SHALL emit a Mist `is` expression with a relational pattern as a C# relational pattern (e.g., `x is > 0`).
7. THE Emitter SHALL emit a Mist `is` expression with logical patterns using C# `and`, `or`, `not` combinators.
8. THE Emitter SHALL emit a Mist `is` expression with a list pattern as a C# list pattern (e.g., `x is [1, 2, ..]`).

### Requirement 9: C# Code Emission for All Pattern Kinds

**User Story:** As a Mist developer, I want every pattern kind to be correctly lowered to its C# equivalent, so that the transpiled code compiles and behaves correctly.

#### Acceptance Criteria

1. THE Emitter SHALL emit a type pattern as `TypeName identifier` in C#.
2. THE Emitter SHALL emit a constant pattern as the literal value in C#.
3. THE Emitter SHALL emit a var pattern as `var identifier` in C#.
4. THE Emitter SHALL emit a discard pattern as `_` in C#.
5. THE Emitter SHALL emit a property pattern as `{ PropertyName: subpattern }` in C#, with nested subpatterns recursively emitted.
6. THE Emitter SHALL emit a relational pattern as `operator constant` in C# (e.g., `> 0`, `<= 100`).
7. THE Emitter SHALL emit an `and` pattern as `pattern1 and pattern2` in C#.
8. THE Emitter SHALL emit an `or` pattern as `pattern1 or pattern2` in C#.
9. THE Emitter SHALL emit a `not` pattern as `not pattern` in C#.
10. THE Emitter SHALL emit a list pattern as `[element1, element2, ..]` in C#.
11. THE Emitter SHALL emit a slice pattern as `..` or `.. identifier` in C#.
12. THE Emitter SHALL emit a parenthesized pattern as `(pattern)` in C#.
13. WHEN a property pattern includes a type name prefix, THE Emitter SHALL emit it as `TypeName { PropertyName: subpattern }` in C#.

### Requirement 10: Pattern Matching Diagnostic Messages

**User Story:** As a Mist developer, I want clear and specific error messages when pattern matching is used incorrectly, so that I can quickly identify and fix the issue.

#### Acceptance Criteria

1. WHEN a type pattern references an undefined type, THE Compiler SHALL emit a diagnostic that names the undefined type and the location of the pattern.
2. WHEN a property pattern references an undefined property, THE Compiler SHALL emit a diagnostic that names the property, the type being matched, and the location.
3. WHEN a constant pattern has an incompatible type, THE Compiler SHALL emit a diagnostic that names the expected type (from the scrutinee) and the actual constant type.
4. WHEN a relational pattern is applied to a non-comparable type, THE Compiler SHALL emit a diagnostic that names the type and explains that relational patterns require comparable types.
5. WHEN a list pattern is applied to a non-array type, THE Compiler SHALL emit a diagnostic that names the type and explains that list patterns require array types.
6. WHEN an `or` pattern has inconsistent variable bindings across alternatives, THE Compiler SHALL emit a diagnostic listing the variable names that differ.
7. WHEN a `not` pattern attempts to introduce a variable, THE Compiler SHALL emit a diagnostic explaining that negated patterns cannot bind variables.

### Requirement 11: Syntax Highlighting for Pattern Matching Keywords

**User Story:** As a Mist developer using an editor, I want the pattern matching keywords to be syntax-highlighted, so that pattern matching code is visually clear.

#### Acceptance Criteria

1. THE TextMate grammar (`mist.tmLanguage.json`) SHALL include `is` in the `keyword.operator.mist` pattern.
2. THE TextMate grammar SHALL include `and`, `or`, and `not` in the `keyword.operator.logical.mist` pattern.
3. WHEN a Mist source file contains pattern matching keywords, THE editor SHALL highlight them consistently with other keywords of the same category.

### Requirement 12: Feature Coverage Documentation Update

**User Story:** As a Mist project maintainer, I want the CSHARP_FEATURES.md document updated to reflect the newly implemented pattern matching features, so that the feature tracking document stays accurate.

#### Acceptance Criteria

1. WHEN the pattern matching feature is implemented, THE Compiler team SHALL update the `Pattern Matching` section of CSHARP_FEATURES.md to mark implemented pattern kinds as ✅.
2. WHEN the pattern matching feature is implemented, THE Compiler team SHALL update the `Expressions — Other` section to mark `is` pattern matching and `Discard (_)` as ✅.
3. THE Compiler team SHALL update the `Summary` table at the bottom of CSHARP_FEATURES.md to reflect the new implementation counts.

### Requirement 13: Pattern Matching Sample Application

**User Story:** As a Mist developer evaluating the language, I want a sample application that demonstrates pattern matching in action, so that I can see how `is` expressions and various pattern kinds work in practice.

#### Acceptance Criteria

1. THE sample application SHALL be located in a new `samples/PatternMatching/` directory following the existing sample app convention.
2. THE sample application SHALL demonstrate an `is` expression with a type pattern inside an `if` condition.
3. THE sample application SHALL demonstrate property patterns matching against object properties.
4. THE sample application SHALL demonstrate relational and logical patterns (e.g., `is > 0 and < 100`).
5. THE sample application SHALL demonstrate list patterns matching against arrays.
6. THE sample application SHALL compile and run successfully through the Mist compiler pipeline, producing correct output via `Console.WriteLine`.
7. THE sample application SHALL include a `static void Main` entry point that exercises all demonstrated pattern matching features and prints observable output.
