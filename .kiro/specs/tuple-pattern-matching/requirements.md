# Requirements Document

## Introduction

This document specifies the requirements for adding tuple positional patterns to the Mist programming language. Tuple positional patterns allow developers to match tuple values by position using a parenthesized pattern syntax (e.g., `(> 0, > 0)`) instead of the current property pattern workaround (`{ Item1: > 0, Item2: > 0 }`). This is modeled after C#'s positional pattern syntax for tuples.

Currently, Mist supports tuples (value tuples with positional and named elements) and a rich pattern matching system (type patterns, constant patterns, relational patterns, logical patterns, property patterns, list patterns, var patterns, and discard patterns). However, matching tuple elements requires using property patterns with `Item1`, `Item2`, etc., which is verbose and unergonomic. Tuple positional patterns provide a natural, concise syntax that mirrors tuple literal construction.

The feature integrates with the existing pattern infrastructure: each element position within a tuple positional pattern accepts any valid subpattern (constants, relational, logical, var, discard, nested tuple patterns, etc.). Tuple positional patterns can appear in both `is` expressions and switch expression arms.

The implementation touches every stage of the compiler pipeline: new grammar rules in Storm for the positional pattern syntax, AST nodes from the parser, pretty-printer support for round-trip fidelity, semantic model construction for element binding, type-checker validation of arity and element type compatibility, and C# code emission that lowers Mist tuple positional patterns to C# positional patterns.

## Glossary

- **Compiler**: The Mist compiler pipeline, including lexing, parsing, semantic model construction, analysis, and C# code emission.
- **Grammar**: The Storm grammar definition that specifies the syntax of the Mist language (`Mist.storm` and `keywords.storm`).
- **Parser**: The component that reads Mist source tokens and produces an abstract syntax tree (AST) conforming to the Grammar.
- **Pretty_Printer**: The component (`MistPrettyPrinter`) that formats a Mist AST back into valid Mist source text.
- **Semantic_Model_Builder**: The component (`SemanticModelBuilder`) that constructs the semantic model from the AST.
- **Type_Checker**: The analysis component (`TypeCheckerAnalyzer`) that validates type rules against the semantic model.
- **Emitter**: The C# code emission component (`CSharpEmitter` / `CSharpSourceWriter`) that translates the Mist AST into C# source files.
- **Tuple_Positional_Pattern**: A pattern of the form `(subpattern1, subpattern2, ..., subpatternN)` that matches a tuple value by testing each element at its corresponding position against the respective subpattern.
- **Tuple_Type**: A structural value type composed of an ordered, fixed-length sequence of element types, written as `(T1, T2, ..., Tn)`.
- **Is_Expression**: An expression of the form `expression is pattern` that evaluates to `bool` and optionally introduces pattern variables into scope.
- **Switch_Expression**: An expression of the form `scrutinee switch { arm1, arm2, ..., armN }` that matches the scrutinee against each arm's pattern.
- **Switch_Arm**: A single branch within a Switch_Expression, consisting of a pattern, an optional when guard, `=>`, and a result expression.
- **Subpattern**: Any valid pattern that appears as an element within a Tuple_Positional_Pattern (constants, relational, logical, var, discard, type, property, nested tuple positional patterns, etc.).
- **Pattern_Variable**: A variable introduced by a type pattern or var pattern within a tuple positional pattern element, scoped to the then-branch or switch arm result.
- **Arity**: The number of elements in a tuple type or tuple positional pattern.

## Requirements

### Requirement 1: Tuple Positional Pattern Grammar

**User Story:** As a Mist developer, I want to write positional patterns using parenthesized syntax to match tuple elements by position, so that I can match tuples concisely without referencing `Item1`, `Item2`, etc.

#### Acceptance Criteria

1. THE Grammar SHALL define a `TuplePositionalPattern` alternative within the `PrimaryPattern` rule, consisting of `LEFT_PARENTHESIS`, a first element pattern, `COMMA`, a second element pattern, zero or more additional `COMMA`-separated element patterns, and `RIGHT_PARENTHESIS`, using the field naming convention `first`, `rest`, and `additional` consistent with existing tuple rules.
2. THE Grammar SHALL require a minimum of two element patterns within a `TuplePositionalPattern` and impose no upper bound on the number of element patterns.
3. THE Grammar SHALL allow each element position within a `TuplePositionalPattern` to contain any valid `Pattern` (the `Pattern` rule, which includes nested `TuplePositionalPattern`, relational, logical, constant, type, var, discard, and property patterns via its existing alternatives).
4. WHEN a `TuplePositionalPattern` is parsed, THE Parser SHALL produce an AST node that captures the ordered list of element subpatterns in positional fields named `first` (the first element pattern), `rest` (the second element pattern), and `additional` (zero or more remaining element patterns), preserving source order.
5. THE Grammar SHALL place the `TuplePositionalPattern` alternative before the `ParenthesizedPattern` alternative in the `PrimaryPattern` rule so that the presence of at least one comma within the parentheses selects the tuple interpretation, while a single pattern in parentheses without a comma continues to match as a `ParenthesizedPattern`.
6. THE Grammar SHALL NOT permit a trailing comma after the last element pattern in a `TuplePositionalPattern` (i.e., `(a, b,)` is a parse error).

### Requirement 2: Tuple Positional Pattern in Is Expressions

**User Story:** As a Mist developer, I want to use tuple positional patterns in `is` expressions, so that I can test whether a tuple's elements match specific patterns in a conditional.

#### Acceptance Criteria

1. WHEN an `is` expression has a Tuple_Positional_Pattern on the right-hand side, THE Parser SHALL parse the pattern as a Tuple_Positional_Pattern regardless of the scrutinee's static type (e.g., `point is (> 0, > 0)`).
2. WHEN a Tuple_Positional_Pattern contains one or more elements that use a var pattern (e.g., `point is (var x, var y)`), THE Semantic_Model_Builder SHALL introduce each pattern variable into the scope of the then-branch with the type of its corresponding tuple element position.
3. WHEN a Tuple_Positional_Pattern element uses a type pattern (e.g., `data is (int n, string s)` where the tuple has `unknown` elements), THE Semantic_Model_Builder SHALL introduce the pattern variables into the scope of the then-branch with the type declared in the type pattern.
4. WHEN a Tuple_Positional_Pattern element uses a discard pattern (e.g., `point is (_, > 0)`), THE Semantic_Model_Builder SHALL NOT introduce any variable for that position.
5. WHEN a Tuple_Positional_Pattern contains a mix of var patterns, type patterns, discard patterns, and non-binding patterns (e.g., `data is (var x, int n, _, > 0)`), THE Semantic_Model_Builder SHALL introduce only the variables from var and type pattern elements into the then-branch scope, each with its respective resolved type.

### Requirement 3: Tuple Positional Pattern in Switch Expressions

**User Story:** As a Mist developer, I want to use tuple positional patterns in switch expression arms, so that I can perform multi-arm matching on tuple values using concise positional syntax.

#### Acceptance Criteria

1. THE Grammar SHALL allow Tuple_Positional_Patterns as the pattern in a Switch_Arm.
2. WHEN a switch expression has a tuple-typed scrutinee and arms with Tuple_Positional_Patterns, THE Parser SHALL parse each arm's pattern as a Tuple_Positional_Pattern, including when other arms in the same switch expression use different pattern kinds (e.g., a discard or var catch-all arm).
3. WHEN a Tuple_Positional_Pattern in a switch arm introduces pattern variables (via var patterns or type patterns), THE Semantic_Model_Builder SHALL make those variables available in that arm's when guard and result expression, and SHALL NOT make them visible in any other arm.
4. THE Exhaustiveness_Checker SHALL treat a Tuple_Positional_Pattern whose every element is a catch-all subpattern (discard `_` or `var` pattern) as covering all values of the scrutinee's tuple type, provided the arm has no when guard.
5. IF a switch expression with a tuple-typed scrutinee has no unguarded arm that covers all tuple values (either a top-level catch-all pattern or a Tuple_Positional_Pattern with all catch-all elements), THEN THE Exhaustiveness_Checker SHALL report the switch expression as non-exhaustive.

### Requirement 4: Type Checker Validation for Tuple Positional Patterns

**User Story:** As a Mist developer, I want the type checker to validate tuple positional patterns against the scrutinee's tuple type, so that I get compile-time errors for arity mismatches and incompatible element patterns.

#### Acceptance Criteria

1. WHEN a Tuple_Positional_Pattern is applied to a scrutinee whose type is not a Tuple_Type and is not an ErrorType, THE Type_Checker SHALL emit a diagnostic error indicating that positional patterns require a tuple type and naming the actual scrutinee type.
2. WHEN a Tuple_Positional_Pattern has a different number of elements than the scrutinee's Tuple_Type arity, THE Type_Checker SHALL emit a diagnostic error indicating the arity mismatch, naming the expected element count (from the Tuple_Type) and the actual element count (from the pattern).
3. WHEN a Tuple_Positional_Pattern is applied to a scrutinee that is a Tuple_Type with matching arity, THE Type_Checker SHALL validate each element subpattern against the corresponding element type of the scrutinee's Tuple_Type, applying the same validation rules as for standalone patterns (relational patterns require a type that supports comparison operators, type patterns require compatible types, constant patterns require type-compatible values).
4. WHEN a Tuple_Positional_Pattern element contains a constant pattern whose type is incompatible with the corresponding tuple element type, THE Type_Checker SHALL emit a diagnostic error indicating the type mismatch at that position.
5. WHEN a Tuple_Positional_Pattern element contains a relational pattern applied to a tuple element whose type does not support comparison operators, THE Type_Checker SHALL emit a diagnostic error for that position.
6. IF the scrutinee's type is an ErrorType, THEN THE Type_Checker SHALL skip validation of the Tuple_Positional_Pattern and emit no diagnostics for that pattern (to suppress cascading errors).

### Requirement 5: Nested Tuple Positional Patterns

**User Story:** As a Mist developer, I want to nest tuple positional patterns within other tuple positional patterns, so that I can match nested tuple structures concisely.

#### Acceptance Criteria

1. THE Grammar SHALL allow a Tuple_Positional_Pattern as an element subpattern within another Tuple_Positional_Pattern at any nesting depth (e.g., `((> 0, > 0), "label")` matching a `((int, int), string)` tuple, or `(((1, 2), 3), 4)` matching a `(((int, int), int), int)` tuple).
2. THE Type_Checker SHALL recursively validate nested Tuple_Positional_Patterns by resolving the element type at each position and, if that element type is itself a Tuple_Type, validating the inner Tuple_Positional_Pattern's arity and element type compatibility against it.
3. IF a nested Tuple_Positional_Pattern is applied to an element position whose type is not a Tuple_Type, THEN THE Type_Checker SHALL emit a diagnostic error indicating the element position and that positional patterns require a tuple type at that position.
4. WHEN a nested Tuple_Positional_Pattern introduces pattern variables at any nesting depth, THE Semantic_Model_Builder SHALL introduce all such variables into the enclosing scope (then-branch for is expressions, or when guard and result expression for switch arms), with each variable typed according to the tuple element at its corresponding position and nesting level.
5. IF two pattern variables introduced at different nesting levels within the same Tuple_Positional_Pattern have the same name, THEN THE Type_Checker SHALL emit a diagnostic error indicating a duplicate pattern variable name.

### Requirement 6: Pretty-Printer Support for Tuple Positional Patterns

**User Story:** As a compiler developer, I want the pretty-printer to correctly format tuple positional patterns, so that round-trip printing preserves the source structure.

#### Acceptance Criteria

1. THE Pretty_Printer SHALL format a Tuple_Positional_Pattern as `(subpattern1, subpattern2, ..., subpatternN)` where elements are separated by `, ` (comma followed by a single space) with no trailing comma, and each element subpattern is formatted by the existing pattern emission logic for its kind.
2. THE Pretty_Printer SHALL format nested Tuple_Positional_Patterns by recursively applying criterion 1 to inner tuple patterns, producing naturally nested parentheses (e.g., `((> 0, > 0), "label")`).
3. WHEN a Mist AST containing a Tuple_Positional_Pattern is pretty-printed to source text and that source text is parsed and pretty-printed again, THE Pretty_Printer SHALL produce output identical to the first printed text (print-idempotence).

### Requirement 7: C# Code Emission for Tuple Positional Patterns

**User Story:** As a Mist developer, I want tuple positional patterns to be correctly transpiled to C# positional patterns, so that the compiled output behaves as specified.

#### Acceptance Criteria

1. THE Emitter SHALL emit a Mist Tuple_Positional_Pattern as a C# positional pattern consisting of an opening parenthesis, each element subpattern separated by a comma and a space, and a closing parenthesis (e.g., Mist `(> 0, > 0)` becomes C# `(> 0, > 0)`).
2. THE Emitter SHALL emit each element subpattern within the positional pattern by delegating to the existing pattern emission logic, supporting relational, constant, logical, var, discard, type, property, and list subpatterns.
3. THE Emitter SHALL emit nested Tuple_Positional_Patterns as nested C# positional patterns by recursively emitting the inner tuple positional pattern as an element (e.g., `((> 0, > 0), "label")`).
4. WHEN a Tuple_Positional_Pattern element introduces a pattern variable via a var pattern, THE Emitter SHALL emit it as `var identifier` within the positional pattern (e.g., `(var x, > 0)`).
5. WHEN a Tuple_Positional_Pattern element introduces a pattern variable via a type pattern, THE Emitter SHALL emit it as a C# type pattern with the type name followed by the identifier within the positional pattern (e.g., `(int n, string s)`).
6. WHEN a Tuple_Positional_Pattern element uses a discard, THE Emitter SHALL emit it as `_` within the C# positional pattern.
7. THE Emitter SHALL produce the same C# positional pattern syntax for a given Tuple_Positional_Pattern regardless of whether it appears in an `is` expression context or a switch arm context.

### Requirement 8: Tuple Positional Pattern Diagnostic Messages

**User Story:** As a Mist developer, I want clear and specific error messages when tuple positional patterns are used incorrectly, so that I can quickly identify and fix the issue.

#### Acceptance Criteria

1. WHEN a Tuple_Positional_Pattern is applied to a scrutinee whose type is not a Tuple_Type, THE Compiler SHALL emit an error-severity diagnostic that includes the actual scrutinee type name and states that positional patterns require a tuple type.
2. WHEN a Tuple_Positional_Pattern has a number of element subpatterns that differs from the scrutinee Tuple_Type's arity, THE Compiler SHALL emit an error-severity diagnostic that states the expected arity (from the Tuple_Type) and the actual arity (from the pattern), along with the source location of the pattern.
3. WHEN a Tuple_Positional_Pattern element has a type-incompatible subpattern, THE Compiler SHALL emit an error-severity diagnostic that names the element position (1-based), the expected element type from the Tuple_Type, and a description of the incompatibility kind (e.g., constant type mismatch, relational operator applied to non-comparable type, type pattern with incompatible target type).
4. IF multiple elements within a single Tuple_Positional_Pattern each have independent errors, THEN THE Compiler SHALL emit a separate diagnostic for each failing element rather than stopping at the first error.

### Requirement 9: Sample Application

**User Story:** As a Mist developer, I want a comprehensive sample application demonstrating tuple positional patterns, so that I can see every supported usage pattern in one place and verify correctness.

#### Acceptance Criteria

1. A sample project SHALL exist at `samples/TuplePatternMatching/Program.mist` with an accompanying `samples/TuplePatternMatching/mist.toml` project file whose name is `"TuplePatternMatching"`.
2. The sample SHALL demonstrate a tuple positional pattern with constant subpatterns in an `is` expression (e.g., matching a `(int, int)` value against `(1, 2)`).
3. The sample SHALL demonstrate a tuple positional pattern with relational subpatterns (e.g., `(> 0, > 0)`).
4. The sample SHALL demonstrate a tuple positional pattern with logical subpatterns (e.g., `(>= 0 and <= 10, > 0)`).
5. The sample SHALL demonstrate a tuple positional pattern with var subpatterns that bind variables.
6. The sample SHALL demonstrate a tuple positional pattern with discard subpatterns.
7. The sample SHALL demonstrate a tuple positional pattern with type subpatterns (matching tuples with `unknown` elements).
8. The sample SHALL demonstrate nested tuple positional patterns (e.g., matching `((int, int), string)`).
9. The sample SHALL demonstrate tuple positional patterns in switch expression arms with at least 3 arms including a catch-all.
10. The sample SHALL demonstrate tuple positional patterns combined with when guards in switch arms.
11. The sample SHALL demonstrate a 3-element tuple positional pattern.
12. WHEN a tuple positional pattern demonstration executes, THE System SHALL print a section header line in the format `"--- <description> ---"` followed by one or more `Console.WriteLine` calls that include the matched values or bound variables, so that each section's pass/fail can be verified by inspecting stdout line-by-line.
13. The sample SHALL include a `public static void RunDemo()` entry point that invokes all demonstrations.
14. `samples/AllFeatures/Program.mist` SHALL call `TuplePatternMatching.Program.RunDemo()` to include the feature in the full-suite sample run.
15. The sample SHALL compile with zero diagnostics and run with a zero exit code using the Mist compiler.

#### Proof Code

```mist
// TuplePatternMatching — Demonstrates tuple positional patterns in Mist.

using Standard;

namespace TuplePatternMatching;

public class Program
{
    public static void Main(string[] args)
    {
        RunDemo();
    }

    public static void RunDemo()
    {
        Console.WriteLine("=== Tuple Pattern Matching Sample ===");

        // PROOF: Tuple positional pattern with constant subpatterns in is expression
        Console.WriteLine("--- Constant subpatterns ---");
        var origin = (0, 0);
        if (origin is (0, 0))
        {
            Console.WriteLine("origin is (0, 0)");
        }

        var point1 = (3, 4);
        if (point1 is (3, 4))
        {
            Console.WriteLine("point1 is (3, 4)");
        }

        // PROOF: Tuple positional pattern with relational subpatterns
        Console.WriteLine("--- Relational subpatterns ---");
        var point2 = (5, 7);
        if (point2 is (> 0, > 0))
        {
            Console.WriteLine("point2 is in quadrant I");
        }

        var point3 = (-2, 3);
        if (point3 is (< 0, > 0))
        {
            Console.WriteLine("point3 is in quadrant II");
        }

        // PROOF: Tuple positional pattern with logical subpatterns
        Console.WriteLine("--- Logical subpatterns ---");
        var score = (85, 90);
        if (score is (>= 80 and <= 100, >= 80 and <= 100))
        {
            Console.WriteLine("both scores are in [80, 100]");
        }

        // PROOF: Tuple positional pattern with var subpatterns binding variables
        Console.WriteLine("--- Var subpatterns ---");
        var data = (42, "hello");
        if (data is (var n, var s))
        {
            Console.WriteLine($"n={n} s={s}");
        }

        // PROOF: Tuple positional pattern with discard subpatterns
        Console.WriteLine("--- Discard subpatterns ---");
        var pair = (10, 20);
        if (pair is (_, > 15))
        {
            Console.WriteLine("second element > 15");
        }

        // PROOF: Tuple positional pattern with type subpatterns on unknown elements
        Console.WriteLine("--- Type subpatterns ---");
        (unknown, unknown) mystery = (123, "world");
        if (mystery is (int i, string str))
        {
            Console.WriteLine($"i={i} str={str}");
        }

        // PROOF: Nested tuple positional patterns
        Console.WriteLine("--- Nested tuple patterns ---");
        var nested = ((1, 2), "label");
        if (nested is ((> 0, > 0), "label"))
        {
            Console.WriteLine("nested: both positive with label");
        }

        // PROOF: Tuple positional patterns in switch expression arms
        Console.WriteLine("--- Switch expression ---");
        var coord = (0, 5);
        var quadrant = coord switch
        {
            (0, 0) => "origin",
            (> 0, > 0) => "quadrant I",
            (< 0, > 0) => "quadrant II",
            (< 0, < 0) => "quadrant III",
            (> 0, < 0) => "quadrant IV",
            (0, _) => "on y-axis",
            (_, 0) => "on x-axis",
        };
        Console.WriteLine($"({coord.Item1},{coord.Item2}) => {quadrant}");

        // PROOF: Tuple positional pattern with when guard in switch arm
        Console.WriteLine("--- When guard ---");
        var measurement = (100, 200);
        var sizeCategory = measurement switch
        {
            (var x, var y) when x + y > 250 => "large",
            (var x, var y) when x + y > 100 => "medium",
            (var _, var _) => "small",
        };
        Console.WriteLine($"({measurement.Item1},{measurement.Item2}) => {sizeCategory}");

        // PROOF: 3-element tuple positional pattern
        Console.WriteLine("--- 3-element tuple ---");
        var rgb = (255, 128, 0);
        if (rgb is (255, >= 0 and <= 255, 0))
        {
            Console.WriteLine("red-ish color with no blue");
        }

        // PROOF: Tuple positional patterns with enum member constants in switch arms
        // PROOF: Parser produces TuplePositionalPatternPrimaryPatternNode (not BareTypePatternPrimaryPatternNode)
        // PROOF: Emitter generates C# positional pattern in switch arm context
        Console.WriteLine("--- Enum tuple switch ---");
        var current = Direction.North;
        var target = Direction.East;
        var turnLabel = (current, target) switch
        {
            (Direction.North, Direction.East) => "right turn",
            (Direction.North, Direction.West) => "left turn",
            (Direction.North, Direction.South) => "u-turn",
            (Direction.South, Direction.North) => "u-turn",
            (Direction.East, Direction.South) => "right turn",
            (Direction.West, Direction.South) => "left turn",
            _ => "other",
        };
        Console.WriteLine($"({current},{target}) => {turnLabel}");

        // PROOF: String-backed enum in tuple positional patterns
        // PROOF: Enum with explicit string values resolves correctly in pattern position
        Console.WriteLine("--- String-backed enum tuple switch ---");
        var severity = LogLevel.Error;
        var category = LogCategory.Security;
        var action = (severity, category) switch
        {
            (LogLevel.Error, LogCategory.Security) => "page oncall immediately",
            (LogLevel.Error, LogCategory.Database) => "alert DBA team",
            (LogLevel.Warning, LogCategory.Security) => "log and review",
            (LogLevel.Info, _) => "store in archive",
            _ => "standard logging",
        };
        Console.WriteLine($"({severity},{category}) => {action}");

        // PROOF: Three-element tuple positional pattern in switch arm
        // PROOF: Parser handles arity > 2 correctly
        Console.WriteLine("--- Three-element tuple switch ---");
        var method = HttpMethod.Post;
        var status = HttpStatus.Forbidden;
        var retryable = false;
        var resolution = (method, status, retryable) switch
        {
            (HttpMethod.Get, HttpStatus.NotFound, _) => "return empty",
            (HttpMethod.Post, HttpStatus.Forbidden, _) => "escalate to auth",
            (_, HttpStatus.ServerError, true) => "retry with backoff",
            (_, HttpStatus.ServerError, false) => "fail permanently",
            _ => "handle normally",
        };
        Console.WriteLine($"({method},{status},{retryable}) => {resolution}");

        // PROOF: Mixed enum and discard subpatterns in tuple positional pattern switch arms
        Console.WriteLine("--- Mixed enum and discard ---");
        var state = Direction.North;
        var anyTarget = Direction.West;
        var description = (state, anyTarget) switch
        {
            (Direction.North, _) => "heading north",
            (_, Direction.South) => "going south",
            _ => "other direction",
        };
        Console.WriteLine($"({state},{anyTarget}) => {description}");

        // PROOF: Enum tuple switch with var binding in element position
        Console.WriteLine("--- Enum with var binding ---");
        var fromDir = Direction.East;
        var toDir = Direction.West;
        var moveDesc = (fromDir, toDir) switch
        {
            (Direction.East, var d) => $"leaving east, heading {d}",
            (var s, Direction.West) => $"from {s} to west",
            _ => "other move",
        };
        Console.WriteLine($"({fromDir},{toDir}) => {moveDesc}");

        // PROOF: Cross-type tuple (enum + int) in switch positional pattern
        // PROOF: Tuple elements of different types resolve independently
        Console.WriteLine("--- Cross-type tuple (enum + int) ---");
        var level = LogLevel.Warning;
        int code = 429;
        var response = (level, code) switch
        {
            (LogLevel.Error, 500) => "internal server error",
            (LogLevel.Error, 503) => "service unavailable",
            (LogLevel.Warning, 429) => "rate limited - slow down",
            (LogLevel.Warning, _) => "generic warning",
            _ => "no action",
        };
        Console.WriteLine($"({level},{code}) => {response}");

        Console.WriteLine("Done!");
    }
}

public enum Direction
{
    North,
    South,
    East,
    West,
}

public enum LogLevel : string
{
    Error = "ERROR",
    Warning = "WARN",
    Info = "INFO",
    Debug = "DEBUG",
}

public enum LogCategory : string
{
    Security,
    Database,
    Network,
    Application,
}

public enum HttpMethod : string
{
    Get,
    Post,
    Put,
    Delete,
}

public enum HttpStatus : int
{
    Ok = 200,
    NotFound = 404,
    Forbidden = 403,
    ServerError = 500,
}
```

**Expected output:**
```
=== Tuple Pattern Matching Sample ===
--- Constant subpatterns ---
origin is (0, 0)
point1 is (3, 4)
--- Relational subpatterns ---
point2 is in quadrant I
point3 is in quadrant II
--- Logical subpatterns ---
both scores are in [80, 100]
--- Var subpatterns ---
n=42 s=hello
--- Discard subpatterns ---
second element > 15
--- Type subpatterns ---
i=123 str=world
--- Nested tuple patterns ---
nested: both positive with label
--- Switch expression ---
(0,5) => on y-axis
--- When guard ---
(100,200) => large
--- 3-element tuple ---
red-ish color with no blue
--- Enum tuple switch ---
(North,East) => right turn
--- String-backed enum tuple switch ---
(Error,Security) => page oncall immediately
--- Three-element tuple switch ---
(Post,Forbidden,False) => escalate to auth
--- Mixed enum and discard ---
(North,West) => heading north
--- Enum with var binding ---
(East,West) => leaving east, heading West
--- Cross-type tuple (enum + int) ---
(Warning,429) => rate limited - slow down
Done!
```

### Requirement 10: Book Documentation Update

**User Story:** As a Mist developer reading the language book, I want the Pattern Matching and Switch Expressions chapters to document tuple positional patterns, so that I can learn the feature from the official reference.

#### Acceptance Criteria

1. THE Pattern Matching chapter (`book/src/type-system/pattern-matching.md`) SHALL include a new section titled "Tuple Positional Patterns" that explains matching tuple elements by position using parenthesized syntax.
2. THE Pattern Matching chapter's tuple positional patterns section SHALL include a snippet demonstrating a basic tuple positional pattern in an `is` expression (e.g., matching `(int, int)` with relational subpatterns), referenced via `{{#include}}` from `book/snippets/type-system/pattern-matching/tuple-positional.mist` with a corresponding `.cs` file.
3. THE Pattern Matching chapter's tuple positional patterns section SHALL include a snippet demonstrating nested tuple positional patterns (e.g., matching `((int, int), string)`), referenced via `{{#include}}` from `book/snippets/type-system/pattern-matching/tuple-positional-nested.mist` with a corresponding `.cs` file.
4. THE Switch Expressions chapter (`book/src/type-system/switch-expressions.md`) SHALL include a new section titled "Tuple Positional Pattern Arms" that explains using tuple positional patterns in switch arms.
5. THE Switch Expressions chapter's tuple positional pattern arms section SHALL include a snippet demonstrating a switch expression with tuple positional pattern arms (including a catch-all), referenced via `{{#include}}` from `book/snippets/type-system/switch-expressions/tuple-positional.mist` with a corresponding `.cs` file.
6. ALL book snippets for this feature SHALL be complete, compilable Mist programs following the book snippet policy (using Standard, namespace, class with Main) and SHALL have their `.cs` files generated by the SnippetEmitter tool.
7. THE new sections SHALL be placed after the existing "List Patterns" section in the Pattern Matching chapter and after the "Relational and Logical Patterns" section in the Switch Expressions chapter.

### Requirement 11: TextMate Grammar Update

**User Story:** As a Mist developer using VS Code, I want tuple positional patterns to be syntax-highlighted correctly, so that I get the same visual cues as other pattern matching constructs.

#### Acceptance Criteria

1. WHEN a tuple positional pattern (two or more comma-separated subpatterns enclosed in parentheses) appears in a pattern context, THE TextMate grammar SHALL assign the following scopes to subpattern elements: relational operators (`<`, `>`, `<=`, `>=`) receive `keyword.operator.comparison.mist`, numeric literals receive `constant.numeric.*.mist`, boolean literals receive `constant.language.boolean.mist`, `var` receives `keyword.other.mist`, type names (uppercase-initial identifiers) receive `entity.name.type.mist`, primitive type keywords receive `storage.type.primitive.mist`, and `_` (discard) receives `variable.language.mist`.
2. WHEN tuple positional patterns appear inside `is` expressions or switch arms, THE TextMate grammar SHALL NOT assign `entity.name.function.mist` to identifiers or type names that precede the opening parenthesis of the tuple pattern.
3. WHEN a tuple positional pattern contains subpatterns separated by commas, THE TextMate grammar SHALL continue applying pattern-context highlighting to all subpatterns after each comma rather than terminating the pattern context at the first comma.

