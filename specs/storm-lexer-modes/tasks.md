# Implementation Plan: Storm Lexer Modes

## Overview

This plan implements lexer mode support across the Storm source generator pipeline. Work proceeds bottom-up through the pipeline layers: AST types first, then frontend (lexer/parser), resolve, validation, IR, emitter, and finally the Mist grammar migration and MistLexer simplification. Each task builds on the previous, and checkpoints verify incremental correctness.

## Tasks

- [x] 1. Extend StormSyntax AST with mode and action types
  - [x] 1.1 Add `StormModeAction`, `StormPushAction`, and `StormPopAction` types to `StormSyntax.cs`
    - Add `public abstract record StormModeAction;`
    - Add `public sealed record StormPushAction(string ModeName) : StormModeAction;`
    - Add `public sealed record StormPopAction() : StormModeAction;`
    - _Requirements: 3.2_
  - [x] 1.2 Add `StormModeDecl` record type to `StormSyntax.cs`
    - Add `public sealed record StormModeDecl(string Name, IReadOnlyList<StormDeclaration> Declarations, int Offset, int Line, int Column) : StormDeclaration(Name, new StormReferenceExpr(Name), Offset, Line, Column);`
    - _Requirements: 3.1, 3.4_
  - [x] 1.3 Add optional `StormModeAction? Action` parameter to `StormTokenDecl` and `StormHiddenTokenDecl`
    - Extend both record types with a new `Action` parameter defaulting to `null`
    - _Requirements: 3.3_

- [x] 2. Extend StormLexer to recognize mode-related tokens
  - [x] 2.1 Add new token kinds to `StormTokenKind` enum in `StormLexer.cs`
    - Add `ModeKeyword`, `DefaultKeyword`, `PushKeyword`, `PopKeyword`, `FatArrow`, `LeftBrace`, `RightBrace`
    - _Requirements: 1.1, 1.2, 2.1_
  - [x] 2.2 Update `ClassifyKeyword` to recognize `mode`, `default`, `push`, `pop`
    - Map `"mode"` → `ModeKeyword`, `"default"` → `DefaultKeyword`, `"push"` → `PushKeyword`, `"pop"` → `PopKeyword`
    - _Requirements: 1.1, 2.1_
  - [x] 2.3 Update `ClassifyOperator` and the main tokenize loop for `=>`, `{`, `}`
    - Check for `=>` as a two-character operator before single `=`
    - Add `{` → `LeftBrace` and `}` → `RightBrace`
    - _Requirements: 2.1_
  - [ ]* 2.4 Write unit tests for new StormLexer tokens
    - Test tokenization of `mode`, `default`, `push`, `pop`, `=>`, `{`, `}` in `StormLexerTests.cs`
    - _Requirements: 1.1, 2.1_

- [x] 3. Extend StormParser to parse mode declarations and action clauses
  - [x] 3.1 Add `ParseModeDecl()` method to `StormParser.cs`
    - Parse `mode Identifier { Declaration* }` syntax
    - Add `ModeKeyword` case to `ParseDeclaration()` switch
    - _Requirements: 1.1, 1.2_
  - [x] 3.2 Add `ParseActionClause()` method to `StormParser.cs`
    - Parse optional `=> push Identifier` or `=> pop` after token body expression
    - Call from `ParseTokenDecl()` and `ParseHiddenTokenDecl()` before consuming semicolon
    - _Requirements: 2.1, 2.6_
  - [x] 3.3 Update `SynchronizeToDeclaration()` to include `ModeKeyword` and `RightBrace`
    - Add these token kinds to the synchronization stop set
    - _Requirements: 1.1_
  - [ ]* 3.4 Write unit tests for mode declaration and action clause parsing
    - Test parsing of `mode Foo { token X = "x"; }`, `token Y = "y" => push Foo;`, `token Z = "z" => pop;`, empty mode blocks
    - Verify AST structure: `StormModeDecl`, `StormPushAction`, `StormPopAction`, null action
    - _Requirements: 1.1, 1.2, 2.1, 2.6_
  - [ ]* 3.5 Write property test for action clause parsing (Property 3)
    - **Property 3: Action Clause Parsing**
    - Generate random token declarations with push/pop/none actions, parse them, verify `Action` property matches
    - **Validates: Requirements 2.1, 2.6**

- [x] 4. Checkpoint — Verify frontend parsing
  - Ensure all tests pass, ask the user if questions arise.

- [x] 5. Add new diagnostic IDs for mode validation
  - [x] 5.1 Add mode-related diagnostic IDs to `StormDiagnosticIds.cs`
    - Add `STORM_DUPLICATE_MODE` (`STORM0011`), `STORM_RULE_IN_MODE` (`STORM0012`), `STORM_UNDEFINED_MODE` (`STORM0013`), `STORM_POP_FROM_DEFAULT` (`STORM0014`), `STORM_RESERVED_MODE_NAME` (`STORM0015`)
    - _Requirements: 1.5, 1.6, 1.7, 2.4, 2.5_

- [x] 6. Create ModeValidationPass
  - [x] 6.1 Create `ModeValidationPass.cs` in `Source/Storm.SourceGenerator/Validate/`
    - Validate duplicate mode names → `STORM0011` error
    - Validate rules inside mode blocks → `STORM0012` error
    - Validate push targets reference declared modes → `STORM0013` error
    - Validate pop from default mode → `STORM0014` warning
    - Validate reserved `default` mode name → `STORM0015` error
    - _Requirements: 1.5, 1.6, 1.7, 2.4, 2.5_
  - [ ]* 6.2 Write unit tests for ModeValidationPass
    - Test each diagnostic condition: duplicate mode, rule in mode, undefined push target, pop from default, reserved name
    - _Requirements: 1.5, 1.6, 1.7, 2.4, 2.5_
  - [ ]* 6.3 Write property test for mode diagnostic detection (Property 7)
    - **Property 7: Mode-Related Diagnostic Detection**
    - Generate grammars with known mode errors, verify at least one diagnostic is reported
    - **Validates: Requirements 1.5, 1.6, 2.4**

- [x] 7. Extend StormGrammar resolve layer for mode declarations
  - [x] 7.1 Update `CollectDeclarations` in `StormGrammar.cs` to handle `StormModeDecl`
    - When a `StormModeDecl` is encountered, add the mode declaration itself and each inner declaration to the declarations list
    - Add a `ModeNames` property that lists all declared mode names (always including `"default"`)
    - _Requirements: 1.3, 1.4_

- [x] 8. Extend GrammarIR with mode metadata
  - [x] 8.1 Add `ModeActionIR` type hierarchy to `Source/Storm.SourceGenerator/IR/`
    - Add `ModeActionIR` (abstract), `PushModeIR(string ModeName)`, `PopModeIR`, `NoModeActionIR`
    - _Requirements: 4.2_
  - [x] 8.2 Add `Mode` and `ModeAction` properties to `TokenIR`
    - Add `string Mode` (default `"default"`) and `ModeActionIR ModeAction` (default `NoModeActionIR`) to `TokenIR`
    - _Requirements: 4.1, 4.2_
  - [x] 8.3 Add `Modes` property to `GrammarIR`
    - Add `IReadOnlyList<string> Modes` to `GrammarIR`, always including `"default"`
    - _Requirements: 4.3_

- [x] 9. Extend GrammarIRLowering for mode-aware lowering
  - [x] 9.1 Update `Lower` method in `GrammarIRLowering.cs` for mode tracking and assignment
    - Scan for `StormModeDecl` nodes to build mode name list
    - Assign each token's `Mode` from enclosing `StormModeDecl` or `"default"` for top-level
    - Convert `StormModeAction` to `ModeActionIR` (push/pop/none)
    - _Requirements: 4.4, 1.3, 1.4_
  - [x] 9.2 Update fragment scoping to be mode-aware
    - Change `fragmentsByName` to `Dictionary<(string Mode, string Name), StormDeclaration>` for mode-scoped lookup
    - Resolve fragments first within current mode, then fall back to default mode
    - _Requirements: 4.5_
  - [ ]* 9.3 Write property test for mode assignment correctness (Property 2)
    - **Property 2: Mode Assignment Correctness**
    - Generate grammars with mixed mode/default tokens, lower to IR, verify `Mode` and `ModeAction` properties
    - **Validates: Requirements 1.3, 1.4, 4.4, 2.2, 2.3**
  - [ ]* 9.4 Write property test for fragment scoping (Property 8)
    - **Property 8: Fragment Scoping Within Modes**
    - Generate grammars with mode-scoped fragments, verify inlining respects mode boundaries
    - **Validates: Requirements 4.5**

- [x] 10. Checkpoint — Verify IR lowering
  - Ensure all tests pass, ask the user if questions arise.

- [x] 11. Extend LexerEmitter for mode-aware code generation
  - [x] 11.1 Emit mode constants and per-mode token groupings in `LexerEmitter.cs`
    - Generate `private const int Mode_default = 0;`, `private const int Mode_Interpolation = 1;`, etc.
    - Generate `ModeTokens` array mapping each mode to its token indices
    - Generate `TokenModes` array mapping each `TokenKind` to its mode index
    - _Requirements: 5.1, 5.2, 5.6, 5.7_
  - [x] 11.2 Restructure `EmitLexMethod` to use mode stack
    - Initialize mode stack with `Mode_default`
    - Only try `TryMatch_*` for tokens in `ModeTokens[modeStack.Peek()]`
    - After emitting a token, apply push/pop action to mode stack
    - Pop safety: if stack has only default mode, pop is a no-op
    - Remove the existing filter that skips `INTERPOLATED_STRING_START/TEXT/END` tokens
    - _Requirements: 5.1, 5.2, 5.3, 5.4, 5.5_
  - [x] 11.3 Emit `GetTokenMode` method
    - Generate `public static int GetTokenMode(int tokenKind) => TokenModes[tokenKind];`
    - _Requirements: 5.7_
  - [ ]* 11.4 Write property test for token kind uniqueness (Property 5)
    - **Property 5: Token Kind Uniqueness Across Modes**
    - Generate grammars with multiple modes, verify all `TokenKind` values are globally unique
    - **Validates: Requirements 5.6**
  - [ ]* 11.5 Write property test for mode-aware lexer correctness (Property 4)
    - **Property 4: Mode-Aware Lexer Correctness**
    - Generate simple mode grammars + input strings, run generated lexer, verify tokens match current mode
    - **Validates: Requirements 5.2, 5.3, 5.4**

- [x] 12. Checkpoint — Verify emitter
  - Ensure all tests pass, ask the user if questions arise.

- [x] 13. Create Storm Pretty-Printer
  - [x] 13.1 Create `StormPrettyPrinter.cs` in `Source/Storm.SourceGenerator/Frontend/`
    - Implement `Print(StormFile)` that formats the AST back to Storm source text
    - Handle `StormModeDecl` as `mode ModeName { ... }` with indented declarations
    - Handle `StormPushAction` as `=> push ModeName` and `StormPopAction` as `=> pop`
    - _Requirements: 9.1, 9.2, 9.3_
  - [ ]* 13.2 Write property test for round-trip (Property 1)
    - **Property 1: Storm Grammar Round-Trip**
    - Generate random `StormFile` ASTs with modes and actions, pretty-print, re-parse, verify equivalence
    - **Validates: Requirements 9.4, 9.1, 9.2, 9.3, 1.1, 2.1**

- [x] 14. Update Mist grammar to use modes for interpolated strings
  - [x] 14.1 Restructure `Mist.storm` to use mode-based interpolated string tokens
    - Add `=> push Interpolation` to `INTERPOLATED_STRING_START`
    - Add `=> push default` to `LEFT_BRACE` and `=> pop` to `RIGHT_BRACE`
    - Create `mode Interpolation { ... }` block with `INTERPOLATED_STRING_TEXT`, `INTERPOLATION_OPEN` (`{` => push default), `INTERPOLATED_STRING_END` (`"` => pop), and `ESCAPE_SEQUENCE` fragment
    - _Requirements: 6.1, 6.2, 6.3, 6.4, 6.5_

- [x] 15. Simplify MistLexer
  - [x] 15.1 Remove post-processing workaround from `MistLexer.cs`
    - Remove `FixInterpolatedStrings`, `LexInterpolatedString`, `LexExpression`, `AdjustLinePosition` methods
    - Simplify `Lex` to directly delegate to the generated lexer and map diagnostics
    - _Requirements: 7.1, 7.2, 7.3_
  - [ ]* 15.2 Write property test for interpolation equivalence (Property 6)
    - **Property 6: Interpolated String Lexing Equivalence**
    - Generate Mist source with interpolated strings, compare token streams from old vs new approach
    - **Validates: Requirements 6.6**

- [x] 16. Update Storm TextMate grammar for syntax highlighting
  - [x] 16.1 Update `storm.tmLanguage.json` with mode-related patterns
    - Add `mode-blocks` pattern: `mode` keyword + mode name
    - Add `action-clauses` patterns: `=> push ModeName` and `=> pop`
    - Update `keywords` to include `mode`, `default`, `push`, `pop`
    - Add brace patterns for `{` and `}`
    - _Requirements: 8.1, 8.2, 8.3, 8.4, 8.5, 8.6_

- [x] 17. Wire ModeValidationPass into the generator pipeline
  - [x] 17.1 Register `ModeValidationPass` in `StormIncrementalGenerator.cs`
    - Add the mode validation pass to the pipeline after name resolution
    - _Requirements: 1.5, 1.6, 1.7, 2.4, 2.5_

- [x] 18. Final checkpoint — Full pipeline verification
  - Ensure all tests pass, ask the user if questions arise.

## Notes

- Tasks marked with `*` are optional and can be skipped for faster MVP
- Each task references specific requirements for traceability
- Checkpoints ensure incremental validation
- Property tests validate universal correctness properties from the design document
- Unit tests validate specific examples and edge cases
- The implementation language is C# throughout, matching the existing codebase
