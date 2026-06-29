# Requirements Document

## Introduction

This document specifies the requirements for fixing the parser disambiguation of tuple positional patterns in switch expression arms in the Mist programming language. Currently, the parser treats `(WorkItemState.Pending, WorkItemState.Searching)` in a switch arm pattern position as a tuple type reference (`BareTypePatternPrimaryPatternNode` containing a `TupleSingleTypeRefSingleTypeRefNode`) instead of producing a `TuplePositionalPatternPrimaryPatternNode`. This happens because the `TypePattern`/`BareTypePattern` alternatives appear before `TuplePositionalPattern` in the `PrimaryPattern` rule, and the `TypeReference` grammar can match `(QualifiedIdentifier, QualifiedIdentifier)` as a tuple type.

The existing `TuplePositionalPatternPrimaryPatternNode` AST node is already handled by the semantic model builder (`CollectFromPrimaryPattern` and `ResolvePrimaryPattern`), the type checker, the exhaustiveness checker, the pretty printer, and the C# emitter for `is` expressions. The gap is exclusively that the parser grammar never produces this node when dotted-name patterns (like enum member references) appear inside parenthesized comma-separated lists in switch arm pattern position.

Once the grammar disambiguation is fixed, tuple positional patterns with enum member subpatterns, constants, relational operators, discards, var bindings, and nested tuples will work uniformly in both `is` expressions and switch expression arms. The C# emitter already handles this node — it emits `(pattern1, pattern2, ...)` — but needs verification in the switch arm context to confirm correct output.

This feature is motivated by the real-world use case in `Pendragon.Tests/StateMachinePropertyTests.mist`, where a state-machine transition validator uses `(current, target) switch { (State.A, State.B) => true, ... }` — valid C# pattern matching that currently fails in Mist.

## Glossary

- **Compiler**: The Mist compiler pipeline, including lexing, parsing, semantic model construction, analysis, and C# code emission.
- **Grammar**: The Storm grammar definition that specifies the syntax of the Mist language (`Mist.storm`).
- **Parser**: The component that reads Mist source tokens and produces an abstract syntax tree (AST) conforming to the Grammar.
- **Semantic_Model_Builder**: The component (`SemanticModelBuilder`) that constructs the semantic model from the AST, including `CollectFromPrimaryPattern` and `ResolvePrimaryPattern` methods.
- **Type_Checker**: The analysis component (`TypeCheckerAnalyzer`) that validates type rules against the semantic model.
- **Exhaustiveness_Checker**: The compiler component (`ExhaustivenessChecker`) that verifies switch expression arm coverage.
- **Emitter**: The C# code emission component (`CSharpEmitter` / `CSharpSourceWriter`) that translates the Mist AST into C# source files.
- **Pretty_Printer**: The component (`MistPrettyPrinter`) that formats a Mist AST back into valid Mist source text.
- **TuplePositionalPatternPrimaryPatternNode**: The AST node representing a positional tuple pattern `(subpattern1, subpattern2, ...)` that matches tuple elements by position.
- **BareTypePatternPrimaryPatternNode**: The AST node representing a type-only pattern (no binding variable), which currently incorrectly captures tuple-like parenthesized expressions in switch arms.
- **TupleSingleTypeRefSingleTypeRefNode**: The AST node for a tuple type reference `(T1, T2, ...)` that erroneously appears inside `BareTypePatternPrimaryPatternNode` when enum member access patterns are used in parenthesized form.
- **Switch_Arm**: A single branch within a switch expression, consisting of a pattern, an optional when guard, `=>`, and a result expression.
- **Enum_Member_Pattern**: A constant pattern that references an enum variant via qualified access (e.g., `WorkItemState.Pending`), resolved during semantic analysis to the enum member's value.
- **PrimaryPattern_Rule**: The Storm grammar rule that defines the alternatives for primary patterns, where disambiguation between type references and positional patterns must occur.
- **Sample_Application**: A self-contained Mist program in the `samples/` directory that demonstrates language features and serves as an integration test.
- **TextMate_Grammar**: The TextMate grammar definition at `Editor/mist-syntax-highlighting/syntaxes/mist.tmLanguage.json` that provides syntax highlighting for Mist source files in VS Code.

## Requirements

### Requirement 1: Grammar Disambiguation for Tuple Positional Patterns

**User Story:** As a Mist developer, I want `(pattern, pattern, ...)` in a switch arm pattern position to be parsed as a tuple positional pattern rather than a tuple type reference, so that I can use Rust/C#-style tuple matching in switch expressions.

#### Acceptance Criteria

1. WHEN the Parser encounters a parenthesized comma-separated list in pattern position (switch arm pattern or `is` expression right-hand side) where the elements contain qualified identifiers (e.g., `EnumName.Variant`), THE Parser SHALL produce a `TuplePositionalPatternPrimaryPatternNode` rather than a `BareTypePatternPrimaryPatternNode` containing a `TupleSingleTypeRefSingleTypeRefNode`.
2. WHEN the Parser encounters `(pattern, pattern, ...)` in pattern position where elements are constants, relational patterns, discard patterns, var patterns, or any other valid subpattern, THE Parser SHALL produce a `TuplePositionalPatternPrimaryPatternNode` with each element captured as a subpattern.
3. THE Grammar SHALL ensure that the `TuplePositionalPattern` alternative in the `PrimaryPattern` rule takes priority over the `BareTypePattern` alternative when the input is a parenthesized comma-separated list of patterns, by reordering the alternatives or restructuring the grammar to prevent the `TypeReference` path from consuming tuple positional pattern syntax.
4. THE Grammar change SHALL NOT break parsing of actual tuple type patterns where a genuine tuple type reference is followed by a binding identifier (e.g., `(int, string) pair` remains a valid `TypePattern`).
5. THE Grammar change SHALL NOT break parsing of parenthesized patterns (single pattern in parentheses without a comma, e.g., `(> 0)` matching a `ParenthesizedPattern`).
6. THE Grammar change SHALL NOT alter the behavior of tuple type references in non-pattern positions (type annotations, method parameters, return types, variable declarations).

### Requirement 2: Enum Member Patterns in Tuple Positional Pattern Elements

**User Story:** As a Mist developer, I want to use enum member references (e.g., `WorkItemState.Pending`) as elements inside tuple positional patterns in switch arms, so that I can match state-machine transitions and multi-field enum combinations concisely.

#### Acceptance Criteria

1. WHEN a tuple positional pattern element is a qualified identifier referencing an enum member (e.g., `WorkItemState.Pending`), THE Semantic_Model_Builder SHALL resolve the element as a constant pattern containing the enum member's value, using the same namespace-aware enum resolution used for enum patterns in `is` expressions.
2. WHEN a switch expression arm uses a tuple positional pattern with enum member elements (e.g., `(WorkItemState.Pending, WorkItemState.Searching) => true`), THE Type_Checker SHALL validate that each enum member element is type-compatible with the corresponding tuple element type of the scrutinee.
3. WHEN multiple switch arms each use tuple positional patterns with different enum member combinations, THE Exhaustiveness_Checker SHALL recognize each arm as covering only the specific constant values named, and SHALL require a catch-all arm or complete enum coverage for exhaustiveness.
4. IF an enum member element references a member that does not exist on the enum type, THEN THE Compiler SHALL emit an error-severity diagnostic naming the unresolved member and the enum type.

### Requirement 3: C# Emission of Tuple Positional Patterns in Switch Arms

**User Story:** As a compiler developer, I want the C# emitter to produce valid C# positional tuple patterns in switch arm context, so that transpiled switch expressions compile and behave correctly.

#### Acceptance Criteria

1. WHEN a switch arm contains a `TuplePositionalPatternPrimaryPatternNode`, THE Emitter SHALL emit the pattern as a C# positional pattern `(subpattern1, subpattern2, ...)` within the switch arm, using the same emission logic used for tuple positional patterns in `is` expression context.
2. WHEN a tuple positional pattern element in a switch arm is an enum member reference (e.g., `WorkItemState.Pending`), THE Emitter SHALL emit the fully-qualified enum member name as a constant pattern within the C# positional pattern (e.g., `(WorkItemState.Pending, WorkItemState.Searching)`).
3. WHEN a tuple positional pattern in a switch arm contains mixed element subpatterns (enum members, relational patterns, discards, var bindings), THE Emitter SHALL emit each element using the appropriate C# pattern syntax (e.g., `(WorkItemState.Pending, > 0, _, var x)`).
4. WHEN a tuple positional pattern in a switch arm has a when guard, THE Emitter SHALL emit the when clause after the closing parenthesis of the positional pattern (e.g., `(var x, var y) when x + y > 100 => "large"`).
5. THE emitted C# switch expression SHALL compile with the .NET C# compiler (Roslyn) and produce the same runtime behavior as the equivalent C# code written by hand.

### Requirement 4: Exhaustiveness Checking for Tuple Positional Patterns in Switch Arms

**User Story:** As a Mist developer, I want the exhaustiveness checker to understand tuple positional patterns in switch arms, so that I get appropriate compile-time errors when not all tuple value combinations are covered.

#### Acceptance Criteria

1. WHEN a switch expression with a tuple-typed scrutinee has only tuple positional pattern arms with constant/enum elements and no catch-all arm, THE Exhaustiveness_Checker SHALL report the switch as non-exhaustive (since enum or integer tuples have combinatorially many values that cannot practically be fully enumerated via constants alone).
2. WHEN a switch expression with a tuple-typed scrutinee has tuple positional pattern arms plus a final catch-all arm (`_` or `var _` or a tuple of all discards `(_, _)`), THE Exhaustiveness_Checker SHALL consider the switch exhaustive.
3. WHEN a tuple positional pattern arm contains a when guard, THE Exhaustiveness_Checker SHALL NOT consider that arm as definitively covering its matched values (guards may evaluate to false at runtime).
4. WHEN a switch arm uses a tuple positional pattern where every element is a catch-all subpattern (`_` or `var`) and the arm has no when guard, THE Exhaustiveness_Checker SHALL treat that arm as a catch-all covering all remaining tuple values.

### Requirement 5: Pretty-Printer Round-Trip for Tuple Positional Patterns in Switch Arms

**User Story:** As a compiler developer, I want the pretty-printer to format tuple positional patterns in switch arms correctly, so that round-trip fidelity is maintained.

#### Acceptance Criteria

1. WHEN a switch arm contains a `TuplePositionalPatternPrimaryPatternNode`, THE Pretty_Printer SHALL format it as `(subpattern1, subpattern2, ..., subpatternN)` with elements separated by `, ` (comma followed by a single space).
2. WHEN a tuple positional pattern element is an enum member reference, THE Pretty_Printer SHALL format it as a qualified identifier (e.g., `WorkItemState.Pending`).
3. FOR ALL valid switch expressions containing tuple positional pattern arms, parsing then pretty-printing then parsing SHALL produce an equivalent AST (round-trip property).

### Requirement 6: Sample Application

**User Story:** As a Mist developer, I want the TuplePatternMatching sample to demonstrate tuple positional patterns in switch expression arms with enum member elements, so that the real-world state-machine pattern is proven to work end-to-end.

#### Acceptance Criteria

1. The sample at `samples/TuplePatternMatching/Program.mist` SHALL demonstrate a tuple positional pattern in a switch arm with enum member constants as elements (e.g., matching `(EnumType.Variant1, EnumType.Variant2)`).
2. The sample SHALL demonstrate a switch expression over a tuple of enum values with multiple arms matching different enum combinations plus a catch-all arm.
3. The sample SHALL demonstrate mixed element patterns in tuple switch arms: at least one arm combining an enum member pattern with a discard pattern (e.g., `(State.Active, _) => ...`).
4. The sample SHALL compile with zero diagnostics and run with a zero exit code using the Mist compiler.
5. `samples/AllFeatures/Program.mist` SHALL continue to call `TuplePatternMatching.Program.RunDemo()` and produce correct output including the new demonstrations.

#### Proof Code

```mist
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
```

This proof code requires the following enum declarations in the `TuplePatternMatching` namespace:

```mist
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

**Expected output** (appended to the existing sample output, before "Done!"):
```
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
```

### Requirement 7: TextMate Grammar Update

**User Story:** As a Mist developer using VS Code, I want tuple positional patterns with enum member references in switch arms to be syntax-highlighted correctly, so that enum names and operators are visually distinct.

#### Acceptance Criteria

1. WHEN a tuple positional pattern in a switch arm contains qualified identifiers (e.g., `Direction.North`), THE TextMate_Grammar SHALL highlight the type/enum portion using `entity.name.type.mist` scope and the member portion using the appropriate member scope, consistent with how enum member access is highlighted elsewhere in the language.
2. WHEN a tuple positional pattern in a switch arm contains relational operators (`<`, `>`, `<=`, `>=`), THE TextMate_Grammar SHALL highlight them using `keyword.operator.comparison.mist` scope.
3. WHEN a tuple positional pattern in a switch arm contains a discard `_`, THE TextMate_Grammar SHALL highlight it using `variable.language.mist` scope.
4. THE TextMate_Grammar SHALL NOT require any changes beyond what is already handled by the existing pattern-context rules, provided the existing rules already cover these token types within parenthesized pattern contexts (verification only -- no change needed if already correct).

### Requirement 8: Book Documentation Update

**User Story:** As a Mist developer reading the language book, I want documentation on using tuple positional patterns in switch expression arms, so that I can learn the state-machine matching pattern from the official reference.

#### Acceptance Criteria

1. THE Switch Expressions chapter (`book/src/type-system/switch-expressions.md`) SHALL include a section titled "Tuple Positional Patterns in Switch Arms" that explains matching tuple values by position using parenthesized syntax in switch arms.
2. THE section SHALL include a compiled snippet demonstrating a switch expression over a tuple of enum values with positional pattern arms, referenced via `{{#include}}` from `book/snippets/type-system/switch-expressions/tuple-positional-switch.mist` with a corresponding `.cs` file.
3. THE section SHALL explain that tuple positional patterns replace the verbose `{ Item1: ..., Item2: ... }` property pattern workaround for tuple matching.
4. ALL book snippets for this feature SHALL be complete, compilable Mist programs following the book snippet policy and SHALL have their `.cs` files generated by the SnippetEmitter tool.
