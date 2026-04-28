# Requirements Document

## Introduction

This feature provides syntax highlighting for Mist language files (`.mist`) in VS Code and Kiro editors. The Mist compiler already has a Storm grammar extension for `.storm` files in `Editor/storm-syntax-highlighting/`. This feature creates an analogous extension for the Mist programming language itself, covering its keywords, types, literals, operators, comments, and interpolated strings.

A key design decision is whether the TextMate grammar should be manually maintained (like the Storm extension) or auto-generated from the Storm grammar definitions (`Mist.storm`, `keywords.storm`). The requirements below capture both the core highlighting needs and the trade-offs of each approach, so the user can make an informed choice during design.

## Glossary

- **Mist_Extension**: The VS Code / Kiro editor extension that provides syntax highlighting for `.mist` files.
- **TextMate_Grammar**: A `.tmLanguage.json` file that defines tokenization rules using TextMate scoping conventions, consumed by VS Code and Kiro for syntax highlighting.
- **Storm_Grammar**: The `.storm` grammar files (`Mist.storm`, `keywords.storm`) that define the lexer tokens and parser rules for the Mist language, processed by the Storm source generator.
- **Grammar_Generator**: An optional tool that reads Storm grammar files and produces a TextMate grammar JSON file automatically.
- **Language_Configuration**: A `language-configuration.json` file that defines editor behaviors such as comment toggling, bracket matching, and auto-closing pairs for Mist files.
- **Scope_Name**: A dot-separated TextMate scope identifier (e.g., `keyword.control.mist`) that maps tokens to editor theme colors.
- **Interpolated_String**: A Mist string literal prefixed with `$` that contains embedded expressions inside `{}` delimiters (e.g., `$"Hello {name}"`).

## Requirements

### Requirement 1: Extension Registration

**User Story:** As a developer, I want the Mist extension to register `.mist` files with the editor, so that opening a `.mist` file activates Mist syntax highlighting automatically.

#### Acceptance Criteria

1. THE Mist_Extension SHALL register the file extension `.mist` with the editor via a `package.json` contributes entry.
2. THE Mist_Extension SHALL declare a language identifier of `mist` with aliases `Mist` and `mist`.
3. THE Mist_Extension SHALL reference a Language_Configuration file that defines Mist-specific editor behaviors.
4. THE Mist_Extension SHALL reference a TextMate_Grammar file with scope name `source.mist`.

### Requirement 2: Language Configuration

**User Story:** As a developer, I want the editor to understand Mist's comment syntax, brackets, and auto-closing pairs, so that I get correct comment toggling and bracket behavior when editing Mist files.

#### Acceptance Criteria

1. THE Language_Configuration SHALL define `//` as the line comment token.
2. THE Language_Configuration SHALL define matching bracket pairs for `()`, `{}`, and `[]`.
3. THE Language_Configuration SHALL define auto-closing pairs for `"`, `'`, `(`, `{`, and `[`.
4. THE Language_Configuration SHALL define surrounding pairs for `"`, `'`, `(`, `{`, and `[`.

### Requirement 3: Keyword Highlighting

**User Story:** As a developer, I want Mist language keywords to be visually distinct, so that I can quickly identify control flow and declaration constructs in my code.

#### Acceptance Criteria

1. THE TextMate_Grammar SHALL assign scope `keyword.control.mist` to control-flow keywords: `if`, `else`, `while`, `for`, `foreach`, `in`, `break`, `return`.
2. THE TextMate_Grammar SHALL assign scope `keyword.declaration.mist` to declaration keywords: `namespace`, `using`, `class`, `struct`.
3. THE TextMate_Grammar SHALL assign scope `storage.modifier.mist` to modifier keywords: `public`, `private`, `static`.
4. THE TextMate_Grammar SHALL assign scope `keyword.other.mist` to the keywords: `new`, `var`, `this`.
5. THE TextMate_Grammar SHALL match keywords only at word boundaries to prevent partial matches within identifiers.

### Requirement 4: Type Highlighting

**User Story:** As a developer, I want primitive type names to be highlighted as types, so that I can distinguish type annotations from other identifiers.

#### Acceptance Criteria

1. THE TextMate_Grammar SHALL assign scope `storage.type.primitive.mist` to primitive type keywords: `int`, `uint`, `long`, `ulong`, `short`, `ushort`, `byte`, `sbyte`, `float`, `double`, `decimal`, `bool`, `char`, `string`.
2. THE TextMate_Grammar SHALL assign scope `storage.type.void.mist` to the `void` keyword.
3. THE TextMate_Grammar SHALL match type keywords only at word boundaries.

### Requirement 5: Literal Highlighting

**User Story:** As a developer, I want numeric, string, and character literals to be highlighted, so that I can easily spot constant values in my code.

#### Acceptance Criteria

1. THE TextMate_Grammar SHALL assign scope `constant.numeric.integer.mist` to integer literals, including optional integer suffixes (`l`, `L`, `u`, `U`, `UL`, `ul`, `Lu`, `lU`, `LU`, `lu`).
2. THE TextMate_Grammar SHALL assign scope `constant.numeric.hex.mist` to hexadecimal literals prefixed with `0x` or `0X`.
3. THE TextMate_Grammar SHALL assign scope `constant.numeric.float.mist` to floating-point literals, including optional float suffixes (`f`, `F`, `d`, `D`, `m`, `M`).
4. THE TextMate_Grammar SHALL assign scope `string.quoted.double.mist` to regular string literals delimited by double quotes.
5. THE TextMate_Grammar SHALL assign scope `string.quoted.single.mist` to character literals delimited by single quotes.
6. THE TextMate_Grammar SHALL assign scope `constant.character.escape.mist` to escape sequences (`\\`, `\"`, `\n`, `\r`, `\t`, `\0`, `\'`) within string and character literals.
7. THE TextMate_Grammar SHALL assign scope `constant.language.boolean.mist` to the literals `true` and `false`.

### Requirement 6: Interpolated String Highlighting

**User Story:** As a developer, I want interpolated strings to be highlighted with distinct scopes for the string text, delimiters, and embedded expressions, so that I can visually parse complex string templates.

#### Acceptance Criteria

1. THE TextMate_Grammar SHALL assign scope `string.interpolated.mist` to the overall interpolated string region starting with `$"` and ending with `"`.
2. THE TextMate_Grammar SHALL assign scope `punctuation.definition.interpolation.begin.mist` to the `{` delimiter that opens an expression hole.
3. THE TextMate_Grammar SHALL assign scope `punctuation.definition.interpolation.end.mist` to the `}` delimiter that closes an expression hole.
4. WHEN an expression hole is encountered inside an interpolated string, THE TextMate_Grammar SHALL apply standard Mist expression highlighting rules to the embedded expression.
5. THE TextMate_Grammar SHALL assign scope `constant.character.escape.mist` to escape sequences within the text portions of interpolated strings.

### Requirement 7: Comment Highlighting

**User Story:** As a developer, I want comments to be highlighted, so that I can distinguish commentary from executable code.

#### Acceptance Criteria

1. THE TextMate_Grammar SHALL assign scope `comment.line.double-slash.mist` to line comments starting with `//` and extending to the end of the line.

### Requirement 8: Operator and Punctuation Highlighting

**User Story:** As a developer, I want operators and punctuation to be scoped, so that editor themes can style them consistently.

#### Acceptance Criteria

1. THE TextMate_Grammar SHALL assign scope `keyword.operator.comparison.mist` to comparison operators: `==`, `!=`, `<`, `>`, `<=`, `>=`.
2. THE TextMate_Grammar SHALL assign scope `keyword.operator.logical.mist` to logical operators: `&&`, `||`, `!`.
3. THE TextMate_Grammar SHALL assign scope `keyword.operator.arithmetic.mist` to arithmetic operators: `+`, `-`, `*`, `/`, `%`.
4. THE TextMate_Grammar SHALL assign scope `keyword.operator.assignment.mist` to the assignment operator `=`.
5. THE TextMate_Grammar SHALL assign scope `punctuation.accessor.mist` to the member access operator `.`.
6. THE TextMate_Grammar SHALL assign scope `punctuation.separator.comma.mist` to `,`.
7. THE TextMate_Grammar SHALL assign scope `punctuation.terminator.statement.mist` to `;`.

### Requirement 9: Declaration Name Highlighting

**User Story:** As a developer, I want class, struct, and method names at their declaration sites to be highlighted as entity names, so that I can quickly locate definitions.

#### Acceptance Criteria

1. WHEN a `class` keyword is followed by an identifier, THE TextMate_Grammar SHALL assign scope `entity.name.type.class.mist` to that identifier.
2. WHEN a `struct` keyword is followed by an identifier, THE TextMate_Grammar SHALL assign scope `entity.name.type.struct.mist` to that identifier.
3. WHEN a type reference or `void` keyword is followed by an identifier and then a `(`, THE TextMate_Grammar SHALL assign scope `entity.name.function.mist` to that identifier.

### Requirement 10: Extension Packaging and Installation

**User Story:** As a developer, I want to install the Mist syntax highlighting extension via a symlink, so that I can iterate on the grammar without reinstalling.

#### Acceptance Criteria

1. THE Mist_Extension SHALL be structured as a directory under `Editor/mist-syntax-highlighting/` containing `package.json`, `language-configuration.json`, and a `syntaxes/` subdirectory with the TextMate_Grammar file.
2. THE Mist_Extension SHALL be installable by creating a symlink from the extension directory into the editor's extensions directory (e.g., `~/.kiro/extensions/mist-syntax-highlighting`).
3. THE Mist_Extension SHALL follow the same structural conventions as the existing Storm syntax highlighting extension in `Editor/storm-syntax-highlighting/`.

### Requirement 11: Grammar Maintenance Strategy Decision

**User Story:** As a project maintainer, I want a clear decision on whether the TextMate grammar is manually maintained or auto-generated from Storm grammar files, so that the team understands the long-term maintenance model.

#### Acceptance Criteria

1. THE requirements document SHALL document the trade-offs of manual maintenance versus auto-generation.
2. IF the manual maintenance approach is chosen, THEN THE TextMate_Grammar SHALL be a hand-crafted JSON file maintained independently of the Storm grammar files.
3. IF the auto-generation approach is chosen, THEN THE Grammar_Generator SHALL read `Mist.storm` and `keywords.storm` and produce a valid `.tmLanguage.json` file.
4. IF the auto-generation approach is chosen, THEN THE Grammar_Generator SHALL map Storm `token` declarations to appropriate TextMate Scope_Names using a configurable mapping.
5. IF the auto-generation approach is chosen, THEN THE Grammar_Generator SHALL be invocable as a build step or standalone command.

### Appendix: Manual vs Auto-Generation Trade-Off Analysis

| Dimension | Manual Maintenance | Auto-Generation |
|---|---|---|
| Sync with grammar | Requires manual updates when `Mist.storm` or `keywords.storm` change | Automatically stays in sync with grammar changes |
| Highlighting quality | Full control over TextMate scoping, regex patterns, and nested contexts | Limited by the semantic gap between parser grammars and TextMate regex patterns |
| Interpolated strings | Can use TextMate `begin`/`end` with nested patterns for expression holes | Difficult to generate — Storm grammar handles this via post-processing in `MistLexer.cs`, not in the grammar itself |
| Complexity | Low — single JSON file, same pattern as the Storm extension | High — requires building and maintaining a generator tool, plus a scope mapping configuration |
| Iteration speed | Edit JSON, reload editor, see results immediately | Edit generator or mapping, run generator, reload editor |
| Precedent | Matches the existing Storm extension approach | No existing precedent in this project |
| Recommended for | Projects where the grammar is relatively stable and highlighting quality matters | Projects with rapidly evolving grammars and many token types where drift is a real risk |

**Recommendation:** Given that (a) the Mist grammar is moderately sized with ~35 token types, (b) interpolated strings require special TextMate handling that cannot be derived from the Storm grammar alone, (c) the existing Storm extension already establishes a manual-maintenance pattern, and (d) the grammar is not changing at a pace that makes drift a serious risk — manual maintenance is the simpler and more practical starting point. Auto-generation can be revisited later if the grammar grows significantly.
