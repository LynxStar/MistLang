# Implementation Plan: Book Content Authoring

## Overview

This plan breaks down the authoring of all 37+ sections of "The Mist Programming Language" mdBook into discrete tasks. Each task produces one markdown file with prose explanations and tabbed code examples (Mist source + emitted C#). Tasks follow the progressive order from SUMMARY.md so earlier concepts are established before later sections reference them.

The verification step for each task is running `mdbook build` in the `book/` directory to confirm well-formed Tab_Block syntax and valid markdown structure.

## Tasks

- [ ] 1. Author Introduction section
  - [ ] 1.1 Write `book/src/introduction.md`
    - Read `samples/HelloMist/` for a representative code example
    - Write prose stating Mist transpiles to C# targeting .NET
    - List key design goals (ownership, traits, type unions, refinement types, pattern matching) with one-sentence descriptions
    - State target audience (developers familiar with C# or similar statically-typed languages)
    - List each part heading with a one-sentence summary
    - Include a ≤15-line Mist example in a Tab_Block with emitted C#
    - _Requirements: 3.1, 3.2, 3.3, 3.4, 3.5, 3.6_

- [ ] 2. Author Getting Started sections
  - [ ] 2.1 Write `book/src/getting-started/installation.md`
    - Read `Source/Mist.Cli/` and project build files for installation details
    - Document prerequisites (.NET SDK 10.0+)
    - Provide numbered installation steps (build from source or published tool)
    - Include verification command and expected output
    - Document supported platforms
    - Document error behavior for missing/old .NET SDK
    - _Requirements: 4.1, 4.2, 4.3, 4.4, 4.5_

  - [ ] 2.2 Write `book/src/getting-started/hello-world.md`
    - Read `samples/Simpliest/` and `samples/HelloMist/` for minimal program structure
    - Walk through creating a new project (directory structure, `mist.toml`, `.mist` file)
    - Show minimal program in a Tab_Block with emitted C#
    - Show `mist build` and `mist run` commands with expected console output
    - Explain each line of the Hello World program
    - Show `mist.toml` contents with `name` field
    - _Requirements: 5.1, 5.2, 5.3, 5.4, 5.5_

  - [ ] 2.3 Write `book/src/getting-started/project-structure.md`
    - Read `samples/Namespaces/` for multi-file project example
    - Document `mist.toml` fields (`name`, `namespace_root`, `[[references]]`)
    - Explain directory layout and `.mist` file discovery
    - Explain multi-file projects via `[[references]]`
    - Document error behavior for missing/invalid `mist.toml`
    - _Requirements: 6.1, 6.2, 6.3, 6.4, 6.5_

- [ ] 3. Checkpoint - Verify Getting Started
  - Run `mdbook build` in `book/` to confirm all Getting Started sections render correctly. Ask the user if questions arise.

- [ ] 4. Author Basics sections (Part 1)
  - [ ] 4.1 Write `book/src/basics/primitive-types.md`
    - Read `samples/Primitives/` for type examples and emitted C#
    - Document all numeric types, bool, char, string (with interpolation and escapes)
    - Document numeric literal suffixes and hex literals
    - Show each type in a Tab_Block with emitted C#
    - _Requirements: 7.1, 7.2, 7.3, 7.4, 7.5, 7.6, 7.7_

  - [ ] 4.2 Write `book/src/basics/void-type.md`
    - Read `samples/VoidType/` for void usage patterns and emitted C#
    - Explain void as first-class value type with `MistVoid` struct
    - Show void as variable type, parameter type, generic type argument
    - Show fire-and-forget vs value-returning void methods in separate Tab_Blocks
    - Document void equality semantics
    - _Requirements: 8.1, 8.2, 8.3, 8.4, 8.5, 8.6_

  - [ ] 4.3 Write `book/src/basics/variables.md`
    - Read `samples/HelloMist/` for variable declaration patterns
    - Document explicit type annotations in a Tab_Block
    - Document `var` type inference in a Tab_Block
    - Document tuple deconstruction (var and typed forms) in Tab_Blocks
    - Explain scoping rules and mutability
    - _Requirements: 9.1, 9.2, 9.3, 9.4, 9.5_

  - [ ] 4.4 Write `book/src/basics/namespaces.md`
    - Read `samples/Namespaces/` for namespace patterns and emitted C#
    - Document file-scoped and block namespace declarations in Tab_Blocks
    - Explain namespace inference from project name
    - Explain nested namespaces mapping to directory structure
    - Document `using` declarations in a Tab_Block
    - Explain same-namespace type references
    - _Requirements: 10.1, 10.2, 10.3, 10.4, 10.5, 10.6_

  - [ ] 4.5 Write `book/src/basics/control-flow.md`
    - Read `samples/HelloMist/` for control flow examples
    - Document if/else if/else, while, for, foreach, break in separate Tab_Blocks
    - Show nested control flow example in a Tab_Block
    - _Requirements: 11.1, 11.2, 11.3, 11.4, 11.5, 11.6_

- [ ] 5. Author Basics sections (Part 2)
  - [ ] 5.1 Write `book/src/basics/functions.md`
    - Read `samples/HelloMist/` and `samples/Classes/` for function examples
    - Document function declarations with parameters and return types in a Tab_Block
    - Document static vs instance methods in separate Tab_Blocks
    - Document access modifiers (`public`, `private`)
    - Document void return type in a Tab_Block
    - Show recursion example in a Tab_Block
    - _Requirements: 12.1, 12.2, 12.3, 12.4, 12.5_

  - [ ] 5.2 Write `book/src/basics/array-literals.md`
    - Read `samples/ArrayLiterals/` for array syntax and emitted C#
    - Document bracket syntax with `var`, empty arrays with explicit type, nested arrays
    - Document arrays as method arguments and return values
    - Document trailing comma support and explicit type annotation
    - Each concept in its own Tab_Block
    - _Requirements: 13.1, 13.2, 13.3, 13.4, 13.5, 13.6_

  - [ ] 5.3 Write `book/src/basics/tuples.md`
    - Read `samples/Tuples/` for tuple syntax and emitted C#
    - Document positional tuples, named tuples, deconstruction, nested tuples
    - State tuples are value types lowering to `ValueTuple`
    - Each concept in its own Tab_Block
    - _Requirements: 14.1, 14.2, 14.3, 14.4, 14.5_

  - [ ] 5.4 Write `book/src/basics/regex-type.md`
    - Read `samples/RegexLiterals/` for regex syntax and emitted C#
    - Document backtick literal syntax, explicit `regex` type annotation
    - Explain compilation to `System.Text.RegularExpressions.Regex`
    - Document `IsMatch` and `IsFullMatch` methods in separate Tab_Blocks
    - _Requirements: 15.1, 15.2, 15.3, 15.4_

- [ ] 6. Checkpoint - Verify Basics
  - Run `mdbook build` in `book/` to confirm all Basics sections render correctly. Ask the user if questions arise.

- [ ] 7. Author Object Model sections (Part 1)
  - [ ] 7.1 Write `book/src/object-model/classes.md`
    - Read `samples/Classes/` for class patterns and emitted C#
    - Document class declaration, constructors, constructor chaining, `this` keyword
    - Document instance methods, static methods, static fields
    - Document access modifiers on members
    - Each concept in its own Tab_Block
    - _Requirements: 16.1, 16.2, 16.3, 16.4, 16.5, 16.6_

  - [ ] 7.2 Write `book/src/object-model/inheritance.md`
    - Read `samples/Inheritance/` for inheritance patterns and emitted C#
    - Document extending a base class, base constructor chaining, method overriding
    - Each concept in its own Tab_Block
    - _Requirements: 17.1, 17.2, 17.3, 17.4_

  - [ ] 7.3 Write `book/src/object-model/structs.md`
    - Read `samples/Classes/` (Color struct) for struct patterns and emitted C#
    - Document struct declaration syntax
    - Explain value-type vs reference-type semantics with a Tab_Block
    - Document `readonly` modifier on structs
    - Each concept in its own Tab_Block
    - _Requirements: 18.1, 18.2, 18.3, 18.4_

  - [ ] 7.4 Write `book/src/object-model/properties.md`
    - Read `samples/Properties/` and `samples/PropertyInitializers/` for property patterns
    - Document block-bodied and expression-bodied accessors, `init` accessor
    - Document auto-implemented properties, property initializers, accessor modifiers
    - Each concept in its own Tab_Block
    - _Requirements: 19.1, 19.2, 19.3, 19.4, 19.5_

  - [ ] 7.5 Write `book/src/object-model/interfaces.md`
    - Read `samples/Interfaces/` and `samples/ShapeInterfaces/` for interface patterns
    - Document interface declaration, class implementation, polymorphic usage
    - Each concept in its own Tab_Block
    - _Requirements: 20.1, 20.2, 20.3, 20.4_

- [ ] 8. Author Object Model sections (Part 2)
  - [ ] 8.1 Write `book/src/object-model/traits.md`
    - Read `samples/Traits/` and `samples/GenericTraits/` for trait patterns and emitted C#
    - Document trait declaration, structural satisfaction, `with` block mapping
    - Document polymorphic usage with trait types
    - Explain difference between traits (structural) and interfaces (nominal)
    - Each concept in its own Tab_Block
    - _Requirements: 21.1, 21.2, 21.3, 21.4, 21.5, 21.6_

  - [ ] 8.2 Write `book/src/object-model/enums.md`
    - Read `samples/Enums/` and `samples/EnumReadonlyRestriction/` for enum patterns
    - Document typed enums, name-only enums, mixed values, union underlying types
    - Document user-defined underlying types, implicit conversion, pattern matching
    - Each concept in its own Tab_Block
    - _Requirements: 22.1, 22.2, 22.3, 22.4, 22.5, 22.6, 22.7, 22.8_

  - [ ] 8.3 Write `book/src/object-model/method-overloading.md`
    - Read `samples/Overloading/` for overloading patterns and emitted C#
    - Document overloading by parameter count and by parameter type
    - Each concept in its own Tab_Block
    - _Requirements: 23.1, 23.2, 23.3_

  - [ ] 8.4 Write `book/src/object-model/extension-methods.md`
    - Read `samples/ExtensionMethods/` for extension method patterns and emitted C#
    - Document `extension(ReceiverType)` block declaration syntax
    - Document calling extension methods via dot-notation
    - Each concept in its own Tab_Block
    - _Requirements: 24.1, 24.2, 24.3_

- [ ] 9. Checkpoint - Verify Object Model
  - Run `mdbook build` in `book/` to confirm all Object Model sections render correctly. Ask the user if questions arise.

- [ ] 10. Author Type System sections (Part 1)
  - [ ] 10.1 Write `book/src/type-system/generics.md`
    - Read `samples/GenericTypes/` for generic patterns and emitted C#
    - Document generic classes, structs, interfaces, methods
    - Document type argument syntax at call sites and multiple type parameters
    - Each concept in its own Tab_Block
    - _Requirements: 25.1, 25.2, 25.3, 25.4, 25.5, 25.6, 25.7_

  - [ ] 10.2 Write `book/src/type-system/generic-constraints.md`
    - Read `samples/GenericConstraints/` for constraint patterns and emitted C#
    - Document `where T : constraint` syntax, class/struct constraints
    - Document interface/trait/base class constraints, constructor constraints
    - Document multiple constraints and multiple where clauses
    - Document constraints on generic methods
    - Each constraint kind in its own Tab_Block
    - _Requirements: 26.1, 26.2, 26.3, 26.4, 26.5, 26.6, 26.7, 26.8, 26.9_

  - [ ] 10.3 Write `book/src/type-system/type-unions.md`
    - Read `samples/TypeUnions/` for union patterns and emitted C#
    - Document union syntax with `|`, union-typed variables/parameters/returns
    - Document narrowing via pattern matching, multi-type unions, reassignment
    - Each concept in its own Tab_Block
    - _Requirements: 27.1, 27.2, 27.3, 27.4, 27.5, 27.6_

  - [ ] 10.4 Write `book/src/type-system/type-aliases.md`
    - Read `samples/TypeAliases/` for alias patterns and emitted C#
    - Document simple aliases, union aliases, closed generic aliases, generic aliases
    - Explain transparency and interchangeability
    - Each alias variant in its own Tab_Block
    - _Requirements: 28.1, 28.2, 28.3, 28.4, 28.5, 28.6_

  - [ ] 10.5 Write `book/src/type-system/newtypes.md`
    - Read `samples/Newtypes/` for newtype patterns and emitted C#
    - Document newtype declaration, wrapping primitives/structs/classes
    - Explain difference from type aliases
    - Document member access and explicit casting
    - Each concept in its own Tab_Block
    - _Requirements: 29.1, 29.2, 29.3, 29.4, 29.5, 29.6_

- [ ] 11. Author Type System sections (Part 2)
  - [ ] 11.1 Write `book/src/type-system/pattern-matching.md`
    - Read `samples/PatternMatching/` for pattern matching examples and emitted C#
    - Document `is` type patterns with binding, bare type patterns, `var` pattern
    - Document property patterns, relational patterns, logical combinators, list patterns
    - Each pattern kind in its own Tab_Block
    - _Requirements: 30.1, 30.2, 30.3, 30.4, 30.5, 30.6, 30.7, 30.8_

  - [ ] 11.2 Write `book/src/type-system/switch-expressions.md`
    - Read `samples/SwitchExpressions/` for switch expression patterns and emitted C#
    - Document postfix switch syntax, constant arms, type pattern arms
    - Document discard pattern, `when` guards, relational/logical patterns in arms
    - Each concept in its own Tab_Block
    - _Requirements: 31.1, 31.2, 31.3, 31.4, 31.5, 31.6, 31.7_

  - [ ] 11.3 Write `book/src/type-system/cast-operators.md`
    - Read `samples/CastOperators/` for cast operator patterns and emitted C#
    - Document `implicit operator` and `explicit operator` declaration syntax
    - Explain automatic vs cast-expression semantics
    - Explain compile-time type resolution
    - Implicit and explicit in separate Tab_Blocks
    - _Requirements: 32.1, 32.2, 32.3, 32.4, 32.5_

  - [ ] 11.4 Write `book/src/type-system/refinement-types.md`
    - Read `samples/RefinementTypes/` and `samples/RefinementComparison/` for refinement patterns
    - Document simple refinements, compound refinements, property pattern refinements
    - Explain compile-time and runtime enforcement
    - Document refinements as variable/parameter/return/field types
    - Document arithmetic producing base type
    - Each concept in its own Tab_Block
    - _Requirements: 33.1, 33.2, 33.3, 33.4, 33.5, 33.6, 33.7_

  - [ ] 11.5 Write `book/src/type-system/dependent-types.md`
    - Read `samples/DependentTypes/` for dependent type patterns and emitted C#
    - Document `Name:PrimitiveType` syntax, allowed value types
    - Document using dependent parameters as values in the type body
    - Document dependent types on classes, structs, interfaces, traits, methods
    - Explain relationship to refinement types
    - Document type identity semantics
    - Each concept in its own Tab_Block
    - _Requirements: 34.1, 34.2, 34.3, 34.4, 34.5, 34.6, 34.7_

- [ ] 12. Checkpoint - Verify Type System
  - Run `mdbook build` in `book/` to confirm all Type System sections render correctly. Ask the user if questions arise.

- [ ] 13. Author Advanced Features sections (Part 1)
  - [ ] 13.1 Write `book/src/advanced/ownership.md`
    - Read `samples/OwnershipSystem/` and `samples/DeepOwnershipTypes/` for ownership patterns
    - Explain five ownership tiers in order (unrestricted, owned, borrowed, frozen, readonly ref)
    - Document `owned` keyword for variables and type declarations
    - Document move semantics and use-after-move errors
    - Document lending rules (readonly ref, frozen, borrowed)
    - Explain value types satisfying all tiers
    - At least one Tab_Block per ownership tier
    - _Requirements: 35.1, 35.2, 35.3, 35.4, 35.5, 35.6_

  - [ ] 13.2 Write `book/src/advanced/ref-parameters.md`
    - Read `samples/RefParameters/` for ref parameter patterns and emitted C#
    - Document `readonly ref`, `borrowed`, and `ref` parameter syntax and semantics
    - At least one Tab_Block per ref parameter kind
    - _Requirements: 36.1, 36.2, 36.3, 36.4_

  - [ ] 13.3 Write `book/src/advanced/readonly-contexts.md`
    - Read `samples/ReadonlyContexts/` for readonly context patterns and emitted C#
    - Document `frozen` parameter modifier and LentFrozen state
    - Document `readonly { }` block statement
    - At least one Tab_Block per construct
    - _Requirements: 37.1, 37.2, 37.3, 37.4_

  - [ ] 13.4 Write `book/src/advanced/structured-context.md`
    - Read `samples/StructuredContext/` for context patterns and emitted C#
    - Document all context declaration forms (`with var`, `with Type`, `with Type _`, `with expr`)
    - Document context block syntax `with(expression) { }`
    - Document method `with` clause syntax
    - Explain type-keyed context, propagation, compile-time verification
    - At least one Tab_Block per context form
    - _Requirements: 38.1, 38.2, 38.3, 38.4, 38.5_

- [ ] 14. Author Advanced Features sections (Part 2)
  - [ ] 14.1 Write `book/src/advanced/unknown-type.md`
    - Read `samples/UnknownType/` and `samples/UnknownReadonlySemantics/` for unknown type patterns
    - Explain `unknown` as top type with no operations permitted
    - Document narrowing via `is` pattern matching
    - Document `unknown` as tuple element type
    - At least one Tab_Block per usage pattern
    - _Requirements: 39.1, 39.2, 39.3, 39.4_

  - [ ] 14.2 Write `book/src/advanced/default-keyword.md`
    - Read `samples/DefaultKeyword/` for default keyword patterns and emitted C#
    - Document `default(T)` and bare `default` forms
    - Explain what `default` produces for each type category
    - Document `default` block declaration in class/struct bodies
    - At least one Tab_Block per form
    - _Requirements: 40.1, 40.2, 40.3, 40.4_

  - [ ] 14.3 Write `book/src/advanced/nested-scoping.md`
    - Read `samples/NestedScoping/` for scoping patterns and emitted C#
    - Document lexical scoping with braces
    - Explain variable shadowing rules
    - Explain ownership/context interaction with nested scopes
    - At least one Tab_Block demonstrating shadowing
    - _Requirements: 41.1, 41.2, 41.3, 41.4_

- [ ] 15. Checkpoint - Verify Advanced Features
  - Run `mdbook build` in `book/` to confirm all Advanced Features sections render correctly. Ask the user if questions arise.

- [ ] 16. Author Standard Library section
  - [ ] 16.1 Write `book/src/standard-library/overview.md`
    - Read `Source/Mist/Stdlib/` for available modules and types
    - Describe the purpose of the standard library
    - List each module/namespace with a one-sentence description
    - Provide brief descriptions of key types and functions per module
    - _Requirements: 42.1, 42.2, 42.3_

- [ ] 17. Author Tooling sections
  - [ ] 17.1 Write `book/src/tooling/cli-usage.md`
    - Read `Source/Mist.Cli/` for CLI command structure and flags
    - Document `build` and `run` commands
    - Document all flags (`--backend`, `--output`, `--bin`, `--mode`, `--quiet`)
    - Document positional argument (project folder path)
    - Provide usage examples for common workflows
    - _Requirements: 43.1, 43.2, 43.3, 43.4_

  - [ ] 17.2 Write `book/src/tooling/editor-support.md`
    - Read `Editor/mist-syntax-highlighting/` for extension details
    - Document the VS Code extension and its location
    - Provide installation instructions
    - Describe features (syntax highlighting, language configuration, file association)
    - _Requirements: 44.1, 44.2, 44.3_

  - [ ] 17.3 Write `book/src/tooling/project-configuration.md`
    - Read `Source/Mist/Driver/ProjectFileParser.cs` for configuration parsing details
    - Document all `mist.toml` fields (`name`, `namespace_root`, `[[references]]`)
    - Provide minimal and multi-project example configurations
    - Document project references (path resolution, circular reference detection)
    - _Requirements: 45.1, 45.2, 45.3_

- [ ] 18. Final checkpoint - Verify complete book
  - Run `mdbook build` in `book/` to confirm the entire book builds without errors. Ask the user if questions arise.

## Notes

- This is a documentation authoring spec — each task produces a markdown file, not executable code
- Verification is done by running `mdbook build` in the `book/` directory after each section is written
- Each task references specific sample programs from the design's Section-to-Sample Mapping table as the source of truth for Mist syntax and emitted C#
- Tab_Block syntax must use exactly: `{{#tabs }}`, `{{#tab name="Mist" }}`, `{{#tab name="Emitted C#" }}`, `{{#endtab }}`, `{{#endtabs }}`
- Code fragments that are not compilable units (shell commands, directory trees, config files) use standalone fenced blocks without Tab_Block wrappers
- Progressive ordering must be maintained — no section should use unexplained concepts from later sections
- Checkpoints ensure incremental validation of the mdBook build

## Task Dependency Graph

```json
{
  "waves": [
    { "id": 0, "tasks": ["1.1"] },
    { "id": 1, "tasks": ["2.1", "2.2", "2.3"] },
    { "id": 2, "tasks": ["4.1", "4.2", "4.3", "4.4", "4.5"] },
    { "id": 3, "tasks": ["5.1", "5.2", "5.3", "5.4"] },
    { "id": 4, "tasks": ["7.1", "7.2", "7.3", "7.4", "7.5"] },
    { "id": 5, "tasks": ["8.1", "8.2", "8.3", "8.4"] },
    { "id": 6, "tasks": ["10.1", "10.2", "10.3", "10.4", "10.5"] },
    { "id": 7, "tasks": ["11.1", "11.2", "11.3", "11.4", "11.5"] },
    { "id": 8, "tasks": ["13.1", "13.2", "13.3", "13.4"] },
    { "id": 9, "tasks": ["14.1", "14.2", "14.3"] },
    { "id": 10, "tasks": ["16.1"] },
    { "id": 11, "tasks": ["17.1", "17.2", "17.3"] }
  ]
}
```
