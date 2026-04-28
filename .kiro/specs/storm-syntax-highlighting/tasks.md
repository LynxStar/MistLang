# Implementation Plan: Storm Syntax Highlighting

## Overview

Build a declarative VS Code / Kiro extension that provides TextMate grammar-based syntax highlighting for `.storm` files. The implementation creates three JSON files (`package.json`, `language-configuration.json`, `storm.tmLanguage.json`) in `Editor/storm-syntax-highlighting/`, sets up snapshot tests using `vscode-tmgrammar-test`, and adds an "Editor Support" section to the project README.

## Tasks

- [x] 1. Create extension directory structure and package manifest
  - Create `Editor/storm-syntax-highlighting/` directory
  - Create `Editor/storm-syntax-highlighting/package.json` with extension metadata (`name`, `displayName`, `version`, `publisher`, `engines.vscode`)
  - Register the `storm` language contribution with `.storm` file extension and `storm` language ID
  - Register the TextMate grammar contribution associating `source.storm` scope with `./syntaxes/storm.tmLanguage.json`
  - Reference `./language-configuration.json` for comment toggling and bracket matching
  - _Requirements: 1.1, 1.2, 1.3, 1.4, 10.1, 10.2_

- [x] 2. Create language configuration
  - Create `Editor/storm-syntax-highlighting/language-configuration.json`
  - Configure `lineComment` as `//`
  - Configure bracket pairs for `[]` and `()`
  - Configure auto-closing pairs for `"`, `[`, and `(`
  - _Requirements: 3.1 (comment support), 1.2_

- [x] 3. Create TextMate grammar file with comments, strings, and character classes
  - Create `Editor/storm-syntax-highlighting/syntaxes/storm.tmLanguage.json`
  - Set top-level `name`, `scopeName` (`source.storm`), and `fileTypes`
  - Implement `comments` repository pattern: match `//` to end of line, assign `comment.line.double-slash.storm`
  - Implement `strings` repository pattern: begin/end with `"`, assign `string.quoted.double.storm`, with nested pattern for escape sequences (`\\\\`, `\\n`, `\\"`, `\\r`, `\\t`, `\\0`, `\\'`) assigned `constant.character.escape.storm`
  - Implement `character-classes` repository pattern: match `[...]` blocks, assign `constant.other.character-class.storm`
  - Wire `comments`, `strings`, `character-classes` into the top-level `patterns` array in priority order
  - _Requirements: 3.1, 4.1, 4.2, 5.1_

- [x] 4. Add keyword, storage type, and operator patterns to the grammar
  - [x] 4.1 Add definition name patterns
    - Implement `definition-names` repository pattern: match `token NAME =`, `rule Name =`, `fragment NAME =` and assign the name capture `entity.name.function.storm`
    - Implement `alternative-labels` repository pattern: match `as Name` and assign the name capture `entity.name.tag.storm`
    - Implement `labeled-fields` repository pattern: match `label:` in `label:Reference` and assign `variable.parameter.storm`
    - _Requirements: 6.1, 6.2, 6.3, 6.4, 8.1_

  - [x] 4.2 Add keyword and storage type patterns
    - Implement `keywords` repository pattern: match `grammar` with `keyword.control.storm`, match `import` with `keyword.control.import.storm`
    - Implement `storage-types` repository pattern: match `token`, `hidden`, `fragment`, `rule` with `storage.type.storm`
    - Implement `as-keyword` repository pattern: match standalone `as` with `keyword.operator.storm`
    - _Requirements: 2.1, 2.2, 2.3, 2.4, 2.5_

  - [x] 4.3 Add operator and punctuation patterns
    - Implement `operators` repository pattern: match `*`, `+`, `?` with `keyword.operator.quantifier.storm`; match `|` with `keyword.operator.storm`; match `=` with `keyword.operator.assignment.storm`
    - Implement `punctuation` repository pattern: match `;` with `punctuation.terminator.storm`
    - _Requirements: 7.1, 7.2, 7.3, 7.4_

  - [x] 4.4 Wire all patterns into the top-level patterns array in correct priority order
    - Order: comments, strings, character-classes, definition-names, alternative-labels, labeled-fields, keywords, storage-types, as-keyword, operators, punctuation
    - _Requirements: 2.1–2.5, 6.1–6.4, 7.1–7.4, 8.1_

- [x] 5. Checkpoint - Validate grammar manually
  - Ensure all JSON files are valid and well-formed, ask the user if questions arise.

- [x] 6. Set up snapshot tests
  - [x] 6.1 Initialize test infrastructure
    - Create `Editor/storm-syntax-highlighting/tests/` directory
    - Add a `package.json` in the extension root (or update existing) with a `devDependencies` entry for `vscode-tmgrammar-test`
    - Add a test script entry (e.g., `"test": "vscode-tmgrammar-test tests/*.storm"`) to the extension `package.json`
    - _Requirements: (testing strategy from design)_

  - [ ]* 6.2 Write snapshot test fixtures for all grammar constructs
    - Create `Editor/storm-syntax-highlighting/tests/test-keywords.storm` — verify `grammar`, `import`, `token`, `hidden`, `fragment`, `rule`, `as` receive expected scopes
    - Create `Editor/storm-syntax-highlighting/tests/test-comments.storm` — verify `//` line comments receive `comment.line.double-slash.storm`
    - Create `Editor/storm-syntax-highlighting/tests/test-strings.storm` — verify double-quoted strings and escape sequences receive correct scopes
    - Create `Editor/storm-syntax-highlighting/tests/test-character-classes.storm` — verify `[...]` receives `constant.other.character-class.storm`
    - Create `Editor/storm-syntax-highlighting/tests/test-definitions.storm` — verify `token FOO =`, `rule Bar =`, `fragment BAZ =` name highlighting
    - Create `Editor/storm-syntax-highlighting/tests/test-labels.storm` — verify `label:Reference` and `as AlternativeName` highlighting
    - Create `Editor/storm-syntax-highlighting/tests/test-operators.storm` — verify `*`, `+`, `?`, `|`, `=`, `;` receive correct scopes
    - _Requirements: 2.1–2.5, 3.1, 4.1, 4.2, 5.1, 6.1–6.4, 7.1–7.4, 8.1_

- [x] 7. Add Editor Support section to README
  - Append an "Editor Support" section to `README.md`
  - Describe the Storm syntax highlighting extension and what it provides
  - Document the symlink installation command: `ln -s "$(pwd)/Editor/storm-syntax-highlighting" ~/.kiro/extensions/storm-syntax-highlighting`
  - Explain that using a symlink ensures changes to the grammar are reflected without reinstallation
  - Instruct the user to reload or restart Kiro after installing the extension
  - _Requirements: 9.1, 9.2, 9.3, 9.4, 9.5_

- [x] 8. Final checkpoint - Ensure all files are valid
  - Ensure all JSON files parse correctly and all tests pass, ask the user if questions arise.

## Notes

- Tasks marked with `*` are optional and can be skipped for faster MVP
- This extension is purely declarative (JSON only) — no executable code is involved
- Property-based testing does not apply; snapshot tests via `vscode-tmgrammar-test` are the appropriate testing strategy
- Each task references specific requirements for traceability
- The grammar pattern priority order is critical for correct highlighting (e.g., `definition-names` must precede `storage-types`)
