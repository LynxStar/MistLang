# Requirements Document

## Introduction

This document specifies the requirements for replacing the current LL(*) prediction strategy in the ParserEmitter with a pure PEG (Parsing Expression Grammar) ordered-choice backtracking strategy. The current ParserEmitter (`Source/Storm.SourceGenerator/Emit/ParserEmitter.cs`) uses a three-tier prediction system: LL(1) switch, bounded lookahead, and backtracking fallback. This complexity produces fragile generated code (~3400 lines in `Mist.Parser.g.cs`) with incomplete state restoration during backtracking, causing the sample application to fail at the semantic model stage.

The new approach keeps the Mist grammar (`Source/Mist/Grammar/Mist.storm`) exactly as-is — no left-factoring, no rule restructuring — and rewrites the ParserEmitter to generate a uniform PEG ordered-choice backtracking pattern for every multi-alternative rule. Every alternative is tried in declaration order; on failure, parser state (position, error flag, diagnostic count) is fully restored before trying the next alternative. Single-alternative rules parse directly without backtracking overhead.

This approach trades theoretical parsing efficiency for correctness, simplicity, and debuggability. The generated parser will be shorter, uniform, and trivially auditable. Memoization (packrat parsing) is noted as a future optimization but is not required for this spec.

This spec builds on:
- `.kiro/specs/mist-compiler-architecture/` — defines the compiler pipeline, Storm grammar system, and ParserEmitter
- `.kiro/specs/mist-language-v1/` — defines the Mist grammar and language features

This spec does not modify the Mist grammar or those existing specs. It replaces the ParserEmitter's code generation strategy.

## Glossary

- **Storm_Grammar**: The Storm grammar file (`Mist.storm`) that defines the lexical and syntactic structure of Mist.
- **ParserEmitter**: The component in `Storm.SourceGenerator` (`Source/Storm.SourceGenerator/Emit/ParserEmitter.cs`) that reads the grammar IR and emits C# recursive-descent parser code.
- **Generated_Parser**: The C# parser class (`MistParser`) produced by the ParserEmitter from the Storm grammar.
- **PEG_Ordered_Choice**: A parsing strategy where alternatives are tried in declaration order; the first alternative that succeeds is committed to. On failure, parser state is fully restored before trying the next alternative.
- **State_Save_Restore**: The act of saving parser position (`_pos`), error flag (`_hasError`), and diagnostic count (`_diagnostics.Count`) before attempting an alternative, and restoring all three if the alternative fails.
- **Speculative_Parse**: An attempt to parse an alternative that may fail. During a speculative parse, `_hasError` being set indicates the current alternative failed, not a permanent parse failure.
- **Genuine_Parse_Failure**: A condition where no alternative in a rule succeeds. Only a genuine parse failure should propagate `_hasError` to the caller.
- **Single_Alternative_Rule**: A grammar rule with exactly one alternative (no `|` separator). These rules parse directly without backtracking overhead.
- **Multi_Alternative_Rule**: A grammar rule with two or more alternatives separated by `|`. These rules use PEG_Ordered_Choice.
- **DiagnosticBag**: The Storm runtime diagnostic collection class that supports `Add(string)`, `Count`, and `TruncateTo(int)` for state restoration during backtracking.
- **FIRST_Set**: A set of tokens that can begin a given grammar alternative. Used by the current ParserEmitter for LL(1) prediction. To be removed in this refactor.
- **Memoization**: Caching the result of a parse attempt at a given position to avoid redundant work. Also known as packrat parsing. Deferred as a future optimization.
- **AST_Node**: A node in the abstract syntax tree produced by the Generated_Parser.
- **Sample_Application**: The Mist console applications in `samples/` that exercise language features end-to-end.

## Requirements

### Requirement 1: PEG Ordered-Choice Semantics for Multi-Alternative Rules

**User Story:** As a compiler developer, I want every multi-alternative grammar rule to use PEG ordered-choice backtracking so that alternatives are tried in declaration order with correct state restoration, eliminating the class of bugs caused by the current mixed prediction strategies.

#### Acceptance Criteria

1. WHEN the ParserEmitter processes a Multi_Alternative_Rule, THE ParserEmitter SHALL emit code that tries each alternative in grammar declaration order, committing to the first alternative that succeeds.
2. WHEN an alternative fails during a speculative parse, THE Generated_Parser SHALL restore `_pos` to the saved value before trying the next alternative.
3. WHEN an alternative fails during a speculative parse, THE Generated_Parser SHALL restore `_hasError` to `false` before trying the next alternative.
4. WHEN an alternative fails during a speculative parse, THE Generated_Parser SHALL call `_diagnostics.TruncateTo(savedDiagCount)` to discard diagnostics added by the failed alternative before trying the next alternative.
5. WHEN an alternative produces a non-null result and `_hasError` is `false`, THE Generated_Parser SHALL return that result immediately without trying remaining alternatives.
6. WHEN all alternatives of a Multi_Alternative_Rule fail, THE Generated_Parser SHALL restore `_pos`, `_hasError`, and diagnostic count to their values from before the first alternative was attempted, and return `null`.

### Requirement 2: Correct State Save and Restore

**User Story:** As a compiler developer, I want the generated parser to save and restore all mutable parser state (position, error flag, diagnostic count) between alternatives so that a failed speculative parse leaves no side effects.

#### Acceptance Criteria

1. THE Generated_Parser SHALL save `_pos`, `_hasError`, and `_diagnostics?.Count ?? 0` into local variables at the start of every Multi_Alternative_Rule parse method, before attempting any alternative.
2. WHEN restoring state before each alternative attempt, THE Generated_Parser SHALL set `_pos` to the saved position, `_hasError` to `false`, and truncate diagnostics to the saved count.
3. WHEN all alternatives fail, THE Generated_Parser SHALL restore `_hasError` to its original saved value (not unconditionally to `false`), preserving any error state that existed before the rule was entered.
4. FOR ALL valid Mist source inputs, parsing then examining `_hasError` SHALL yield `false` when the parse succeeds, regardless of how many speculative alternatives were tried and rejected internally.
5. FOR ALL invalid Mist source inputs where at least one rule has no matching alternative, THE Generated_Parser SHALL set `_hasError` to `true` only when the outermost failing rule propagates the failure.

### Requirement 3: Single-Alternative Rules Parse Directly

**User Story:** As a compiler developer, I want single-alternative grammar rules to parse directly without any backtracking overhead so that the generated code is efficient for the common case.

#### Acceptance Criteria

1. WHEN the ParserEmitter processes a Single_Alternative_Rule, THE ParserEmitter SHALL emit a direct parse method without saving or restoring `_pos`, `_hasError`, or diagnostic count.
2. THE Generated_Parser code for Single_Alternative_Rules SHALL contain no `saved = _pos` pattern, no `_hasError = false` reset, and no `TruncateTo` call.
3. THE Generated_Parser code for Single_Alternative_Rules SHALL begin with `if (_hasError) return null;` to respect the halt-on-first-error contract.

### Requirement 4: Grammar Preservation

**User Story:** As a compiler developer, I want the Mist grammar file to remain exactly as-is so that no grammar restructuring, left-factoring, or rule splitting is required for this refactor.

#### Acceptance Criteria

1. THE Storm_Grammar file (`Source/Mist/Grammar/Mist.storm`) SHALL NOT be modified as part of this refactor.
2. THE Storm_Grammar file (`Source/Mist/Grammar/keywords.storm`) SHALL NOT be modified as part of this refactor.
3. THE Generated_Parser SHALL correctly parse all valid Mist programs using the existing grammar without any grammar transformations.

### Requirement 5: ParserEmitter Simplification

**User Story:** As a compiler developer, I want the ParserEmitter to be simplified by removing the FIRST set computation, LL(1) switch emission, and bounded lookahead code paths so that the emitter has a single, uniform code generation strategy.

#### Acceptance Criteria

1. THE ParserEmitter SHALL NOT compute FIRST sets for grammar alternatives.
2. THE ParserEmitter SHALL NOT emit `switch (Peek())` LL(1) prediction code for any rule.
3. THE ParserEmitter SHALL NOT emit bounded lookahead chains using `Peek(0)` and `Peek(1)` for any rule.
4. THE ParserEmitter SHALL NOT emit the `TryParse` memoization helper method or the `_memo` dictionary field in the generated parser.
5. THE ParserEmitter SHALL emit only two code patterns: direct parsing for Single_Alternative_Rules, and PEG_Ordered_Choice for Multi_Alternative_Rules.
6. THE ParserEmitter SHALL remove or delete the `ComputeFirstSet`, `DetermineStrategy`, `EmitLL1Switch`, `EmitBoundedLookahead`, `FindOverlappingAlts`, and `BuildLookaheadCondition` methods and the `PredictionStrategy` enum.
7. THE ParserEmitter SHALL retain the `Peek`, `Cur`, `Eat`, `Match`, `Require`, `Error`, `Span`, and `AttachHidden` helper methods unchanged.


### Requirement 6: Error Flag Semantics During Speculative Parsing

**User Story:** As a compiler developer, I want the `_hasError` flag to only be set permanently when no alternative succeeds (a genuine parse failure) so that speculative parsing of alternatives does not prematurely halt the parser.

#### Acceptance Criteria

1. WHILE the Generated_Parser is speculatively trying an alternative within a Multi_Alternative_Rule, THE Generated_Parser SHALL treat `_hasError` being set as a signal that the current alternative failed, not as a permanent parse failure.
2. WHEN an alternative sets `_hasError` during speculative parsing, THE Generated_Parser SHALL reset `_hasError` to `false` before attempting the next alternative.
3. WHEN all alternatives of a Multi_Alternative_Rule fail, THE Generated_Parser SHALL restore `_hasError` to its value from before the rule was entered and return `null`, allowing the caller to handle the failure.
4. WHEN a Multi_Alternative_Rule succeeds (one alternative matches), THE Generated_Parser SHALL return the result with `_hasError` remaining `false`, regardless of how many prior alternatives set `_hasError` during their speculative attempts.
5. THE `Require` helper method SHALL continue to set `_hasError` to `true` and add a diagnostic when a required token is missing, as this behavior is correct within the context of a single alternative attempt.

### Requirement 7: Generated Code Pattern for Multi-Alternative Rules

**User Story:** As a compiler developer, I want the generated code for multi-alternative rules to follow a consistent, readable pattern so that the generated parser is easy to debug and audit.

#### Acceptance Criteria

1. THE Generated_Parser code for each Multi_Alternative_Rule SHALL follow this structure: (a) save `_pos`, `_hasError`, and diagnostic count; (b) for each alternative in order: reset `_pos` to saved, set `_hasError` to `false`, truncate diagnostics to saved count, attempt the alternative, return result if non-null and `_hasError` is `false`; (c) after all alternatives fail: restore `_pos` to saved, restore `_hasError` to saved value, truncate diagnostics to saved count, return `null`.
2. THE Generated_Parser code for each alternative attempt SHALL be enclosed in its own block scope (`{ }`) to prevent local variable name collisions between alternatives.
3. THE Generated_Parser code SHALL use `var saved = _pos;`, `var savedError = _hasError;`, and `var savedDiagCount = _diagnostics?.Count ?? 0;` as the state-saving pattern at the top of each Multi_Alternative_Rule method.
4. THE success check for each alternative SHALL be `if (result != null && !_hasError) return result;` to ensure both that a node was produced and that no error occurred during parsing.

### Requirement 8: Sample Application End-to-End Compilation

**User Story:** As a Mist developer, I want the sample applications to compile end-to-end through the full pipeline (lex → parse → semantic model → type check → C# transpile → dotnet build) after the ParserEmitter rewrite, so that the refactor is validated against real code.

#### Acceptance Criteria

1. WHEN the `mist` CLI is invoked on `samples/HelloMist/`, THE compiler SHALL produce a working .NET executable without parse errors, semantic model errors, or transpiler errors.
2. WHEN the `mist` CLI is invoked on `samples/Simpliest/`, THE compiler SHALL produce a working .NET executable without errors.
3. THE Generated_Parser for the PEG-based strategy SHALL successfully parse all `.mist` files in the `samples/` directory, producing non-null AST roots for every file.

### Requirement 9: Existing Test Compatibility

**User Story:** As a compiler developer, I want all existing parser tests to continue passing after the ParserEmitter rewrite so that the refactor does not introduce regressions.

#### Acceptance Criteria

1. THE PEG-based Generated_Parser SHALL parse all inputs that the current Generated_Parser parses successfully, producing semantically equivalent AST_Nodes.
2. THE PEG-based Generated_Parser SHALL reject all inputs that the current Generated_Parser rejects, producing equivalent error diagnostics.
3. WHEN the project test suite is executed after the refactor, THE test suite SHALL report zero new test failures compared to the pre-refactor baseline.

### Requirement 10: Generated Parser Simplicity and Debuggability

**User Story:** As a compiler developer, I want the generated parser to be simpler and more debuggable than the current one so that parser issues can be diagnosed by reading the generated code.

#### Acceptance Criteria

1. THE Generated_Parser SHALL use a single uniform code pattern for all Multi_Alternative_Rules, with no variation in strategy between rules.
2. THE Generated_Parser SHALL contain no `switch (Peek())` statements for alternative selection.
3. THE Generated_Parser SHALL contain no `TryParse` memoization calls or `_memo` dictionary references.
4. THE Generated_Parser SHALL contain no `Peek(1)` or deeper lookahead calls for alternative selection.
5. THE Generated_Parser code for each rule SHALL be self-contained and readable without needing to understand FIRST set theory or lookahead computation.

### Requirement 11: DiagnosticBag API Compatibility

**User Story:** As a compiler developer, I want the Storm runtime DiagnosticBag to support the `Count` property and `TruncateTo(int)` method required by PEG backtracking so that diagnostic state can be correctly saved and restored.

#### Acceptance Criteria

1. THE Storm runtime DiagnosticBag class (emitted by `AstEmitter.EmitRuntimeTypes`) SHALL expose a `Count` property returning the current number of diagnostics.
2. THE Storm runtime DiagnosticBag class SHALL expose a `TruncateTo(int count)` method that removes all diagnostics added after index `count`.
3. WHEN `TruncateTo` is called with a count equal to the current `Count`, THE DiagnosticBag SHALL remain unchanged.
4. WHEN `TruncateTo` is called with a count less than the current `Count`, THE DiagnosticBag SHALL remove exactly `Count - count` diagnostics from the end of the collection.

### Requirement 12: Future Optimization — Memoization (Non-Requirement)

**User Story:** As a compiler developer, I want the spec to acknowledge that memoization (packrat parsing) is a known optimization for PEG parsers so that it can be added later without redesigning the backtracking infrastructure.

#### Acceptance Criteria

1. THE ParserEmitter SHALL NOT emit memoization code (no `_memo` dictionary, no `TryParse` helper) in this version.
2. THE PEG_Ordered_Choice code pattern SHALL be structured such that adding per-rule memoization in a future version requires only wrapping each alternative-dispatch method body, without changing the state save/restore logic.
3. THE design document for this spec SHALL include a section noting memoization as a future optimization, describing the packrat parsing approach and where memoization hooks would be inserted.
