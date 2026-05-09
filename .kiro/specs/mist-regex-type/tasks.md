# Implementation Plan: Mist Regex Type

## Overview

This plan implements `regex` as a first-class type in Mist, spanning the full compiler pipeline: grammar extensions (keyword + literal token), stdlib declaration, lexer/parser (Storm-generated), semantic model (desugaring, type inference, keyword alias), type checker (validation, conversions, pattern matching, refinements), exhaustiveness checker, pretty printer, C# transpiler (type map, intrinsics, pattern emission), TextMate grammar, and a sample application.

## Tasks

- [x] 1. Grammar and standard library foundations
  - [x] 1.1 Add `REGEX` keyword token to `keywords.storm` and `REGEX_LITERAL` token, `RegexLiteralExpression`, `RegexConstant`, and `RegexType` rules to `Mist.storm`
    - Add `token REGEX = "regex";` to `keywords.storm` alongside other primitive type keywords
    - Add `token REGEX_LITERAL` rule to `Mist.storm` matching backtick-delimited content with `\\` and `` \` `` escape sequences
    - Add `RegexLiteralExpression` alternative to the `Primary` rule
    - Add `RegexConstant` alternative to the `ConstantLiteral` rule
    - Add `REGEX` as `RegexType` variant in the `PrimitiveType` rule
    - _Requirements: 1.1, 1.4, 3.7, 4.1, 7.1_

  - [x] 1.2 Create `Source/Standard/Bootstrap/Regex.mist` stdlib declaration
    - Declare `public readonly intrinsic class Regex` in the `Standard` namespace
    - Include constructor `Regex(string pattern)`, property `Pattern`, methods `IsMatch(string)` and `IsFullMatch(string)`
    - Declare implicit operator `string(Regex r)` and explicit operator `Regex(string s)`
    - _Requirements: 2.1, 2.2, 2.3, 2.4, 2.5, 2.6, 2.7, 2.8, 2.9_

  - [x] 1.3 Add `["regex"] = "Standard.Regex"` entry to `KeywordAliases.cs`
    - Add the mapping entry to the `Map` dictionary
    - Update the existing `KeywordAliasesTests` count assertion from 15 to 16
    - _Requirements: 1.2_

  - [x] 1.4 Add `["Standard.Regex"] = "System.Text.RegularExpressions.Regex"` entry to `CSharpTypeMap.cs`
    - Add the fully-qualified stdlib entry to `BackingTypes`
    - Add a `ClassType` match case in `GetTypeName` for `Standard.Regex` → `"System.Text.RegularExpressions.Regex"`
    - _Requirements: 8.1_

  - [x] 1.5 Register regex intrinsic emission templates in `IntrinsicEmissionTable.cs`
    - Register `Standard.Regex:IsMatch(Standard.String)` template
    - Register `Standard.Regex:IsFullMatch(Standard.String)` template with `^(?:...)$` anchoring
    - Register `Standard.Regex:Pattern()` template mapping to `.ToString()`
    - Register implicit cast `Standard.Regex:implicit(Standard.String)` → `.ToString()`
    - Register explicit cast `Standard.Regex:explicit(Standard.Regex)` → `new Regex(..., RegexOptions.Compiled)`
    - _Requirements: 8.4, 8.5, 8.6, 8.7, 8.8, 8.10_

- [x] 2. Checkpoint - Verify grammar compiles and foundations are in place
  - Ensure all tests pass, ask the user if questions arise.

- [x] 3. Semantic model and type checker implementation
  - [x] 3.1 Implement regex literal desugaring in `SemanticModelBuilder.cs`
    - When encountering `RegexLiteralExpressionPrimaryNode`, record as `Standard.Regex` constructor invocation with pattern string argument
    - Infer `Standard.Regex` type for `var` declarations initialized with regex literals
    - Preserve pattern string verbatim (no additional escaping)
    - _Requirements: 4.3, 4.4, 5.1, 5.2_

  - [x] 3.2 Implement compile-time regex validation in the type checker
    - Validate regex literal patterns using `new System.Text.RegularExpressions.Regex(pattern, RegexOptions.None)` at compile time
    - Emit `MIST_TC_REGEX_INVALID` diagnostic on failure with error description and offset
    - Still record expression type as `Standard.Regex` on failure (prevent cascading errors)
    - _Requirements: 4.6, 5.3, 5.4, 5.5_

  - [x] 3.3 Implement string↔regex conversion rules in the type checker
    - Accept implicit `regex → string` conversion (assignment, parameter, return)
    - Reject non-literal `string → regex` without explicit cast (emit type mismatch diagnostic)
    - Accept explicit cast `(regex)stringVar`
    - Accept string literal assigned to `regex` target if pattern is valid (compile-time validated shorthand)
    - Reject string literal assigned to `regex` target if pattern is invalid
    - _Requirements: 6.1, 6.2, 6.3, 6.4, 6.5, 6.6, 6.7, 6.8, 6.9, 6.10_

  - [x] 3.4 Implement regex pattern matching validation in the type checker
    - Validate scrutinee type is `string` when regex pattern is used in `is` or switch arm
    - Emit `MIST_TC_REGEX_SCRUTINEE` diagnostic if scrutinee is not `string`
    - Validate regex pattern syntax at pattern position (emit diagnostic if invalid)
    - Ignore capture groups in regex patterns (no bindings introduced)
    - _Requirements: 7.2, 7.3, 7.4, 7.7, 7.8_

  - [x] 3.5 Update exhaustiveness checker for regex patterns
    - Treat switch arms with `RegexConstant` patterns as non-exhaustive
    - Ensure switch expressions with only regex patterns (no catch-all) produce non-exhaustive warning
    - _Requirements: 7.5_

  - [x] 3.6 Implement regex refinement type support
    - Validate regex refinement predicates are only on `string` base types (emit `MIST_TC_REGEX_REFINEMENT` otherwise)
    - Validate regex pattern syntax in refinement predicates
    - Evaluate regex against string literals at compile time using full-match semantics
    - Emit `MIST_TC_REFINEMENT_FAIL` if literal doesn't match
    - Emit `MIST_TC_REFINEMENT_NONCONST` for non-constant string assigned to regex-refined type
    - _Requirements: 11.1, 11.2, 11.3, 11.4, 11.5, 11.6_

- [x] 4. Checkpoint - Verify semantic analysis passes
  - Ensure all tests pass, ask the user if questions arise.

- [x] 5. Pretty printer and C# transpiler
  - [x] 5.1 Add regex literal support to `MistPrettyPrinter.cs`
    - Handle `RegexLiteralExpressionPrimaryNode` in `EmitPrimary` — emit token lexeme (backtick-delimited)
    - Handle `RegexConstantConstantLiteralNode` in `EmitConstantLiteral` — emit token lexeme
    - Handle `RegexTypePrimitiveTypeNode` in `EmitPrimitiveType` — emit `"regex"`
    - Handle empty regex literal (emit two consecutive backticks)
    - _Requirements: 10.1, 10.2, 10.3, 10.4_

  - [x] 5.2 Write property test for pretty-printer round-trip (Property 1)
    - **Property 1: Pretty-printer round-trip for regex literals**
    - Generate random valid regex patterns, build Mist source with regex literals in expression and pattern positions, parse → pretty-print → parse → pretty-print → assert identical
    - Test class: `RegexPrettyPrinterRoundTripTests`
    - In-process only (0 process spawns)
    - **Validates: Requirements 10.1, 10.2, 10.3, 10.4**

  - [x] 5.3 Implement regex literal emission in `CSharpEmitter.cs`
    - Emit `new Regex(<escaped_pattern>, RegexOptions.Compiled)` for `RegexLiteralExpression`
    - Emit `using System.Text.RegularExpressions;` when file references `Standard.Regex`
    - Apply C# string escaping to the regex pattern
    - _Requirements: 8.2, 8.3_

  - [x] 5.4 Implement regex pattern matching emission in `CSharpEmitter.cs`
    - Emit `Regex.IsMatch(scrutinee, "^(?:pattern)$")` for `is` expressions with regex patterns
    - Emit `string s when Regex.IsMatch(s, "^(?:pattern)$")` guard for switch arms with regex patterns
    - Hoist regex patterns to `private static readonly Regex` fields when inside loops or evaluated multiple times
    - _Requirements: 9.1, 9.2, 9.3, 9.5_

- [x] 6. Checkpoint - Verify transpilation works end-to-end
  - Ensure all tests pass, ask the user if questions arise.

- [x] 7. Property-based tests for type checker and conversions
  - [x] 7.1 Write property test for compile-time regex validation (Property 2)
    - **Property 2: Regex literal compile-time validation**
    - Generate random strings (mix of valid and invalid .NET regex patterns), compile Mist programs in-process, assert diagnostic presence/absence matches validity
    - Test class: `RegexCompileTimeValidationTests`
    - In-process only (0 process spawns)
    - **Validates: Requirements 4.6, 5.3, 5.4, 7.7**

  - [x] 7.2 Write property test for conversion rules (Property 3)
    - **Property 3: Conversion rule correctness**
    - Generate programs with various conversion scenarios (regex→string, string→regex with/without cast, string literal→regex valid/invalid), compile in-process, assert type checker accepts/rejects correctly
    - Test class: `RegexConversionRuleTests`
    - In-process only (0 process spawns)
    - **Validates: Requirements 6.1, 6.2, 6.3, 6.5, 6.6, 6.8, 6.9, 6.10**

  - [x] 7.3 Write property test for pattern matching type safety (Property 4)
    - **Property 4: Regex pattern matching type safety**
    - Generate programs with regex patterns on various scrutinee types (string, int, bool, custom), compile in-process, assert diagnostics for non-string scrutinees and acceptance for string scrutinees
    - Test class: `RegexPatternMatchingTypeTests`
    - In-process only (0 process spawns)
    - **Validates: Requirements 7.2, 7.3, 7.5**

  - [x] 7.4 Write property test for refinement type validation (Property 6)
    - **Property 6: Regex refinement type validation**
    - Generate regex-refined types + string literal assignments, compile in-process, assert acceptance when literal matches and rejection when it doesn't
    - Test class: `RegexRefinementTypeTests`
    - In-process only (0 process spawns)
    - **Validates: Requirements 11.2, 11.5, 11.6**

- [x] 8. TextMate grammar and editor support
  - [x] 8.1 Update TextMate grammar for `regex` keyword and regex literals
    - Add `regex` to the primitive type keywords pattern (scope: `storage.type.primitive.mist`)
    - Add `regex-literals` repository entry with begin/end pattern for backtick delimiters (scope: `string.regexp.mist`)
    - Add escape sequence highlighting within regex literals (scope: `constant.character.escape.mist`)
    - Insert regex literal pattern after `comments`, `interpolated-strings`, and `strings` in top-level patterns
    - _Requirements: 12.1, 12.2, 12.3, 12.4, 12.5_

- [x] 9. Sample application and integration
  - [x] 9.1 Create `samples/RegexLiterals/Program.mist` and `samples/RegexLiterals/mist.toml`
    - Demonstrate: regex literal declaration (var and typed), implicit regex→string conversion, explicit string→regex cast, string literal shorthand, IsMatch, IsFullMatch, Pattern property, `is` expression with regex, switch expression with regex, regex-refined type, passing regex to string parameter, passing string literal to regex parameter, passing string variable with explicit cast to regex parameter
    - Print labeled output in `key=value` format with `--- Section Name ---` headers
    - Include `public static void RunDemo()` and `public static void Main(string[] args)` calling `RunDemo()`
    - _Requirements: 13.1–13.17, 13.20_

  - [x] 9.2 Create baseline file and integrate with AllFeatures
    - Create `samples/__baselines__/RegexLiterals.txt` with expected stdout output
    - Add `RegexLiterals.Program.RunDemo()` call to `samples/AllFeatures/Program.mist`
    - Add `[[references]]` entry to `samples/AllFeatures/mist.toml` pointing to RegexLiterals project
    - _Requirements: 13.16, 13.18, 13.19_

  - [x] 9.3 Write batched E2E property test for transpilation correctness (Property 5)
    - **Property 5: End-to-end transpilation correctness (batched)**
    - Generate 100 valid Mist programs using regex features, build single batched program, compile in-process via CompilerDriver, run output DLL once, compare results line-by-line
    - Test class: `RegexTranspilationPropertyTests`
    - 1 process spawn per test run (batched pattern, ~1.5s)
    - **Validates: Requirements 8.1–8.10, 9.1, 9.2, 9.5**

- [x] 10. Documentation and final verification
  - [x] 10.1 Update `CSHARP_FEATURES.md` with regex type feature
    - Add `Regex Literals` row to the `Expressions > Literals` table with status `✅`
    - Add notes describing backtick-delimited syntax and Standard.Regex mapping
    - _Requirements: 14.1_

- [x] 11. Final checkpoint - Ensure all tests pass
  - Ensure all tests pass, ask the user if questions arise.

## Notes

- Tasks marked with `*` are optional and can be skipped for faster MVP
- Each task references specific requirements for traceability
- Checkpoints ensure incremental validation
- Property tests validate universal correctness properties from the design document
- All property tests except Property 5 use in-process compilation only (0 process spawns)
- Property 5 uses the approved batched pattern: 1 compile (in-process) + 1 process spawn (~1.5s)
- Grammar changes (task 1.1) trigger Storm source generation — rebuild required before parser/lexer tests
- The `Standard/Bootstrap/Regex.mist` file follows the same pattern as `String.mist` and other bootstrap types

## Task Dependency Graph

```json
{
  "waves": [
    { "id": 0, "tasks": ["1.1", "1.2"] },
    { "id": 1, "tasks": ["1.3", "1.4", "1.5"] },
    { "id": 2, "tasks": ["3.1", "3.2", "5.1", "8.1"] },
    { "id": 3, "tasks": ["3.3", "3.4", "3.5", "5.2"] },
    { "id": 4, "tasks": ["3.6", "5.3"] },
    { "id": 5, "tasks": ["5.4", "7.1", "7.2", "7.3", "7.4"] },
    { "id": 6, "tasks": ["9.1", "10.1"] },
    { "id": 7, "tasks": ["9.2", "9.3"] }
  ]
}
```
