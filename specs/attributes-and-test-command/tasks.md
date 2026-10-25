# Implementation Plan: Attributes and Test Command

## Overview

This plan implements three interconnected features: attribute syntax parsing/emission, the `mist test` CLI command, and project output type configuration. The work is structured to build grammar and data model foundations first, wire them through the compiler pipeline, then add the CLI command and documentation/samples last.

## Tasks

- [x] 1. Define grammar rules and data model foundations
  - [x] 1.1 Add attribute grammar rules to `Source/Mist/Grammar/Mist.storm`
    - Add `AttributeList`, `Attribute`, `AttributeArgumentList`, and `AttributeArgument` rules
    - `AttributeArgument` has two alternatives: `NamedAttributeArgument` and `PositionalAttributeArgument`
    - Modify `ClassDeclaration`, `StructDeclaration`, `AbstractDeclaration`, `EnumDeclaration`, `MethodDeclaration`, `ConstructorDeclaration`, and `FieldDeclaration` to accept `attributes:AttributeList*` before `modifiers:Modifier*`
    - _Requirements: 2.1, 2.2, 2.3, 2.4, 2.5, 2.6, 2.7_

  - [x] 1.2 Add `ProjectOutputType` enum and update `ProjectConfiguration` record
    - Create `ProjectOutputType` enum (`Executable`, `Library`) in `Source/Mist/Driver/Contracts/`
    - Add `OutputType` parameter (default `Executable`) to `ProjectConfiguration` record
    - _Requirements: 14.1, 14.2_

  - [x] 1.3 Add `OutputType` to `CompilerOptions` record
    - Add `ProjectOutputType OutputType = ProjectOutputType.Executable` parameter to `CompilerOptions`
    - _Requirements: 14.4, 14.5_

  - [x] 1.4 Add `Test` variant to `CliCommand` enum
    - Add `Test` to the `CliCommand` enum in `Source/Mist.CLI/Args/CliArguments.cs` with XML doc comment
    - _Requirements: 5.1_

- [x] 2. Implement project file parsing and CLI argument changes
  - [x] 2.1 Parse `output_type` field in `ProjectFileParser`
    - Read optional `output_type` string from the TOML model
    - Validate case-sensitive exact match against `"executable"` and `"library"`
    - Emit `MIST0059` diagnostic for invalid values
    - Default to `Executable` when absent
    - _Requirements: 14.1, 14.2, 14.3_

  - [x] 2.2 Update `CliArgumentParser` to recognize `test` command verb
    - Add parsing for `"test"` positional argument, setting `Command = CliCommand.Test`
    - Validate `test` + `--mode lex` as incompatible (same pattern as `run` + `--mode lex`)
    - _Requirements: 5.1, 5.2, 5.7_

  - [x] 2.3 Update CLI usage message to include `test` command
    - Add `"  test               Compile and run tests"` to the usage output in `Program.cs`
    - _Requirements: 5.6_

  - [x] 2.4 Write property test for output type parsing validation (Property 4)
    - **Property 4: Output Type Parsing Validation**
    - Generate random strings and verify: `"executable"` and `"library"` produce success with correct enum value; all other strings produce a diagnostic error
    - **Validates: Requirements 14.1, 14.2, 14.3**

  - [x] 2.5 Write property test for CLI test command argument parsing (Property 3)
    - **Property 3: CLI Test Command Argument Parsing**
    - Generate random subsets of valid flags prepended with `test`, parse, and verify all flags correctly extracted with `Command == Test`
    - **Validates: Requirements 5.1, 5.2**

- [x] 3. Checkpoint - Ensure all tests pass
  - Ensure all tests pass, ask the user if questions arise.

- [x] 4. Implement output type through compiler pipeline
  - [x] 4.1 Propagate `OutputType` from `ProjectConfiguration` through `CompilerOptions` in `Program.cs`
    - Update `CompileProject` helper to pass `config.OutputType` into `CompilerOptions`
    - _Requirements: 14.4, 14.5_

  - [x] 4.2 Update `CSharpEmitter.EmitCsproj` to accept and emit correct `OutputType`
    - Modify `EmitCsproj` to accept `ProjectOutputType` parameter
    - Emit `<OutputType>Exe</OutputType>` for `Executable`, `<OutputType>Library</OutputType>` for `Library`
    - Thread the output type through from `CSharpTranspilerBackend.Emit` and `CSharpEmitter.Emit`
    - _Requirements: 14.4, 14.5_

  - [x] 4.3 Update `EntryPointAnalyzer` for library mode
    - When `OutputType == Library`: skip Main entry point validation entirely
    - When `OutputType == Library` and top-level statements exist: emit `MIST0115` error
    - Pass `OutputType` via `CompilerOptions` or a new mechanism to the analyzer
    - _Requirements: 14.6, 14.7, 14.8_

  - [x] 4.4 Add `mist run` validation for library projects
    - In `RunRunCommand`, parse `mist.toml` and check `output_type` before compilation
    - If `"library"`, print error and return exit code 1 without compiling
    - _Requirements: 8.1, 8.2, 8.3_

- [x] 5. Implement attribute emission in `CSharpEmitter`
  - [x] 5.1 Emit attribute annotations on type declarations
    - When a type declaration node has `AttributeList` children, emit each `AttributeList` as `[attr1, attr2]` on its own line before the declaration
    - Handle qualified names, no-arg attributes (no parens), and multiple separate attribute lists
    - _Requirements: 3.1, 3.5, 3.6, 3.7_

  - [x] 5.2 Emit attribute annotations on method and constructor declarations
    - Same emission logic for methods and constructors with attribute lists
    - _Requirements: 3.2_

  - [x] 5.3 Emit attribute arguments (positional, named, type arguments)
    - Emit positional arguments first, then named arguments as `Name = value`
    - Emit type arguments on the attribute name (e.g., `[JsonConverter<MyConverter>]`)
    - Emit type literal expressions as `typeof(T)` wrapping
    - _Requirements: 3.3, 3.4, 3.8, 4.6_

  - [x] 5.4 Write property test for attribute emission round-trip (Property 1)
    - **Property 1: Attribute Emission Round-Trip**
    - Generate random attribute configurations (varying counts, argument types, named/positional, type args, qualified names), compile in-memory, verify emitted C# preserves structure
    - Use batched compilation with shared CompilerDriver instance
    - **Validates: Requirements 1.1, 1.2, 1.3, 1.5, 1.6, 3.1, 3.2, 3.3, 3.4, 3.6, 3.7, 3.8**

  - [x] 5.5 Write property test for declaration-position disambiguation (Property 2)
    - **Property 2: Declaration-Position Disambiguation**
    - Generate programs with both attribute annotations on declarations AND bracket expressions in method bodies, compile, verify both parse correctly
    - **Validates: Requirements 1.10**

  - [x] 5.6 Write property test for type literal attribute arguments (Property 6)
    - **Property 6: Type Literal Attribute Arguments Emit as typeof**
    - Generate various type references used as attribute arguments, compile, verify emitted C# contains `typeof(...)` wrapping
    - **Validates: Requirements 4.6**

- [x] 6. Checkpoint - Ensure all tests pass
  - Ensure all tests pass, ask the user if questions arise.

- [x] 7. Implement `mist test` CLI command
  - [x] 7.1 Implement `RunTestCommand` in `Program.cs`
    - Compile the project using `CompileProject` (same as build)
    - On compilation failure: print diagnostics to stderr, return exit code 1
    - On success: invoke `dotnet test <csproj-path>` with stdout/stderr streaming
    - Return `dotnet test` exit code
    - Handle `dotnet` not found: print `error: Failed to start 'dotnet' process.` to stderr, return 1
    - _Requirements: 5.3, 5.4, 5.5, 7.1, 7.2, 7.3, 7.4, 7.5, 7.6_

  - [x] 7.2 Implement test package warning detection
    - After successful compilation but before invoking `dotnet test`, scan AST for method declarations with attributes ending in `Fact`, `Theory`, `Test`, or `TestMethod`
    - If found and no package in `mist.toml` contains "Test" or "xunit" (case-insensitive), emit warning `MIST0102`
    - _Requirements: 6.3_

  - [x] 7.3 Wire `CliCommand.Test` into the `switch` in `Main`
    - Add case for `CliCommand.Test` calling `RunTestCommand`
    - _Requirements: 5.3, 5.5_

  - [x] 7.4 Write property test for test package warning detection (Property 5)
    - **Property 5: Test Package Warning Detection**
    - Generate combinations of method attribute names and package lists, verify warning presence/absence matches expected condition
    - **Validates: Requirements 6.3**

- [x] 8. Checkpoint - Ensure all tests pass
  - Ensure all tests pass, ask the user if questions arise.

- [x] 9. Create sample applications
  - [x] 9.1 Create `samples/Attributes/` sample project
    - Create `samples/Attributes/mist.toml` with project name
    - Create `samples/Attributes/Program.mist` with the proof code from Requirement 10 verbatim
    - Demonstrates: attributes on class, struct, enum, abstract, method; no-arg, positional, named, multiple in one list, multiple lists, type literal args, generic type args
    - Must produce the exact expected output specified in Requirement 10
    - _Requirements: 10.1, 10.2, 10.3, 10.4, 10.5, 10.6, 10.8_

  - [x] 9.2 Integrate Attributes sample into AllFeatures orchestrator
    - Add `Attributes = { path = "../Attributes" }` to `samples/AllFeatures/mist.toml` references
    - Add `Attributes.Program.RunDemo();` call in `samples/AllFeatures/Program.mist`
    - _Requirements: 10.7_

  - [x] 9.3 Create `samples/Testing/` sample project
    - Create `samples/Testing/mist.toml` with `output_type = "library"` and xUnit packages
    - Create `samples/Testing/Tests.mist` with the proof code from Requirement 11 verbatim
    - Includes `[Fact]` tests and `[Theory]`/`[InlineData]` parameterized tests
    - Must compile with `mist build` and all tests pass with `mist test`
    - _Requirements: 11.1, 11.2, 11.3, 11.4, 11.5, 11.6_

- [x] 10. Update TextMate grammar and highlight.js
  - [x] 10.1 Update TextMate grammar for attribute syntax highlighting
    - Add `meta.attribute.mist` pattern recognizing `[` at beginning of line or after modifier
    - Scope brackets as `punctuation.definition.attribute.begin.mist` / `punctuation.definition.attribute.end.mist`
    - Scope attribute name as `entity.other.attribute-name.mist`
    - Handle argument highlighting (strings, numbers, booleans) and type arguments within attribute scope
    - _Requirements: 9.1, 9.2, 9.3, 9.4, 9.5, 9.6_

  - [x] 10.2 Update highlight.js grammar for book code highlighting
    - Update both `book/theme/mist-lang.js` and `book/theme/highlight.js` to recognize attribute syntax
    - Apply appropriate CSS classes for attribute names, arguments, and brackets
    - _Requirements: 16.1, 16.2, 16.3_

- [x] 11. Create book documentation and snippets
  - [x] 11.1 Create book snippets for attributes chapter
    - Create snippet files in `book/snippets/advanced/attributes/` demonstrating:
      - Basic attribute usage (no args, positional, named)
      - Type literal and generic type arguments
      - Multiple attributes and multiple lists
      - Attributes on types vs methods
    - Create corresponding empty `.cs` files and run SnippetEmitter tool
    - At least 4 compiled snippets required
    - _Requirements: 12.2, 12.3_

  - [x] 11.2 Write `book/src/advanced/attributes.md` chapter
    - Cover attribute syntax, valid targets (types and methods), supported argument types
    - Use `{{#include}}` references to snippet files with tabbed format
    - _Requirements: 12.1, 12.2_

  - [x] 11.3 Update `book/src/SUMMARY.md` to include attributes chapter
    - Add entry after ".NET Interop" in the Advanced Features section
    - _Requirements: 12.4_

- [x] 12. Create steering files and TODO documentation
  - [x] 12.1 Update main steering file content to mention `mist test` and `output_type`
    - Add high-level mention of `mist test` command (compiles and runs tests via backend runner)
    - Document `output_type` field with both valid values and their effects
    - _Requirements: 15.1_

  - [x] 12.2 Create dedicated testing steering file
    - Create a separate testing steering file documenting: test project setup with packages, writing test methods with attributes, `mist test` invocation of `dotnet test`, `mist test` works on any project regardless of output type
    - Must be a separate file from main project steering
    - _Requirements: 15.2, 15.3_

  - [x] 12.3 Create `project/TODO-test-runner.md`
    - Document that `mist test` currently delegates to backend's `dotnet test`
    - Note future Mist-native test runner is desired but out of scope for v1 with rationale
    - Outline what a native runner would need: test discovery, assertion library, result reporting format, IDE integration
    - _Requirements: 13.1, 13.2, 13.3_

- [x] 13. Final checkpoint - Ensure all tests pass
  - Ensure all tests pass, ask the user if questions arise.

## Notes

- Tasks marked with `*` are optional and can be skipped for faster MVP
- Each task references specific requirements for traceability
- Checkpoints ensure incremental validation
- Property tests validate universal correctness properties from the design document
- The Storm parser generator automatically produces AST node types from grammar rules — no manual AST construction needed
- The `[` and `]` tokens (LEFT_BRACKET, RIGHT_BRACKET) are already defined in the grammar
- Attribute emission is transparent — no semantic analysis of attribute contents is performed
- Property-based tests use FsCheck with batched compilation (shared CompilerDriver, 1 process spawn per test class)
- All test output must be ASCII-only

## Task Dependency Graph

```json
{
  "waves": [
    { "id": 0, "tasks": ["1.1", "1.2", "1.3", "1.4"] },
    { "id": 1, "tasks": ["2.1", "2.2", "2.3"] },
    { "id": 2, "tasks": ["2.4", "2.5", "4.1", "4.2", "4.3", "4.4"] },
    { "id": 3, "tasks": ["5.1", "5.2"] },
    { "id": 4, "tasks": ["5.3", "5.4", "5.5", "5.6"] },
    { "id": 5, "tasks": ["7.1", "7.2"] },
    { "id": 6, "tasks": ["7.3", "7.4"] },
    { "id": 7, "tasks": ["9.1", "9.3", "10.1", "10.2"] },
    { "id": 8, "tasks": ["9.2", "11.1", "12.1", "12.2", "12.3"] },
    { "id": 9, "tasks": ["11.2", "11.3"] }
  ]
}
```
