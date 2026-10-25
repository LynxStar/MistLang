# Requirements Document

## Introduction

This document specifies the requirements for adding exhaustive switch expressions to the Mist programming language. Switch expressions provide a concise, expression-level construct for multi-arm pattern matching that returns a value. Unlike C#'s switch expression, Mist's switch expression enforces Rust-style exhaustiveness checking: the compiler verifies at compile time that the arms of a switch expression collectively cover all possible values of the scrutinee type. If any value could reach the end of the switch without matching an arm, the compiler emits a diagnostic error.

The syntax follows C#'s postfix form (`expr switch { arms }`), where each arm consists of a pattern, an optional `when` guard, and a result expression separated by `=>`. The existing pattern matching infrastructure (type patterns, constant patterns, var patterns, discard patterns, property patterns, relational patterns, logical patterns, and list patterns) is reused as the pattern language within switch arms.

Exhaustiveness checking operates over the scrutinee's static type. For `bool`, both `true` and `false` must be covered. For type unions (`int | string`), every constituent type must be covered. For numeric and string types, a wildcard (`_`) or `var` pattern is required unless the arms provably cover all values. A discard pattern (`_`) or `var` pattern serves as the universal catch-all. The exhaustiveness checker reports which cases are missing when coverage is incomplete.

This feature touches every stage of the compiler pipeline: new keywords and grammar rules in Storm, AST nodes from the parser, pretty-printer support for round-trip fidelity, semantic model construction for arm result type unification, type-checker validation of exhaustiveness and pattern correctness, and C# code emission that lowers Mist switch expressions to C# switch expressions.

## Glossary

- **Compiler**: The Mist compiler pipeline, including lexing, parsing, semantic model construction, analysis, and C# code emission.
- **Grammar**: The Storm grammar definition that specifies the syntax of the Mist language (`Mist.storm` and `keywords.storm`).
- **Parser**: The component that reads Mist source tokens and produces an abstract syntax tree (AST) conforming to the Grammar.
- **Pretty_Printer**: The component (`MistPrettyPrinter`) that formats a Mist AST back into valid Mist source text.
- **Semantic_Model_Builder**: The component (`SemanticModelBuilder`) that constructs the semantic model from the AST.
- **Type_Checker**: The analysis component (`TypeCheckerAnalyzer`) that validates type rules against the semantic model.
- **Emitter**: The C# code emission component (`CSharpEmitter` / `CSharpSourceWriter`) that translates the Mist AST into C# source files.
- **Switch_Expression**: An expression of the form `scrutinee switch { arm1, arm2, ..., armN }` that evaluates the scrutinee, matches it against each arm's pattern in order, and returns the result expression of the first matching arm.
- **Scrutinee**: The expression being matched in a Switch_Expression, appearing before the `switch` keyword.
- **Switch_Arm**: A single branch within a Switch_Expression, consisting of a pattern, an optional When_Guard, the `=>` token, and a result expression.
- **When_Guard**: An optional boolean condition on a Switch_Arm, written as `when condition` after the pattern, that must evaluate to `true` for the arm to match.
- **Exhaustiveness_Checker**: The compiler component that verifies a Switch_Expression's arms collectively cover all possible values of the Scrutinee's static type.
- **Wildcard_Pattern**: The discard pattern `_` that matches any value without binding a variable. Serves as a catch-all arm.
- **Catch_All_Arm**: A Switch_Arm whose pattern is a Wildcard_Pattern or a `var` pattern, guaranteeing it matches any value not matched by preceding arms.
- **Result_Type**: The unified type of a Switch_Expression, determined by finding the common type of all arm result expressions.
- **Type_Union**: A Mist type of the form `T1 | T2 | ... | Tn` representing a value that is one of the constituent types.
- **TextMate_Grammar**: The TextMate grammar definition at `Editor/mist-syntax-highlighting/syntaxes/mist.tmLanguage.json` that provides syntax highlighting for Mist source files in VS Code.
- **Sample_Application**: A self-contained Mist program in the `samples/` directory that demonstrates language features and serves as an integration test.

## Requirements

### Requirement 1: Switch Expression Keywords and Tokens

**User Story:** As a compiler developer, I want the Storm grammar to define the `switch` and `when` keyword tokens, so that the lexer can recognize switch expression syntax.

#### Acceptance Criteria

1. THE Grammar SHALL define a `SWITCH` keyword token with the value `"switch"`.
2. THE Grammar SHALL define a `WHEN` keyword token with the value `"when"`.
3. THE Grammar SHALL reuse the existing `FATARROW` (`=>`) token for separating patterns from result expressions in switch arms.

### Requirement 2: Switch Expression Grammar

**User Story:** As a Mist developer, I want to write switch expressions using postfix `switch { }` syntax with pattern arms, so that I can perform multi-arm pattern matching that returns a value.

#### Acceptance Criteria

1. THE Grammar SHALL define a `SwitchExpression` rule as a postfix expression consisting of an expression (the Scrutinee), the `SWITCH` keyword, an opening brace, a comma-separated list of Switch_Arms, and a closing brace.
2. THE Grammar SHALL define a `SwitchArm` rule consisting of a Pattern, an optional When_Guard, the `FATARROW` token, and a result expression.
3. THE Grammar SHALL define the When_Guard as the `WHEN` keyword followed by a boolean expression.
4. THE Grammar SHALL allow trailing commas after the last Switch_Arm in the arm list.
5. THE Grammar SHALL allow zero arms in a switch expression (the exhaustiveness checker will reject it unless the scrutinee type is uninhabited).
6. WHEN a switch expression is parsed, THE Parser SHALL produce an AST node that captures the Scrutinee expression and the ordered list of Switch_Arms, each containing its pattern, optional guard expression, and result expression.

### Requirement 3: Switch Expression Precedence and Associativity

**User Story:** As a Mist developer, I want switch expressions to integrate naturally with other expressions, so that I can use them in assignments, arguments, and compound expressions without ambiguity.

#### Acceptance Criteria

1. THE Grammar SHALL place the `switch` postfix operator at a precedence level that allows `expr switch { ... }` to be used as the right-hand side of an assignment (e.g., `var result = x switch { ... };`).
2. THE Grammar SHALL allow the Scrutinee of a switch expression to be any expression that does not itself contain an unparenthesized switch expression at the same level (preventing ambiguous nesting).
3. THE Grammar SHALL allow switch expressions to appear as arguments to method calls (e.g., `Console.WriteLine(x switch { ... })`).
4. THE Grammar SHALL allow switch expressions to appear as elements in tuple literals (e.g., `(x switch { ... }, y)`).
5. WHEN a switch expression is used in a context requiring a statement terminator, THE Parser SHALL require a semicolon after the closing brace (e.g., `var r = x switch { ... };`).

### Requirement 4: Pattern Reuse in Switch Arms

**User Story:** As a Mist developer, I want to use all existing pattern types in switch arms, so that I can leverage type patterns, constant patterns, property patterns, relational patterns, logical patterns, list patterns, var patterns, and discard patterns within switch expressions.

#### Acceptance Criteria

1. THE Grammar SHALL allow Type_Patterns (e.g., `int i`) in Switch_Arm patterns.
2. THE Grammar SHALL allow Constant_Patterns (e.g., `42`, `"hello"`, `true`) in Switch_Arm patterns.
3. THE Grammar SHALL allow Var_Patterns (e.g., `var x`) in Switch_Arm patterns.
4. THE Grammar SHALL allow Discard_Patterns (`_`) in Switch_Arm patterns.
5. THE Grammar SHALL allow Property_Patterns (e.g., `{ Length: > 5 }`) in Switch_Arm patterns.
6. THE Grammar SHALL allow Relational_Patterns (e.g., `> 0`, `<= 100`) in Switch_Arm patterns.
7. THE Grammar SHALL allow Logical_Patterns (e.g., `> 0 and < 100`, `1 or 2 or 3`) in Switch_Arm patterns.
8. THE Grammar SHALL allow List_Patterns (e.g., `[1, 2, ..]`) in Switch_Arm patterns.
9. WHEN a pattern in a Switch_Arm introduces a pattern variable (via a type pattern or var pattern), THE Semantic_Model_Builder SHALL make that variable available within the arm's When_Guard expression and result expression.

### Requirement 5: Switch Expression Type Inference

**User Story:** As a compiler developer, I want the type checker to infer the result type of a switch expression from its arms, so that the expression integrates with Mist's type system.

#### Acceptance Criteria

1. THE Type_Checker SHALL determine the Result_Type of a Switch_Expression by finding the common type of all arm result expressions.
2. WHEN all arm result expressions have the same type, THE Type_Checker SHALL use that type as the Result_Type.
3. WHEN arm result expressions have different types that share a common base type or implicit conversion target, THE Type_Checker SHALL use the common type as the Result_Type.
4. IF no common type can be determined for the arm result expressions, THEN THE Type_Checker SHALL report a diagnostic error indicating that the switch arms have incompatible types, listing the conflicting types.
5. THE Type_Checker SHALL verify that each arm's result expression is assignable to the inferred Result_Type.

### Requirement 6: Exhaustiveness Checking

**User Story:** As a Mist developer, I want the compiler to verify that my switch expression covers all possible values of the scrutinee type, so that I get a compile-time error instead of a runtime exception when a case is missing.

#### Acceptance Criteria

1. THE Exhaustiveness_Checker SHALL verify that the Switch_Arms of a Switch_Expression collectively cover all possible values of the Scrutinee's static type.
2. WHEN the Scrutinee type is `bool`, THE Exhaustiveness_Checker SHALL require that both `true` and `false` are covered by the arms (either by constant patterns or by a catch-all).
3. WHEN the Scrutinee type is a Type_Union (e.g., `int | string`), THE Exhaustiveness_Checker SHALL require that every constituent type is covered by at least one arm (either by type patterns or by a catch-all).
4. WHEN the Scrutinee type is a numeric type (`int`, `long`, `double`, etc.), a `string`, or any type with an unbounded value space, THE Exhaustiveness_Checker SHALL require a Catch_All_Arm (`_` or `var` pattern) unless the arms provably cover all values through constant patterns or relational patterns.
5. WHEN a Switch_Arm contains a When_Guard, THE Exhaustiveness_Checker SHALL NOT consider that arm as definitively covering its pattern's value space (guards may evaluate to false at runtime).
6. IF the Switch_Arms do not exhaustively cover the Scrutinee type, THEN THE Exhaustiveness_Checker SHALL report a diagnostic error indicating which cases are not covered.
7. THE Exhaustiveness_Checker SHALL recognize a `_` (discard) pattern as covering all remaining values of any type.
8. THE Exhaustiveness_Checker SHALL recognize a `var` pattern as covering all remaining values of any type.

### Requirement 7: Unreachable Arm Detection

**User Story:** As a Mist developer, I want the compiler to warn me when a switch arm can never be reached, so that I can identify dead code in my pattern matching logic.

#### Acceptance Criteria

1. WHEN a Switch_Arm's pattern is subsumed by a preceding arm's pattern (the preceding arm matches all values that the later arm would match), THE Type_Checker SHALL report a diagnostic warning indicating the arm is unreachable.
2. WHEN a Catch_All_Arm (`_` or `var` pattern) appears before other arms, THE Type_Checker SHALL report a diagnostic warning for each subsequent arm indicating it is unreachable.
3. WHEN a constant pattern arm appears after another arm with the same constant pattern, THE Type_Checker SHALL report a diagnostic warning indicating the duplicate arm is unreachable.

### Requirement 8: When Guard Type Checking

**User Story:** As a Mist developer, I want the compiler to validate that when guards are boolean expressions and that pattern variables are accessible within them, so that I get clear errors for invalid guard conditions.

#### Acceptance Criteria

1. THE Type_Checker SHALL verify that the When_Guard expression evaluates to type `bool`.
2. IF a When_Guard expression does not evaluate to `bool`, THEN THE Type_Checker SHALL report a diagnostic error indicating the guard must be a boolean expression.
3. WHEN a Switch_Arm pattern introduces a pattern variable (e.g., `int n when n > 0`), THE Type_Checker SHALL make the pattern variable available in the When_Guard's scope with the type determined by the pattern.
4. WHEN a Switch_Arm pattern introduces a pattern variable, THE Type_Checker SHALL make the pattern variable available in the arm's result expression scope.

### Requirement 9: Switch Expression Semantic Model

**User Story:** As a compiler developer, I want the semantic model to represent switch expressions with their scrutinee, arms, patterns, guards, and result types, so that downstream passes can analyze and emit them.

#### Acceptance Criteria

1. THE Semantic_Model_Builder SHALL create a semantic representation for each Switch_Expression that includes the Scrutinee expression, the ordered list of Switch_Arms, and the inferred Result_Type.
2. THE Semantic_Model_Builder SHALL create a semantic representation for each Switch_Arm that includes the resolved pattern, the optional When_Guard expression, and the result expression.
3. THE Semantic_Model_Builder SHALL register pattern variables introduced by Switch_Arm patterns in a scope that encompasses the arm's guard and result expression.
4. THE Semantic_Model_Builder SHALL resolve the Scrutinee expression's type and make it available to the Exhaustiveness_Checker.

### Requirement 10: C# Code Emission for Switch Expressions

**User Story:** As a compiler developer, I want the C# emitter to produce valid C# switch expressions from Mist switch expressions, so that the transpiled code compiles and behaves correctly.

#### Acceptance Criteria

1. THE Emitter SHALL emit a Mist Switch_Expression as a C# switch expression using the syntax `scrutinee switch { arm1, arm2, ..., armN }`.
2. THE Emitter SHALL emit each Switch_Arm as a C# switch arm with the pattern, optional `when` guard, `=>` separator, and result expression.
3. THE Emitter SHALL emit Type_Patterns in switch arms as C# type patterns (e.g., `int i => ...`).
4. THE Emitter SHALL emit Constant_Patterns in switch arms as C# constant patterns (e.g., `42 => ...`).
5. THE Emitter SHALL emit Discard_Patterns in switch arms as C# discard patterns (e.g., `_ => ...`).
6. THE Emitter SHALL emit Var_Patterns in switch arms as C# var patterns (e.g., `var x => ...`).
7. THE Emitter SHALL emit Property_Patterns in switch arms as C# property patterns (e.g., `{ Length: > 5 } => ...`).
8. THE Emitter SHALL emit Relational_Patterns in switch arms as C# relational patterns (e.g., `> 0 => ...`).
9. THE Emitter SHALL emit Logical_Patterns in switch arms as C# logical patterns (e.g., `> 0 and < 100 => ...`).
10. THE Emitter SHALL emit List_Patterns in switch arms as C# list patterns (e.g., `[1, 2, ..] => ...`).
11. THE Emitter SHALL emit When_Guards as C# `when` clauses on switch arms (e.g., `int n when n > 0 => ...`).
12. WHEN the Scrutinee is a Type_Union, THE Emitter SHALL emit the switch expression against the union's helper type, using the union's discriminator properties for type pattern matching consistent with existing union emission patterns.

### Requirement 11: Switch Expression Pretty Printing

**User Story:** As a compiler developer, I want the pretty printer to format switch expression AST nodes back into Mist source text, so that round-trip fidelity is maintained.

#### Acceptance Criteria

1. THE Pretty_Printer SHALL format a Switch_Expression as `scrutinee switch { arm1, arm2, ..., armN }` with each arm on its own line and indented within the braces.
2. THE Pretty_Printer SHALL format each Switch_Arm as `pattern => resultExpr` with the pattern, fat arrow, and result expression on a single line.
3. THE Pretty_Printer SHALL format When_Guards as `pattern when guard => resultExpr`.
4. THE Pretty_Printer SHALL format trailing commas after each arm.
5. THE Pretty_Printer SHALL format all pattern types within switch arms using the existing pattern pretty-printing logic.
6. FOR ALL valid Switch_Expression AST nodes, parsing then pretty-printing then parsing SHALL produce an equivalent AST (round-trip property).

### Requirement 12: TextMate Grammar Update for Switch Expressions

**User Story:** As a Mist developer using VS Code, I want switch expression keywords to be syntax-highlighted correctly, so that switch expressions are visually distinct and readable in the editor.

#### Acceptance Criteria

1. THE TextMate_Grammar SHALL highlight the `switch` keyword using the `keyword.control.mist` scope.
2. THE TextMate_Grammar SHALL highlight the `when` keyword using the `keyword.control.mist` scope.
3. THE TextMate_Grammar SHALL highlight the `=>` fat arrow operator within switch arms using an operator scope (e.g., `keyword.operator.arrow.mist`).
4. WHEN `switch` or `when` appear inside string literals or comments, THE TextMate_Grammar SHALL NOT apply keyword highlighting to those occurrences.

### Requirement 13: Sample Application

**User Story:** As a Mist developer, I want a comprehensive sample application demonstrating switch expressions, so that I can see every supported usage pattern in one place and verify correctness.

#### Acceptance Criteria

1. A sample project SHALL exist at `samples/SwitchExpressions/Program.mist`.
2. The sample SHALL demonstrate a switch expression with constant patterns matching integer values.
3. The sample SHALL demonstrate a switch expression with constant patterns matching string values.
4. The sample SHALL demonstrate a switch expression with constant patterns matching boolean values (exhaustive without wildcard: `true` and `false` arms).
5. The sample SHALL demonstrate a switch expression with a discard pattern (`_`) as the catch-all arm.
6. The sample SHALL demonstrate a switch expression with a `var` pattern as the catch-all arm.
7. The sample SHALL demonstrate a switch expression with type patterns matching a Type_Union scrutinee (e.g., `int | string`), covering all constituent types.
8. The sample SHALL demonstrate a switch expression with relational patterns (e.g., `> 0`, `<= 100`).
9. The sample SHALL demonstrate a switch expression with logical patterns (e.g., `> 0 and < 100`).
10. The sample SHALL demonstrate a switch expression with property patterns (e.g., `{ Length: > 5 }`).
11. The sample SHALL demonstrate a switch expression with list patterns (e.g., `[1, 2, ..]`).
12. The sample SHALL demonstrate a switch expression with `when` guards on arms.
13. The sample SHALL demonstrate a switch expression used as the right-hand side of a variable assignment.
14. The sample SHALL demonstrate a switch expression used as a method argument.
15. The sample SHALL demonstrate a switch expression with a tuple scrutinee (e.g., `(x, y) switch { ... }`).
16. The sample SHALL include a `public static void RunDemo()` entry point that the AllFeatures orchestrator can call.
17. Each demonstrated permutation SHALL print labeled output to stdout so correctness is observable.
18. The sample SHALL compile and run without errors using the Mist compiler.

### Requirement 14: AllFeatures Sample Integration

**User Story:** As a Mist developer, I want the AllFeatures sample to include switch expression usage alongside existing features, so that I can see how switch expressions integrate with the rest of the language.

#### Acceptance Criteria

1. `samples/AllFeatures/Program.mist` SHALL call `SwitchExpressions.Program.RunDemo()` to include the feature in the full-suite sample run.
2. THE AllFeatures Sample_Application SHALL continue to compile and produce correct output for all previously demonstrated features.

### Requirement 15: Exhaustiveness Checking for Bool Scrutinee

**User Story:** As a Mist developer, I want the compiler to recognize when both `true` and `false` are covered for a bool scrutinee, so that I don't need a redundant wildcard arm.

#### Acceptance Criteria

1. WHEN the Scrutinee type is `bool` and the arms include constant patterns for both `true` and `false`, THE Exhaustiveness_Checker SHALL consider the switch exhaustive without requiring a catch-all arm.
2. WHEN the Scrutinee type is `bool` and only one of `true` or `false` is covered without a catch-all, THE Exhaustiveness_Checker SHALL report a diagnostic error naming the missing boolean value.
3. WHEN the Scrutinee type is `bool` and a catch-all arm is present, THE Exhaustiveness_Checker SHALL consider the switch exhaustive regardless of which boolean constants are explicitly matched.

### Requirement 16: Exhaustiveness Checking for Type Union Scrutinee

**User Story:** As a Mist developer, I want the compiler to verify that all constituent types of a union are covered in a switch expression, so that I handle every possible type variant.

#### Acceptance Criteria

1. WHEN the Scrutinee type is a Type_Union and the arms include type patterns covering every constituent type, THE Exhaustiveness_Checker SHALL consider the switch exhaustive without requiring a catch-all arm.
2. WHEN the Scrutinee type is a Type_Union and one or more constituent types are not covered by any arm, THE Exhaustiveness_Checker SHALL report a diagnostic error listing the uncovered constituent types.
3. WHEN the Scrutinee type is a Type_Union and a catch-all arm is present, THE Exhaustiveness_Checker SHALL consider the switch exhaustive regardless of which constituent types are explicitly matched.
4. WHEN the Scrutinee type is a Type_Union with nested unions (e.g., `(int | string) | bool`), THE Exhaustiveness_Checker SHALL flatten the union and check coverage of all leaf constituent types.

