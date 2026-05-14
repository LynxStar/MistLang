# Requirements Document

## Introduction

The Mist Book is a static documentation site — modeled after the Rust Book and Rustonomicon — that introduces every feature of the Mist programming language in a progressive, ordered manner. The scope of this spec covers selecting a static site generator, defining the ordered table of contents, and producing a stubbed-out site that builds and opens in a browser with a single command. Content authoring is explicitly out of scope; only chapter stubs with placeholder text are produced.

## Glossary

- **Mist_Book**: The static documentation site that teaches the Mist programming language feature-by-feature.
- **mdBook**: The Rust-based static site generator (github.com/rust-lang/mdBook) used to build the Mist Book. Installable via `cargo install mdbook`.
- **mdbook-tabs**: An mdBook preprocessor plugin that renders tabbed content blocks within chapters. Installable via `cargo install mdbook-tabs`.
- **Chapter**: A single page in the Mist Book covering one language feature or concept.
- **Table_of_Contents**: The ordered list of all chapters defining the reading path through the Mist Book, expressed as a `SUMMARY.md` file consumed by mdBook.
- **Stub**: A chapter file containing only a title and placeholder text, ready for future content authoring.
- **Build_Command**: The single CLI command a developer runs to generate the static site and open it in a browser.

## Requirements

### Requirement 1: mdBook as the Static Site Generator

**User Story:** As a Mist language developer, I want the documentation site to use mdBook — the same tool used by the Rust Book — so that the project gets a proven, book-oriented documentation experience with minimal setup.

#### Acceptance Criteria

1. THE Mist_Book SHALL use mdBook as its static site generator, installed via `cargo install mdbook`.
2. THE mdBook SHALL produce a complete static HTML site from Markdown source files without requiring a runtime server.
3. THE mdBook SHALL render a sidebar navigation structure reflecting the Table_of_Contents hierarchy via `SUMMARY.md`.
4. THE mdBook SHALL render code blocks with syntax highlighting.
5. THE Mist_Book SHALL include a `book.toml` configuration file that defines the book title, authors, and preprocessor settings.

### Requirement 2: Tabbed Code Blocks via mdbook-tabs

**User Story:** As a Mist learner, I want to see the Mist source code alongside the emitted C# in a tabbed view, so that I can understand what the transpiler produces for each language construct.

#### Acceptance Criteria

1. THE Mist_Book SHALL use the `mdbook-tabs` preprocessor to render tabbed code blocks.
2. THE `mdbook-tabs` preprocessor SHALL be declared in `book.toml` under `[preprocessor.tabs]`.
3. THE tabbed code blocks SHALL support at minimum a "Mist" tab and an "Emitted C#" tab per example.
4. THE tab syntax SHALL follow the `mdbook-tabs` convention using `{{#tabs}}`, `{{#tab name="..."}}`, and `{{/tab}}`/`{{/tabs}}` delimiters.
5. THE chapter stubs SHALL include one example tabbed code block demonstrating the Mist/C# tab pattern as a template for future content authors.

### Requirement 3: Single-Command Build and Preview

**User Story:** As a developer, I want to build and open the documentation site with a single command, so that I can preview changes without manual multi-step processes.

#### Acceptance Criteria

1. WHEN a developer runs the Build_Command from the repository root, THE mdBook SHALL produce the static site output in a predictable output directory.
2. WHEN the build completes successfully, THE Build_Command SHALL open the generated site in the developer's default browser.
3. THE Build_Command SHALL be a shell script checked into the repository at a discoverable location within the book directory.
4. IF mdBook is not installed, THEN THE Build_Command SHALL print a clear error message explaining how to install it via `cargo install mdbook`.
5. IF mdbook-tabs is not installed, THEN THE Build_Command SHALL print a clear error message explaining how to install it via `cargo install mdbook-tabs`.

### Requirement 4: Ordered Table of Contents

**User Story:** As a future Mist learner, I want the book to present language features in a progressive order from basics to advanced topics, so that I can learn the language incrementally without encountering unexplained concepts.

#### Acceptance Criteria

1. THE Table_of_Contents SHALL begin with introductory material (installation, hello world, project structure) before any language features.
2. THE Table_of_Contents SHALL order language features so that each chapter depends only on concepts introduced in prior chapters.
3. THE Table_of_Contents SHALL cover all implemented Mist language features including: primitive types, variables, control flow, functions, classes, structs, interfaces, traits, enums, generics, pattern matching, type unions, refinement types, dependent types, ownership, extension methods, structured context, and the unknown type.
4. THE Table_of_Contents SHALL group related features into logical parts (e.g., "Basics", "Type System", "Advanced Features").
5. THE Table_of_Contents SHALL include a final section covering tooling (CLI usage, editor support, project configuration).

### Requirement 5: Stubbed Chapter Files

**User Story:** As a documentation contributor, I want every chapter to exist as a stub file with a title and placeholder, so that the site structure is navigable and I can fill in content incrementally.

#### Acceptance Criteria

1. WHEN the site is built, THE Mist_Book SHALL contain one Markdown file per Chapter defined in the Table_of_Contents.
2. THE Stub SHALL contain the chapter title as a level-1 heading.
3. THE Stub SHALL contain placeholder text indicating the chapter is awaiting content (e.g., "This chapter is under construction.").
4. THE Stub SHALL be a valid Markdown file that mdBook processes without errors.

### Requirement 6: Repository Integration

**User Story:** As a project maintainer, I want the Mist Book source to live inside the existing MistLang repository, so that documentation stays versioned alongside the compiler.

#### Acceptance Criteria

1. THE Mist_Book source files SHALL reside in the `book/` directory at the repository root.
2. THE Mist_Book build output SHALL be excluded from version control via `.gitignore`.
3. THE Mist_Book configuration and source files SHALL be committed to version control.
4. THE Mist_Book directory structure SHALL not interfere with the existing solution build (`dotnet build` on the solution SHALL continue to work unchanged).

### Requirement 7: Mist Syntax Highlighting for mdBook

**User Story:** As a Mist learner reading the book, I want Mist code blocks to be syntax-highlighted, so that keywords, types, strings, and comments are visually distinct and the code is easy to read.

#### Acceptance Criteria

1. THE Mist_Book SHALL include a custom highlight.js language definition for Mist.
2. THE highlight.js definition SHALL recognize all keywords defined in `Source/Mist/Grammar/keywords.storm`, categorized appropriately (declaration keywords, control flow, modifiers, primitive types, literals).
3. THE highlight.js definition SHALL recognize string literals (including interpolated strings), numeric literals, single-line comments (`//`), and operators.
4. THE highlight.js definition SHALL be bundled into mdBook's theme directory so that code blocks tagged as ` ```mist ` render with highlighting.
5. THE `book.toml` SHALL be configured to use the custom theme directory containing the highlight.js build.

### Requirement 8: README Documentation

**User Story:** As a contributor or newcomer to the repository, I want the project README to explain how to build and preview the Mist Book, so that I can generate the documentation without reading the spec.

#### Acceptance Criteria

1. THE repository README SHALL include a section explaining how to build and preview the Mist Book.
2. THE README section SHALL list the prerequisites (`cargo install mdbook` and `cargo install mdbook-tabs`).
3. THE README section SHALL provide the command(s) to build and serve the book locally.
4. THE README section SHALL indicate where the book source files are located within the repository.
