# Requirements Document

## Introduction

This feature provides syntax highlighting for `.storm` grammar definition files used by the Mist compiler project. Storm is a DSL for defining lexer tokens and parser rules that drive the Mist language's lexer and parser generation. The implementation is a VS Code / Kiro TextMate grammar extension that highlights Storm-specific keywords, token definitions, parser rules, comments, strings, character classes, and operators. The feature also includes README documentation covering extension installation and a symlink-based workflow for seamless development iteration.

## Glossary

- **Storm_Extension**: A VS Code / Kiro extension that provides TextMate grammar-based syntax highlighting for `.storm` files.
- **TextMate_Grammar**: A JSON-based grammar specification (`.tmLanguage.json`) that defines scoping rules for syntax highlighting in VS Code-compatible editors.
- **Storm_File**: A file with the `.storm` extension containing grammar definitions for the Mist compiler, including token definitions, fragment definitions, and parser rules.
- **Kiro_Extensions_Directory**: The directory where Kiro loads extensions from, typically `~/.kiro/extensions/`.
- **Symlink**: A filesystem symbolic link that points to the extension source directory, allowing the editor to always load the latest version without manual copying.
- **Package_Manifest**: The `package.json` file at the root of the extension that declares the extension metadata, language contributions, and grammar associations.

## Requirements

### Requirement 1: Extension Package Structure

**User Story:** As a developer, I want the Storm syntax highlighting extension to be a well-formed VS Code extension, so that Kiro can load and activate it.

#### Acceptance Criteria

1. THE Storm_Extension SHALL contain a `package.json` manifest declaring the extension name, version, publisher, and engine compatibility.
2. THE Package_Manifest SHALL register a language contribution with the identifier `storm` and the file extension `.storm`.
3. THE Package_Manifest SHALL register a TextMate grammar contribution that associates the `storm` language with a `.tmLanguage.json` grammar file.
4. THE Storm_Extension SHALL reside in a directory named `storm-syntax-highlighting` within the project repository (e.g., `Editor/storm-syntax-highlighting/`).

### Requirement 2: Keyword Highlighting

**User Story:** As a developer editing Storm files, I want Storm-specific keywords to be visually distinct, so that I can quickly identify grammar structure.

#### Acceptance Criteria

1. WHEN a Storm_File contains the keyword `grammar`, THE TextMate_Grammar SHALL assign it a `keyword.control` scope.
2. WHEN a Storm_File contains the keyword `import`, THE TextMate_Grammar SHALL assign it a `keyword.control.import` scope.
3. WHEN a Storm_File contains the keywords `token`, `hidden`, or `fragment`, THE TextMate_Grammar SHALL assign each a `keyword.other` or `storage.type` scope.
4. WHEN a Storm_File contains the keyword `rule`, THE TextMate_Grammar SHALL assign it a `keyword.other` or `storage.type` scope.
5. WHEN a Storm_File contains the keyword `as`, THE TextMate_Grammar SHALL assign it a `keyword.operator` scope.

### Requirement 3: Comment Highlighting

**User Story:** As a developer, I want comments in Storm files to be highlighted as comments, so that I can distinguish documentation from grammar definitions.

#### Acceptance Criteria

1. WHEN a Storm_File contains a line comment starting with `//`, THE TextMate_Grammar SHALL assign the entire comment line a `comment.line.double-slash` scope.

### Requirement 4: String Literal Highlighting

**User Story:** As a developer, I want string literals in Storm files to be highlighted, so that I can identify token patterns defined as quoted strings.

#### Acceptance Criteria

1. WHEN a Storm_File contains a double-quoted string literal (e.g., `"namespace"`), THE TextMate_Grammar SHALL assign it a `string.quoted.double` scope.
2. WHEN a double-quoted string contains an escape sequence (e.g., `\\\\`, `\\n`, `\\"`), THE TextMate_Grammar SHALL assign the escape sequence a `constant.character.escape` scope.

### Requirement 5: Character Class Highlighting

**User Story:** As a developer, I want character class expressions in Storm files to be highlighted, so that I can visually parse regex-like token patterns.

#### Acceptance Criteria

1. WHEN a Storm_File contains a character class expression enclosed in `[` and `]` (e.g., `[a-zA-Z_]`), THE TextMate_Grammar SHALL assign it a `constant.other.character-class` scope.

### Requirement 6: Identifier and Rule Name Highlighting

**User Story:** As a developer, I want token names, rule names, and labeled field names to be visually distinct, so that I can navigate grammar structure quickly.

#### Acceptance Criteria

1. WHEN a token definition uses the pattern `token NAME =`, THE TextMate_Grammar SHALL assign `NAME` an `entity.name.type` or `entity.name.function` scope.
2. WHEN a rule definition uses the pattern `rule Name =`, THE TextMate_Grammar SHALL assign `Name` an `entity.name.type` or `entity.name.function` scope.
3. WHEN a fragment definition uses the pattern `fragment NAME =`, THE TextMate_Grammar SHALL assign `NAME` an `entity.name.type` or `entity.name.function` scope.
4. WHEN an alternative label uses the pattern `as AlternativeName`, THE TextMate_Grammar SHALL assign `AlternativeName` an `entity.name.tag` or `entity.name.type` scope.

### Requirement 7: Operator and Punctuation Highlighting

**User Story:** As a developer, I want operators and punctuation in Storm grammar files to be highlighted, so that I can read rule structures clearly.

#### Acceptance Criteria

1. WHEN a Storm_File contains the operators `*`, `+`, or `?`, THE TextMate_Grammar SHALL assign each a `keyword.operator.quantifier` scope.
2. WHEN a Storm_File contains the alternation operator `|`, THE TextMate_Grammar SHALL assign it a `keyword.operator` scope.
3. WHEN a Storm_File contains the assignment operator `=`, THE TextMate_Grammar SHALL assign it a `keyword.operator.assignment` scope.
4. WHEN a Storm_File contains the semicolon `;`, THE TextMate_Grammar SHALL assign it a `punctuation.terminator` scope.

### Requirement 8: Label Highlighting in Rules

**User Story:** As a developer, I want labeled fields (e.g., `name:IDENTIFIER`) in parser rules to be highlighted, so that I can identify field bindings at a glance.

#### Acceptance Criteria

1. WHEN a parser rule contains a labeled field in the form `label:Reference`, THE TextMate_Grammar SHALL assign `label` a `variable.other` or `variable.parameter` scope.

### Requirement 9: README Installation Documentation

**User Story:** As a developer, I want clear instructions in the README for installing the Storm syntax highlighting extension, so that I can set it up in Kiro without guesswork.

#### Acceptance Criteria

1. THE README SHALL contain a section titled "Editor Support" or equivalent that describes the Storm syntax highlighting extension.
2. THE README SHALL document how to install the extension by copying or symlinking it into the Kiro_Extensions_Directory.
3. THE README SHALL include the exact shell command to create a symbolic link from the extension source directory to the Kiro_Extensions_Directory.
4. THE README SHALL explain that using a symlink ensures the extension automatically reflects changes made to the TextMate grammar source without requiring reinstallation.
5. THE README SHALL instruct the user to reload or restart Kiro after installing or symlinking the extension.

### Requirement 10: File Association

**User Story:** As a developer, I want Kiro to automatically recognize `.storm` files and apply syntax highlighting, so that I do not need to manually select the language mode.

#### Acceptance Criteria

1. WHEN a file with the `.storm` extension is opened in Kiro, THE Storm_Extension SHALL automatically activate and apply Storm syntax highlighting.
2. THE Package_Manifest SHALL associate the `.storm` file extension with the `storm` language identifier.
