# Requirements Document

## Introduction

This feature upgrades the Storm meta-grammar system to support regex literals as a first-class construct. Currently, Storm grammars rely on character classes (`[a-zA-Z_]`) for pattern matching and string literals (`"fn"`) for exact matches. Regex literals, delimited by backticks (`` ` ``), provide a generic mechanism for expressing arbitrary patterns using standard regular expression syntax. This eliminates the need for "magic" character classes and enables richer pattern matching in grammar definitions.

The scope covers three areas: extending the Storm grammar language itself (lexer, parser, AST, IR, and code emitter), updating the Storm syntax highlighting extension, and migrating the Mist grammar (`Mist.storm`) to leverage regex literals where appropriate.

## Glossary

- **Storm**: The meta-grammar language used to define lexer and parser grammars. Grammar files use the `.storm` extension.
- **Mist**: A programming language whose grammar is defined using Storm (in `Mist.storm` and `keywords.storm`).
- **StormLexer**: The hand-written lexer (`StormLexer.cs`) that tokenizes `.storm` source files into a list of `StormToken` values.
- **StormParser**: The recursive-descent parser (`StormParser.cs`) that consumes tokens from the StormLexer and produces a `StormFile` AST.
- **StormSyntax**: The module (`StormSyntax.cs`) defining all AST node types for Storm grammar files.
- **ExpressionIR**: The module (`ExpressionIR.cs`) defining lowered intermediate representation nodes for grammar expressions.
- **GrammarIRLowering**: The module (`GrammarIRLowering.cs`) that lowers AST expressions into IR, performing fragment inlining and reference resolution.
- **LexerEmitter**: The code generator (`LexerEmitter.cs`) that emits C# lexer source code from the lowered `GrammarIR`.
- **Regex_Literal**: A backtick-delimited pattern in a Storm grammar file (e.g., `` `[a-zA-Z_]+` ``) representing a regular expression to be matched at runtime.
- **Char_Class**: A bracket-delimited character class in Storm (e.g., `[a-zA-Z_]`) that matches a single character from a set.
- **String_Literal**: A double-quote-delimited exact string match in Storm (e.g., `"fn"`).
- **tmLanguage**: The TextMate grammar format used by VS Code extensions for syntax highlighting.

## Requirements

### Requirement 1: Tokenize Regex Literals in StormLexer

**User Story:** As a grammar author, I want the Storm lexer to recognize backtick-delimited regex literals, so that I can use regex patterns in my grammar definitions.

#### Acceptance Criteria

1. WHEN the StormLexer encounters a backtick character (`` ` ``) in the source text, THE StormLexer SHALL consume all characters up to and including the next unescaped closing backtick and produce a token of kind `RegexLiteral`.
2. WHEN a regex literal contains an escaped backtick sequence (`` \` ``), THE StormLexer SHALL treat the escaped backtick as a literal character within the pattern and continue scanning for the closing delimiter.
3. IF the StormLexer reaches the end of the source text without finding a closing backtick, THEN THE StormLexer SHALL emit a diagnostic error indicating an unterminated regex literal.
4. THE StormLexer SHALL add a `RegexLiteral` variant to the `StormTokenKind` enum.
5. THE StormLexer SHALL store the full backtick-delimited text (including delimiters) as the token lexeme.

### Requirement 2: Parse Regex Literals in StormParser

**User Story:** As a grammar author, I want the Storm parser to recognize regex literal tokens in grammar expressions, so that regex patterns can appear anywhere a character class or string literal can appear.

#### Acceptance Criteria

1. WHEN the StormParser encounters a `RegexLiteral` token in a primary expression position, THE StormParser SHALL produce a `StormRegexLiteralExpr` AST node containing the pattern text (without backtick delimiters).
2. THE StormParser SHALL accept `RegexLiteral` tokens in the same expression positions where `StringLiteral` and `CharClass` tokens are accepted (token bodies, fragment bodies, and rule bodies).
3. THE StormParser SHALL include `StormTokenKind.RegexLiteral` in the `IsExpressionStart` check so that regex literals can begin a sequence element.

### Requirement 3: Define Regex Literal AST Node

**User Story:** As a compiler developer, I want a dedicated AST node for regex literals, so that downstream phases can distinguish regex patterns from string literals and character classes.

#### Acceptance Criteria

1. THE StormSyntax module SHALL define a `StormRegexLiteralExpr` record type that extends `StormExpression` and contains a `Pattern` property of type `string`.
2. THE `StormRegexLiteralExpr` node SHALL store the regex pattern without the backtick delimiters.

### Requirement 4: Define Regex Literal IR Node

**User Story:** As a compiler developer, I want a dedicated IR node for regex literals, so that the code emitter can generate appropriate matching code.

#### Acceptance Criteria

1. THE ExpressionIR module SHALL define a `RegexLiteralIR` class that extends `ExpressionIR` and contains a `Pattern` property of type `string`.
2. THE `RegexLiteralIR` node SHALL store the regex pattern without backtick delimiters.

### Requirement 5: Lower Regex Literal AST to IR

**User Story:** As a compiler developer, I want the IR lowering phase to handle regex literal AST nodes, so that they are correctly represented in the lowered grammar.

#### Acceptance Criteria

1. WHEN the GrammarIRLowering encounters a `StormRegexLiteralExpr` during fragment inlining, THE GrammarIRLowering SHALL produce a `RegexLiteralIR` node with the same pattern.
2. WHEN the GrammarIRLowering encounters a `StormRegexLiteralExpr` during rule expression lowering, THE GrammarIRLowering SHALL produce a `RegexLiteralIR` node with the same pattern.

### Requirement 6: Emit C# Regex Matching Code

**User Story:** As a grammar author, I want the generated lexer to use `System.Text.RegularExpressions.Regex` for regex literal patterns, so that regex literals produce correct matching behavior at runtime.

#### Acceptance Criteria

1. WHEN the LexerEmitter encounters a `RegexLiteralIR` node, THE LexerEmitter SHALL generate C# code that uses `System.Text.RegularExpressions.Regex` to match the pattern against the input at the current position.
2. THE LexerEmitter SHALL generate regex matching code that anchors the match at the current position (i.e., the match starts at `pos`, not at an arbitrary later position).
3. THE LexerEmitter SHALL generate code that returns the length of the match on success and `0` on failure, consistent with the return convention of other `TryMatch` methods.
4. THE LexerEmitter SHALL emit a `using System.Text.RegularExpressions;` directive in the generated lexer file when at least one token uses a regex literal.
5. WHEN the LexerEmitter generates a `Regex` instance for a pattern, THE LexerEmitter SHALL use the `Compiled` option for runtime performance.

### Requirement 7: Update Storm Syntax Highlighting for Regex Literals

**User Story:** As a grammar author, I want regex literals to be visually distinct in my editor, so that I can easily identify regex patterns in Storm grammar files.

#### Acceptance Criteria

1. THE storm.tmLanguage.json file SHALL define a `regex-literals` pattern that matches backtick-delimited content.
2. THE `regex-literals` pattern SHALL assign the scope `string.regexp.storm` to the matched content.
3. THE storm.tmLanguage.json file SHALL include the `regex-literals` pattern in the top-level patterns array.
4. WHEN a regex literal contains an escaped backtick (`` \` ``), THE syntax highlighting SHALL treat the escaped backtick as part of the regex content and continue highlighting until the actual closing backtick.

### Requirement 8: Migrate Mist Grammar to Use Regex Literals

**User Story:** As a Mist language maintainer, I want the Mist grammar to use regex literals instead of character classes where appropriate, so that the grammar is more readable and leverages the new feature.

#### Acceptance Criteria

1. WHEN a character class in `Mist.storm` expresses a pattern that maps directly to a standard regex (e.g., `[ \t\r\n]+` → `` `\s+` ``), THE Mist grammar SHALL replace the character class with an equivalent regex literal.
2. WHEN a character class in `Mist.storm` uses negation (e.g., `[^\n]*`), THE Mist grammar SHALL replace the character class with an equivalent regex literal (e.g., `` `[^\n]*` ``).
3. WHEN a fragment in `Mist.storm` consists entirely of a character class (e.g., `fragment HEX_DIGIT = [0-9a-fA-F];`), THE Mist grammar SHALL replace the character class with an equivalent regex literal.
4. THE migrated Mist grammar SHALL produce identical tokenization results for all valid Mist source files compared to the pre-migration grammar.
