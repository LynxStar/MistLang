# Implementation Plan: Array Literals

## Overview

This plan implements array literal syntax (`[expr, expr, ...]`) for the Mist compiler. The implementation proceeds grammar-first (since the parser is Storm-generated), then hand-written compiler pipeline code (semantic model builder, type checker, C# emitter, pretty printer), then sample apps, then tests. Each task builds incrementally on the previous one so there is no orphaned code.

**UPDATE**: This plan has been revised to correct empty array literal semantics. The empty array literal is now `[]` (bare brackets), NOT `type[]`. The syntax `type[]` in expression position remains a type literal only. Tasks marked `[ ]` need to be re-implemented; tasks marked `[x]` are unchanged from the original implementation.

## Tasks

- [x] 1. Update Storm grammar with array literal expressions
  - [x] 1.1 Add `EmptyArrayLiteralExpression` and `ArrayLiteralExpression` alternatives to the `Primary` parser rule in `Source/Mist/Grammar/Mist.storm`
    - Add `| LEFT_BRACKET RIGHT_BRACKET as EmptyArrayLiteralExpression` as a new alternative in the `Primary` rule, placed before the existing `ArrayLiteralExpression` alternative so empty brackets match first
    - The existing `| LEFT_BRACKET first:Expression (COMMA rest:Expression)* COMMA? RIGHT_BRACKET as ArrayLiteralExpression` remains for non-empty arrays
    - _Requirements: 1.1, 1.2, 1.3, 2.1, 11.1, 11.2, 11.3, 11.4, 11.5_

- [ ] 2. Checkpoint — Build the solution and verify the Storm source generator produces the new AST nodes
  - Run `dotnet build` on the solution and ensure zero warnings
  - Verify that `EmptyArrayLiteralExpressionPrimaryNode` is generated (no fields)
  - Verify that `ArrayLiteralExpressionPrimaryNode` is generated with `First` (ExpressionNode) and `Rest` (ImmutableArray<ExpressionNode>) properties
  - Ensure all existing tests pass, ask the user if questions arise.

- [ ] 3. Update hand-written compiler pipeline code for new empty array semantics
  - [ ] 3.1 Add `EmptyArrayLiteralExpressionPrimaryNode` case to `SemanticModelBuilder.cs`
    - In the primary expression type resolution switch, add a new case for `EmptyArrayLiteralExpressionPrimaryNode`
    - When a target type is available (from variable declaration annotation) and is an `ArrayType`, use the target element type
    - When no target type is available, produce `ErrorType` (the type checker will emit a diagnostic)
    - _Requirements: 2.2, 2.3, 5.4, 5.5_

  - [ ] 3.2 Update `ArrayLiteralExpressionPrimaryNode` case in `SemanticModelBuilder.cs` (if needed)
    - Verify the existing non-empty array literal handling is unchanged
    - _Requirements: 5.1_

  - [ ] 3.3 Add `EmptyArrayLiteralExpressionPrimaryNode` case to `TypeCheckerAnalyzer.cs`
    - When assigned to a typed variable, validate the target type is an array type
    - When no target type is available, emit a diagnostic error indicating empty array literals require an explicit element type annotation
    - _Requirements: 2.2, 2.3, 5.4, 5.5_

  - [x] 3.4 Verify existing `ArrayLiteralExpressionPrimaryNode` case in `TypeCheckerAnalyzer.cs`
    - The existing `CheckArrayLiteral` method for non-empty arrays remains unchanged
    - _Requirements: 5.2, 5.3, 6.1, 6.2, 6.3, 6.4_

  - [ ] 3.5 Add `EmptyArrayLiteralExpressionPrimaryNode` case to `CSharpEmitter.cs`
    - In the `EmitPrimary` switch, add a new case for `EmptyArrayLiteralExpressionPrimaryNode`
    - When the resolved type has a known element type T (from target type), emit `Array.Empty<T>()`
    - When no target type is available, this is a compile error — the type checker will have already reported a diagnostic, so no emission is needed
    - _Requirements: 8.1_

  - [x] 3.6 Revert `TypeLiteralExpression` special case in `CSharpEmitter.cs`
    - Remove the special case that treated `TypeLiteralExpression` with array type as an empty array literal
    - `TypeLiteralExpression` with array type should revert to its original behavior (emitting `typeof(T[])` or equivalent type literal emission)
    - _Requirements: 2.4_

  - [ ] 3.7 Add `EmptyArrayLiteralExpressionPrimaryNode` case to `MistPrettyPrinter.cs`
    - In the `EmitPrimary` switch, add a new case that emits `[]`
    - _Requirements: 9.2_

  - [x] 3.8 Verify existing `ArrayLiteralExpressionPrimaryNode` case in `MistPrettyPrinter.cs`
    - The existing non-empty array literal pretty printing remains unchanged
    - _Requirements: 9.1, 9.3_

- [ ] 4. Checkpoint — Build and verify zero warnings with all pipeline changes
  - Run `dotnet build` on the solution and ensure zero warnings
  - Ensure all existing tests pass, ask the user if questions arise.

- [ ] 5. Update sample application
  - [ ] 5.1 Update `samples/ArrayLiterals/Program.mist`
    - Change empty array literal from `int[] e = int[];` to `int[] e = [];`
    - // Note: `var foo = [];` is a compile error (cannot infer element type for empty array literal)
    - All other demos (single-element, multi-element, trailing comma, nested, method argument, return value, assignment RHS, explicit type annotation, string array, boolean array) remain unchanged
    - _Requirements: 1.1, 1.2, 1.3, 2.1, 2.2, 2.3, 7.1, 7.2, 7.3, 8.1, 11.1, 11.2, 11.3, 11.4, 11.5_

  - [x] 5.2 Verify `samples/AllFeatures/Program.mist` still calls `ArrayLiterals.Program.RunDemo()`
    - No change needed — the call is already in place
    - _Requirements: (sample app policy)_

- [ ] 6. Checkpoint — Compile and run sample apps end-to-end
  - Compile the ArrayLiterals sample and AllFeatures sample using the Mist compiler
  - Run the compiled output and verify stdout matches expected labeled output
  - Ensure all tests pass, ask the user if questions arise.

- [ ] 7. Update parser and type checker unit tests
  - [ ] 7.1 Update parser unit tests for empty array literal
    - Update `Test/Mist.Tests/Parsing/ArrayLiteralParserTests.cs`
    - Add test: `[]` parses as `EmptyArrayLiteralExpressionPrimaryNode`
    - Add test: `int[] e = [];` parses with `EmptyArrayLiteralExpressionPrimaryNode` as initializer
    - Add test: `var foo = [];` parses with `EmptyArrayLiteralExpressionPrimaryNode` as initializer
    - Remove or update any test that expected `[]` to be a parse error
    - Verify: `int[]` in expression position still parses as `TypeLiteralExpression` (not an empty array)
    - Existing non-empty array literal parser tests remain unchanged
    - _Requirements: 1.1, 1.2, 1.3, 2.1, 2.4, 3.1, 3.2, 3.3, 4.1, 4.2, 4.3, 11.1, 11.2, 11.3, 11.4, 11.5_

  - [ ] 7.2 Update type checker unit tests for empty array literal
    - Update `Test/Mist.Tests/Analysis/ArrayLiteralTypeCheckerTests.cs`
    - Add test: `int[] e = [];` infers as `int[]` without error
    - Add test: `string[] s = [];` infers as `string[]` without error
    - Add test: `var foo = [];` emits a diagnostic error
    - Remove or update any test that expected `int[]` in expression position to be an empty array literal
    - Add test: `int[]` in expression position is a type literal (not an empty array)
    - Existing non-empty array literal type checker tests remain unchanged
    - _Requirements: 5.1, 5.2, 5.3, 5.4, 5.5, 6.1, 6.2, 6.3, 6.4_

- [ ] 8. Update C# emitter and pretty printer unit tests
  - [ ] 8.1 Update C# emitter unit tests for empty array literals
    - Update `Test/Mist.Tests/Backend/ArrayLiteralEmitterTests.cs`
    - Add test: `int[] e = [];` emits `int[] e = Array.Empty<int>()`
    - Add test: `var foo = [];` produces a compile error (no emitted output)
    - Remove or update any test that expected `int[]` in expression position to emit `Array.Empty<int>()`
    - Add test: `int[]` in expression position emits as a type literal (e.g., `typeof(int[])`)
    - Existing non-empty array literal emitter tests remain unchanged
    - _Requirements: 7.1, 7.2, 7.3, 8.1_

  - [ ] 8.2 Update pretty printer unit tests for empty array literals
    - Update `Test/Mist.Tests/Parsing/ArrayLiteralPrettyPrinterTests.cs`
    - Add test: `[]` pretty-prints as `[]`
    - Remove or update any test that expected empty array to pretty-print as `int[]`
    - Existing non-empty array literal pretty printer tests remain unchanged
    - _Requirements: 9.1, 9.2, 9.3_

- [x] 9. Checkpoint — Run all tests and verify zero failures
  - Run `dotnet test` on the full test suite
  - Ensure all tests pass, ask the user if questions arise.

- [ ] 10. Update property-based tests
  - [x] 10.1 Verify property test for array literal element count (Property 1) — no change needed
    - `Test/Mist.Tests/Parsing/ArrayLiteralElementCountPropertyTests.cs`
    - **Property 1: Array literal parsing produces correct element count**
    - Non-empty array literal parsing is unchanged
    - **Validates: Requirements 1.1, 11.1, 11.2, 11.3, 11.4, 11.5**

  - [x] 10.2 Verify property test for trailing comma equivalence (Property 2) — no change needed
    - `Test/Mist.Tests/Parsing/ArrayLiteralTrailingCommaPropertyTests.cs`
    - **Property 2: Trailing comma equivalence**
    - Non-empty array literal trailing comma behavior is unchanged
    - **Validates: Requirements 1.2**

  - [x] 10.3 Verify property test for expression-position disambiguation (Property 3) — no change needed
    - `Test/Mist.Tests/Parsing/ArrayLiteralDisambiguationPropertyTests.cs`
    - **Property 3: Expression-position disambiguation**
    - Index suffix vs array literal disambiguation is unchanged
    - **Validates: Requirements 3.1, 3.2, 3.3**

  - [x] 10.4 Verify property test for pattern-context disambiguation (Property 4) — no change needed
    - `Test/Mist.Tests/Parsing/ArrayLiteralPatternDisambiguationPropertyTests.cs`
    - **Property 4: Pattern-context disambiguation**
    - Pattern context disambiguation is unchanged
    - **Validates: Requirements 4.1, 4.2, 4.3**

  - [x] 10.5 Verify property test for homogeneous element type inference (Property 5) — no change needed
    - `Test/Mist.Tests/Analysis/ArrayLiteralTypeInferencePropertyTests.cs`
    - **Property 5: Homogeneous element type inference**
    - Non-empty array type inference is unchanged
    - **Validates: Requirements 5.1, 5.3**

  - [x] 10.6 Verify property test for incompatible element type diagnostic (Property 6) — no change needed
    - `Test/Mist.Tests/Analysis/ArrayLiteralIncompatibleTypePropertyTests.cs`
    - **Property 6: Incompatible element type diagnostic**
    - Incompatible type diagnostic behavior is unchanged
    - **Validates: Requirements 5.2**

  - [x] 10.7 Verify property test for void and unknown element rejection (Property 7) — no change needed
    - `Test/Mist.Tests/Analysis/ArrayLiteralVoidUnknownPropertyTests.cs`
    - **Property 7: Void and unknown element rejection**
    - Void/unknown rejection behavior is unchanged
    - **Validates: Requirements 6.3, 6.4**

  - [ ] 10.8 Update property test for C# emission format (Property 8)
    - Update `Test/Mist.Tests/Backend/ArrayLiteralEmissionPropertyTests.cs`
    - **Property 8: C# emission format**
    - Update empty array literal generation to use `[]` syntax instead of `int[]`
    - Verify `int[] e = [];` emits `Array.Empty<int>()` pattern
    - Only verify `Array.Empty<T>()` with known target type; no `Array.Empty<object>()` case
    - Non-empty array literal emission tests remain unchanged
    - **Validates: Requirements 7.1, 7.3, 8.1**

  - [ ] 10.9 Update property test for pretty-print round trip (Property 9)
    - Update `Test/Mist.Tests/Parsing/ArrayLiteralRoundTripPropertyTests.cs`
    - **Property 9: Pretty-print round trip**
    - Add `[]` to the set of generated array literal source texts
    - Verify `[]` → parse → pretty-print → `[]` → parse produces equivalent AST
    - Non-empty array literal round-trip tests remain unchanged
    - **Validates: Requirements 9.1, 9.2, 9.3**

- [ ] 11. Final checkpoint — Ensure all tests pass
  - Run `dotnet test` on the full test suite
  - Ensure all tests pass, ask the user if questions arise.

## Notes

- Tasks marked with `[x]` are unchanged from the original implementation or verified to need no changes
- Tasks marked with `[ ]` need to be implemented or updated for the new empty array literal semantics
- Each task references specific requirements for traceability
- Checkpoints ensure incremental validation
- Property tests validate universal correctness properties from the design document
- Unit tests validate specific examples and edge cases
- All tests use in-process compilation via `CompilerDriver.Compile()` or in-process lexer/parser — no per-iteration process spawning per the test-process-spawning policy
- Grammar changes come first because the Storm source generator must produce `EmptyArrayLiteralExpressionPrimaryNode` before hand-written code can reference it
- The key semantic change: `[]` is now a valid empty array literal; `int[]` in expression position is a type literal only
- `EmptyArrayLiteralExpression` must appear before `ArrayLiteralExpression` in the grammar so the parser matches empty brackets first
- Disambiguation is handled structurally by the grammar: Primary alternatives are tried before PostfixSuffix, and pattern rules are separate from expression rules
