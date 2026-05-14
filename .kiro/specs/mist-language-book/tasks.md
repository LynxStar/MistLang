# Implementation Plan

## Overview

Set up an mdBook-based documentation site ("The Mist Book") for the Mist programming language, including project scaffolding, stubbed chapter content, custom syntax highlighting, a build script, and repository housekeeping.

## Tasks

- [x] 1. Initialize mdBook project structure
  - [x] 1.1 Create `book/` directory at the repository root
  - [x] 1.2 Create `book/book.toml` with title, authors, language, build-dir, theme path, and `[preprocessor.tabs]`
  - [x] 1.3 Create `book/src/SUMMARY.md` with the full table of contents (all chapters and sections)
  - [x] 1.4 Create `book/theme/` directory (empty for now, populated in Task 3)
  - **Requirements:** Req 1 (AC 1, 5), Req 6 (AC 1, 3)

- [x] 2. Create stubbed chapter files
  - [x] 2.1 Create `book/src/introduction.md` with title and placeholder text
  - [x] 2.2 Create `book/src/getting-started/installation.md` with title, placeholder, and one example tabbed code block
  - [x] 2.3 Create `book/src/getting-started/hello-world.md` with title and placeholder
  - [x] 2.4 Create `book/src/getting-started/project-structure.md` with title and placeholder
  - [x] 2.5 Create `book/src/basics/primitive-types.md` with title, placeholder, and one example tabbed code block
  - [x] 2.6 Create `book/src/basics/variables.md` with title and placeholder
  - [x] 2.7 Create `book/src/basics/control-flow.md` with title and placeholder
  - [x] 2.8 Create `book/src/basics/functions.md` with title and placeholder
  - [x] 2.9 Create `book/src/object-model/classes.md` with title, placeholder, and one example tabbed code block
  - [x] 2.10 Create `book/src/object-model/structs.md` with title and placeholder
  - [x] 2.11 Create `book/src/object-model/interfaces.md` with title and placeholder
  - [x] 2.12 Create `book/src/object-model/traits.md` with title and placeholder
  - [x] 2.13 Create `book/src/object-model/enums.md` with title and placeholder
  - [x] 2.14 Create `book/src/object-model/extension-methods.md` with title and placeholder
  - [x] 2.15 Create `book/src/type-system/generics.md` with title, placeholder, and one example tabbed code block
  - [x] 2.16 Create `book/src/type-system/type-unions.md` with title and placeholder
  - [x] 2.17 Create `book/src/type-system/type-aliases.md` with title and placeholder
  - [x] 2.18 Create `book/src/type-system/pattern-matching.md` with title and placeholder
  - [x] 2.19 Create `book/src/type-system/refinement-types.md` with title and placeholder
  - [x] 2.20 Create `book/src/type-system/dependent-types.md` with title and placeholder
  - [x] 2.21 Create `book/src/advanced/ownership.md` with title, placeholder, and one example tabbed code block
  - [x] 2.22 Create `book/src/advanced/structured-context.md` with title and placeholder
  - [x] 2.23 Create `book/src/advanced/unknown-type.md` with title and placeholder
  - [x] 2.24 Create `book/src/tooling/cli-usage.md` with title, placeholder, and one example tabbed code block
  - [x] 2.25 Create `book/src/tooling/editor-support.md` with title and placeholder
  - [x] 2.26 Create `book/src/tooling/project-configuration.md` with title and placeholder
  - **Requirements:** Req 2 (AC 3, 4, 5), Req 4 (AC 1–5), Req 5 (AC 1–4)

- [x] 3. Create custom highlight.js with Mist language definition
  - [x] 3.1 Write the Mist language definition for highlight.js covering: declaration keywords, control flow, modifiers, other keywords, primitive types, literals, strings (regular and interpolated), char literals, single-line comments, numeric literals (int, float, hex, binary with suffixes), and operators
  - [x] 3.2 Download the highlight.js version bundled by the current mdBook release
  - [x] 3.3 Build a custom highlight.js bundle that includes the default languages plus the Mist definition
  - [x] 3.4 Place the built `highlight.js` file in `book/theme/highlight.js`
  - [x] 3.5 Verify that ` ```mist ` code blocks render with syntax highlighting when the book is built
  - **Requirements:** Req 1 (AC 4), Req 7 (AC 1–5)

- [x] 4. Create build script
  - [x] 4.1 Create `book/build.sh` with executable permissions
  - [x] 4.2 Add check for `mdbook` on PATH; print install instructions and exit 1 if missing
  - [x] 4.3 Add check for `mdbook-tabs` on PATH; print install instructions and exit 1 if missing
  - [x] 4.4 Run `mdbook build` from the `book/` directory
  - [x] 4.5 Open `book/output/index.html` in the default browser using `open` (macOS)
  - [x] 4.6 Add a comment noting the `xdg-open` alternative for Linux
  - **Requirements:** Req 3 (AC 1–5)

- [x] 5. Update .gitignore
  - [x] 5.1 Add `book/output/` to the repository root `.gitignore`
  - [x] 5.2 Verify that `dotnet build` on the solution still works unchanged
  - **Requirements:** Req 6 (AC 2, 4)

- [x] 6. Update README
  - [x] 6.1 Add a "Documentation (The Mist Book)" section to `README.md` after the "Editor Support" section
  - [x] 6.2 List prerequisites: `cargo install mdbook` and `cargo install mdbook-tabs`
  - [x] 6.3 Document the build/preview command: `./book/build.sh`
  - [x] 6.4 Note that book source files are in `book/src/`
  - **Requirements:** Req 8 (AC 1–4)

## Task Dependency Graph

```json
{
  "waves": [
    ["1", "5", "6"],
    ["2", "3"],
    ["4"]
  ]
}
```

## Notes

Tasks 5 and 6 are independent and can run in parallel with any other task (wave 1). Tasks 2 and 3 both depend on Task 1 providing the `book/` directory structure. Task 4 depends on both Tasks 2 and 3 because the build script needs chapter content and the custom highlight.js to be in place.
