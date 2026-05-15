# Design Document: Book Content Authoring

## Overview

This design describes the process and patterns for authoring content across all 37 sections of "The Mist Programming Language" mdBook. The work is purely documentation — filling existing stub markdown files with accurate prose, code examples, and tabbed Mist/C# pairs. No new code, infrastructure, or language features are introduced.

The core challenge is **accuracy**: every Mist code example must use valid syntax (verified against the grammar and sample programs), and every "Emitted C#" tab must show what the transpiler actually produces (verified by compiling samples and inspecting output).

## Architecture

The book is a static mdBook project with a single preprocessor (`mdbook-tabs`) for tabbed code blocks. The architecture is flat:

```
book/
├── book.toml              # mdBook config (tabs preprocessor enabled)
├── src/
│   ├── SUMMARY.md         # Table of contents (already complete)
│   ├── introduction.md    # Part 0
│   ├── getting-started/   # Part 1 (3 sections)
│   ├── basics/            # Part 2 (9 sections)
│   ├── object-model/      # Part 3 (9 sections)
│   ├── type-system/       # Part 4 (10 sections)
│   ├── advanced/          # Part 5 (7 sections)
│   ├── standard-library/  # Part 6 (1 section)
│   └── tooling/           # Part 7 (3 sections)
└── theme/                 # Custom CSS/JS for tabs
```

Each section is a standalone `.md` file. The SUMMARY.md and directory structure are already established and must not change.

## Components and Interfaces

### Component 1: Section Content Template

Every section follows a consistent content pattern. The template varies by section category:

**Category A: Language Feature Sections** (Requirements 7–41)

Each language feature section follows this structure:

1. **Title** — H1 heading matching the SUMMARY.md link text
2. **Introduction paragraph** — 2–4 sentences explaining what the feature is, why it exists, and when to use it
3. **Concept blocks** — One or more subsections, each covering a distinct aspect of the feature:
   - Brief prose explanation (1–3 sentences)
   - A Tab_Block showing the Mist syntax and emitted C#
4. **Summary or notes** (optional) — Key takeaways, gotchas, or forward references

**Category B: Getting Started Sections** (Requirements 3–6)

These are tutorial-style sections with a narrative flow:

1. **Title** — H1 heading
2. **Narrative prose** — Step-by-step instructions or explanations
3. **Code blocks** — Mix of Tab_Blocks (for compilable Mist), standalone fenced blocks (for shell commands, file contents, directory trees), and inline code
4. **Expected output** — What the reader should see after following the steps

**Category C: Reference Sections** (Requirements 42–45)

These are reference-style sections with structured information:

1. **Title** — H1 heading
2. **Overview paragraph** — Purpose and scope
3. **Structured content** — Tables, definition lists, or subsections organized by topic
4. **Examples** — Concrete usage examples where applicable

### Component 2: Tab_Block Format

The mdbook-tabs preprocessor requires this exact syntax:

```markdown
{{#tabs }}
{{#tab name="Mist" }}
```mist
// Mist source code
```
{{#endtab }}
{{#tab name="Emitted C#" }}
```csharp
// Corresponding C# output
```
{{#endtab }}
{{#endtabs }}
```

Rules:
- The Mist tab always appears first
- The C# tab always appears second
- Tab names are exactly `"Mist"` and `"Emitted C#"`
- Language identifiers are `mist` and `csharp`
- Each distinct concept gets its own Tab_Block (not combined into one large example)
- Code fragments that are not compilable units (e.g., syntax diagrams, partial snippets) use a standalone fenced block without a Tab_Block wrapper

### Component 3: Source-of-Truth for Mist Syntax

Accurate Mist syntax is determined from these sources, in priority order:

1. **Sample programs** (`samples/` directory) — The canonical reference for valid Mist syntax. Each feature has a dedicated sample (e.g., `samples/Traits/`, `samples/Enums/`, `samples/TypeUnions/`). These compile and run successfully.
2. **Grammar definition** (`Source/Mist/Grammar/Mist.storm`) — The formal grammar specifying all tokens and production rules. Use to verify syntax details (operator precedence, delimiter requirements, etc.).
3. **Syntax highlighting tests** (`Editor/mist-syntax-highlighting/tests/`) — Additional syntax examples covering edge cases.

The authoring process for each section:
1. Read the corresponding sample program(s) to understand the feature's syntax
2. Extract representative examples that demonstrate each acceptance criterion
3. Simplify examples for pedagogical clarity (shorter, focused on one concept at a time)
4. Verify syntax against the grammar if uncertain

### Component 4: Source-of-Truth for Emitted C#

Accurate C# output is determined by:

1. **Compiling sample programs** — Run `mist build` on the relevant sample project and inspect the generated `.cs` files in the `out/` directory. This is the ground truth.
2. **CSharpEmitter source** (`Source/Mist/Backends/CSharpTranspiler/CSharpEmitter.cs`) — When compilation isn't practical, the emitter source code documents the transformation rules:
   - Traits emit as interfaces with wrapper classes
   - Type unions emit as discriminated union structs (`Union_T1_T2`)
   - Dependent types emit as generic types with const-carrier interfaces
   - Void-as-value emits as `MistVoid` struct
   - Enums emit as readonly structs with a `Value` field
   - Newtypes emit as wrapper structs
   - Extension methods emit as static classes with static methods
   - Refinement types emit as the base type with runtime validation
   - Structured context emits using a `MistContext` thread-local stack

The authoring process for each C# tab:
1. Compile the sample with `mist build` (or `mist run` to also verify output)
2. Open the generated `.cs` file from `out/`
3. Extract the relevant portion, simplified to match the Mist tab's scope
4. Remove generated boilerplate that doesn't aid understanding (e.g., `#nullable enable`, `[CompilerGenerated]` attributes) unless the boilerplate is pedagogically relevant

### Component 5: Progressive Ordering Enforcement

The book's part/section ordering defines the progressive learning path:

```
Part 1: Getting Started     → assumes general programming knowledge only
Part 2: Basics              → introduces fundamental constructs
Part 3: Object Model        → builds on basics (classes, structs, interfaces, traits)
Part 4: Type System         → builds on object model (generics, unions, pattern matching)
Part 5: Advanced Features   → builds on type system (ownership, refinements, dependent types)
Part 6: Standard Library    → references all prior parts
Part 7: Tooling             → standalone reference material
```

Rules for progressive ordering:
- A section may freely reference concepts from earlier sections or the current section
- If a section must mention a concept from a later section, it includes a forward-reference note: *"We'll cover [concept] in detail in the [Section Name] chapter."*
- Code examples in a section only use syntax that has been introduced in prior sections or the current section (exception: `using System;` and `Console.WriteLine` are assumed known from Hello World onward)

## Data Models

### Section Metadata

Each section maps to a requirement and has these attributes:

| Attribute | Description |
|-----------|-------------|
| File path | `book/src/{part}/{section}.md` |
| Requirement | The requirement number (3–45) defining its acceptance criteria |
| Category | A (language feature), B (getting started), or C (reference) |
| Sample source | Path(s) to the `samples/` project(s) used as source of truth |
| Dependencies | List of prior sections whose concepts are used |

### Section-to-Sample Mapping

| Section | Primary Sample(s) |
|---------|-------------------|
| Introduction | `samples/HelloMist/` |
| Hello World | `samples/Simpliest/`, `samples/HelloMist/` |
| Project Structure | `samples/Namespaces/` (multi-file) |
| Primitive Types | `samples/Primitives/` |
| Void Type | `samples/VoidType/` |
| Variables | `samples/HelloMist/` |
| Namespaces | `samples/Namespaces/` |
| Control Flow | `samples/HelloMist/` |
| Functions | `samples/HelloMist/`, `samples/Classes/` |
| Array Literals | `samples/ArrayLiterals/` |
| Tuples | `samples/Tuples/` |
| Regex Type | `samples/RegexLiterals/` |
| Classes | `samples/Classes/` |
| Inheritance | `samples/Inheritance/` |
| Structs | `samples/Classes/` (Color struct) |
| Properties | `samples/Properties/`, `samples/PropertyInitializers/` |
| Interfaces | `samples/Interfaces/`, `samples/ShapeInterfaces/` |
| Traits | `samples/Traits/`, `samples/GenericTraits/` |
| Enums | `samples/Enums/`, `samples/EnumReadonlyRestriction/` |
| Method Overloading | `samples/Overloading/` |
| Extension Methods | `samples/ExtensionMethods/` |
| Generics | `samples/GenericTypes/` |
| Generic Constraints | `samples/GenericConstraints/` |
| Type Unions | `samples/TypeUnions/` |
| Type Aliases | `samples/TypeAliases/` |
| Newtypes | `samples/Newtypes/` |
| Pattern Matching | `samples/PatternMatching/` |
| Switch Expressions | `samples/SwitchExpressions/` |
| Cast Operators | `samples/CastOperators/` |
| Refinement Types | `samples/RefinementTypes/`, `samples/RefinementComparison/` |
| Dependent Types | `samples/DependentTypes/` |
| Ownership | `samples/OwnershipSystem/`, `samples/DeepOwnershipTypes/` |
| Ref Parameters | `samples/RefParameters/` |
| Readonly Contexts | `samples/ReadonlyContexts/` |
| Structured Context | `samples/StructuredContext/` |
| Unknown Type | `samples/UnknownType/`, `samples/UnknownReadonlySemantics/` |
| Default Keyword | `samples/DefaultKeyword/` |
| Nested Scoping | `samples/NestedScoping/` |
| Standard Library | stdlib source (`Source/Mist/Stdlib/`) |
| CLI Usage | `Source/Mist.Cli/` (CLI source) |
| Editor Support | `Editor/mist-syntax-highlighting/` |
| Project Configuration | `Source/Mist/Driver/ProjectFileParser.cs` |

## Error Handling

Not applicable — this is a documentation authoring spec. There is no runtime error handling to design.

If a section's code example cannot be verified (e.g., the sample doesn't compile or the emitted C# doesn't match expectations), the section should be flagged for review rather than published with unverified content.

## Testing Strategy

Property-based testing does not apply to this feature. The work product is markdown documentation, not executable code with input/output behavior. There are no pure functions, no data transformations, and no universal properties to verify across random inputs.

### Verification Approach

Instead of automated tests, correctness is verified through:

1. **Compilation verification** — Each Mist code example in a Tab_Block must be extractable into a valid `.mist` file that compiles without errors using `mist build`. This is verified manually or via a future CI step that extracts and compiles book examples.

2. **Output verification** — Each "Emitted C#" tab must match what the compiler actually produces. Verified by compiling the corresponding sample and comparing the `out/` directory contents.

3. **mdBook build verification** — Running `mdbook build` in the `book/` directory must succeed without errors, confirming all Tab_Block syntax is well-formed.

4. **Progressive ordering review** — Manual review that no section uses unexplained concepts from later sections.

### What Would Be Tested If This Were Code

If this were a software feature, the testing strategy would include:
- **Unit tests**: Verify each section file exists and contains the required structural elements (H1 heading, at least one Tab_Block for feature sections, etc.)
- **Integration tests**: Verify `mdbook build` succeeds and produces valid HTML
- **Content extraction tests**: Extract Mist code from Tab_Blocks and verify they compile

These could be implemented as a CI validation step in the future but are out of scope for the content authoring itself.
