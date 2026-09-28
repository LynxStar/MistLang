# Requirements Document

## Introduction

This document specifies the requirements for making regex a first-class type in the Mist programming language. The feature introduces `Standard.Regex` as a readonly intrinsic type in the standard library, adds `regex` as a reserved keyword alias (mirroring how `string` aliases `Standard.String`), and provides backtick-delimited regex literal syntax that desugars to `Standard.Regex` construction.

The feature integrates with Mist's existing type system through a safe implicit cast from `regex` to `string` and an explicit cast from `string` to `regex`. String literals assigned to `regex` variables are validated at compile time and do not require an explicit cast. It extends the pattern matching system to support regex literal patterns in `is` expressions and switch arms, where a regex pattern tests whether a string value matches the regex. As a consequence of implicit conversions, string literal patterns in pattern matching contexts also gain regex matching semantics.

Regex literals use backtick delimiters (`` `pattern` ``), which are already used in Storm grammar files for meta-grammar regex patterns. At the Mist language level, backticks serve the same purpose — delimiting a regex pattern — but produce a `Standard.Regex` value rather than a Storm grammar construct. The Mist lexer must be extended to tokenize backtick-delimited content as regex literal tokens.

The regex flavor is pinned to .NET's `System.Text.RegularExpressions.Regex` since the backend targets C#. The C# transpiler maps `Standard.Regex` to `System.Text.RegularExpressions.Regex` and regex literals to compiled `Regex` instances.

Key design decisions:
- `regex` is a reserved keyword aliasing `Standard.Regex` (same mechanism as `string` → `Standard.String`)
- Backtick delimiters for regex literals (`` `[a-z]+` ``) — no conflict with existing Mist syntax (backticks are only used in Storm meta-grammar, not in Mist source)
- `regex → string` is an **implicit** conversion (always safe, extracts the pattern string)
- `string → regex` is an **explicit** conversion (requires explicit cast syntax) because invalid patterns cannot be detected at compile time for non-literal strings
- **Special case**: a string *literal* assigned to a `regex` variable is validated at compile time and desugars to the explicit operator — no runtime check needed (e.g., `regex x = "valid";` works if the literal is a valid pattern, compile error otherwise)
- The regex type is abstract enough that future backends can decide their own behavior/details for the backing implementation
- Regex literal patterns in `is` expressions test full-match semantics (the entire string must match)
- String literal patterns in pattern matching contexts implicitly convert to regex patterns
- Pattern matching with regex is always non-exhaustive (regex patterns cannot prove exhaustiveness)
- `Standard.Regex` is a readonly intrinsic class (immutable after construction)
- Refinement types can use regex patterns as predicates on string types

## Glossary

- **Mist_Compiler**: The Mist language compiler, encompassing lexing, parsing, semantic analysis, and code generation stages.
- **Lexer**: The Storm-generated lexical analysis component that tokenizes Mist source text.
- **Parser**: The Storm-generated parser that produces an AST from a token stream according to the Mist grammar.
- **Pretty_Printer**: The `MistPrettyPrinter` component that formats a Mist AST back into syntactically valid Mist source text.
- **Semantic_Model_Builder**: The `SemanticModelBuilder` component that constructs the semantic model (symbol table, type information) from the parsed AST.
- **Type_Checker**: The type-checking analyzer that validates type correctness across the program.
- **CSharp_Transpiler**: The C# code generation backend that emits equivalent C# source from the Mist semantic model and AST.
- **Standard_Regex**: The `Standard.Regex` type in the Mist standard library — a readonly intrinsic class representing a compiled regular expression pattern.
- **Regex_Keyword**: The reserved keyword `regex` that aliases `Standard.Regex`, following the same mechanism as `string` → `Standard.String`.
- **Regex_Literal**: A backtick-delimited expression in Mist source (e.g., `` `[a-z]+` ``) that evaluates to a `Standard.Regex` value.
- **Regex_Pattern**: A pattern used in `is` expressions and switch arms that tests whether a string value matches a regex. Written as a Regex_Literal in pattern position.
- **Full_Match**: The semantics of regex pattern matching where the entire input string must match the regex pattern: the match starts at the first character and consumes the last, so a trailing newline the pattern does not consume makes the match fail. Equivalent to wrapping the pattern as `\A(?:pattern)\z`, NOT `^(?:pattern)$`: .NET's `$` also matches before a final newline, and multiline mode makes `^` and `$` match at every line. The wrapping must not change what the pattern means: a pattern that is not a valid regex on its own is refused rather than wrapped (it could close the group early, `a)|(?:b`), and a pattern ending inside an inline-`(?x)` `#` comment still gets its closing parenthesis.
- **Keyword_Alias_Mapping**: The fixed mapping from reserved keywords to their fully-qualified standard library type names (e.g., `int → Standard.Int32`, `string → Standard.String`, `regex → Standard.Regex`).

## Requirements

### Requirement 1: Regex Reserved Keyword

**User Story:** As a Mist developer, I want `regex` to be a reserved keyword in the language, so that I can use it as a type name without imports, consistent with other primitive-like types.

#### Acceptance Criteria

1. THE Grammar SHALL define a `REGEX` keyword token with the value `"regex"` in the primitive type keywords section of `keywords.storm`, following the same declaration style as the existing primitive type keyword tokens (`INT`, `STRING`, `BOOL`, etc.).
2. THE Keyword_Alias_Mapping in `KeywordAliases.cs` SHALL include the entry `["regex"] = "Standard.Regex"`, so that the `regex` keyword resolves to the `Standard.Regex` type without requiring a `using Standard;` directive.
3. WHEN the Lexer encounters the identifier text `regex` in source, THE Lexer SHALL produce a `REGEX` keyword token rather than an `IDENTIFIER` token.
4. THE Grammar SHALL include `REGEX` as a variant in the `PrimitiveType` rule (as `RegexType`), so that `regex` can be used in any type annotation position where `PrimitiveType` is accepted (including `TypeReference`, `SingleTypeRef`, and `TypeParameter` rules) alongside `int`, `string`, `bool`, etc.
5. IF source code uses `regex` as a plain identifier (e.g., variable name or method name), THEN THE Lexer SHALL produce a `REGEX` keyword token, causing the Parser to report a syntax error at that position.

### Requirement 2: Standard.Regex Intrinsic Type

**User Story:** As a Mist developer, I want a `Standard.Regex` type in the standard library that represents a compiled regular expression, so that regex values have a well-defined type with discoverable members.

#### Acceptance Criteria

1. THE Standard_Library SHALL declare `Regex` as a `public readonly intrinsic class` in the `Standard` namespace, in a file named `Regex.mist` within the standard library source directory.
2. THE Standard_Regex type SHALL declare a public constructor that accepts a single `string` parameter named `pattern` representing the regex pattern.
3. THE Standard_Regex type SHALL declare a public readonly property `Pattern` of type `string` that returns the original pattern string used to construct the regex.
4. THE Standard_Regex type SHALL declare a public method `IsMatch(string input)` that returns `bool`, testing whether the input string matches the regex pattern (partial match — the pattern can match anywhere in the input).
5. THE Standard_Regex type SHALL declare a public method `IsFullMatch(string input)` that returns `bool`, testing whether the entire input string matches the regex pattern (Full_Match — anchored at the absolute start and end of the input, `\A...\z`).
6. THE Standard_Regex type SHALL declare an implicit cast operator from `Standard.Regex` to `Standard.String` that returns the `Pattern` property value.
7. THE Standard_Regex type SHALL declare an **explicit** cast operator from `Standard.String` to `Standard.Regex` that constructs a new `Standard.Regex` from the string value. This is explicit because the conversion can fail at runtime for non-literal strings with invalid patterns.
8. THE Standard_Regex type SHALL be marked with a type-level `intrinsic` modifier, making it a backend-backed type whose runtime representation is provided by the C# backend.
9. THE Standard_Regex type SHALL NOT declare any mutable fields or settable properties, consistent with the `readonly` modifier.

### Requirement 3: Regex Literal Lexing

**User Story:** As a Mist developer, I want to write regex patterns using backtick delimiters, so that I have a concise literal syntax for creating regex values.

#### Acceptance Criteria

1. WHEN the Lexer encounters a backtick character (`` ` ``) in Mist source text, THE Lexer SHALL consume all characters up to and including the next unescaped closing backtick and produce a `REGEX_LITERAL` token.
2. WHEN a regex literal contains an escaped backtick sequence (`` \` ``) or an escaped backslash sequence (`\\`), THE Lexer SHALL treat the two-character escape as a literal character within the pattern and SHALL NOT interpret the second character as a closing delimiter.
3. IF the Lexer reaches the end of the source text without finding a closing backtick, THEN THE Lexer SHALL emit a diagnostic error indicating an unterminated regex literal and the line and column where the opening backtick was found, and SHALL NOT produce a `REGEX_LITERAL` token for that sequence.
4. WHEN the Lexer encounters two consecutive backtick characters with no content between them (`` `` ``), THE Lexer SHALL produce a valid `REGEX_LITERAL` token representing an empty pattern.
5. THE Lexer SHALL store the full backtick-delimited text (including delimiters) as the token lexeme.
6. WHEN a regex literal contains newline characters between the opening and closing backticks, THE Lexer SHALL include the newline characters in the token lexeme and produce a valid `REGEX_LITERAL` token spanning multiple lines.
7. THE Grammar SHALL define a `REGEX_LITERAL` token rule in `Mist.storm` that matches a backtick, followed by zero or more characters where `\\` and `` \` `` are recognized escape sequences, followed by a closing backtick.

### Requirement 4: Regex Literal Expression Parsing

**User Story:** As a Mist developer, I want regex literals to be valid expressions that produce `Standard.Regex` values, so that I can assign them to variables and pass them to methods.

#### Acceptance Criteria

1. THE Grammar SHALL define a `RegexLiteralExpression` alternative in the `Primary` rule that accepts a `REGEX_LITERAL` token.
2. WHEN the Parser encounters a `REGEX_LITERAL` token in expression position, THE Parser SHALL produce a `RegexLiteralExpression` AST node containing the pattern text (without backtick delimiters).
3. WHEN the Semantic_Model_Builder encounters a `RegexLiteralExpression` AST node, THE Semantic_Model_Builder SHALL record the expression type as `Standard.Regex`.
4. WHEN a variable is declared as `` var x = `[a-z]+`; ``, THE Semantic_Model_Builder SHALL infer the variable type as `Standard.Regex`.
5. WHEN a variable is declared as `` regex x = `[a-z]+`; ``, THE Type_Checker SHALL verify that the regex literal is assignable to the `regex` type annotation.
6. IF the `REGEX_LITERAL` token contains an invalid regex pattern (one that cannot be compiled by .NET's regex engine), THEN THE Semantic_Model_Builder SHALL emit a diagnostic indicating the pattern is malformed, and SHALL still record the expression type as `Standard.Regex`.
7. WHEN the Parser encounters an empty regex literal (consecutive backtick delimiters with no content between them), THE Parser SHALL produce a `RegexLiteralExpression` AST node with an empty string as the pattern text.
8. IF a `REGEX_LITERAL` token appears in a non-expression context where `Primary` is not expected, THEN THE Parser SHALL emit a diagnostic indicating an unexpected token.

### Requirement 5: Regex Literal Desugaring

**User Story:** As a compiler developer, I want regex literals to desugar to `Standard.Regex` constructor calls, so that the semantic model has a uniform representation for regex values.

#### Acceptance Criteria

1. WHEN the Semantic_Model_Builder encounters a `RegexLiteralExpression` with pattern `P`, THE Semantic_Model_Builder SHALL record the expression in the semantic model as a constructor invocation of `Standard.Regex` with the pattern string `P` as the sole argument.
2. THE Semantic_Model_Builder SHALL preserve the regex pattern string verbatim (including all backslash escape sequences) without additional escaping or transformation when recording the desugared constructor argument.
3. WHEN the Type_Checker validates a regex literal, THE Type_Checker SHALL verify that the pattern string is a syntactically valid .NET regular expression at compile time using `RegexOptions.None`.
4. IF a regex literal contains a syntactically invalid regex pattern, THEN THE Type_Checker SHALL emit a diagnostic error indicating the regex syntax error, including the error description from the .NET regex parser and the zero-based character offset within the pattern where the error was detected.
5. THE compile-time regex validation SHALL use the same regex flavor as the runtime (.NET `System.Text.RegularExpressions`).

### Requirement 6: Conversions Between String and Regex

**User Story:** As a Mist developer, I want a safe implicit conversion from `regex` to `string` and an explicit conversion from `string` to `regex`, so that I avoid runtime exceptions from invalid patterns while still being able to convert when needed.

#### Acceptance Criteria

1. WHEN a `regex` value is assigned to a `string` variable, THE Type_Checker SHALL resolve the implicit cast operator from `Standard.Regex` to `Standard.String` and accept the assignment without emitting a type mismatch diagnostic.
2. WHEN a `regex` argument is passed to a method parameter of type `string`, THE Type_Checker SHALL apply the implicit conversion from `Standard.Regex` to `Standard.String` and accept the call without emitting a type mismatch diagnostic.
3. WHEN a return statement in a method with return type `string` returns a `regex` expression, THE Type_Checker SHALL apply the implicit conversion from `Standard.Regex` to `Standard.String` and accept the return without emitting a type mismatch diagnostic.
4. THE `regex → string` implicit conversion SHALL succeed unconditionally for any valid `Standard.Regex` instance, extracting the pattern string without throwing an exception.
5. WHEN a non-literal `string` value is assigned to a `regex` variable without an explicit cast (e.g., `regex x = myStringVar;`), THE Type_Checker SHALL emit a type mismatch diagnostic indicating that an explicit cast is required.
6. WHEN a `string` value is explicitly cast to `regex` (e.g., `regex x = (regex)myStringVar;`), THE Type_Checker SHALL resolve the explicit cast operator from `Standard.String` to `Standard.Regex` and accept the assignment.
7. THE `string → regex` explicit conversion SHALL be a runtime operation in the generated C# code: if the string value is not a syntactically valid regex pattern, the conversion SHALL throw an exception at runtime.
8. WHEN a **string literal** that is a syntactically valid regex pattern is assigned to a `regex` variable (e.g., `regex x = "[a-z]+";`), THE Type_Checker SHALL validate the literal at compile time, accept the assignment without requiring an explicit cast, and desugar it to the explicit cast operator (since the pattern is proven valid at compile time, no runtime check is needed).
9. IF a **string literal** assigned to a `regex` variable is NOT a syntactically valid regex pattern (e.g., `regex x = "[invalid";`), THEN THE Type_Checker SHALL emit a compile-time diagnostic error indicating the regex syntax error.
10. WHEN a `string` argument is passed to a method parameter of type `regex`, THE Type_Checker SHALL require an explicit cast unless the argument is a string literal with a valid regex pattern.

### Requirement 7: Regex Pattern in Pattern Matching

**User Story:** As a Mist developer, I want to use regex literals in `is` expressions and switch arms, so that I can test whether a string matches a regex pattern using the existing pattern matching syntax.

#### Acceptance Criteria

1. THE Grammar SHALL accept a `REGEX_LITERAL` token as a valid `ConstantPattern` alternative in the `PrimaryPattern` rule (as `RegexConstant`).
2. WHEN an `is` expression tests a value against a regex literal pattern (e.g., `` haystack is `[a-z]+` ``), THE Type_Checker SHALL validate that the scrutinee type is `string`.
3. IF the scrutinee of an `is` expression or switch arm containing a regex literal pattern has a type other than `string`, THEN THE Type_Checker SHALL emit a diagnostic error indicating that regex patterns require a `string` scrutinee.
4. WHEN a regex literal pattern is used in pattern matching, THE pattern SHALL use Full_Match semantics: the entire scrutinee string must match the regex pattern for the pattern to succeed.
5. WHEN a regex literal pattern is used in a switch arm, THE Exhaustiveness_Checker SHALL treat the arm as non-exhaustive (regex patterns cannot prove they cover all possible string values).
6. WHEN a string literal is used as a pattern in an `is` expression or switch arm where the scrutinee is a `string`, THE Type_Checker SHALL treat the string literal pattern as an exact equality check (existing behavior), NOT as a regex pattern.
7. IF a regex literal pattern contains a syntactically invalid regex (as defined by the .NET `System.Text.RegularExpressions.Regex` syntax), THEN THE Type_Checker SHALL emit a diagnostic error at the pattern position indicating the regex is malformed.
8. WHEN a regex literal pattern contains capture groups, THE pattern matcher SHALL ignore capture groups and use the overall match result only (no bindings are introduced by regex patterns).

### Requirement 8: C# Transpilation of Standard.Regex

**User Story:** As a compiler developer, I want the C# backend to correctly map `Standard.Regex` to `System.Text.RegularExpressions.Regex`, so that regex operations work correctly at runtime.

#### Acceptance Criteria

1. THE CSharp_Transpiler SHALL map `Standard.Regex` to `System.Text.RegularExpressions.Regex` in the backing type map.
2. WHEN a generated file references the `Regex` type, THE CSharp_Transpiler SHALL emit a `using System.Text.RegularExpressions;` directive in that file.
3. WHEN the CSharp_Transpiler encounters a `RegexLiteralExpression`, THE CSharp_Transpiler SHALL emit `new Regex(<escaped_pattern_string>, RegexOptions.Compiled)` in C#, where `<escaped_pattern_string>` is the regex pattern from the literal with C# string escaping applied.
4. WHEN the CSharp_Transpiler encounters the `IsFullMatch` method call on a `Standard.Regex` instance, THE CSharp_Transpiler SHALL emit a call that uses `Regex.IsMatch` with the pattern wrapped in `\A(?:...)\z` anchoring to ensure the entire input is matched.
5. WHEN the CSharp_Transpiler encounters the `IsMatch` method call on a `Standard.Regex` instance, THE CSharp_Transpiler SHALL emit a call to `Regex.IsMatch(input)`.
6. WHEN the CSharp_Transpiler encounters the `Pattern` property access on a `Standard.Regex` instance, THE CSharp_Transpiler SHALL emit a call to the `.ToString()` method on the `System.Text.RegularExpressions.Regex` instance.
7. WHEN the CSharp_Transpiler encounters the implicit cast from `regex` to `string`, THE CSharp_Transpiler SHALL emit a call to `.ToString()` on the `Regex` instance.
8. WHEN the CSharp_Transpiler encounters the explicit cast from `string` to `regex`, THE CSharp_Transpiler SHALL emit `new Regex(stringValue, RegexOptions.Compiled)`.
9. IF the `IsFullMatch` or `IsMatch` method is called on a `Standard.Regex` instance with an argument that is not of type `Standard.String`, THEN THE CSharp_Transpiler SHALL report a diagnostic error indicating that the method expects a string argument.
10. WHEN the CSharp_Transpiler registers `Standard.Regex` intrinsic methods in the IntrinsicEmissionTable, THE CSharp_Transpiler SHALL register entries keyed as `Standard.Regex:IsFullMatch(Standard.String)` and `Standard.Regex:IsMatch(Standard.String)` with emission templates that produce the corresponding C# calls.

### Requirement 9: C# Transpilation of Regex Pattern Matching

**User Story:** As a compiler developer, I want regex patterns in `is` expressions and switch arms to be correctly transpiled to C#, so that pattern matching with regex works at runtime.

#### Acceptance Criteria

1. WHEN the CSharp_Transpiler encounters an `is` expression with a regex literal pattern (e.g., `` x is `[a-z]+` ``), THE CSharp_Transpiler SHALL emit a C# expression that calls `Regex.IsMatch(scrutinee, @"\A(?:pattern)\z")`, wrapping the original regex in a non-capturing group with absolute start- and end-of-input anchors to enforce Full_Match semantics.
2. WHEN the CSharp_Transpiler encounters a switch arm with a regex literal pattern, THE CSharp_Transpiler SHALL emit the arm as a `string s when Regex.IsMatch(s, @"\A(?:pattern)\z")` guard in C#, since C# does not natively support regex patterns in switch expressions.
3. WHEN the CSharp_Transpiler emits a regex pattern that appears inside a loop or is evaluated more than once at runtime, THE CSharp_Transpiler SHALL emit the pattern as a `private static readonly Regex` field with `RegexOptions.Compiled`, rather than constructing the regex inline on each evaluation.
4. IF the scrutinee of a regex pattern match is not typed as `string`, THEN THE CSharp_Transpiler SHALL report a diagnostic error indicating that regex patterns require a string-typed scrutinee.
5. THE CSharp_Transpiler SHALL produce C# code that compiles without errors for all regex pattern matching constructs where the scrutinee is typed as `string` and the regex literal contains a syntactically valid .NET regular expression.

### Requirement 10: Pretty-Printer Support for Regex Literals

**User Story:** As a compiler developer, I want the pretty printer to format regex literal AST nodes back into valid Mist source text, so that round-trip fidelity is maintained.

#### Acceptance Criteria

1. WHEN the Pretty_Printer encounters a `RegexLiteralExpression` node, THE Pretty_Printer SHALL emit the pattern enclosed in backtick delimiters (e.g., `` `[a-z]+` ``), preserving all escape sequences verbatim from the AST node.
2. WHEN the Pretty_Printer encounters a `RegexConstant` pattern node (regex literal used as a pattern in an `is` expression or switch arm), THE Pretty_Printer SHALL emit the regex literal in backtick delimiters using the same format as expression-position regex literals.
3. WHEN the Pretty_Printer encounters an empty regex literal (empty pattern string), THE Pretty_Printer SHALL emit two consecutive backticks (`` `` ``) with no content between them.
4. FOR ALL valid Mist ASTs containing regex literal expressions and regex patterns, parsing the pretty-printed output and then pretty-printing again SHALL produce identical text (round-trip property).

### Requirement 11: Regex and Refinement Types

**User Story:** As a Mist developer, I want to use regex patterns as refinement predicates on string types, so that I can define constrained string types like `Email` or `PhoneNumber`.

#### Acceptance Criteria

1. THE Grammar SHALL allow a regex literal as a valid refinement predicate on a `string` base type in a type alias declaration (e.g., `` type Email = string `^[^@]+@[^@]+$`; ``).
2. WHEN the Semantic_Model_Builder encounters a type alias with a regex refinement predicate, THE Semantic_Model_Builder SHALL validate that the regex pattern is syntactically valid and create a Refinement_Type that stores the base type (`string`) and the regex pattern.
3. IF the Semantic_Model_Builder encounters a regex refinement predicate on a non-`string` base type, THEN THE Semantic_Model_Builder SHALL emit a diagnostic error indicating that regex refinement predicates are only valid on `string` types.
4. IF the Semantic_Model_Builder encounters a regex refinement predicate whose pattern is not a syntactically valid regular expression, THEN THE Semantic_Model_Builder SHALL emit a diagnostic error indicating the invalid regex pattern.
5. WHEN a string literal is assigned to a regex-refined type, THE Type_Checker SHALL evaluate the regex against the literal value at compile time using Full_Match semantics (the entire literal must match the pattern) and emit a diagnostic error if the literal does not match.
6. WHEN a non-constant string expression is assigned to a regex-refined type, THE Type_Checker SHALL emit a diagnostic error indicating that the assignment requires pattern matching validation (e.g., `if (input is Email email) { ... }`).
7. WHEN pattern matching tests a string value against a regex-refined type (e.g., `input is Email`), THE runtime SHALL evaluate the regex predicate against the string value using Full_Match semantics (the entire string must match the pattern for the test to succeed).

### Requirement 12: TextMate Grammar Update

**User Story:** As a Mist developer using VS Code, I want regex literals and the `regex` keyword to be syntax-highlighted correctly, so that regex patterns are visually distinct in the editor.

#### Acceptance Criteria

1. THE TextMate grammar SHALL recognize `regex` as a type keyword and apply the scope `storage.type.primitive.mist`, by including it in the same pattern rule as the other primitive type keywords (`int`, `string`, `bool`, etc.).
2. THE TextMate grammar SHALL define a begin/end pattern that matches single-line backtick-delimited content (`` ` `` to `` ` ``) and applies the scope `string.regexp.mist` to the entire regex literal including delimiters.
3. THE TextMate grammar SHALL highlight the following escape sequences within regex literals with the scope `constant.character.escape.mist`: backslash followed by any of `` ` ``, `\`, `d`, `w`, `s`, `D`, `W`, `S`, `n`, `r`, `t`, `b`, `B`, `.`, `^`, `$`, `*`, `+`, `?`, `(`, `)`, `[`, `]`, `{`, `}`, `|`, or `/`.
4. THE TextMate grammar SHALL include the regex literal pattern after the `comments`, `interpolated-strings`, and `strings` rules in the top-level patterns list, so that backtick characters appearing inside a string literal or comment are consumed by those higher-priority rules first.
5. IF a backtick-delimited regex literal contains an unrecognized escape sequence (backslash followed by a character not in the set defined in criterion 3), THEN THE TextMate grammar SHALL NOT apply the `constant.character.escape.mist` scope to that sequence.

### Requirement 13: Sample Application

**User Story:** As a Mist developer, I want a comprehensive sample application demonstrating regex literals, so that I can see every supported usage pattern in one place and verify correctness.

#### Acceptance Criteria

1. A sample project SHALL exist at `samples/RegexLiterals/Program.mist` with an accompanying `mist.toml` project file following the same structure as other sample projects.
2. The sample SHALL demonstrate declaring a variable with a regex literal (`` var pattern = `[a-z]+`; ``).
3. The sample SHALL demonstrate declaring a variable with explicit `regex` type annotation (`` regex pattern = `[a-z]+`; ``).
4. The sample SHALL demonstrate implicit conversion from `regex` to `string` (assigning a regex value to a string variable).
5. The sample SHALL demonstrate explicit conversion from `string` to `regex` (casting a string variable to regex with `(regex)myString`).
6. The sample SHALL demonstrate a string literal assigned to a `regex` variable without explicit cast (compile-time validated shorthand).
6. The sample SHALL demonstrate calling `IsMatch` on a regex value.
7. The sample SHALL demonstrate calling `IsFullMatch` on a regex value.
8. The sample SHALL demonstrate accessing the `Pattern` property on a regex value.
9. The sample SHALL demonstrate regex literal pattern matching with `is` expression (e.g., `` if (input is `[a-z]+`) ``).
10. The sample SHALL demonstrate regex literal pattern matching in a switch expression.
11. The sample SHALL demonstrate a regex-refined string type (e.g., `` type Email = string `^[^@]+@[^@]+$`; ``) and pattern matching against it.
12. The sample SHALL demonstrate passing a regex value to a method expecting a string parameter (implicit conversion).
13. The sample SHALL demonstrate passing a string literal to a method expecting a regex parameter (compile-time validated).
14. The sample SHALL demonstrate passing a string variable to a method expecting a regex parameter using explicit cast.
15. Each demonstrated feature usage (criteria 2–14) SHALL print at least one output line to stdout in `key=value` format, preceded by a section header line in `--- Section Name ---` format, so that correctness is observable and diff-comparable against the baseline.
16. A baseline file SHALL exist at `samples/__baselines__/RegexLiterals.txt` containing the expected stdout output, with content matching the actual output produced by running the sample.
17. The sample SHALL include a `public static void RunDemo()` entry point and a `public static void Main(string[] args)` method that calls `RunDemo()`.
18. `samples/AllFeatures/Program.mist` SHALL call `RegexLiterals.Program.RunDemo()` to include the feature in the full-suite sample run.
19. `samples/AllFeatures/mist.toml` SHALL be updated to add a `[[references]]` entry pointing to the RegexLiterals sample project.
20. WHEN the sample is compiled and run using the Mist compiler, THE System SHALL produce a zero exit code and emit no diagnostic errors or warnings.

### Requirement 14: Feature Coverage Documentation Update

**User Story:** As a Mist project maintainer, I want the CSHARP_FEATURES.md document updated to reflect the newly implemented regex type feature, so that the feature tracking document stays accurate.

#### Acceptance Criteria

1. WHEN the regex type feature is implemented, THE Compiler team SHALL add a `Regex Literals` row to the `Expressions > Literals` table in CSHARP_FEATURES.md with status `✅` and a Notes column value describing the feature.
2. WHEN the `Regex Literals` entry is added, THE Compiler team SHALL update the `Summary` table row for `Expressions — Literals` by incrementing the `✅ Implemented` count by 1.
3. IF the `Regex Literals` feature was previously listed in CSHARP_FEATURES.md with status `🔲`, THEN THE Compiler team SHALL decrement the `🔲 Remaining` count by 1 in the corresponding `Expressions — Literals` summary row.
