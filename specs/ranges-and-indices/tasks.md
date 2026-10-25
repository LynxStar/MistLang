# Implementation Plan: Ranges and Indices

## Overview

This plan threads the index-from-end operator `^`, the range operator `..`, and generalized C# 11 list/slice patterns through the classic Mist pipeline (lexer -> grammar/parser -> semantic model -> type checker -> emitter -> pretty printer), surfaces `Standard.Index`/`Standard.Range`, migrates pre-existing user `Range` types out of the way, and proves the feature end-to-end through the locked `samples/RangesAndIndices` proof code wired into the existing `AllFeatures` baseline.

Implementation language is C# (the Mist compiler itself); the design uses concrete C#, so no language selection is required.

All tests are **in-process** via a shared, reused `CompilerDriver`. This feature adds **zero** new process-spawning tests. Runtime semantics (design Properties 12, 13, 17 — index-from-end offsets, slice inclusive-start/exclusive-end with array-copy/span-view/string-copy, and list/slice match semantics) are validated only by wiring the sample into the existing `AllFeatures_CompilesRunsAndMatchesBaseline` test and updating `samples/__baselines__/AllFeatures.out`. Property-based tests use FsCheck, run a minimum of 100 iterations, and are each tagged `// Feature: ranges-and-indices, Property N: <text>`.

The grammar edits (2.1-2.4) plus the Range-tier ripple (3.1) are a single logical unit: intermediate steps between them will not leave a green build because the parser must be regenerated and every `Multiplicative` consumer re-threaded before the tree compiles again. The build returns green at the first checkpoint (task 4).

## Tasks

- [x] 1. Lexer: add the from-end operator token
  - [x] 1.1 Add the `CARET` (`^`) token to `Source/Mist/Grammar/tokens/operators.storm`
    - Declare `token CARET = "^";` beside the existing single-character operators (near `EXCLAMATION`)
    - Confirm `DOTDOT` (`..`) is reused unchanged and remains declared before `DOT` so maximal-munch tokenizes `..` as one `DOTDOT`
    - No `FLOAT_LITERAL` change: `0..2` already lexes as INTEGER, DOTDOT, INTEGER because floats require a digit after `.`
    - _Requirements: 1.1, 1.2, 1.3, 1.4, 1.5_
  - [ ]* 1.2 Lexer unit tests for the new token
    - Assert `^` lexes as a single `CARET` token
    - Assert `0..2` lexes as INTEGER_LITERAL, DOTDOT, INTEGER_LITERAL (no float interpretation)
    - _Requirements: 1.3, 1.4_

- [x] 2. Grammar and parser: from-end, range, and generalized slice
  - [x] 2.1 Add the from-end alternative to the `Unary` rule in `Source/Mist/Grammar/Mist.storm`
    - Add `| CARET operand:Unary as FromEndExpression`
    - Operand type `Unary` admits integer literals, identifiers/member access, and parenthesized expressions
    - _Requirements: 2.1, 2.2, 2.3, 2.4_
  - [x] 2.2 Insert the `Range` precedence tier and retype `Multiplicative` operands in `Mist.storm`
    - Add rule `Range = start:Unary? DOTDOT end:Unary? as RangeExpression | operand:Unary as NonRange` immediately above `Unary` (RangeExpression listed first for longest-match)
    - Change `Multiplicative` operands from `Unary` to `Range`
    - This yields `^1..^2` = `(^1)..(^2)`, `..n - 1` = `(..n) - 1`, and `^a + b` = `(^a) + b`
    - _Requirements: 3.1, 3.2, 3.3, 3.4, 3.5, 4.1, 4.2, 4.3, 4.4_
  - [x] 2.3 Generalize `SliceElement` to carry an optional full sub-pattern in `Mist.storm`
    - Change `DOTDOT name:(IDENTIFIER | DISCARD)?` to `DOTDOT subpattern:Pattern?`
    - _Requirements: 19.3_
  - [x] 2.4 Regenerate the parser from the grammar (no hand edits to generated code)
    - Produces `FromEndExpressionUnaryNode`, `RangeExpressionRangeNode`, `NonRangeRangeNode`, retyped `MultiplicativeNode.Left/.Right` (now `RangeNode`), and `SliceElementListPatternElementNode.Subpattern`
    - `IndexSuffixPostfixSuffixNode.Index` stays an `ExpressionNode`, so `arr[^1]`, `arr[1..3]`, `arr[..]` parse with no `IndexSuffix` change
    - _Requirements: 2.5, 3.6_
  - [ ]* 2.5 Parser unit tests for the new forms
    - Cover `^1`/`^count`/`^items.Length`/`^(count + 1)`; the four range forms `a..b`/`a..`/`..b`/`..`; from-end bounds `1..^1`/`^3..`/`..^2`; element access `arr[^1]`/`arr[1..3]`/`arr[..]`; generalized slice patterns (`..`, `.. _`, `.. var name`, `.. [.., 5]`, `.. { Length: > 0 }`) and empty list pattern `[]`
    - _Requirements: 2.5, 3.6, 19.1, 19.2, 19.4, 19.5, 19.6_

- [x] 3. Thread the Range tier through every `Multiplicative` consumer
  - [x] 3.1 Add `UnwrapRange` and re-thread all consumers so the solution compiles again
    - Add a shared `UnwrapRange(RangeNode) -> UnaryNode?` helper (returns the inner `Unary` for `NonRange`, null for a genuine range)
    - Update `CSharpEmitter.EmitMultiplicative` and `ScanMultiplicativeForValueReturningCalls`
    - Update `MistPrettyPrinter.EmitMultiplicative`
    - Update `TypeCheckerAnalyzer.CheckMultiplicativeNode`, its read-analysis pass, and the `TryExtract*` / chain-navigation helpers that walk `Multiplicative -> Unary -> Postfix`
    - Update `SemanticModelBuilder`'s `MultiplicativeNode` resolution
    - Fix test-constructed ASTs that assign a `UnaryNode` directly to `MultiplicativeNode.Left/.Right`
    - Let the C# compiler enumerate the affected sites (the `UnaryNode`/`RangeNode` mismatch pinpoints each one)
    - _Requirements: 4.4_
  - [ ]* 3.2 Property test: from-end binds tighter than range
    - **Property 2: From-end binds tighter than range** — parsing `^a..^b` yields a range whose bounds are the from-end expressions of `a` and `b`
    - **Validates: Requirements 4.1**
    - _Requirements: 4.1_
    - _Properties: 2_
  - [ ]* 3.3 Property test: range bounds are unary-level
    - **Property 3: Range bounds are unary-level** — parsing `..n - t` yields a subtraction whose left operand is the range `..n`
    - **Validates: Requirements 4.2**
    - _Requirements: 4.2_
    - _Properties: 3_
  - [ ]* 3.4 Property test: from-end binds tighter than binary arithmetic
    - **Property 4: From-end binds tighter than binary arithmetic** — parsing `^a + b` yields an addition whose left operand is the from-end expression of `a`
    - **Validates: Requirements 4.3**
    - _Requirements: 4.3_
    - _Properties: 4_

- [x] 4. Checkpoint - grammar, parser, and precedence are green
  - Ensure all tests pass, ask the user if questions arise.

- [x] 5. Surface the `Index` and `Range` types
  - [x] 5.1 Add the Bootstrap declarations and CLR mappings
    - Add `Source/Standard/Bootstrap/Index.mist` and `Source/Standard/Bootstrap/Range.mist` as `intrinsic public struct` in `namespace Standard`, following the `Span.mist` pattern
    - Add `CSharpTypeMap.BackingTypes` entries `Standard.Index -> System.Index` and `Standard.Range -> System.Range`
    - Add `WellKnownTypeMap.Entries` entries `(System, Index) -> (Standard, Index)` and `(System, Range) -> (Standard, Range)`
    - Verify the exact `CSharpTypeMap` / `WellKnownTypeMap` member names in source before editing
    - _Requirements: 5.3, 5.4, 5.5, 5.6_
  - [ ]* 5.2 Unit tests for type surfacing
    - Assert `Index` and `Range` are spellable in `Standard` (declare typed variable/parameter/field/return) and map to `System.Index`/`System.Range`
    - _Requirements: 5.3, 5.4, 5.5, 5.6_

- [x] 6. Semantic model resolution
  - [x] 6.1 Resolve the new expression nodes and thread the Range tier
    - `FromEndExpression -> Standard.Index`; `RangeExpression -> Standard.Range` (independent of which bounds are present); `NonRange` adopts its operand's type
    - Update `MultiplicativeNode` resolution to read `Range` operands (`NonRange` contributes inner type; `RangeExpression` contributes `Standard.Range`)
    - _Requirements: 5.1, 5.2_
  - [ ]* 6.2 Property test: index expressions resolve to Index
    - **Property 5: Index expressions resolve to Index** — any `^k` with an integer operand resolves to `Standard.Index`
    - **Validates: Requirements 5.1**
    - _Requirements: 5.1_
    - _Properties: 5_
  - [ ]* 6.3 Property test: range expressions resolve to Range
    - **Property 6: Range expressions resolve to Range** — any range in any of the four bound forms resolves to `Standard.Range`
    - **Validates: Requirements 5.2**
    - _Requirements: 5.2_
    - _Properties: 6_
  - [x] 6.4 Element-access result typing and shared type predicates
    - Add `IsCountableType`, `IsIndexableType`, `IsSliceableType`, `TryGetElementType`, `TryGetSliceResultType` alongside the existing type utilities
    - Set `receiver[Index]` result to the element type; set `receiver[Range]` result per receiver kind (array -> same array, span -> same span, string -> string, general sliceable -> declared slice-result type)
    - _Requirements: 8.1, 8.2, 8.3, 8.4, 8.5_
  - [ ]* 6.5 Property test: index element-access result typing
    - **Property 9: Index element-access result typing** — for any Indexable_Type receiver and `Standard.Index` argument, the element access is accepted with result type equal to the element type
    - **Validates: Requirements 8.1**
    - _Requirements: 8.1_
    - _Properties: 9_
  - [ ]* 6.6 Property test: range element-access result typing
    - **Property 10: Range element-access result typing** — for any Sliceable_Type receiver and `Standard.Range` argument, the result type is the array/span/string/declared slice-result type as appropriate
    - **Validates: Requirements 8.2, 8.3, 8.4, 8.5**
    - _Requirements: 8.2, 8.3, 8.4, 8.5_
    - _Properties: 10_
  - [x] 6.7 Slice sub-pattern binding types
    - For a `SliceElement` whose `Subpattern` is a binding (`var` or declaration/type pattern), set the bound variable's Compile_Time_Type to the receiver's slice-result type (`TryGetSliceResultType`)
    - _Requirements: 20.2_

- [x] 7. Type checker
  - [x] 7.1 Register the new and repurposed diagnostics in `TypeCheckDiagnostics.cs`
    - Repurpose `MIST_TC_0054` (`PatternListRequiresArray` -> `PatternListRequiresIndexable`, broadened message)
    - Add `MIST_TC_0181`..`MIST_TC_0187` with the messages from the design's Diagnostic IDs table; retain `MIST_TC_0055`
    - Verify the current highest `MIST_TC_*` id is `0180` before numbering
    - _Requirements: 12.5, 20.6_
  - [x] 7.2 Implement `CheckFromEndExpression`
    - Accept integer-compatible operands; emit `MIST_TC_0181` for a non-integer operand (naming the type); emit `MIST_TC_0182` for a `void` operand
    - _Requirements: 6.1, 6.2, 6.3, 12.1_
  - [ ]* 7.3 Property test: index-from-end operand type checking
    - **Property 7: Index-from-end operand type checking** — accepted for integer operands; otherwise exactly one diagnostic (`MIST_TC_0181` non-integer, `MIST_TC_0182` void) naming the operand type and identifying the source span
    - **Validates: Requirements 6.1, 6.2, 6.3, 12.1**
    - _Requirements: 6.1, 6.2, 6.3, 12.1_
    - _Properties: 7_
  - [x] 7.4 Implement `CheckRange`
    - Each present bound must be integer-compatible or `Standard.Index`; otherwise reject the entire range with `MIST_TC_0183` (naming the bound type and its span)
    - An absent bound is always valid; a present-but-invalid bound is never treated as absent
    - _Requirements: 7.1, 7.2, 7.3, 7.4, 7.5, 12.2_
  - [ ]* 7.5 Property test: range bound type checking
    - **Property 8: Range bound type checking** — every absent bound and every integer/`Index` present bound accepted; any other present bound rejects the whole range with exactly one `MIST_TC_0183`
    - **Validates: Requirements 7.1, 7.2, 7.3, 7.4, 7.5, 12.2**
    - _Requirements: 7.1, 7.2, 7.3, 7.4, 7.5, 12.2_
    - _Properties: 8_
  - [x] 7.6 Extend `CheckIndexSuffix` to branch on the argument type
    - `Standard.Index` argument requires `IsIndexableType` else `MIST_TC_0184`; `Standard.Range` argument requires `IsSliceableType` else `MIST_TC_0185`; integer/custom-indexer behavior unchanged
    - _Requirements: 8.6, 8.7, 12.3, 12.4_
  - [ ]* 7.7 Property test: element-access receiver rejection
    - **Property 11: Element-access receiver rejection** — a non-indexable receiver with an `Index` argument emits exactly one `MIST_TC_0184`; a non-sliceable receiver with a `Range` argument emits exactly one `MIST_TC_0185`; each names the receiver type and identifies the span
    - **Validates: Requirements 8.6, 8.7, 12.3, 12.4**
    - _Requirements: 8.6, 8.7, 12.3, 12.4_
    - _Properties: 11_
  - [x] 7.8 Broaden `CheckListPatternElementList`
    - Require an `IsIndexableType` receiver else repurposed `MIST_TC_0054`; type-check element sub-patterns against the element type
    - Require `IsSliceableType` for a slice sub-pattern else `MIST_TC_0187`, and check it against the slice-result type; emit `MIST_TC_0186` for more than one slice element; retain `MIST_TC_0055`
    - _Requirements: 20.1, 20.3, 20.4, 20.5_
  - [ ]* 7.9 Property test: list and slice pattern type checking
    - **Property 16: List and slice pattern type checking** — list patterns on indexable receivers accepted with element sub-patterns checked against the element type; slice sub-patterns require sliceable receivers and bind to the slice-result type; `MIST_TC_0054`/`MIST_TC_0186`/`MIST_TC_0187` emitted for the respective error cases
    - **Validates: Requirements 20.1, 20.2, 20.3, 20.4, 20.5**
    - _Requirements: 20.1, 20.2, 20.3, 20.4, 20.5_
    - _Properties: 16_

- [x] 8. Checkpoint - type resolution and type checking are green
  - Ensure all tests pass, ask the user if questions arise.

- [x] 9. C# emitter
  - [x] 9.1 Emit the index and range expressions via an `EmitRange` dispatcher
    - `FromEndExpression -> ^(<operand>)` (operand always parenthesized); `RangeExpression -> <start>..<end>` preserving present/absent bounds; `NonRange` passthrough
    - Route `EmitMultiplicative`'s `Range` operands through `EmitRange`
    - _Requirements: 11.1, 11.2, 11.6_
  - [ ]* 9.2 Emission-shape unit and property test for index/range expressions
    - **Property 14: Index and range emission shape** — `^(<operand>)` for index; present/absent bounds preserved for ranges; `<receiver>[<argument>]` for element access with parentheses where required
    - **Validates: Requirements 11.1, 11.2, 11.3, 11.4, 11.6**
    - _Requirements: 11.1, 11.2, 11.6_
    - _Properties: 14_
  - [x] 9.3 Element access: native lowering plus `Slice(int, int)` fallback
    - Emit `receiver[arg]` and rely on C# built-in Index/Range lowering for arrays (new array), spans (view), and strings (new string)
    - Add the explicit `Slice(int, int)` fallback for a Mist-sliceable receiver that C# will not auto-lower
    - _Requirements: 11.3, 11.4, 11.5, 9.1, 9.2, 9.3, 9.4, 10.1, 10.2, 10.3, 10.4, 10.5, 10.6, 10.7_
  - [x] 9.4 Extend `EmitListPatternElement` for slice sub-patterns
    - Emit `..` (no sub-pattern) and `.. <subpattern>` by reading `Subpattern` (not the removed `Name`); reproduce `.. var m`, `.. _`, `.. int[] rest`, `.. [.., 5]`, `.. { Length: > 0 }`; preserve list-element order; emit in the corresponding C# `is`/`switch` position
    - _Requirements: 22.1, 22.2, 22.3, 22.4, 21.1, 21.2, 21.3, 21.4, 21.5_
  - [ ]* 9.5 Emission-shape tests for list and slice patterns
    - **Property 18: List and slice pattern emission shape** — `[<elements>]` with order preserved; `..` for a bare slice; `.. <subpattern>` for a carrying slice; emitted in the corresponding C# `is`/`switch` position
    - **Validates: Requirements 22.1, 22.2, 22.3, 22.4**
    - _Requirements: 22.1, 22.2, 22.3, 22.4_
    - _Properties: 18_

- [x] 10. Pretty printer
  - [x] 10.1 Print the new expression and pattern forms and thread the Range tier
    - `^operand` (no space); the four range forms with no space around `..`; element access `receiver[argument]`; list patterns `[...]`; slice elements `..` and `.. <subpattern>` (single space after `..`)
    - Route `EmitMultiplicative`'s `Range` operands through the range tier
    - _Requirements: 13.1, 13.2, 13.3, 23.1, 23.2_
  - [ ]* 10.2 Round-trip property test for index and range expressions
    - **Property 1: Index and range expression round-trip** — parse -> print -> parse yields a structurally equivalent AST for all index/range forms in any Postfix value position
    - **Validates: Requirements 2.1, 2.2, 2.3, 2.4, 2.5, 3.1, 3.2, 3.3, 3.4, 3.5, 3.6, 4.4, 13.1, 13.2, 13.3, 13.4**
    - _Requirements: 13.1, 13.2, 13.3, 13.4_
    - _Properties: 1_
  - [ ]* 10.3 Round-trip property test for list and slice patterns
    - **Property 15: List and slice pattern round-trip** — parse -> print -> parse yields a structurally equivalent AST for empty, fixed-length, bare-slice, and carrying-slice patterns in `is` and `switch` positions
    - **Validates: Requirements 19.1, 19.2, 19.3, 19.4, 19.5, 19.6, 19.7, 23.1, 23.2, 23.3**
    - _Requirements: 23.1, 23.2, 23.3_
    - _Properties: 15_

- [x] 11. Checkpoint - emission and pretty-printing are green
  - Ensure all tests pass, ask the user if questions arise.

- [x] 12. Verify array-literal vs list-pattern disambiguation still holds
  - [x]* 12.1 Run and, if needed, extend `ArrayLiteralPatternDisambiguationPropertyTests`
    - Verification only after the `SliceElement` change; this requirement is already satisfied by existing code, so add no new production code
    - _Requirements: 19.7_

- [x] 13. TextMate grammar highlighting
  - [x] 13.1 Highlight `^` and `..` in `Editor/mist-syntax-highlighting/syntaxes/mist.tmLanguage.json`
    - Scope `^` and `..` as `keyword.operator.mist` in expression position and slice-pattern position, reusing the existing operator scope; introduce no new tokens for list/slice patterns
    - _Requirements: 14.1, 14.2, 14.3, 14.4_

- [x] 14. Migrate existing `Range` type names
  - [x] 14.1 Migrate `samples/Iterators/Program.mist`: `Range` -> `CountSequence`
    - Rename the counting enumerable (with nested `Enumerator`), the constructor, every `new Range(...)` (incl. `new Range(4)`, `new Range(count)`), and comments; stdout baseline stays unchanged
    - _Requirements: 18.1, 18.2, 18.3_
  - [x] 14.2 Migrate `samples/RefinementTypes/Program.mist`: `Range` -> `Interval`, `ValidRange` -> `ValidInterval`
    - Update the constructor, the alias declaration, every `new Range(...)` (incl. `new Range(10, 50)`), refinement patterns (incl. `is ValidRange validR`), the `Describe()` string literal (e.g. `Interval(10, 50)`), and the expected-output baseline
    - _Requirements: 18.4, 18.5_
  - [x] 14.3 Migrate `book/snippets/type-system/refinement-types/property-pattern.mist`: `Range` -> `Interval`, `ValidRange` -> `ValidInterval`
    - Update the constructor, alias, every `new Range(10, 50)`, and patterns (incl. `is ValidRange valid`); regenerate the corresponding `.cs` snippet per the Book Snippet Policy; update chapter prose that references the snippet's `Range` type
    - _Requirements: 18.6, 18.7, 18.8_
  - [ ]* 14.4 Migration guard tests
    - Assert no user type named `Range` remains in the migrated files; assert the `Range` method and `[Range(...)]` attribute uses are untouched; assert migrated samples/snippets compile
    - _Requirements: 18.9, 18.10, 18.11, 18.12_

- [x] 15. Sample app and AllFeatures integration
  - [x] 15.1 Create `samples/RangesAndIndices/Program.mist` and wire it into `AllFeatures`
    - Place the locked proof code from requirements section 15 VERBATIM inside `RunDemo()` (immutable: if it fails to compile, fix the compiler, not the proof code)
    - Add the project reference and a `RangesAndIndices.Program.RunDemo()` call in `samples/AllFeatures/Program.mist`, and update `samples/__baselines__/AllFeatures.out`
    - Runtime Properties 12, 13, 17 are validated here through the existing `AllFeatures_CompilesRunsAndMatchesBaseline` test; add NO new spawning test
    - _Requirements: 15.1, 15.2, 15.3, 15.4, 15.5, 15.6, 15.7_
    - _Properties: 12, 13, 17_

- [x] 16. Checkpoint - sample compiles and AllFeatures baseline matches (runtime Properties 12, 13, 17 validated)
  - Ensure all tests pass, ask the user if questions arise.

- [x] 17. Documentation and feature tracking
  - [x] 17.1 Book: add the `^`/`..` section and the list/slice-pattern section
    - Cover index-from-end access; the four range forms; from-end bounds in ranges; inclusive-start/exclusive-end slicing; array (new array), span (view), and string (new string) slicing; stored `Index`/`Range` values; fixed-length list patterns; element sub-patterns; the slice pattern `..`; `.. var name` binding; `is`/`switch` usage; the empty list pattern `[]`; and the at-most-one-slice constraint
    - All examples are compiled snippets per the Book Snippet Policy; reference the new section(s) in `book/SUMMARY.md`
    - _Requirements: 16.1, 16.2, 16.3, 16.4, 16.5_
  - [x] 17.2 Update `CSHARP_FEATURES.md`
    - Mark the index-from-end operator `^`, the range operator `..`, list patterns, and slice patterns implemented; update the summary counts
    - _Requirements: 17.1, 17.2, 17.3_

- [x] 18. Final checkpoint - full suite green
  - Ensure all tests pass, ask the user if questions arise.

## Notes

- Tasks marked with `*` are optional test sub-tasks and can be skipped for a faster MVP; they are still scheduled in the dependency graph.
- All tests are in-process through a shared, reused `CompilerDriver`; this feature adds zero process-spawning tests. Runtime Properties 12, 13, and 17 are validated exclusively by task 15.1 (the proof-code sample wired into the existing `AllFeatures` baseline).
- Property-based tests use FsCheck, run a minimum of 100 iterations, and are tagged `// Feature: ranges-and-indices, Property N: <text>`.
- The grammar edits (2.1-2.4) plus the ripple (3.1) form the first build-restoring unit; the build is expected green from checkpoint task 4 onward.
- Each task references specific requirement sub-clauses for traceability; design correctness properties are annotated with `_Properties: N_`.

## Task Dependency Graph

```json
{
  "waves": [
    { "id": 0, "tasks": ["1.1", "5.1"] },
    { "id": 1, "tasks": ["2.1", "5.2"] },
    { "id": 2, "tasks": ["2.2"] },
    { "id": 3, "tasks": ["2.3"] },
    { "id": 4, "tasks": ["2.4"] },
    { "id": 5, "tasks": ["3.1"] },
    { "id": 6, "tasks": ["2.5", "3.2", "3.3", "3.4", "6.1", "7.1"] },
    { "id": 7, "tasks": ["6.2", "6.3", "6.4", "7.2"] },
    { "id": 8, "tasks": ["6.5", "6.6", "6.7", "7.4"] },
    { "id": 9, "tasks": ["7.3", "7.6"] },
    { "id": 10, "tasks": ["7.5", "7.8"] },
    { "id": 11, "tasks": ["7.7", "9.1"] },
    { "id": 12, "tasks": ["7.9", "9.3"] },
    { "id": 13, "tasks": ["9.2", "9.4"] },
    { "id": 14, "tasks": ["9.5", "10.1"] },
    { "id": 15, "tasks": ["10.2", "10.3"] },
    { "id": 16, "tasks": ["12.1", "13.1", "14.1", "14.2", "14.3"] },
    { "id": 17, "tasks": ["14.4", "15.1"] },
    { "id": 18, "tasks": ["17.1", "17.2"] }
  ]
}
```
