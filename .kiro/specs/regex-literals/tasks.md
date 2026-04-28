# Implementation Plan: Regex Literals

## Overview

Add regex literals as a first-class expression type to the Storm meta-grammar language. Regex literals use backtick delimiters (`` `pattern` ``) and flow through the full compiler pipeline: lexer → parser → AST → IR → C# code emission. Changes are additive and follow existing patterns for `StringLiteral` / `CharClass` at each layer.

## Tasks

- [x] 1. Add RegexLiteral token kind and lexer scanning
  - [x] 1.1 Add `RegexLiteral` variant to `StormTokenKind` enum in `Source/Storm.SourceGenerator/Frontend/StormLexer.cs`
    - Add `RegexLiteral` between `CharClass` and `Equals` in the enum
    - Add XML doc comment: `/// <summary>A backtick-delimited regex literal.</summary>`
    - _Requirements: 1.4_

  - [x] 1.2 Add backtick scanning branch in `StormLexer.Tokenize()` in `Source/Storm.SourceGenerator/Frontend/StormLexer.cs`
    - Add a new `if (c == '`')` branch after the `[` (CharClass) branch
    - Scan from opening backtick to closing backtick, handling `\` escapes (including `\``)
    - On unterminated literal (EOF without closing backtick), emit `STORM_SYNTAX_ERROR` diagnostic: "Unterminated regex literal"
    - Store full backtick-delimited text (including delimiters) as the token lexeme
    - Produce `StormToken(StormTokenKind.RegexLiteral, lexeme, ...)`
    - Mirror the existing string literal scanning logic, substituting backtick for double-quote
    - _Requirements: 1.1, 1.2, 1.3, 1.5_

  - [x] 1.3 Write unit tests for regex literal lexing
    - Add tests in `Test/Storm.SourceGenerator.Tests/StormLexerTests.cs`
    - Test: lex simple regex literal `` `abc` `` → `RegexLiteral` token with lexeme `` `abc` ``
    - Test: lex regex with escaped backtick `` `a\`b` `` → single `RegexLiteral` token
    - Test: lex unterminated regex literal → diagnostic emitted
    - Test: lex empty regex literal `` `` `` → `RegexLiteral` token with lexeme `` `` ``
    - _Requirements: 1.1, 1.2, 1.3, 1.5_

- [x] 2. Add AST node and parser support for regex literals
  - [x] 2.1 Add `StormRegexLiteralExpr` record to `Source/Storm.SourceGenerator/Frontend/StormSyntax.cs`
    - Add after `StormCharClassExpr`
    - `public sealed record StormRegexLiteralExpr(string Pattern) : StormExpression;`
    - `Pattern` stores the regex without backtick delimiters
    - _Requirements: 3.1, 3.2_

  - [x] 2.2 Add `RegexLiteral` case in `ParsePrimary()` in `Source/Storm.SourceGenerator/Frontend/StormParser.cs`
    - Add `case StormTokenKind.RegexLiteral:` that strips backtick delimiters via `lexeme.Substring(1, lexeme.Length - 2)` and returns `new StormRegexLiteralExpr(pattern)`
    - No escape processing — regex escapes pass through verbatim to the .NET Regex engine
    - _Requirements: 2.1_

  - [x] 2.3 Add `StormTokenKind.RegexLiteral` to `IsExpressionStart()` in `Source/Storm.SourceGenerator/Frontend/StormParser.cs`
    - Add to the switch statement so regex literals can begin a sequence element
    - _Requirements: 2.3_

  - [x] 2.4 Write unit tests for regex literal parsing
    - Add tests in `Test/Storm.SourceGenerator.Tests/StormParserTests.cs`
    - Test: parse regex literal in token body → `StormRegexLiteralExpr` with correct pattern
    - Test: parse regex literal in fragment body → `StormRegexLiteralExpr`
    - Test: parse regex literal as first element in a sequence → accepted
    - _Requirements: 2.1, 2.2, 2.3_

  - [x] 2.5 Write property test: lex-parse round trip preserves regex pattern
    - Add test in `Test/Storm.SourceGenerator.Tests/` (new file or existing property test file)
    - **Property 1: Lex-parse round trip preserves regex pattern**
    - For any string `p` not containing an unescaped backtick, wrapping in `token T = \`p\`;`, tokenizing, and parsing SHALL produce `StormRegexLiteralExpr` with `Pattern == p`
    - Use FsCheck generator for printable ASCII strings excluding unescaped backticks
    - **Validates: Requirements 1.1, 1.5, 2.1, 3.2**

- [x] 3. Checkpoint — Lexer, parser, and AST
  - Ensure all tests pass, ask the user if questions arise.

- [x] 4. Add IR node and lowering support
  - [x] 4.1 Add `RegexLiteralIR` class to `Source/Storm.SourceGenerator/IR/ExpressionIR.cs`
    - Add after `InlineFragmentIR`
    - `public sealed class RegexLiteralIR : ExpressionIR` with `public string Pattern { get; }` and constructor
    - _Requirements: 4.1, 4.2_

  - [x] 4.2 Add `StormRegexLiteralExpr` case in `InlineFragments()` in `Source/Storm.SourceGenerator/IR/GrammarIRLowering.cs`
    - `case StormRegexLiteralExpr regexExpr: return new RegexLiteralIR(regexExpr.Pattern);`
    - _Requirements: 5.1_

  - [x] 4.3 Add `StormRegexLiteralExpr` case in `LowerRuleExpression()` in `Source/Storm.SourceGenerator/IR/GrammarIRLowering.cs`
    - `case StormRegexLiteralExpr regexExpr: return new RegexLiteralIR(regexExpr.Pattern);`
    - _Requirements: 5.2_

  - [x] 4.4 Write unit tests for IR lowering of regex literals
    - Add tests in `Test/Storm.SourceGenerator.Tests/GrammarIRLoweringTests.cs`
    - Test: token with regex literal body → `RegexLiteralIR` with correct pattern
    - Test: rule referencing a token that uses regex literal → correct IR structure
    - _Requirements: 5.1, 5.2_

  - [x] 4.5 Write property test: IR lowering preserves regex pattern
    - **Property 2: IR lowering preserves regex pattern**
    - For any `StormRegexLiteralExpr` with pattern `p`, lowering through both `InlineFragments` and `LowerRuleExpression` SHALL produce `RegexLiteralIR` with `Pattern == p`
    - Use FsCheck with arbitrary string generator
    - **Validates: Requirements 4.2, 5.1, 5.2**

- [x] 5. Emit C# regex matching code in LexerEmitter
  - [x] 5.1 Add `RegexLiteralIR` case in `GenerateMatchExpr()` in `Source/Storm.SourceGenerator/Emit/LexerEmitter.cs`
    - `case RegexLiteralIR regex: return GenerateRegexLiteralMatch(regex.Pattern, posVar);`
    - _Requirements: 6.1_

  - [x] 5.2 Add `GenerateRegexLiteralMatch()` method in `Source/Storm.SourceGenerator/Emit/LexerEmitter.cs`
    - Generate a static `Regex` field reference: `new Regex(@"^(?:PATTERN)", RegexOptions.Compiled)`
    - Generate match code: `var m = _regex_N.Match(input, pos); return (m.Success && m.Index == pos) ? m.Length : 0;`
    - Anchor with `^` and wrap user pattern in `(?:...)` non-capturing group
    - _Requirements: 6.1, 6.2, 6.3, 6.5_

  - [x] 5.3 Add static Regex field emission and conditional `using` directive in `Source/Storm.SourceGenerator/Emit/LexerEmitter.cs`
    - Track whether any token uses `RegexLiteralIR` during emission
    - If so, add `using System.Text.RegularExpressions;` to the generated file header
    - Emit `private static readonly Regex _regex_N = ...` fields at class level for each unique pattern
    - _Requirements: 6.4, 6.5_

  - [x] 5.4 Write unit tests for regex literal code emission
    - Add tests in `Test/Storm.SourceGenerator.Tests/EmitterTests.cs`
    - Test: emit with regex literal → generated code contains `using System.Text.RegularExpressions;`
    - Test: emit without regex literal → no regex using directive
    - Test: generated match code contains `RegexOptions.Compiled` and anchored pattern
    - _Requirements: 6.1, 6.2, 6.3, 6.4, 6.5_

  - [x] 5.5 Write property test: emitter generates anchored compiled Regex code
    - **Property 3: Emitter generates anchored compiled Regex code**
    - For any `RegexLiteralIR` with pattern `p`, the generated C# code SHALL contain `RegexOptions.Compiled`, the pattern anchored with `^`, and a return expression yielding match length or `0`
    - Use FsCheck with arbitrary pattern string generator
    - **Validates: Requirements 6.1, 6.2, 6.3, 6.5**

- [x] 6. Checkpoint — Full pipeline through code emission
  - Ensure all tests pass, ask the user if questions arise.

- [x] 7. Update Storm syntax highlighting
  - [x] 7.1 Add `regex-literals` pattern to repository in `Editor/storm-syntax-highlighting/syntaxes/storm.tmLanguage.json`
    - Add `"regex-literals"` entry with `"name": "string.regexp.storm"`, `"begin": "\`"`, `"end": "\`"`
    - Include inner pattern for escape sequences: `"match": "\\\\."` with scope `constant.character.escape.storm`
    - _Requirements: 7.1, 7.2, 7.4_

  - [x] 7.2 Add `regex-literals` include to top-level patterns array in `Editor/storm-syntax-highlighting/syntaxes/storm.tmLanguage.json`
    - Add `{ "include": "#regex-literals" }` after `#strings` and before `#character-classes`
    - _Requirements: 7.3_

- [x] 8. Migrate Mist grammar to use regex literals
  - [x] 8.1 Replace character classes with regex literals in `Source/Mist/Grammar/Mist.storm`
    - Replace `[ \t\r\n]+` with `` `\s+` `` in WHITESPACE token
    - Replace `[^\n]*` with `` `[^\n]*` `` in LINE_COMMENT token
    - Replace `[0-9a-fA-F]` with `` `[0-9a-fA-F]` `` in HEX_DIGIT fragment
    - Replace `[lLuU]` with `` `[lLuU]` `` in INT_SUFFIX fragment
    - Replace `[fFdDmM]` with `` `[fFdDmM]` `` in FLOAT_SUFFIX fragment
    - Replace `[0-9]+` with `` `[0-9]+` `` in INTEGER_LITERAL and FLOAT_LITERAL tokens
    - Replace `[xX]` with `` `[xX]` `` in HEX_LITERAL token
    - Replace `[^'\\]` with `` `[^'\\]` `` in CHAR_LITERAL token
    - Replace `[^"\\]` with `` `[^"\\]` `` in STRING_LITERAL token
    - Replace `[^"\\{]` with `` `[^"\\{]` `` in INTERPOLATED_STRING_TEXT token
    - Collapse IDENTIFIER token `[a-zA-Z_][a-zA-Z0-9_]*` into single regex literal `` `[a-zA-Z_][a-zA-Z0-9_]*` ``
    - Replace ESCAPE_SEQUENCE fragment character classes as appropriate
    - _Requirements: 8.1, 8.2, 8.3_

  - [x] 8.2 Write property test: migration produces identical tokenization
    - **Property 4: Mist grammar migration produces identical tokenization**
    - For any valid Mist source text, tokenizing with the migrated `Mist.storm` (regex literals) SHALL produce a token sequence identical in token kinds and lexemes to the original grammar
    - Use FsCheck to generate valid Mist source snippets (identifiers, numbers, strings, whitespace, operators)
    - **Validates: Requirements 8.4**

- [x] 9. Final checkpoint — Full integration
  - Ensure all tests pass, ask the user if questions arise.

## Notes

- Tasks marked with `*` are optional and can be skipped for faster MVP
- Each task references specific requirements for traceability
- The project uses xUnit with FsCheck.Xunit for property-based testing
- All C# changes are in the `Storm.SourceGenerator` project; tests go in `Storm.SourceGenerator.Tests`
- The implementation follows existing patterns — each new type mirrors `StringLiteral` / `CharClass` at its respective layer
