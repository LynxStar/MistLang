# Implementation Plan: Switch Expressions

## Overview

This plan implements exhaustive switch expressions across the full Mist compiler pipeline: grammar → lexer → parser → semantic model → type checker (including exhaustiveness checker) → C# emitter → pretty printer → syntax highlighting → samples. Tasks follow the compiler pipeline order so each stage builds on the previous one. The implementation language is C# (matching the existing codebase), with Storm grammar files for parser rules.

## Tasks

- [x] 1. Grammar and Lexer: Add switch expression keywords and parser rules
  - [x] 1.1 Add `switch` and `when` keyword tokens to `Source/Mist/Grammar/keywords.storm`
    - Add `token SWITCH = "switch";` and `token WHEN = "when";` after the existing pattern matching keywords (`IS`, `AND`, `OR`, `NOT`)
    - _Requirements: 1.1, 1.2_
  - [x] 1.2 Add `SwitchExpressionSuffix` alternative to the `PostfixSuffix` rule in `Source/Mist/Grammar/Mist.storm`
    - Add `| SWITCH LEFT_BRACE arms:SwitchArmList? RIGHT_BRACE as SwitchExpressionSuffix` to the `PostfixSuffix` rule after the existing alternatives (MethodCallSuffix, MemberAccessSuffix, IndexSuffix, InvocationSuffix)
    - _Requirements: 2.1, 2.6, 3.1, 3.2, 3.3, 3.4_
  - [x] 1.3 Add `SwitchArmList`, `SwitchArm`, and `WhenGuard` rules to `Source/Mist/Grammar/Mist.storm`
    - Define `rule SwitchArmList = SwitchArm (COMMA SwitchArm)* COMMA?;` for comma-separated arms with optional trailing comma
    - Define `rule SwitchArm = pattern:Pattern guard:WhenGuard? SUCH_THAT result:Expression;` reusing existing `Pattern` rule and `SUCH_THAT` (`=>`) token
    - Define `rule WhenGuard = WHEN condition:Expression;` using the new `WHEN` keyword
    - _Requirements: 2.1, 2.2, 2.3, 2.4, 2.5, 4.1, 4.2, 4.3, 4.4, 4.5, 4.6, 4.7, 4.8_

- [x] 2. Checkpoint — Verify grammar compiles
  - Rebuild the solution to ensure the Storm source generator produces the new AST node types (`SwitchExpressionSuffixPostfixSuffixNode`, `SwitchArmListNode`, `SwitchArmNode`, `WhenGuardNode`) from the updated grammar. Ensure all tests pass, ask the user if questions arise.

- [x] 3. Semantic Model: Extend expression type resolution for switch expressions
  - [x] 3.1 Extend `ResolveExpression` / postfix suffix handling in `Source/Mist/Semantics/Model/SemanticModelBuilder.cs` to handle `SwitchExpressionSuffixPostfixSuffixNode`
    - When processing postfix suffixes, detect `SwitchExpressionSuffixPostfixSuffixNode`
    - Resolve the scrutinee type (already resolved as the accumulated postfix expression)
    - For each `SwitchArmNode`, create a child scope for pattern variables
    - Register pattern variables introduced by the arm's pattern (reuse existing pattern variable registration logic from `is` expressions)
    - Resolve the guard expression (if present) within the arm scope
    - Resolve the result expression within the arm scope
    - _Requirements: 9.1, 9.2, 9.3, 9.4, 4.9_
  - [x] 3.2 Determine and register the unified result type of the switch expression
    - Collect all arm result expression types
    - Find the common type (same type → use it; common base → use base; no common type → `UnknownType.Instance`)
    - Store the switch expression's type in the `expressionTypes` dictionary
    - _Requirements: 5.1, 5.2, 5.3, 9.1_

- [x] 4. Checkpoint — Verify semantic model builds correctly
  - Ensure all tests pass, ask the user if questions arise.

- [x] 5. Type Checker: Add switch expression diagnostics and exhaustiveness checking
  - [x] 5.1 Add diagnostic IDs MIST_TC_0060 through MIST_TC_0063 to the diagnostics infrastructure
    - MIST_TC_0060 (Error): "Switch expression is not exhaustive. Missing cases: {cases}"
    - MIST_TC_0061 (Error): "Switch arms have incompatible types: '{type1}' and '{type2}'"
    - MIST_TC_0062 (Error): "When guard must be a boolean expression, got '{actualType}'"
    - MIST_TC_0063 (Warning): "Switch arm is unreachable"
    - _Requirements: 6.6, 5.4, 8.2, 7.1_
  - [x] 5.2 Add when guard type validation to `TypeCheckerAnalyzer` in `Source/Mist/Analysis/Builtin/TypeCheckerAnalyzer.cs`
    - Walk each switch arm's guard expression and verify it resolves to `bool`
    - Emit MIST_TC_0062 if the guard type is not `bool`
    - _Requirements: 8.1, 8.2_
  - [x] 5.3 Add arm result type compatibility checking to `TypeCheckerAnalyzer`
    - Collect all arm result types and verify they have a common type
    - Emit MIST_TC_0061 if no common type can be determined
    - Verify each arm's result expression is assignable to the inferred result type
    - _Requirements: 5.1, 5.2, 5.3, 5.4, 5.5_
  - [x] 5.4 Create `ExhaustivenessChecker` class in `Source/Mist/Analysis/Builtin/ExhaustivenessChecker.cs`
    - Implement `CheckExhaustiveness(IType scrutineeType, IReadOnlyList<(PatternNode Pattern, bool HasGuard)> arms)` returning uncovered cases
    - Algorithm: (1) catch-all without guard → exhaustive; (2) bool → track true/false coverage; (3) type union → flatten and track constituent type coverage; (4) numeric/string/unbounded → require catch-all
    - Arms with guards never count toward exhaustiveness coverage
    - _Requirements: 6.1, 6.2, 6.3, 6.4, 6.5, 6.7, 6.8, 15.1, 15.2, 15.3, 16.1, 16.2, 16.3, 16.4_
  - [x] 5.5 Integrate `ExhaustivenessChecker` into `TypeCheckerAnalyzer`
    - Invoke `ExhaustivenessChecker.CheckExhaustiveness` for each switch expression
    - Emit MIST_TC_0060 with the list of missing cases if not exhaustive
    - _Requirements: 6.1, 6.6_
  - [x] 5.6 Add unreachable arm detection to `TypeCheckerAnalyzer`
    - After exhaustiveness checking, perform a second pass to detect unreachable arms
    - If an unguarded catch-all appears at position `i`, emit MIST_TC_0063 for all arms at positions `> i`
    - If a constant pattern duplicates a preceding unguarded constant pattern, emit MIST_TC_0063
    - If a type pattern duplicates a preceding unguarded type pattern for the same type, emit MIST_TC_0063
    - _Requirements: 7.1, 7.2, 7.3_

- [x] 6. Checkpoint — Verify type checker diagnostics
  - Ensure all tests pass, ask the user if questions arise.

- [x] 7. C# Emitter: Emit switch expressions as C# switch expressions
  - [x] 7.1 Extend `EmitPostfixSuffix` in `Source/Mist/Backends/CSharpTranspiler/CSharpEmitter.cs` to handle `SwitchExpressionSuffixPostfixSuffixNode`
    - Emit `{scrutinee} switch { arm1, arm2, ..., armN }` with multi-line formatting
    - For each arm: emit pattern, optional `when` guard, `=>` separator, and result expression followed by comma
    - Reuse existing `EmitPattern` method for all pattern types in arms
    - _Requirements: 10.1, 10.2, 10.3, 10.4, 10.5, 10.6, 10.7, 10.8, 10.9, 10.10, 10.11_
  - [x] 7.2 Handle union type scrutinee emission in switch expressions
    - When the scrutinee is a type union, emit the switch expression against the union's helper type
    - Use the union's discriminator properties (`Is{Type}` checks and `As{Type}` extraction) for type pattern matching, consistent with existing union emission patterns for `is` expressions
    - _Requirements: 10.12_

- [x] 8. Pretty Printer: Format switch expressions for round-trip fidelity
  - [x] 8.1 Extend `EmitPostfixSuffix` in `Source/Mist/Parsing/MistPrettyPrinter.cs` to handle `SwitchExpressionSuffixPostfixSuffixNode`
    - Format as `{scrutinee} switch { pattern => result, pattern when guard => result, ... }`
    - Format each arm with pattern, optional `when` guard, `=>`, and result expression
    - Include trailing comma after each arm
    - Reuse existing pattern pretty-printing logic for all pattern types
    - _Requirements: 11.1, 11.2, 11.3, 11.4, 11.5, 11.6_

- [x] 9. Syntax Highlighting: Update TextMate grammar for switch/when keywords
  - [x] 9.1 Add `switch` and `when` to the `keyword.control.mist` pattern in `Editor/mist-syntax-highlighting/syntaxes/mist.tmLanguage.json`
    - Update the keyword control pattern regex to include `switch|when` alongside existing control flow keywords (`if|else|while|for|foreach|in|break|return`)
    - _Requirements: 12.1, 12.2_
  - [x] 9.2 Verify `=>` fat arrow operator highlighting
    - Confirm the existing `keyword.operator.arrow.mist` pattern already covers `=>` in switch arm contexts — no change needed if already present
    - _Requirements: 12.3_
  - [x] 9.3 Verify string/comment exclusion for `switch` and `when`
    - Confirm the existing TextMate grammar matches strings and comments before keywords, so `switch`/`when` inside strings or comments do not receive keyword highlighting — no change needed if already correct
    - _Requirements: 12.4_

- [x] 10. Checkpoint — Verify all compiler pipeline stages work together
  - Ensure all tests pass, ask the user if questions arise.

- [x] 11. Sample Applications: Create SwitchExpressions sample and update AllFeatures
  - [x] 11.1 Create `samples/SwitchExpressions/Program.mist` entry point with `public static void RunDemo()` method
    - Create the `samples/SwitchExpressions/` directory with a `Program.mist` that demonstrates all switch expression features
    - Use `namespace SwitchExpressions;`
    - _Requirements: 13.1, 13.16, 13.17, 13.18_
  - [x] 11.2 Demonstrate switch expressions with constant patterns (integer, string, boolean)
    - Integer constant patterns with wildcard catch-all
    - String constant patterns with wildcard catch-all
    - Boolean constant patterns covering both `true` and `false` (exhaustive without wildcard)
    - _Requirements: 13.2, 13.3, 13.4_
  - [x] 11.3 Demonstrate switch expressions with discard (`_`) and `var` catch-all patterns
    - Show `_` as catch-all arm
    - Show `var x` as catch-all arm that binds the value
    - _Requirements: 13.5, 13.6_
  - [x] 11.4 Demonstrate switch expressions with type patterns matching a type union scrutinee
    - Define a union type (e.g., `int | string`) and switch over it with type patterns covering all constituent types
    - _Requirements: 13.7_
  - [x] 11.5 Demonstrate switch expressions with relational and logical patterns
    - Relational patterns (e.g., `> 0`, `<= 100`)
    - Logical patterns (e.g., `> 0 and < 100`)
    - _Requirements: 13.8, 13.9_
  - [x] 11.6 Demonstrate switch expressions with property patterns and list patterns
    - Property patterns (e.g., `{ Length: > 5 }`)
    - List patterns (e.g., `[1, 2, ..]`)
    - _Requirements: 13.10, 13.11_
  - [x] 11.7 Demonstrate switch expressions with `when` guards
    - Show arms with `when` guard conditions using pattern variables (e.g., `int n when n > 0 => ...`)
    - _Requirements: 13.12_
  - [x] 11.8 Demonstrate switch expressions in various expression contexts
    - As right-hand side of variable assignment
    - As method argument (e.g., `Console.WriteLine(x switch { ... })`)
    - With a tuple scrutinee (e.g., `(x, y) switch { ... }`)
    - _Requirements: 13.13, 13.14, 13.15_
  - [x] 11.9 Update `samples/AllFeatures/Program.mist` to call `SwitchExpressions.Program.RunDemo()`
    - Add the call alongside existing feature demonstrations
    - Ensure all previously demonstrated features still work
    - _Requirements: 14.1, 14.2_

- [x] 12. Checkpoint — Compile and run sample applications
  - Compile `samples/SwitchExpressions/` and `samples/AllFeatures/` end-to-end. Verify correct output. Ensure all tests pass, ask the user if questions arise.

- [x] 13. Property-Based Tests
  - [x] 13.1 Write PBT generator for random valid Mist switch expression source text
    - Create `Test/Mist.Tests/Generators/MistSwitchExpressionGenerator.cs`
    - Generate random switch expressions with varying: scrutinee types (bool, int, string, type unions), arm counts (0–10), pattern types (constant, type, var, discard, property, relational, logical, list), guard presence/absence, trailing comma presence/absence, context (assignment RHS, method argument, tuple element)
    - **Property 1: Parse–Print–Parse Round Trip**
    - **Validates: Requirements 2.1, 2.2, 2.3, 2.4, 2.5, 2.6, 3.1, 3.3, 3.4, 4.1–4.8, 11.1–11.6**
  - [x] 13.2 Write PBT for parse–print–parse round trip (Property 1)
    - Create `Test/Mist.Tests/Parsing/SwitchExpressionRoundTripPropertyTests.cs`
    - For any valid Mist source with switch expressions, parse → pretty-print → parse produces structurally equivalent AST
    - Uses shared `CompilerDriver` instance, in-process, no spawning
    - **Property 1: Parse–Print–Parse Round Trip**
    - **Validates: Requirements 11.6**
  - [x] 13.3 Write PBT for bool exhaustiveness (Property 2)
    - Create `Test/Mist.Tests/Analysis/BoolExhaustivenessPropertyTests.cs`
    - For any switch expression with a `bool` scrutinee and any combination of `true`/`false` constant pattern arms and catch-all arms (with or without guards), verify exhaustiveness checker reports exhaustive iff both `true` and `false` are covered by unguarded arms OR an unguarded catch-all is present
    - In-process, no spawning
    - **Property 2: Bool Exhaustiveness**
    - **Validates: Requirements 6.1, 6.2, 6.5, 6.7, 6.8, 15.1, 15.2, 15.3**
  - [x] 13.4 Write PBT for type union exhaustiveness (Property 3)
    - Create `Test/Mist.Tests/Analysis/TypeUnionExhaustivenessPropertyTests.cs`
    - For any type union scrutinee (including nested unions) and any combination of type pattern arms and catch-all arms, verify exhaustiveness checker reports exhaustive iff every leaf constituent type is covered by an unguarded type pattern arm OR an unguarded catch-all is present
    - In-process, no spawning
    - **Property 3: Type Union Exhaustiveness**
    - **Validates: Requirements 6.1, 6.3, 6.5, 6.7, 6.8, 16.1, 16.2, 16.3, 16.4**
  - [x] 13.5 Write PBT for unbounded type exhaustiveness (Property 4)
    - Create `Test/Mist.Tests/Analysis/UnboundedExhaustivenessPropertyTests.cs`
    - For any switch expression with a numeric or string scrutinee and any combination of constant/relational pattern arms, verify exhaustiveness checker reports exhaustive iff an unguarded catch-all is present
    - In-process, no spawning
    - **Property 4: Unbounded Type Exhaustiveness**
    - **Validates: Requirements 6.1, 6.4, 6.5, 6.7, 6.8**
  - [x] 13.6 Write PBT for unreachable arm detection (Property 5)
    - Create `Test/Mist.Tests/Analysis/UnreachableArmPropertyTests.cs`
    - For any switch expression where an unguarded catch-all appears at position `i`, verify MIST_TC_0063 is emitted for every arm at position `j > i`. For duplicate constant patterns, verify MIST_TC_0063 is emitted for the later arm
    - In-process, no spawning
    - **Property 5: Unreachable Arm Detection**
    - **Validates: Requirements 7.1, 7.2, 7.3**
  - [x] 13.7 Write PBT for guard type validation (Property 6)
    - Create `Test/Mist.Tests/Analysis/GuardTypeValidationPropertyTests.cs`
    - For any switch arm with a `when` guard whose condition does not evaluate to `bool`, verify MIST_TC_0062 is emitted. For guards that evaluate to `bool`, verify no guard-related diagnostic is emitted
    - In-process, no spawning
    - **Property 6: Guard Type Validation**
    - **Validates: Requirements 8.1, 8.2**
  - [x] 13.8 Write PBT for result type unification (Property 7)
    - Create `Test/Mist.Tests/Analysis/ResultTypeUnificationPropertyTests.cs`
    - For any switch expression where all arm result expressions have the same type `T`, verify the inferred result type is `T`. For incompatible types, verify MIST_TC_0061 is emitted
    - In-process, no spawning
    - **Property 7: Result Type Unification**
    - **Validates: Requirements 5.1, 5.2, 5.4, 5.5**
  - [x] 13.9 Write PBT for pattern variable scoping (Property 8)
    - Create `Test/Mist.Tests/Semantics/PatternVariableScopingPropertyTests.cs`
    - For any switch arm whose pattern introduces a pattern variable, verify the variable is accessible with the correct type in both the arm's when guard and result expression
    - In-process via semantic model, no spawning
    - **Property 8: Pattern Variable Scoping**
    - **Validates: Requirements 4.9, 8.3, 8.4**
  - [x] 13.10 Write PBT for end-to-end emission correctness (Property 9)
    - Create `Test/Mist.Tests/Backend/SwitchEmissionPropertyTests.cs`
    - For any valid, exhaustive Mist switch expression, verify the C# emitter produces output that compiles as valid C# and produces the correct runtime result
    - Batched: generate all scenarios upfront, compile once, run once, compare output lines
    - **Property 9: End-to-End Emission Correctness**
    - **Validates: Requirements 10.1–10.12**

- [x] 14. Final checkpoint — Ensure all tests pass
  - Run the full test suite (`dotnet test`). Ensure all tests pass, ask the user if questions arise.

## Notes

- Tasks marked with `*` are optional and can be skipped for faster MVP
- Each task references specific requirements for traceability
- Checkpoints ensure incremental validation after each pipeline stage
- Property tests validate universal correctness properties from the design document
- The grammar changes (Task 1) must be completed first as the Storm source generator produces AST node types that all subsequent stages depend on
- The `PostfixSuffix` rule ordering places `SwitchExpressionSuffix` after existing alternatives since `SWITCH` is a keyword token and won't conflict with other alternatives
- The exhaustiveness checker is a separate class for independent testability
- All PBT tests are in-process except emission correctness (Property 9) which uses batched compilation (1 compile + 1 run)
