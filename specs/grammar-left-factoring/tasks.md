# Implementation Plan: PEG Ordered-Choice Parser Emitter

## Overview

Rewrite the ParserEmitter to replace the three-tier LL(*) prediction strategy with a uniform PEG ordered-choice backtracking pattern. The implementation proceeds incrementally: verify prerequisites, strip legacy code, rewrite the two core emission methods, clean up parser state, then validate with builds, existing tests, and new property/integration tests.

## Tasks

- [x] 1. Verify DiagnosticBag API exists in AstEmitter
  - Open `Source/Storm.SourceGenerator/Emit/AstEmitter.cs` and confirm `DiagnosticBag` emits `Count` property and `TruncateTo(int)` method
  - No code changes expected — this is a prerequisite check
  - If missing, add the `Count` property and `TruncateTo` method to the emitted `DiagnosticBag` class
  - _Requirements: 11.1, 11.2, 11.3, 11.4_

- [x] 2. Strip legacy prediction code from ParserEmitter
  - [x] 2.1 Remove the `PredictionStrategy` enum, `ComputeFirstSet`, `ComputeFirstSetInner`, `IsNullable`, `DetermineStrategy`, `AreDisjoint`, `FindOverlappingAlts`, `BuildLookaheadCondition`, and `FindFirstRequiredToken` methods from `Source/Storm.SourceGenerator/Emit/ParserEmitter.cs`
    - Remove the `DefaultMaxLookahead` constant
    - Remove the `EmitLL1Switch` method
    - Remove the `EmitBoundedLookahead` method
    - Keep `EmitBacktracking` temporarily (it will be rewritten in task 3)
    - _Requirements: 5.1, 5.2, 5.3, 5.6_

  - [x] 2.2 Remove `_memo` field from `EmitParserState` and `TryParse` helper from `EmitHelpers`
    - In `EmitParserState`: remove the line emitting `private static Dictionary<(int, string), (bool, int)>? _memo;`
    - In `EmitHelpers`: remove the entire `TryParse` method emission block
    - In `EmitHelpers` `Init` method: remove the line `_memo = new Dictionary<(int, string), (bool, int)>();`
    - _Requirements: 5.4, 10.3, 12.1_

- [x] 3. Rewrite `EmitMultiAlt` to emit PEG ordered-choice pattern
  - Replace the current `EmitMultiAlt` method body (which computes FIRST sets and dispatches to LL1/lookahead/backtracking) with a single code path that emits the PEG ordered-choice pattern directly
  - The new method must: (a) emit state save (`var saved = _pos; var savedError = _hasError; var savedDiagCount = _diagnostics?.Count ?? 0;`), (b) for each alternative in declaration order emit state reset + `{ }` scoped block using `EmitAltBodyWithCheck`, (c) emit epilogue restoring `_hasError` to `savedError` and returning `null`
  - Remove the old `EmitBacktracking` method since its logic is now inlined into `EmitMultiAlt`
  - Each alternative block must reset `_pos = saved; _hasError = false; _diagnostics?.TruncateTo(savedDiagCount);` before attempting
  - Success check must be `if (ok && !_hasError) return result;`
  - _Requirements: 1.1, 1.2, 1.3, 1.4, 1.5, 1.6, 2.1, 2.2, 2.3, 6.2, 6.3, 6.4, 7.1, 7.2, 7.3, 7.4, 10.1_

- [x] 4. Simplify `EmitSingleAlt` to remove FIRST set guard
  - Remove the `ComputeFirstSet` call and the `if (!(condition)) return null;` guard block from `EmitSingleAlt`
  - The method should begin with just `var sp = _pos;` followed by `EmitAltBody` — the `if (_hasError) return null;` guard is already emitted by `EmitInternalParseMethod` at the top of the method
  - Verify no references to `ComputeFirstSet` remain in the file
  - _Requirements: 3.1, 3.2, 3.3, 10.5_

- [x] 5. Remove FIRST set usage from unlabeled Optional and ZeroOrMore in `EmitFullExprParse`
  - In the `OptionalIR` case of `EmitFullExprParse`: replace the `ComputeFirstSet` guard with a position-save/restore approach — save `_pos` before attempting the inner parse, restore on failure
  - In the `ZeroOrMoreIR` case of `EmitFullExprParse`: replace the `ComputeFirstSet`-based `while` condition with a position-progress-based loop (`while (!_hasError)` with break on no progress or null result), matching the pattern already used in `EmitListExprParse`
  - This ensures no calls to `ComputeFirstSet` remain anywhere in the file
  - _Requirements: 5.1, 5.6_

- [x] 6. Checkpoint — Clean build of the source generator
  - Run `dotnet build` on the solution to verify `Storm.SourceGenerator` compiles without errors
  - Fix any compilation errors from the refactoring (missing references, unused usings, etc.)
  - Ensure all tests pass, ask the user if questions arise.

- [x] 7. Rebuild the Mist project and verify the generated parser compiles
  - Run `dotnet build Source/Mist/Mist.csproj` to trigger the source generator and produce the new `Mist.Parser.g.cs`
  - Verify the generated parser compiles without errors
  - Inspect the generated `Mist.Parser.g.cs` to confirm: no `switch (Peek())` for alt selection, no `Peek(1)` lookahead, no `TryParse` calls, no `_memo` references
  - _Requirements: 5.2, 5.3, 5.4, 10.2, 10.3, 10.4_

- [x] 8. Run existing parser tests
  - Run `dotnet test Test/Mist.Tests/` to execute the existing `NullablePrefixParsingTests` and any other parser tests
  - All existing tests must pass with zero new failures
  - _Requirements: 9.1, 9.2, 9.3_

- [x] 9. Run sample applications end-to-end
  - Run the Mist compiler on `samples/HelloMist/` and verify it produces a working .NET executable through the full pipeline (lex → parse → semantic model → type check → C# transpile → dotnet build)
  - Run the Mist compiler on `samples/Simpliest/` and verify it produces a working .NET executable
  - _Requirements: 8.1, 8.2, 8.3_

- [x] 10. Checkpoint — Ensure all tests pass
  - Run `dotnet test` on the full solution
  - Ensure all tests pass, ask the user if questions arise.

- [x] 11. Update ParserEmitter unit tests
  - [x] 11.1 Update existing `ParserEmitter_EmptyGrammar_ProducesEmptyParser` test in `Test/Storm.SourceGenerator.Tests/EmitterTests.cs` if the empty grammar output changed
    - _Requirements: 10.1_

  - [x] 11.2 Add test: multi-alternative rule emits PEG ordered-choice pattern (state save, per-alt reset, scoped blocks, epilogue)
    - Create a grammar with a multi-alt rule (e.g., `rule Expr = Number as Lit | Ident as Var;`)
    - Emit the parser and verify the output contains `var saved = _pos`, `var savedError = _hasError`, `var savedDiagCount`, per-alternative `_pos = saved` resets, and `_hasError = savedError` in the epilogue
    - _Requirements: 7.1, 7.3, 7.4_

  - [x] 11.3 Add test: single-alternative rule emits direct parse without backtracking artifacts
    - Create a grammar with a single-alt rule (e.g., `rule Stmt = Number;`)
    - Emit the parser and verify the output does NOT contain `var saved = _pos`, `_hasError = false` (as reset), or `TruncateTo`
    - _Requirements: 3.1, 3.2_

  - [x] 11.4 Add test: no legacy artifacts in emitted code
    - Create a grammar with both single-alt and multi-alt rules
    - Emit the parser and verify: no `switch (Peek())` for alt selection, no `Peek(1)`, no `TryParse`, no `_memo`
    - _Requirements: 5.2, 5.3, 5.4, 10.2, 10.3, 10.4_

  - [x] 11.5 Add test: emitted parser state does not declare `_memo`
    - Emit a parser and verify the output does not contain `_memo`
    - _Requirements: 5.4, 12.1_

- [x] 12. Add property-based tests for emitted code patterns
  - [x] 12.1 Write property test for Property 1: Multi-alternative rules emit PEG ordered-choice pattern
    - **Property 1: Multi-alternative rules emit PEG ordered-choice pattern**
    - Generate random `RuleIR` instances with 2+ alternatives using FsCheck, build an `EmissionContext`, call `ParserEmitter.Emit`, and verify the emitted code contains exactly one state-save preamble, one state-reset block per alternative, `{ }` scoped blocks, and an epilogue restoring `_hasError` to `savedError`
    - **Validates: Requirements 1.1, 1.2, 1.3, 1.4, 1.5, 1.6, 2.1, 2.2, 2.3, 7.1, 7.2, 7.3, 7.4**

  - [x] 12.2 Write property test for Property 2: Single-alternative rules emit direct parse pattern
    - **Property 2: Single-alternative rules emit direct parse pattern**
    - Generate random `RuleIR` instances with exactly 1 alternative, emit code, verify it begins with `if (_hasError) return null;` and does NOT contain `var saved = _pos`, `_hasError = false` (as reset), or `TruncateTo`
    - **Validates: Requirements 3.1, 3.2, 3.3**

  - [x] 12.3 Write property test for Property 3: No legacy prediction artifacts in emitted code
    - **Property 3: No legacy prediction artifacts in emitted code**
    - Generate random `RuleIR` instances (any arity), emit code, verify no `switch (Peek())` for alt selection, no `Peek(1)`, no `TryParse`, no `_memo`
    - **Validates: Requirements 5.2, 5.3, 5.4, 10.2, 10.3, 10.4**

  - [x] 12.4 Write property test for Property 4: DiagnosticBag TruncateTo preserves prefix
    - **Property 4: DiagnosticBag TruncateTo preserves prefix**
    - Generate random sequences of `Add`/`TruncateTo` operations on a `DiagnosticBag`, verify `Count` equals the truncation target and the first `n` messages are preserved
    - **Validates: Requirements 11.3, 11.4**

- [x] 13. Final checkpoint — Ensure all tests pass
  - Run `dotnet test` on the full solution
  - Verify zero test failures across all test projects
  - Ensure all tests pass, ask the user if questions arise.

## Notes

- Tasks marked with `*` are optional and can be skipped for faster MVP
- The grammar files (`Mist.storm`, `keywords.storm`) are not modified (Requirement 4)
- The `AstEmitter` is not modified — `DiagnosticBag` already has `Count` and `TruncateTo`
- FsCheck.Xunit 3.1.0 is already a dependency in `Storm.SourceGenerator.Tests`
- Property tests validate universal correctness properties from the design document
- Unit tests validate specific examples and edge cases
- Checkpoints ensure incremental validation after each major phase
