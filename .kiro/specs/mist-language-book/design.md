# Design Document

## Overview

This design describes the implementation of the Mist Book — a static documentation site built with mdBook. The deliverable is a fully scaffolded book that builds and opens in a browser, with stubbed chapters, tabbed Mist/C# code blocks, and custom syntax highlighting for Mist code.

## Architecture

### Directory Layout

```
book/
├── book.toml                  # mdBook configuration
├── build.sh                   # Single-command build + open script
├── theme/
│   └── highlight.js           # Custom highlight.js bundle with Mist language
└── src/
    ├── SUMMARY.md             # Table of contents (drives sidebar navigation)
    ├── introduction.md        # Landing page
    ├── getting-started/
    │   ├── installation.md
    │   ├── hello-world.md
    │   └── project-structure.md
    ├── basics/
    │   ├── primitive-types.md
    │   ├── variables.md
    │   ├── control-flow.md
    │   └── functions.md
    ├── object-model/
    │   ├── classes.md
    │   ├── structs.md
    │   ├── interfaces.md
    │   ├── traits.md
    │   ├── enums.md
    │   └── extension-methods.md
    ├── type-system/
    │   ├── generics.md
    │   ├── type-unions.md
    │   ├── type-aliases.md
    │   ├── pattern-matching.md
    │   ├── refinement-types.md
    │   └── dependent-types.md
    ├── advanced/
    │   ├── ownership.md
    │   ├── structured-context.md
    │   └── unknown-type.md
    └── tooling/
        ├── cli-usage.md
        ├── editor-support.md
        └── project-configuration.md
```

### book.toml Configuration

```toml
[book]
title = "The Mist Programming Language"
authors = ["The Mist Contributors"]
language = "en"
src = "src"

[build]
build-dir = "output"

[output.html]
theme = "theme"

[preprocessor.tabs]
```

Key decisions:
- `build-dir = "output"` keeps generated HTML in `book/output/` (gitignored).
- `theme = "theme"` tells mdBook to load the custom highlight.js from `book/theme/`.
- `[preprocessor.tabs]` enables the mdbook-tabs plugin with default settings.

### Custom highlight.js for Mist

mdBook ships a bundled highlight.js. To add Mist support, we provide a custom `highlight.js` file in the theme directory that includes the Mist language definition. The approach:

1. Download the same highlight.js version that mdBook bundles.
2. Write a Mist language definition module (`mist.js`) that registers with highlight.js.
3. Build a custom bundle that includes the default languages plus Mist.
4. Place the resulting `highlight.js` in `book/theme/`.

The Mist language definition will cover:

| Category | Patterns |
|----------|----------|
| Declaration keywords | `namespace`, `using`, `class`, `struct`, `interface`, `trait`, `extension`, `type`, `enum` |
| Control flow | `if`, `else`, `while`, `for`, `foreach`, `in`, `break`, `return`, `switch`, `when` |
| Modifiers | `public`, `private`, `static`, `intrinsic`, `implicit`, `explicit`, `readonly`, `mutable`, `owned`, `frozen`, `ref`, `borrowed` |
| Other keywords | `new`, `var`, `this`, `get`, `set`, `init`, `field`, `operator`, `with`, `as`, `default`, `where` |
| Primitive types | `int`, `uint`, `long`, `ulong`, `short`, `ushort`, `byte`, `sbyte`, `float`, `double`, `decimal`, `bool`, `char`, `string`, `regex`, `unknown`, `void` |
| Literals | `true`, `false`, numeric (int, float, hex, binary with suffixes) |
| Strings | Double-quoted, interpolated (`$"...{expr}..."`), char literals |
| Comments | Single-line `//` |
| Operators | Arithmetic, comparison, logical (`and`, `or`, `not`, `is`), assignment, arrow (`=>`) |
| Pattern matching | `is`, `and`, `or`, `not` as contextual operators |

The definition uses highlight.js's `keywords` object for categorized keyword lists and `contains` array for string/number/comment modes.

### Tabbed Code Block Pattern

Chapter stubs will include a template demonstrating the tab syntax:

```markdown
{{#tabs}}
{{#tab name="Mist"}}
\```mist
// Mist code here
\```
{{/tab}}
{{#tab name="Emitted C#"}}
\```csharp
// Corresponding C# output here
\```
{{/tab}}
{{/tabs}}
```

This renders as a tabbed interface where readers can switch between the Mist source and the transpiler output.

### Build Script (`build.sh`)

The script will:
1. Check that `mdbook` is on PATH; if not, print install instructions and exit.
2. Check that `mdbook-tabs` is on PATH; if not, print install instructions and exit.
3. Run `mdbook build` from the `book/` directory.
4. Open `book/output/index.html` in the default browser (`open` on macOS, `xdg-open` on Linux).

### .gitignore Updates

Add to the repository root `.gitignore`:
```
book/output/
```

### README Update

Add a "Documentation" section to the existing README after the "Editor Support" section:

```markdown
## Documentation (The Mist Book)

The Mist Book lives in the `book/` directory and is built with [mdBook](https://rust-lang.github.io/mdBook/).

### Prerequisites

```bash
cargo install mdbook
cargo install mdbook-tabs
```

### Build and Preview

```bash
./book/build.sh
```

This builds the book and opens it in your default browser. The source files are in `book/src/`.
```

## Decisions

1. **Flat highlight.js file in theme/** — Rather than committing the full highlight.js build toolchain, we commit the pre-built `highlight.js` file directly. This avoids adding Node.js as a build dependency. The file is regenerated manually when keywords change.

2. **Chapter grouping** — Chapters are grouped into subdirectories (`getting-started/`, `basics/`, `object-model/`, `type-system/`, `advanced/`, `tooling/`) matching the logical parts in the table of contents. This keeps the `src/` directory organized as the book grows.

3. **One example tab block per stub** — Only the first chapter in each section includes the tabbed code block template. This avoids repetitive boilerplate while still providing a reference for content authors.

4. **macOS-first build script** — The build script uses `open` for macOS. A comment in the script notes the `xdg-open` alternative for Linux. Windows is not supported by the script (users can run `mdbook build` directly).
