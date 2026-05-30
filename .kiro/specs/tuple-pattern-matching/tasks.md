# Implementation Plan: Tuple Positional Pattern Matching

## Overview

This plan implements tuple positional patterns across the entire Mist compiler pipeline. The dependency order ensures each stage builds on the previous: grammar first (everything depends on it), then pretty-printer (needed for round-trip testing), semantic model, type checker, exhaustiveness checker, C# emitter, diagnostics, sample app, book documentation, TextMate grammar, and finally property-based tests.

All implementation is in C#. Property-based tests use FsCheck with the shared `CompilerDriver` pattern and in-process compilation (no per-iteration process spawns). The sample app is verified via the existing AllFeatures baseline test.

## Tasks

- [x] 1. Grammar and parser foundation
  - [x] 1.1 Add `TuplePositionalPattern` alternative to `PrimaryPattern` in Storm grammar
    - Add the new alternative in `Source/Mist/Grammar/Mist.storm` within the `PrimaryPattern` rule
    - Use fields: `first:Pattern COMMA rest:Pattern (COMMA additional:Pattern)*`
    - Place BEFORE the `ParenthesizedPattern` alternative so comma presence disambiguates
    - Name the alternative `TuplePositionalPattern`
    - No trailing comma permitted (no `COMMA?` after last element)
    - Verify the solution builds and the source generator produces `TuplePositionalPatternPrimaryPatternNode` with `First`, `Rest`, and `Additionals` properties
    - _Requirements: 1.1, 1.2, 1.3, 1.4, 1.5, 1.6_

- [x] 2. Pretty-printer support
  - [x] 2.1 Add `TuplePositionalPattern` case to `MistPrettyPrinter`
    - Add a case in the `EmitPrimaryPattern` method (or equivalent pattern-emission switch) for `TuplePositionalPatternPrimaryPatternNode`
    - Emit `(sub1, sub2, ..., subN)` with comma-space separation, no trailing comma
    - Recursively delegate each element to the existing pattern emission logic
    - Handle nested tuple positional patterns via recursion
    - _Requirements: 6.1, 6.2, 6.3_

- [x] 3. Checkpoint - Verify grammar and pretty-printer
  - Ensure the solution builds with zero warnings, ask the user if questions arise.

- [x] 4. Semantic model builder
  - [x] 4.1 Add `TuplePositionalPattern` handling to `SemanticModelBuilder`
    - Extend `CollectFromPattern` (or equivalent) to handle `TuplePositionalPatternPrimaryPatternNode`
    - Resolve the scrutinee type; if it's a `TupleType`, iterate element patterns passing corresponding element types
    - If not a tuple type or is `ErrorType`, recurse into subpatterns with `ErrorType` to still collect variables
    - Extend `ResolvePattern` to recursively resolve each element subpattern with the corresponding tuple element type
    - Ensure var patterns and type patterns within elements introduce variables into the then-branch scope (for `is`) or arm scope (for switch)
    - Ensure discard and non-binding patterns do NOT introduce variables
    - Handle nested tuple positional patterns recursively at any depth
    - _Requirements: 2.1, 2.2, 2.3, 2.4, 2.5, 3.3, 5.4_

- [x] 5. Type checker validation
  - [x] 5.1 Add `TuplePositionalPattern` validation to `TypeCheckerAnalyzer`
    - Add a case in `CheckPrimaryPattern` (or equivalent) for `TuplePositionalPatternPrimaryPatternNode`
    - If scrutinee is `ErrorType`, skip validation (suppress cascading errors)
    - If scrutinee is not a `TupleType`, emit `TuplePositionalPatternRequiresTupleType` diagnostic
    - If pattern arity ≠ tuple arity, emit `TuplePositionalPatternArityMismatch` diagnostic
    - If arity matches, validate each element subpattern against the corresponding element type via recursive `CheckPattern`
    - Validate each element independently — do not stop at first error
    - Handle nested tuple positional patterns recursively (inner tuple type at element position)
    - If nested pattern applied to non-tuple element, emit diagnostic for that position
    - _Requirements: 4.1, 4.2, 4.3, 4.4, 4.5, 4.6, 5.2, 5.3, 8.1, 8.2, 8.3, 8.4_

  - [x] 5.2 Add duplicate pattern variable detection for nested tuple patterns
    - Ensure the existing duplicate-variable-name detection covers variables introduced at different nesting levels within the same tuple positional pattern
    - If two pattern variables at different nesting levels share the same name, emit a diagnostic error
    - _Requirements: 5.5_

- [x] 6. Exhaustiveness checker
  - [x] 6.1 Extend exhaustiveness checker for tuple positional patterns
    - In `IsCatchAllPattern` (or equivalent), add a case for `TuplePositionalPatternPrimaryPatternNode`
    - A tuple positional pattern is a catch-all if and only if every element is a catch-all (discard or var pattern, recursively for nested tuples)
    - Ensure when guards still prevent an arm from being considered exhaustive
    - _Requirements: 3.4, 3.5_

- [x] 7. Checkpoint - Verify semantic model, type checker, and exhaustiveness
  - Ensure the solution builds with zero warnings, ask the user if questions arise.

- [x] 8. C# emitter
  - [x] 8.1 Add `TuplePositionalPattern` emission to `CSharpEmitter`
    - Add a case in the pattern emission method for `TuplePositionalPatternPrimaryPatternNode`
    - Emit `(e1, e2, ..., eN)` where each `ei` is the C# emission of the corresponding subpattern
    - Delegate each element to existing pattern emission logic (relational, constant, logical, var, discard, type, property, list)
    - Handle nested tuple positional patterns via recursion
    - Emit `var identifier` for var pattern elements
    - Emit `TypeName identifier` for type pattern elements
    - Emit `_` for discard elements
    - Produce identical output regardless of `is` expression vs switch arm context
    - _Requirements: 7.1, 7.2, 7.3, 7.4, 7.5, 7.6, 7.7_

- [x] 9. Diagnostic message registration
  - [x] 9.1 Register new diagnostic IDs for tuple positional pattern errors
    - Add `TuplePositionalPatternRequiresTupleType` diagnostic ID with error severity
    - Add `TuplePositionalPatternArityMismatch` diagnostic ID with error severity
    - Ensure message templates include the actual type name and arity counts as specified
    - Verify element-level diagnostics reuse existing diagnostic IDs (type incompatibility, relational on non-comparable)
    - _Requirements: 8.1, 8.2, 8.3, 8.4_

- [x] 10. Checkpoint - Verify full pipeline compiles
  - Ensure the solution builds with zero warnings, ask the user if questions arise.

- [x] 11. Sample application
  - [x] 11.1 Create `samples/TuplePatternMatching/mist.toml` project file
    - Create `samples/TuplePatternMatching/mist.toml` with `name = "TuplePatternMatching"`
    - _Requirements: 9.1_

  - [x] 11.2 Create `samples/TuplePatternMatching/Program.mist` with proof code
    - Create the file using the exact proof code from Requirement 9 verbatim (immutable proof code policy)
    - Use Allman brace style throughout
    - Include all demonstrations: constant, relational, logical, var, discard, type, nested, switch arms, when guards, 3-element
    - Include `public static void RunDemo()` entry point
    - Each section prints `"--- <description> ---"` header followed by result lines
    - _Requirements: 9.1, 9.2, 9.3, 9.4, 9.5, 9.6, 9.7, 9.8, 9.9, 9.10, 9.11, 9.12, 9.13, 9.15_

  - [x] 11.3 Wire `TuplePatternMatching` into `samples/AllFeatures`
    - Add `TuplePatternMatching.Program.RunDemo()` call to `samples/AllFeatures/Program.mist`
    - Add `[[references]] path = "../TuplePatternMatching"` to `samples/AllFeatures/mist.toml`
    - _Requirements: 9.14_

  - [x] 11.4 Update AllFeatures baseline file
    - Run the AllFeatures sample and capture the new expected output including TuplePatternMatching section
    - Update `samples/__baselines__/AllFeatures.out` with the new output
    - Verify the sample compiles with zero diagnostics and runs with exit code 0
    - _Requirements: 9.15_

- [x] 12. Checkpoint - Verify sample app end-to-end
  - Ensure all tests pass (including `SamplesCompileAndMatchBaselineTest`), ask the user if questions arise.

- [x] 13. Book documentation
  - [x] 13.1 Create book snippets for tuple positional patterns
    - Create `book/snippets/type-system/pattern-matching/tuple-positional.mist` — basic tuple positional pattern in `is` expression with relational subpatterns
    - Create `book/snippets/type-system/pattern-matching/tuple-positional.cs` — empty file (SnippetEmitter will populate)
    - Create `book/snippets/type-system/pattern-matching/tuple-positional-nested.mist` — nested tuple positional patterns
    - Create `book/snippets/type-system/pattern-matching/tuple-positional-nested.cs` — empty file
    - Create `book/snippets/type-system/switch-expressions/tuple-positional.mist` — switch expression with tuple positional pattern arms
    - Create `book/snippets/type-system/switch-expressions/tuple-positional.cs` — empty file
    - All snippets must be complete compilable programs: `using Standard;`, namespace, class with `Main`
    - Run `dotnet run --project tools/SnippetEmitter` to generate the `.cs` files
    - _Requirements: 10.2, 10.3, 10.5, 10.6_

  - [x] 13.2 Add "Tuple Positional Patterns" section to Pattern Matching chapter
    - Add new section to `book/src/type-system/pattern-matching.md` after the "List Patterns" section
    - Title: "Tuple Positional Patterns"
    - Explain matching tuple elements by position using parenthesized syntax
    - Include `{{#include}}` references to `tuple-positional.mist` and `tuple-positional-nested.mist` snippets using the tabbed include pattern
    - _Requirements: 10.1, 10.2, 10.3, 10.7_

  - [x] 13.3 Add "Tuple Positional Pattern Arms" section to Switch Expressions chapter
    - Add new section to `book/src/type-system/switch-expressions.md` after the "Relational and Logical Patterns" section
    - Title: "Tuple Positional Pattern Arms"
    - Explain using tuple positional patterns in switch arms
    - Include `{{#include}}` reference to `tuple-positional.mist` snippet using the tabbed include pattern
    - _Requirements: 10.4, 10.5, 10.7_

- [x] 14. TextMate grammar update
  - [x] 14.1 Verify and update TextMate grammar for tuple positional patterns
    - Review `Editor/mist-syntax-highlighting/syntaxes/mist.tmLanguage.json` for correct highlighting of tuple positional patterns in pattern contexts
    - Verify relational operators get `keyword.operator.comparison.mist`
    - Verify numeric literals get `constant.numeric.*.mist`
    - Verify `var` gets `keyword.other.mist`, `_` gets `variable.language.mist`
    - Verify type names get `entity.name.type.mist`, primitive types get `storage.type.primitive.mist`
    - Ensure identifiers/type names before `(` in pattern context do NOT get `entity.name.function.mist`
    - Ensure comma-separated subpatterns all receive pattern-context highlighting
    - Add a dedicated pattern-context rule if testing reveals incorrect highlighting
    - _Requirements: 11.1, 11.2, 11.3_

- [x] 15. Checkpoint - Verify documentation and highlighting
  - Ensure the solution builds with zero warnings and book snippets compile via SnippetEmitter, ask the user if questions arise.

- [x] 16. Property-based tests
  - [x] 16.1 Write property test for pretty-print round-trip (Property 1)
    - **Property 1: Pretty-print round-trip**
    - **Validates: Requirements 1.1, 1.3, 1.4, 5.1, 6.1, 6.2, 6.3**
    - Create `Test/Mist.Tests/Parsing/TuplePositionalPatternRoundTripPropertyTests.cs`
    - Generate random tuple positional pattern ASTs (arbitrary subpatterns, nesting depths, arities ≥ 2)
    - Pretty-print → parse → pretty-print → verify identical output
    - Use in-process compilation only (no process spawn)
    - Use FsCheck with `MaxTest = 100`

  - [x] 16.2 Write property test for variable introduction correctness (Property 2)
    - **Property 2: Variable introduction correctness**
    - **Validates: Requirements 2.2, 2.3, 2.4, 2.5, 5.4**
    - Create `Test/Mist.Tests/Semantics/TuplePositionalPatternVariablePropertyTests.cs`
    - Generate random tuple types + patterns with mix of var/type/discard/non-binding elements
    - Run `SemanticModelBuilder` in-process, verify scope contains exactly the expected variables with correct types
    - Use FsCheck with `MaxTest = 100`

  - [x] 16.3 Write property test for non-tuple type diagnostic (Property 3)
    - **Property 3: Non-tuple type diagnostic**
    - **Validates: Requirements 4.1, 8.1**
    - Create `Test/Mist.Tests/Analysis/TuplePositionalPatternNonTupleDiagnosticPropertyTests.cs`
    - Generate random non-tuple scrutinee types, compile in-process with shared `CompilerDriver`
    - Verify exactly one diagnostic error whose message includes the actual type's display name
    - Use FsCheck with `MaxTest = 100`

  - [x] 16.4 Write property test for arity mismatch diagnostic (Property 4)
    - **Property 4: Arity mismatch diagnostic**
    - **Validates: Requirements 4.2, 8.2**
    - Create `Test/Mist.Tests/Analysis/TuplePositionalPatternArityMismatchPropertyTests.cs`
    - Generate random tuple types with arity N and patterns with M ≠ N elements
    - Compile in-process, verify exactly one diagnostic with both N and M in message
    - Use FsCheck with `MaxTest = 100`

  - [x] 16.5 Write property test for recursive element type validation (Property 5)
    - **Property 5: Recursive element type validation**
    - **Validates: Requirements 4.3, 5.2**
    - Create `Test/Mist.Tests/Analysis/TuplePositionalPatternElementValidationPropertyTests.cs`
    - Generate matching-arity patterns with valid/invalid elements including nested tuples
    - Compile in-process, verify correct diagnostics for each invalid element
    - Use FsCheck with `MaxTest = 100`

  - [x] 16.6 Write property test for C# emission (Property 6)
    - **Property 6: C# emission produces valid positional pattern syntax**
    - **Validates: Requirements 7.1, 7.2, 7.3, 7.4, 7.5, 7.6, 7.7**
    - Create `Test/Mist.Tests/Backend/TuplePositionalPatternEmissionPropertyTests.cs`
    - Generate random valid tuple positional patterns, emit via `CSharpEmitter` in-process
    - Verify output matches `(e1, e2, ..., eN)` format
    - Verify identical output for `is` expression vs switch arm context
    - Use FsCheck with `MaxTest = 100`

  - [x] 16.7 Write property test for exhaustiveness (Property 7)
    - **Property 7: Exhaustiveness for all-catch-all tuple patterns**
    - **Validates: Requirements 3.4, 3.5**
    - Create `Test/Mist.Tests/Analysis/TuplePositionalPatternExhaustivenessPropertyTests.cs`
    - Generate all-catch-all tuple patterns (all elements are `_` or `var`, recursively for nested)
    - Compile in-process, verify no exhaustiveness diagnostic
    - Generate patterns with at least one non-catch-all element and no other catch-all arm, verify exhaustiveness diagnostic IS emitted
    - Use FsCheck with `MaxTest = 100`

  - [x] 16.8 Write property test for per-element independent diagnostics (Property 8)
    - **Property 8: Per-element independent diagnostics**
    - **Validates: Requirements 8.3, 8.4**
    - Create `Test/Mist.Tests/Analysis/TuplePositionalPatternPerElementDiagnosticPropertyTests.cs`
    - Generate patterns with J elements having type-incompatible subpatterns (J ≥ 1)
    - Compile in-process, verify at least J separate diagnostics emitted
    - Use FsCheck with `MaxTest = 100`

- [x] 17. Final checkpoint - Ensure all tests pass
  - Ensure all tests pass, ask the user if questions arise.

## Notes

- Tasks marked with `*` are optional and can be skipped for faster MVP
- Each task references specific requirements for traceability
- Checkpoints ensure incremental validation
- Property tests validate universal correctness properties using FsCheck with in-process compilation (shared `CompilerDriver` pattern)
- Unit tests validate specific examples and edge cases
- The sample app is verified via the existing `AllFeatures_CompilesRunsAndMatchesBaseline` integration test (no new process spawn)
- Book snippets are verified via `dotnet run --project tools/SnippetEmitter` (1 process spawn)
- All property-based tests use in-process compilation — no per-iteration process spawns

## Task Dependency Graph

```json
{
  "waves": [
    { "id": 0, "tasks": ["1.1"] },
    { "id": 1, "tasks": ["2.1"] },
    { "id": 2, "tasks": ["4.1"] },
    { "id": 3, "tasks": ["5.1", "5.2", "6.1"] },
    { "id": 4, "tasks": ["8.1", "9.1"] },
    { "id": 5, "tasks": ["11.1", "11.2"] },
    { "id": 6, "tasks": ["11.3", "11.4", "14.1"] },
    { "id": 7, "tasks": ["13.1"] },
    { "id": 8, "tasks": ["13.2", "13.3"] },
    { "id": 9, "tasks": ["16.1", "16.2", "16.3", "16.4", "16.5", "16.6", "16.7", "16.8"] }
  ]
}
```
