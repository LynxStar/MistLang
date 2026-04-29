# Implementation Plan: Unified Diagnostic Formatting

## Overview

This plan implements unified diagnostic formatting across the Mist compiler and Storm parser generator. The work proceeds bottom-up: first upgrade the Storm runtime types emitted by `AstEmitter`, then update the generated lexer and parser error reporting in `LexerEmitter` and `ParserEmitter`, then propagate location data in `MistLexer`, `MistParser`, and `SemanticModelBuilder`, and finally relocate the `DiagnosticFormatter` to a shared location. Each task builds on the previous one so that generated code changes are available before consumer code is updated.

## Tasks

- [x] 1. Upgrade Storm runtime DiagnosticBag to store structured entries
  - [x] 1.1 Add `DiagnosticEntry` struct and update `DiagnosticBag` in `AstEmitter.EmitRuntimeTypes()`
    - In `Source/Storm.SourceGenerator/Emit/AstEmitter.cs`, update the `EmitRuntimeTypes()` method to emit a `DiagnosticEntry` readonly struct with `Message` (string), `FilePath` (string?), `Line` (int), and `Column` (int) properties and a constructor
    - Update the emitted `DiagnosticBag` class to store `List<DiagnosticEntry>` instead of `List<string>`
    - Add an overloaded `Add(string message, string? filePath, int line, int column)` method that creates a `DiagnosticEntry`
    - Keep the existing `Add(string message)` overload for backward compatibility (creates entry with null path, 0 line, 0 column)
    - Add an `Entries` property (`IReadOnlyList<DiagnosticEntry>`) alongside the existing `Messages` property
    - Update `TruncateTo` to operate on the entries list
    - _Requirements: 1.1, 1.4_

  - [x] 1.2 Write unit tests for `AstEmitter` DiagnosticEntry and DiagnosticBag emission
    - In `Test/Storm.SourceGenerator.Tests`, add tests verifying the emitted source contains the `DiagnosticEntry` struct with all four fields
    - Verify the emitted `DiagnosticBag` has both `Add` overloads, `Entries` property, and backward-compatible `Messages` property
    - _Requirements: 1.1, 1.4_

- [x] 2. Update generated lexer error reporting to pass structured location
  - [x] 2.1 Update `LexerEmitter.EmitDfaLexMethod()` to pass file path, line, and column to `diagnostics.Add()`
    - In `Source/Storm.SourceGenerator/Emit/LexerEmitter.cs`, locate the error-handling `else` block in `EmitDfaLexMethod()` where `diagnostics.Add(...)` is called
    - Change from `diagnostics.Add($"Unexpected character '{ch}' at line {startPos.Line}, column {startPos.Column}")` to `diagnostics.Add($"Unexpected character '{ch}'", source.Path, startPos.Line, startPos.Column)`
    - The message no longer embeds position information since it is now carried structurally
    - _Requirements: 1.2_

  - [x] 2.2 Write unit tests for `LexerEmitter` structured error reporting
    - In `Test/Storm.SourceGenerator.Tests`, add tests verifying the emitted lexer source calls `diagnostics.Add` with four arguments (message, filePath, line, column) instead of a single interpolated string
    - _Requirements: 1.2_

- [x] 3. Update generated parser error reporting to pass structured location
  - [x] 3.1 Update `ParserEmitter.EmitHelpers()` to pass structured location in `Require` and `Error` methods
    - In `Source/Storm.SourceGenerator/Emit/ParserEmitter.cs`, update the emitted `Init` method to store the source file path from `ts.Source.Path` in a `[ThreadStatic]` field (e.g., `_sourcePath`)
    - Update the emitted `Require` method to call `_diagnostics?.Add($"Expected token kind {kind} but found '{cur.Lexeme}'", _sourcePath, cur.Start.Line, cur.Start.Column)` instead of embedding position in the message
    - Update the emitted `Error` method to call `_diagnostics?.Add($"{message}", _sourcePath, cur.Start.Line, cur.Start.Column)` instead of embedding position in the message
    - _Requirements: 1.3_

  - [x] 3.2 Write unit tests for `ParserEmitter` structured error reporting
    - In `Test/Storm.SourceGenerator.Tests`, add tests verifying the emitted parser source stores `_sourcePath` in `Init` and passes structured arguments in `Require` and `Error`
    - _Requirements: 1.3_

- [x] 4. Checkpoint - Ensure all tests pass
  - Ensure all tests pass, ask the user if questions arise.

- [x] 5. Propagate location data in MistLexer diagnostic mapping
  - [x] 5.1 Update `MistLexer.Lex()` to extract structured fields from Storm `DiagnosticEntry`
    - In `Source/Mist/Lexing/MistLexer.cs`, change the diagnostic mapping loop from iterating `stormDiagnostics.Messages` to iterating `stormDiagnostics.Entries`
    - For each `DiagnosticEntry`, construct a `LinePositionSpan` from `entry.Line` and `entry.Column` when both are positive; otherwise set `Location` to null
    - Set `CompilationUnitPath` to `entry.FilePath ?? source.Path`
    - _Requirements: 2.1, 2.2, 2.3_

  - [x] 5.2 Write property test for MistLexer mapping (Property 3)
    - **Property 3: MistLexer mapping preserves structured fields**
    - Generate random `DiagnosticEntry` values with non-empty file paths and positive line/column, invoke the mapping logic, and verify the resulting `Diagnostic` has `Location.Start.Line == entry.Line`, `Location.Start.Column == entry.Column`, and `CompilationUnitPath == entry.FilePath`
    - Use FsCheck.Xunit with minimum 100 iterations
    - **Validates: Requirements 2.1, 2.2**

- [x] 6. Propagate location data in MistParser diagnostic mapping
  - [x] 6.1 Update `MistParser.Parse()` to extract structured fields from Storm `DiagnosticEntry`
    - In `Source/Mist/Parsing/MistParser.cs`, change the diagnostic mapping loop from iterating `stormDiagnostics.Messages` to iterating `stormDiagnostics.Entries`
    - For each `DiagnosticEntry`, construct a `LinePositionSpan` from `entry.Line` and `entry.Column` when both are positive; otherwise set `Location` to null
    - Set `CompilationUnitPath` to `entry.FilePath ?? tokens.Source.Path`
    - _Requirements: 3.1, 3.2, 3.3_

  - [x] 6.2 Write property test for MistParser mapping (Property 4)
    - **Property 4: MistParser mapping preserves structured fields**
    - Generate random `DiagnosticEntry` values with non-empty file paths and positive line/column, invoke the mapping logic, and verify the resulting `Diagnostic` has `Location.Start.Line == entry.Line`, `Location.Start.Column == entry.Column`, and `CompilationUnitPath == entry.FilePath`
    - Use FsCheck.Xunit with minimum 100 iterations
    - **Validates: Requirements 3.1, 3.2**

- [x] 7. Propagate location data in SemanticModelBuilder
  - [x] 7.1 Add a `MakeDiagnosticWithLocation` helper to `SemanticModelBuilder`
    - In `Source/Mist/Semantics/Model/SemanticModelBuilder.cs`, add a private static helper method `MakeDiagnosticWithLocation(string id, DiagnosticSeverity severity, string message, Storm.Runtime.SyntaxNode node, SourceText? sourceText)` that resolves the node's `Span` to a `LinePositionSpan` via `sourceText.ToLinePositionSpan()` when the source text is available and the span has non-zero length
    - Set `Location` to null when `sourceText` is null or span length is zero
    - Set `CompilationUnitPath` to `sourceText?.Path`
    - _Requirements: 4.1, 4.2, 4.3, 4.4_

  - [x] 7.2 Update existing diagnostic creation sites in `SemanticModelBuilder` to use the helper
    - Update `Pass1_ResolveBaseTypes` to use `MakeDiagnosticWithLocation` for `MIST_UNDEFINED_BASE_TYPE` and `MIST_CANNOT_INHERIT_STRUCT` diagnostics, passing the relevant syntax node and resolved `SourceText`
    - Update `ResolveInterfaceNames` to use the helper for `MIST_UNDEFINED_INTERFACE` and `MIST_NOT_AN_INTERFACE` diagnostics
    - Update any other diagnostic creation sites in the builder passes that reference specific syntax nodes (e.g., cycle detection, interface satisfaction, member redeclaration) to use the helper
    - Preserve the existing fallback behavior: when no `SourceText` is available, `Location` remains null
    - _Requirements: 4.1, 4.2, 4.3, 4.4_

  - [x] 7.3 Write property test for SemanticModelBuilder span-to-location resolution (Property 5)
    - **Property 5: SemanticModelBuilder span-to-location resolution**
    - Generate random `SourceText` with non-empty content and random `SourceSpan` values within bounds, verify `ToLinePositionSpan()` produces `Start.Line >= 1` and `Start.Column >= 1`, and that a diagnostic created for a node at that span has non-null `Location` matching the computed `LinePositionSpan`
    - Use FsCheck.Xunit with minimum 100 iterations
    - **Validates: Requirements 4.1, 4.2, 4.3**

  - [x] 7.4 Write unit test for SemanticModelBuilder null SourceText fallback
    - Verify that when `sourceText` is null, the helper produces a `Diagnostic` with `Location: null`
    - Verify that when span length is zero, the helper produces a `Diagnostic` with `Location: null`
    - _Requirements: 4.4_

- [x] 8. Checkpoint - Ensure all tests pass
  - Ensure all tests pass, ask the user if questions arise.

- [x] 9. Relocate DiagnosticFormatter to shared location
  - [x] 9.1 Move `DiagnosticFormatter` from `Mist.CLI.Output` to `Mist.Common.Diagnostics`
    - Move `Source/Mist.CLI/Output/DiagnosticFormatter.cs` to `Source/Mist/Common/Diagnostics/DiagnosticFormatter.cs`
    - Update the namespace from `Mist.CLI.Output` to `Mist.Common.Diagnostics`
    - Update `Source/Mist.CLI` references: change `using Mist.CLI.Output;` to `using Mist.Common.Diagnostics;` in any file that calls `DiagnosticFormatter.Format()`
    - Update `Test/Mist.CLI.Tests/Output/DiagnosticFormatterTests.cs` to reference the new namespace
    - _Requirements: 5.5_

  - [x] 9.2 Write property test for formatter output format (Property 2)
    - **Property 2: Formatter output format**
    - Generate random `Diagnostic` values covering all three cases (with location + path, path only, neither), format each, and verify the output matches the expected pattern using regex: `<file>(<line>,<column>): <severity> <id>: <message>`, `<file>: <severity> <id>: <message>`, or `<severity> <id>: <message>`
    - Verify severity token is lowercase `error` or `warning`
    - Use FsCheck.Xunit with minimum 100 iterations
    - **Validates: Requirements 5.1, 5.2, 5.3, 5.4**

  - [x] 9.3 Write property test for formatter round-trip (Property 1)
    - **Property 1: Formatter round-trip**
    - Generate random `Diagnostic` values with non-null `Location` and `CompilationUnitPath`, format with `DiagnosticFormatter.Format()`, parse with `RoslynDiagnosticMapper.TryParse()`, and verify the parsed result recovers the original severity, diagnostic id, and message
    - Use FsCheck.Xunit with minimum 100 iterations
    - **Validates: Requirements 5.6**

- [x] 10. Verify Storm frontend diagnostic location consistency
  - [x] 10.1 Write property test for Storm frontend diagnostic location completeness (Property 6)
    - **Property 6: Storm frontend diagnostic location completeness**
    - Generate random invalid Storm grammar source texts with non-null file paths, run through `StormLexer` or `StormParser`, and verify all produced `StormDiagnostic` records have non-null `FilePath` and `Line > 0` and `Column > 0`
    - Use FsCheck.Xunit with minimum 100 iterations
    - **Validates: Requirements 6.3**

  - [x] 10.2 Write property test for stored location matching computed position (Property 7)
    - **Property 7: Stored diagnostic location matches computed position**
    - Generate random source texts and valid character offsets, verify the line/column stored in a `DiagnosticEntry` by the generated lexer equals the line/column computed by `SourceText.ToLinePositionSpan()` for a span starting at that offset
    - Use FsCheck.Xunit with minimum 100 iterations
    - **Validates: Requirements 1.5**

- [x] 11. Final checkpoint - Ensure all tests pass
  - Ensure all tests pass, ask the user if questions arise.

## Notes

- Tasks marked with `*` are optional and can be skipped for faster MVP
- Each task references specific requirements for traceability
- Checkpoints ensure incremental validation
- Property tests validate universal correctness properties from the design document
- Unit tests validate specific examples and edge cases
- The Storm frontend (Requirement 6) is confirmed working by code review; tasks 10.1 and 10.2 add property test coverage for ongoing confidence
- All code is C# targeting .NET 10 with FsCheck.Xunit 3.1.0 for property-based tests
