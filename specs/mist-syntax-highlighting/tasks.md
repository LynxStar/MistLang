# Implementation Plan: Mist Syntax Highlighting

## Overview

Build a declarative VS Code / Kiro extension that provides TextMate grammar-based syntax highlighting for `.mist` files. The implementation creates three JSON files (`package.json`, `language-configuration.json`, `mist.tmLanguage.json`) in `Editor/mist-syntax-highlighting/`, sets up snapshot tests using `vscode-tmgrammar-test`, and updates the project README. The Mist grammar is significantly more complex than the Storm grammar, covering keywords, primitive types, numeric/character/interpolated string literals, declaration names, operators, and punctuation.

## Tasks

- [x] 1. Create extension directory structure and package manifest
  - Create `Editor/mist-syntax-highlighting/` directory
  - Create `Editor/mist-syntax-highlighting/package.json` with extension metadata (`name`, `displayName`, `version`, `publisher`, `engines.vscode`)
  - Register the `mist` language contribution with `.mist` file extension, language ID `mist`, and aliases `Mist`, `mist`
  - Register the TextMate grammar contribution associating `source.mist` scope with `./syntaxes/mist.tmLanguage.json`
  - Reference `./language-configuration.json` for comment toggling and bracket matching
  - Add `scripts.test` entry for `npx vscode-tmgrammar-test -g syntaxes/mist.tmLanguage.json tests/*.mist`
  - Add `devDependencies` entry for `vscode-tmgrammar-test`
  - _Requirements: 1.1, 1.2, 1.3, 1.4, 10.1, 10.2, 10.3_

- [x] 2. Create language configuration
  - Create `Editor/mist-syntax-highlighting/language-configuration.json`
  - Configure `lineComment` as `//`
  - Configure bracket pairs for `()`, `{}`, and `[]`
  - Configure auto-closing pairs for `"`, `'`, `(`, `{`, and `[`
  - Configure surrounding pairs for `"`, `'`, `(`, `{`, and `[`
  - _Requirements: 2.1, 2.2, 2.3, 2.4_

- [x] 3. Create TextMate grammar file with comments, strings, and character literals
  - Create `Editor/mist-syntax-highlighting/syntaxes/mist.tmLanguage.json`
  - Set top-level `name` (`Mist`), `scopeName` (`source.mist`), and `fileTypes` (`.mist`)
  - Implement `comments` repository pattern: match `//` to end of line, assign `comment.line.double-slash.mist`
  - Implement `interpolated-strings` repository pattern: begin/end with `$"`/`"`, assign `string.interpolated.mist`, with nested patterns for escape sequences (`constant.character.escape.mist`), and expression holes (`{`/`}`) with `punctuation.definition.interpolation.begin.mist` / `punctuation.definition.interpolation.end.mist` and `include: source.mist` for nested expression highlighting
  - Implement `strings` repository pattern: begin/end with `"`, assign `string.quoted.double.mist`, with nested escape sequence pattern (`\\`, `\"`, `\n`, `\r`, `\t`, `\0`, `\'`) assigned `constant.character.escape.mist`
  - Implement `chars` repository pattern: begin/end with `'`, assign `string.quoted.single.mist`, with nested escape sequence pattern assigned `constant.character.escape.mist`
  - Wire `comments`, `interpolated-strings`, `strings`, `chars` into the top-level `patterns` array in that priority order
  - _Requirements: 5.4, 5.5, 5.6, 6.1, 6.2, 6.3, 6.4, 6.5, 7.1_


- [x] 4. Add declaration names, keywords, types, and literal patterns to the grammar
  - [x] 4.1 Add declaration name patterns
    - Implement `declaration-names` repository with three sub-patterns:
    - Match `class` keyword followed by identifier, assign capture 2 as `entity.name.type.class.mist` and capture 1 as `keyword.declaration.mist`
    - Match `struct` keyword followed by identifier, assign capture 2 as `entity.name.type.struct.mist` and capture 1 as `keyword.declaration.mist`
    - Match method declarations (return type or `void` followed by identifier followed by `(`), assign method name capture as `entity.name.function.mist`
    - _Requirements: 9.1, 9.2, 9.3_

  - [x] 4.2 Add keyword patterns
    - Implement `keywords` repository with four sub-patterns using `\b` word boundaries:
    - Control-flow keywords (`if`, `else`, `while`, `for`, `foreach`, `in`, `break`, `return`) → `keyword.control.mist`
    - Declaration keywords (`namespace`, `using`, `class`, `struct`) → `keyword.declaration.mist`
    - Modifier keywords (`public`, `private`, `static`) → `storage.modifier.mist`
    - Other keywords (`new`, `var`, `this`) → `keyword.other.mist`
    - _Requirements: 3.1, 3.2, 3.3, 3.4, 3.5_

  - [x] 4.3 Add type patterns
    - Implement `types` repository with two sub-patterns:
    - Primitive types (`int`, `uint`, `long`, `ulong`, `short`, `ushort`, `byte`, `sbyte`, `float`, `double`, `decimal`, `bool`, `char`, `string`) → `storage.type.primitive.mist`
    - Void keyword → `storage.type.void.mist`
    - Use `\b` word boundaries to prevent partial matches
    - _Requirements: 4.1, 4.2, 4.3_

  - [x] 4.4 Add literal patterns
    - Implement `literals` repository with four sub-patterns in priority order:
    - Hex literals (`0x`/`0X` prefix with optional suffixes) → `constant.numeric.hex.mist`
    - Float literals (decimal form and suffix-only form) → `constant.numeric.float.mist`
    - Integer literals (with optional suffixes `l`, `L`, `u`, `U`, `UL`, `ul`, etc.) → `constant.numeric.integer.mist`
    - Boolean literals (`true`, `false`) → `constant.language.boolean.mist`
    - _Requirements: 5.1, 5.2, 5.3, 5.7_

  - [x] 4.5 Add operator and punctuation patterns
    - Implement `operators` repository with four sub-patterns (multi-char before single-char):
    - Comparison operators (`==`, `!=`, `<=`, `>=`, `<`, `>`) → `keyword.operator.comparison.mist`
    - Logical operators (`&&`, `||`, `!`) → `keyword.operator.logical.mist`
    - Arithmetic operators (`+`, `-`, `*`, `/`, `%`) → `keyword.operator.arithmetic.mist`
    - Assignment operator (`=`) → `keyword.operator.assignment.mist`
    - Implement `punctuation` repository with three sub-patterns:
    - Member access (`.`) → `punctuation.accessor.mist`
    - Comma (`,`) → `punctuation.separator.comma.mist`
    - Semicolon (`;`) → `punctuation.terminator.statement.mist`
    - _Requirements: 8.1, 8.2, 8.3, 8.4, 8.5, 8.6, 8.7_

  - [x] 4.6 Wire all patterns into the top-level patterns array in correct priority order
    - Order: comments, interpolated-strings, strings, chars, declaration-names, keywords, types, literals, operators, punctuation
    - _Requirements: 3.1–3.5, 4.1–4.3, 5.1–5.7, 8.1–8.7, 9.1–9.3_

- [x] 5. Checkpoint - Validate grammar manually
  - Ensure all JSON files are valid and well-formed, ask the user if questions arise.

- [x] 6. Set up snapshot tests
  - [x] 6.1 Initialize test infrastructure
    - Create `Editor/mist-syntax-highlighting/tests/` directory
    - Ensure `package.json` has `devDependencies` for `vscode-tmgrammar-test` and a `test` script entry
    - _Requirements: (testing strategy from design)_

  - [x] 6.2 Write snapshot test fixtures for keyword highlighting
    - Create `Editor/mist-syntax-highlighting/tests/keywords.mist` — verify all four keyword groups (`keyword.control.mist`, `keyword.declaration.mist`, `storage.modifier.mist`, `keyword.other.mist`) receive correct scopes
    - Include word boundary tests: `interface` ≠ `int`, `format` ≠ `for`, `breaking` ≠ `break`
    - _Requirements: 3.1, 3.2, 3.3, 3.4, 3.5_

  - [x] 6.3 Write snapshot test fixtures for types and literals
    - Create `Editor/mist-syntax-highlighting/tests/types.mist` — verify primitive types and void get correct scopes with word boundary enforcement
    - Create `Editor/mist-syntax-highlighting/tests/literals.mist` — verify integers with suffixes, hex literals, floats, strings with escapes, char literals, and booleans
    - _Requirements: 4.1, 4.2, 4.3, 5.1, 5.2, 5.3, 5.4, 5.5, 5.6, 5.7_

  - [x] 6.4 Write snapshot test fixtures for interpolated strings
    - Create `Editor/mist-syntax-highlighting/tests/interpolated-strings.mist` — verify `string.interpolated.mist` scope, interpolation delimiters, nested expression highlighting, and escape sequences within interpolated strings
    - _Requirements: 6.1, 6.2, 6.3, 6.4, 6.5_

  - [x] 6.5 Write snapshot test fixtures for comments, operators, declarations, and punctuation
    - Create `Editor/mist-syntax-highlighting/tests/comments.mist` — verify `comment.line.double-slash.mist` in various positions
    - Create `Editor/mist-syntax-highlighting/tests/operators.mist` — verify comparison, logical, arithmetic, and assignment operator scopes; test multi-char vs single-char precedence (`==` vs `=`, `<=` vs `<`)
    - Create `Editor/mist-syntax-highlighting/tests/declarations.mist` — verify `class Foo`, `struct Bar`, `void DoSomething()` entity name highlighting
    - Create `Editor/mist-syntax-highlighting/tests/punctuation.mist` — verify dot, comma, semicolon scopes
    - _Requirements: 7.1, 8.1–8.7, 9.1–9.3_

  - [x] 6.6 Write edge case test fixture
    - Create `Editor/mist-syntax-highlighting/tests/edge-cases.mist` — verify keyword substrings in identifiers, operator ambiguities, and other boundary conditions
    - _Requirements: 3.5, 4.3_

- [x] 7. Update README with Mist editor support section
  - Append Mist extension information to the "Editor Support" section in `README.md`
  - Document the symlink installation command: `ln -s "$(pwd)/Editor/mist-syntax-highlighting" ~/.kiro/extensions/mist-syntax-highlighting`
  - Explain that using a symlink ensures changes to the grammar are reflected without reinstallation
  - Instruct the user to reload or restart Kiro after installing the extension
  - _Requirements: 10.1, 10.2, 10.3_

- [x] 8. Final checkpoint - Ensure all files are valid
  - Ensure all JSON files parse correctly and all tests pass, ask the user if questions arise.

## Notes

- Tasks marked with `*` are optional and can be skipped for faster MVP
- This extension is purely declarative (JSON only) — no executable code is involved
- Property-based testing does not apply; snapshot tests via `vscode-tmgrammar-test` are the appropriate testing strategy
- Each task references specific requirements for traceability
- The grammar pattern priority order is critical for correct highlighting — interpolated strings must precede regular strings, declaration names must precede keywords, and multi-char operators must precede single-char operators
- The interpolated string pattern is the most complex component, using nested begin/end with `include: source.mist` for expression hole highlighting
