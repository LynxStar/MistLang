# Editor Support

Mist ships with a VS Code extension that provides syntax highlighting, language configuration, and file association for `.mist` files. The extension lives in the repository at `Editor/mist-syntax-highlighting/` and can be installed locally from source.

## The VS Code Extension

The extension is named **Mist Syntax Highlighting** (`mist-syntax-highlighting`) and is located in the `Editor/mist-syntax-highlighting/` directory of the Mist repository. It targets VS Code 1.75 or later and registers the `mist` language identifier for all files with the `.mist` extension.

## Installation

To install the extension from the repository source:

1. Clone or download the Mist repository.
2. Open VS Code.
3. Open the Command Palette (`Ctrl+Shift+P` on Windows/Linux, `Cmd+Shift+P` on macOS).
4. Run **Extensions: Install from VSIX...** — or, for a development install, use **Developer: Install Extension from Location...** and select the `Editor/mist-syntax-highlighting/` folder.

Alternatively, you can copy the extension folder directly into your VS Code extensions directory:

```
cp -r Editor/mist-syntax-highlighting ~/.vscode/extensions/mist-syntax-highlighting
```

After installation, restart VS Code. Any file with a `.mist` extension will automatically be recognized as a Mist source file.

## Features

### Syntax Highlighting

The extension provides a TextMate grammar (`syntaxes/mist.tmLanguage.json`) that highlights Mist source code with scopes for:

| Category | Examples | Scope |
|----------|----------|-------|
| Comments | `// line comment` | `comment.line.double-slash.mist` |
| Keywords | `if`, `else`, `while`, `for`, `return`, `switch` | `keyword.control.mist` |
| Declarations | `class`, `struct`, `interface`, `trait`, `enum`, `type` | `keyword.declaration.mist` |
| Modifiers | `public`, `private`, `static`, `readonly`, `owned`, `frozen` | `storage.modifier.mist` |
| Built-in types | `int`, `string`, `bool`, `float`, `double` | `storage.type.primitive.mist` |
| Strings | `"hello"`, `$"interpolated {x}"` | `string.quoted.double.mist`, `string.interpolated.mist` |
| Regex literals | `` `\d+` `` | `string.regexp.mist` |
| Numeric literals | `42`, `0xFF`, `3.14f` | `constant.numeric.*` |
| Type names | `MyClass`, `IEnumerable` | `entity.name.type.mist` |
| Function names | method declarations and calls | `entity.name.function.mist` |
| Operators | `+`, `-`, `==`, `is`, `=>` | `keyword.operator.*` |

### Language Configuration

The extension includes a `language-configuration.json` that configures VS Code's editing behavior for Mist files:

- **Comment toggling** — `Ctrl+/` inserts or removes `//` line comments.
- **Bracket matching** — Parentheses `()`, braces `{}`, and square brackets `[]` are matched and highlighted as pairs.
- **Auto-closing pairs** — Typing an opening delimiter (`"`, `'`, `(`, `{`, `[`) automatically inserts the corresponding closing delimiter.
- **Surrounding pairs** — Selecting text and typing a delimiter wraps the selection in the matching pair.

### File Association

The extension registers the `.mist` file extension with the `mist` language identifier. VS Code will:

- Apply Mist syntax highlighting to any file ending in `.mist`.
- Display "Mist" in the language mode indicator in the status bar.
- Use the Mist language configuration for comment toggling and bracket behavior in `.mist` files.

## Running Grammar Tests

The extension includes a test suite for verifying the TextMate grammar against sample Mist files. To run the tests:

```
cd Editor/mist-syntax-highlighting
npm install
npm test
```

The test runner (`vscode-tmgrammar-test`) validates that tokens in the test files under `tests/` receive the expected scopes. This is useful when contributing grammar changes to ensure existing highlighting is not broken.
