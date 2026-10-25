# Tasks

## Task 1: Extend Storm Grammar with StatementItem

- [x] Add `| statement:Statement as StatementItem` to the `TopLevelItem` rule in `Source/Mist/Grammar/Mist.storm`
- [x] Rebuild the Storm-generated parser to produce `StatementItemTopLevelItemNode`
- [x] Verify that a minimal `.mist` file with a statement at file scope (e.g., `var x = 1;`) parses without errors and produces a `CompilationUnitNode` with a `StatementItemTopLevelItemNode` in its `Items` array

**Requirements:** 1.1, 1.2

## Task 2: Add `HasTopLevelStatements` to CompilationUnit

- [x] Add `public bool HasTopLevelStatements { get; init; }` property to `Source/Mist/Driver/Contracts/CompilationUnit.cs`
- [x] In `CompilerDriver.Compile()`, after parsing each AST, set `HasTopLevelStatements = true` on the corresponding `CompilationUnit` if the AST contains any `StatementItemTopLevelItemNode` items
- [x] Verify the flag is correctly set for a file with top-level statements and not set for a file without them

**Requirements:** 1.1

## Task 3: Parse `entry_point` Field in mist.toml

- [x] Add `string? EntryPoint = null` parameter to the `ProjectConfiguration` record in `Source/Mist/Driver/Contracts/ProjectConfiguration.cs`
- [x] In `ProjectFileParser.Parse()`, read the optional `entry_point` string field from the TOML model after `namespace_root` parsing
- [x] Validate that `entry_point` is not empty/whitespace when present (emit `MIST_PROJECT_EMPTY_ENTRY_POINT` diagnostic)
- [x] Store the parsed value in the returned `ProjectConfiguration`

**Requirements:** 4.1, 4.2, 4.3, 4.4

## Task 4: Thread Entry Point Config Through the Pipeline

- [x] Add `string? EntryPoint = null` to `CompilerOptions` in `Source/Mist/Driver/Contracts/CompilerOptions.cs`
- [x] Add `string? EntryPoint = null` to `MistCompilation` (or equivalent) so analyzers can access it
- [x] In `CompilerDriver.Compile()`, read the `entry_point` from the parsed `ProjectConfiguration` and pass it through to the compilation context
- [x] Ensure the CLI (`Mist.Cli`) populates `CompilerOptions.EntryPoint` from the parsed project file when available

**Requirements:** 4.1, 4.2

## Task 5: Create EntryPointAnalyzer

- [x] Create `Source/Mist/Analysis/Builtin/EntryPointAnalyzer.cs` implementing `IAnalyzer`
- [x] Implement Rule 1 — Multiple TLS files: scan user-code units for `HasTopLevelStatements`, emit `MIST_EP_0001` if more than one
- [x] Implement Rule 2 — TLS + Main conflict: detect coexistence, check `entry_point` config, emit `MIST_EP_0002` (no config) or `MIST_EP_0003` (class name specified with TLS present)
- [x] Implement Rule 3 — Multiple Mains without TLS: detect multiple `static void Main` methods across classes, check `entry_point` config, emit `MIST_EP_0004` (no config) or `MIST_EP_0007` (specified class not found)
- [x] Implement Rule 4 — `entry_point = "top-level"` but no TLS: emit `MIST_EP_0005`
- [x] Implement Rule 5 — TLS + block namespace: emit `MIST_EP_0006`
- [x] Register `EntryPointAnalyzer` in the analyzer array in the CLI/driver setup

**Requirements:** 2.1, 2.2, 3.1, 3.2, 3.3, 3.4, 4.1, 4.2, 4.3, 4.4, 4.5, 4.6, 4.7

## Task 6: Semantic Model — Skip Statements in Pass 1, Register Types Normally

- [x] In `SemanticModelBuilder.Pass1_RegisterTypes()`, when iterating `TopLevelItem` nodes, skip `StatementItemTopLevelItemNode` (no type registration needed for statements)
- [x] Verify that type declarations appearing after top-level statements in the same file are still registered in their correct namespace (inferred or file-scoped)
- [x] Verify that a file-scoped namespace in a TLS file applies to type declarations but does not affect the statements

**Requirements:** 1.4, 1.5

## Task 7: Semantic Model — Resolve Top-Level Statements in Pass 2

- [x] In `SemanticModelBuilder`, for compilation units with `HasTopLevelStatements == true`, create a synthetic scope for the TLS file's statements
- [x] Synthesize an implicit `args` local variable of type `string[]` in the TLS scope (resolved from the stdlib `Array<String>` type)
- [x] Resolve each `StatementItemTopLevelItemNode`'s statement within the synthetic scope using the existing `Pass2_ResolveStatement` logic (or equivalent)
- [x] Ensure local variables declared in earlier top-level statements are visible to later ones (sequential scope)
- [x] Validate `return` statements: integer expression → valid (exit code), no expression → valid (exit 0), non-integer expression → type error

**Requirements:** 1.1, 1.2, 6.1, 6.2, 7.1, 7.2, 7.3

## Task 8: C# Emitter — Emit Top-Level Statements

- [x] In `CSharpSourceWriter.EmitCompilationUnit()`, detect if the compilation unit has `StatementItemTopLevelItemNode` items
- [x] Implement `EmitTopLevelStatementUnit()` that:
  - Emits `using` declarations at the top
  - Emits top-level statements directly (no class/method wrapper) using existing `EmitStatement()` logic
  - Emits type declarations after statements, wrapped in their namespace block
- [x] Ensure no namespace declaration wraps the top-level statements themselves
- [x] Verify that `args` references in TLS emit correctly (C# top-level statements have implicit `args`)
- [x] Verify that `return <int>` emits as `return <int>;` (C# TLS supports this for exit codes)

**Requirements:** 5.1, 5.2, 5.3, 5.4, 5.5

## Task 9: Pretty Printer — Handle StatementItemTopLevelItemNode

- [x] In `MistPrettyPrinter.EmitCompilationUnit()`, add a case for `StatementItemTopLevelItemNode` that calls the existing `EmitStatement()` method at indent level 0
- [x] Verify round-trip: parse a TLS file → pretty-print → parse again → compare ASTs for equivalence
- [x] Ensure statements are printed without any wrapping class/method indentation

**Requirements:** 8.1, 8.2

## Task 10: CompilationUnitNodeExtensions — Add GetTopLevelStatements Helper

- [x] Add `GetTopLevelStatements()` extension method to `Source/Mist/Generated/CompilationUnitNodeExtensions.cs` that returns all `StatementItemTopLevelItemNode` items from a `CompilationUnitNode`
- [x] Add `HasTopLevelStatements()` extension method that returns `true` if any `StatementItemTopLevelItemNode` exists in the items

**Requirements:** 1.1

## Task 11: Namespace Interaction — File-Scoped Namespace in TLS Files

- [x] Ensure the semantic model builder correctly handles a TLS file with a file-scoped namespace: the namespace applies to type declarations but not to the statements
- [x] In the C# emitter, when a TLS file has a file-scoped namespace, emit the type declarations inside a `namespace X { }` block after the top-level statements
- [x] Verify that block namespace declarations in a TLS file are caught by the EntryPointAnalyzer (MIST_EP_0006)

**Requirements:** 1.5, 1.6, 5.4

## Task 12: Sample Application

- [x] Create `samples/TopLevelStatements/mist.toml` with `name = "TopLevelStatements"` and `entry_point = "top-level"`
- [x] Create `samples/TopLevelStatements/Program.mist` with top-level statements exercising all permutation axes:
  - Variable declarations (explicit type and `var`)
  - Expression statements (Console.WriteLine, string interpolation)
  - Control flow (`if`, `for`, `while`, `foreach`)
  - Implicit `args` variable usage
  - `return` with integer exit code
  - Type declarations (class, struct) after statements
  - File-scoped namespace declaration
  - Using declarations
- [x] Add a class with `public static void RunDemo()` and `static void Main(string[] args)` in the same project to demonstrate disambiguation
- [x] Add `[[references]]` entry for TopLevelStatements in `samples/AllFeatures/mist.toml`
- [x] Add `TopLevelStatements.Program.RunDemo()` call in `samples/AllFeatures/Program.mist`
- [x] Verify the sample compiles and runs correctly with `mist run samples/TopLevelStatements`

**Requirements:** 9.1, 9.2, 9.3, 9.4, 9.5, 9.6, 9.7

## Task 13: Book Documentation

- [x] Create `book/src/getting-started/top-level-statements.md` with:
  - Introduction explaining TLS purpose and syntax
  - Mist + emitted C# tabbed example (using `{{#include}}` from snippet files)
  - Single TLS file constraint documentation
  - Entry point conflict rules (TLS+Main, multiple Mains)
  - `entry_point` field documentation with both `"top-level"` and class name examples
  - Implicit `args` variable documentation
  - `return` statement exit code behavior
  - File-scoped namespace interaction
- [x] Create snippet files under `book/snippets/getting-started/top-level-statements/`
- [x] Add the chapter to `book/src/SUMMARY.md` in the Getting Started section
- [x] Update `book/src/tooling/project-configuration.md` to document the new `entry_point` field
- [x] Verify the book builds with `mdbook build`

**Requirements:** 10.1, 10.2, 10.3, 10.4, 10.5, 10.6, 10.7
