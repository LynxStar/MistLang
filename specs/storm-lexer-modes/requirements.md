# Requirements Document

## Introduction

The Storm grammar DSL currently generates a single-mode lexer that processes all tokens in one flat pass. This works for most languages, but context-sensitive tokenization — such as string interpolation — requires the lexer to switch between different sets of token rules depending on context. Today, the Mist compiler works around this limitation with a hand-written post-processing pass in `MistLexer.cs` that detects `Error($) + STRING_LITERAL` patterns and re-lexes interpolated string regions manually.

This feature introduces lexer mode support to the Storm grammar DSL, modeled after the push/pop mode semantics found in ANTLR and Roslyn. Mode declarations allow grammar authors to define context-specific token sets, and mode-transition actions (`push`, `pop`) on token declarations control when the lexer enters and exits those contexts. The Storm source generator will emit a mode-aware lexer, the Mist grammar will be updated to use modes for interpolated strings (eliminating the manual workaround), and the Storm syntax highlighting will be updated to recognize the new keywords.

## Glossary

- **Storm**: A custom grammar DSL used to define lexer tokens and parser rules for the Mist language. Storm grammar files (`.storm`) are consumed by the `Storm.SourceGenerator` to produce C# lexer and parser code.
- **Storm_Source_Generator**: The C# incremental source generator (`Source/Storm.SourceGenerator`) that reads `.storm` files and emits C# lexer, parser, and AST classes.
- **Storm_Lexer_Frontend**: The lexer within the Storm source generator (`StormLexer.cs`) that tokenizes `.storm` grammar files themselves.
- **Storm_Parser_Frontend**: The parser within the Storm source generator (`StormParser.cs`) that parses `.storm` grammar files into a `StormFile` AST.
- **Storm_Syntax_AST**: The set of record types in `StormSyntax.cs` that represent the parsed structure of a `.storm` file.
- **GrammarIR**: The lowered intermediate representation (`GrammarIR.cs`) that holds resolved tokens and rules after fragment inlining and anonymous literal materialization.
- **TokenIR**: The IR representation of a single token declaration, including its name, expression, hidden status, and declaration index.
- **LexerEmitter**: The code generator (`LexerEmitter.cs`) that emits C# lexer source code from the `GrammarIR`.
- **Mist_Grammar**: The `.storm` files (`Mist.storm`, `keywords.storm`) that define the Mist language's lexer tokens and parser rules.
- **MistLexer**: The hand-written C# wrapper (`MistLexer.cs`) that post-processes Storm-generated tokens to fix up interpolated string regions.
- **Lexer_Mode**: A named set of token rules that the lexer uses in a specific context. The lexer maintains a mode stack; `push` transitions to a new mode, and `pop` returns to the previous mode.
- **Default_Mode**: The implicit `default` mode that contains all token declarations not explicitly assigned to a named mode. `default` is a reserved keyword and cannot be used as a user-defined mode name. All existing grammars operate entirely in the default mode.
- **Mode_Stack**: A stack data structure maintained by the generated lexer at runtime. `push` pushes a new mode onto the stack, and `pop` pops the current mode, reverting to the previous one.
- **Storm_TextMate_Grammar**: The TextMate grammar file (`storm.tmLanguage.json`) that provides syntax highlighting for `.storm` files in VS Code.

## Requirements

### Requirement 1: Mode Declaration Syntax in Storm Grammar

**User Story:** As a grammar author, I want to declare named lexer modes in my `.storm` files, so that I can group tokens by the context in which they should be matched.

#### Acceptance Criteria

1. WHEN a `mode` declaration is encountered in a `.storm` file, THE Storm_Parser_Frontend SHALL parse it as a named mode block containing zero or more token declarations.
2. THE Storm_Parser_Frontend SHALL accept mode declarations using the syntax `mode ModeName { ... }` where the body contains `token`, `hidden token`, and `fragment` declarations.
3. WHEN a token is declared outside any mode block, THE Storm_Source_Generator SHALL assign that token to the `default` mode.
4. WHEN a token is declared inside a mode block, THE Storm_Source_Generator SHALL assign that token exclusively to the named Lexer_Mode.
5. IF a mode declaration uses a name that duplicates another mode declaration, THEN THE Storm_Source_Generator SHALL report a diagnostic error indicating the duplicate mode name.
6. IF a mode block contains a `rule` declaration, THEN THE Storm_Source_Generator SHALL report a diagnostic error because parser rules are not permitted inside mode blocks.
7. IF a mode declaration uses the reserved name `default`, THEN THE Storm_Source_Generator SHALL report a diagnostic error because `default` is a reserved mode name representing the implicit top-level mode.

### Requirement 2: Mode Transition Actions on Token Declarations

**User Story:** As a grammar author, I want to attach `push` and `pop` actions to token declarations, so that matching a token can cause the lexer to switch context.

#### Acceptance Criteria

1. THE Storm_Parser_Frontend SHALL accept an optional action clause on token declarations using the syntax `=> push ModeName` or `=> pop` after the token body expression and before the semicolon.
2. WHEN a token declaration includes `=> push ModeName`, THE Storm_Source_Generator SHALL record that matching this token causes the generated lexer to push `ModeName` onto the Mode_Stack.
3. WHEN a token declaration includes `=> pop`, THE Storm_Source_Generator SHALL record that matching this token causes the generated lexer to pop the current mode from the Mode_Stack.
4. IF a `push` action references a mode name that is not declared in the grammar, THEN THE Storm_Source_Generator SHALL report a diagnostic error indicating the undefined mode.
5. IF a `pop` action is attached to a token in the `default` mode, THEN THE Storm_Source_Generator SHALL report a diagnostic warning because popping from the default mode has no parent mode to return to.
6. WHEN a token declaration has no action clause, THE Storm_Source_Generator SHALL treat it as having no mode transition (the lexer stays in the current mode).

### Requirement 3: Storm Syntax AST Extensions for Modes

**User Story:** As a Storm source generator developer, I want the Storm syntax AST to represent mode declarations and mode actions, so that downstream IR lowering and code emission can process them.

#### Acceptance Criteria

1. THE Storm_Syntax_AST SHALL include a `StormModeDecl` record type that holds the mode name and a list of `StormDeclaration` nodes for the tokens declared within the mode.
2. THE Storm_Syntax_AST SHALL include a `StormModeAction` abstract record type with `StormPushAction(ModeName)` and `StormPopAction` subtypes.
3. THE `StormTokenDecl` and `StormHiddenTokenDecl` record types SHALL include an optional `StormModeAction` property to represent the action clause.
4. THE `StormFile` record type SHALL include `StormModeDecl` nodes in its declarations list.

### Requirement 4: GrammarIR Extensions for Mode-Aware Tokens

**User Story:** As a Storm source generator developer, I want the GrammarIR to carry mode assignment and mode action metadata for each token, so that the lexer emitter can generate mode-aware code.

#### Acceptance Criteria

1. THE TokenIR class SHALL include a `Mode` property of type `string` that holds the name of the Lexer_Mode the token belongs to, defaulting to `"default"`.
2. THE TokenIR class SHALL include a `ModeAction` property that represents the optional mode transition (`PushMode(name)`, `PopMode`, or `None`).
3. THE GrammarIR class SHALL include a property that lists all declared mode names, including `"default"`.
4. THE GrammarIRLowering process SHALL assign each token to its declared mode and propagate mode action metadata from the AST to the IR.
5. IF a fragment is declared inside a mode block, THEN THE GrammarIRLowering process SHALL scope that fragment to the enclosing mode and inline it only within tokens of the same mode.

### Requirement 5: Mode-Aware Lexer Code Generation

**User Story:** As a grammar author, I want the Storm-generated lexer to support mode-based tokenization at runtime, so that context-sensitive tokens are matched correctly without manual post-processing.

#### Acceptance Criteria

1. THE LexerEmitter SHALL generate a lexer class that maintains a Mode_Stack initialized with the `default` mode.
2. WHEN the generated lexer attempts to match a token, THE LexerEmitter SHALL emit code that only considers tokens belonging to the current mode on top of the Mode_Stack.
3. WHEN a matched token has a `push` action, THE generated lexer SHALL push the specified mode onto the Mode_Stack after emitting the token.
4. WHEN a matched token has a `pop` action, THE generated lexer SHALL pop the current mode from the Mode_Stack after emitting the token.
5. IF a `pop` action is executed when the Mode_Stack contains only the `default` mode, THEN THE generated lexer SHALL remain in the `default` mode and not produce a runtime error.
6. THE generated `TokenKind` enum SHALL include token kinds from all modes, with each token kind being globally unique across modes.
7. THE generated lexer SHALL include a method or property that exposes which mode a given `TokenKind` belongs to, enabling downstream consumers to query mode membership.

### Requirement 6: Mist Grammar Update for Interpolated Strings Using Modes

**User Story:** As a Mist language developer, I want the Mist grammar to use Storm lexer modes for interpolated string tokenization, so that the hand-written post-processing workaround in `MistLexer.cs` can be removed.

#### Acceptance Criteria

1. THE Mist_Grammar SHALL declare an `Interpolation` mode that contains tokens for matching text segments, escape sequences, interpolation start delimiters (`{`), and the closing quote (`"`).
2. THE `INTERPOLATED_STRING_START` token in the `default` mode SHALL include a `=> push Interpolation` action.
3. THE `INTERPOLATED_STRING_END` token in the `Interpolation` mode SHALL include a `=> pop` action.
4. WHEN an interpolation opening brace (`{`) is matched inside the `Interpolation` mode, THE Mist_Grammar SHALL define a token with a `=> push default` action so that the expression inside the braces is lexed using the default token rules.
5. WHEN a closing brace (`}`) is matched in the `default` mode after being pushed from the `Interpolation` mode, THE Mist_Grammar SHALL define a token with a `=> pop` action to return to the `Interpolation` mode.
6. WHEN the Mist_Grammar uses mode-based interpolated string tokens, THE MistLexer post-processing workaround (`FixInterpolatedStrings` method and related helper methods) SHALL be removable without changing the token stream output for valid interpolated string inputs.

### Requirement 7: MistLexer Simplification

**User Story:** As a Mist compiler developer, I want the MistLexer wrapper to be simplified after mode support is available, so that the codebase is easier to maintain.

#### Acceptance Criteria

1. WHEN the Storm-generated lexer produces correct interpolated string tokens via mode-based tokenization, THE MistLexer SHALL delegate directly to the generated lexer without post-processing interpolated string regions.
2. THE MistLexer SHALL remove the `FixInterpolatedStrings`, `LexInterpolatedString`, and `LexExpression` private methods once mode-based tokenization is verified to produce equivalent output.
3. THE MistLexer SHALL continue to map Storm diagnostics to Mist diagnostics as it does today.

### Requirement 8: Storm TextMate Grammar Update for Mode Syntax

**User Story:** As a Storm grammar author, I want the Storm syntax highlighting in VS Code to recognize `mode`, `push`, and `pop` keywords, so that mode-related syntax is properly highlighted.

#### Acceptance Criteria

1. THE Storm_TextMate_Grammar SHALL highlight the `mode` keyword with the `storage.type.storm` scope when it appears as a mode block declaration.
2. THE Storm_TextMate_Grammar SHALL highlight `push` and `pop` as keywords with the `keyword.control.storm` scope when they appear in action clauses.
3. THE Storm_TextMate_Grammar SHALL highlight the mode name argument in `push ModeName` with the `entity.name.tag.storm` scope.
4. THE Storm_TextMate_Grammar SHALL highlight the `=>` arrow operator in action clauses with the `keyword.operator.storm` scope.
5. THE Storm_TextMate_Grammar SHALL highlight token and fragment declarations inside `mode` blocks with the same scopes used for top-level declarations.
6. THE Storm_TextMate_Grammar SHALL highlight the `default` keyword with the `entity.name.tag.storm` scope when it appears as a mode name in `push default`.

### Requirement 9: Storm Grammar Pretty-Printer for Mode Syntax

**User Story:** As a Storm tooling developer, I want a pretty-printer that can format Storm grammar files including mode declarations and action clauses, so that round-trip testing can verify the parser.

#### Acceptance Criteria

1. THE Storm_Pretty_Printer SHALL format `StormModeDecl` nodes as `mode ModeName { ... }` with contained declarations indented.
2. THE Storm_Pretty_Printer SHALL format `push` actions as `=> push ModeName` appended after the token body expression.
3. THE Storm_Pretty_Printer SHALL format `pop` actions as `=> pop` appended after the token body expression.
4. FOR ALL valid Storm grammar files containing mode declarations, parsing then pretty-printing then parsing SHALL produce an equivalent `StormFile` AST (round-trip property).
