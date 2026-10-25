# Implementation Plan: Target-Typed New Expressions

## Overview

This plan implements target-typed `new()` expressions across the Mist compiler pipeline: Storm grammar, parser-generated AST nodes, semantic model builder (target type resolution), type checker (validation and diagnostics), C# emitter (fully-typed output), pretty-printer (round-trip formatting), TextMate grammar verification, sample application, book documentation, and feature tracking update.

## Tasks

- [x] 1. Grammar and parser support
  - [x] 1.1 Add TargetTypedNewExpression and TargetTypedNewExpressionNoArgs rules to Mist.storm
    - Add two new alternatives to the `Primary` rule in `Source/Mist/Grammar/Mist.storm`
    - `| NEW LEFT_PARENTHESIS arguments:ArgumentList? RIGHT_PARENTHESIS initializer:PropertyInitializerBlock? as TargetTypedNewExpression`
    - `| NEW initializer:PropertyInitializerBlock as TargetTypedNewExpressionNoArgs`
    - Place them after the existing `ObjectCreationExpression` and `ObjectCreationExpressionNoArgs` alternatives
    - Build the project to trigger the Storm source generator and produce the new AST node types
    - Verify `TargetTypedNewExpressionPrimaryNode` and `TargetTypedNewExpressionNoArgsPrimaryNode` are generated
    - _Requirements: 1.1, 1.2, 1.3, 1.4, 1.5, 1.6_

  - [x] 1.2 Write property test for parsing disambiguation (Property 2)
    - **Property 2: Parsing disambiguation**
    - **Validates: Requirements 1.4, 1.5**
    - Create `Test/Mist.Tests/Parsing/TargetTypedNewDisambiguationPropertyTests.cs`
    - Generate random valid type names and argument lists; verify `new TypeName(args)` always produces `ObjectCreationExpressionPrimaryNode`
    - Verify `new(args)` always produces `TargetTypedNewExpressionPrimaryNode`
    - Verify `new { ... }` always produces `TargetTypedNewExpressionNoArgsPrimaryNode`
    - In-process only (no process spawning)

- [x] 2. Pretty-printer support
  - [x] 2.1 Implement pretty-printer cases for target-typed new expression nodes
    - Add cases for `TargetTypedNewExpressionPrimaryNode` and `TargetTypedNewExpressionNoArgsPrimaryNode` in `Source/Mist/Parsing/MistPrettyPrinter.cs`
    - `TargetTypedNewExpressionPrimaryNode` → `new(args)` or `new(args) { ... }`
    - `TargetTypedNewExpressionNoArgsPrimaryNode` → `new { ... }`
    - Follow existing patterns for argument list and property initializer emission
    - _Requirements: 2.1, 2.2, 2.3, 2.4_

  - [x] 2.2 Write property test for parsing round-trip (Property 1)
    - **Property 1: Parsing round-trip**
    - **Validates: Requirements 2.5**
    - Create `Test/Mist.Tests/Parsing/TargetTypedNewRoundTripPropertyTests.cs`
    - Generate target-typed new expressions with varying argument counts and optional property initializers
    - Parse → pretty-print → re-parse and verify AST equivalence
    - In-process only (no process spawning)

- [x] 3. Checkpoint - Ensure grammar and pretty-printer work
  - Ensure all tests pass, ask the user if questions arise.

- [x] 4. Semantic model builder — target type resolution
  - [x] 4.1 Add `_targetTypedNewTargetType` thread-static field and resolution logic to SemanticModelBuilder
    - Add `[ThreadStatic] private static IType? _targetTypedNewTargetType;` to `Source/Mist/Semantics/Model/SemanticModelBuilder.cs`
    - Add save/set/resolve/restore logic at each target type propagation point:
      - Field initializer (`ResolveFieldDeclaration`) — set from declared field type
      - Typed local variable (`ResolveVariableDeclaration` for TypedVariableDeclaration) — set from declared type
      - Return statement (`ResolveStatement` for ReturnStatement) — set from `_currentMethodReturnType`
      - Method argument (`ResolveArgumentList`) — set from parameter type
      - Assignment RHS (`ResolveAssignment`) — set from target variable type
    - Add resolution case for `TargetTypedNewExpressionPrimaryNode`:
      - Read `_targetTypedNewTargetType`; if null or ErrorType, record ErrorType on the node
      - Otherwise resolve arguments using constructor parameter types from the target type
      - Resolve property initializer values if present
      - Record the target type as the expression type
    - Add resolution case for `TargetTypedNewExpressionNoArgsPrimaryNode` (same pattern, no arguments)
    - For `var` declarations, ensure `_targetTypedNewTargetType` is NOT set (remains null)
    - _Requirements: 3.1, 3.2, 3.3, 3.4, 3.5, 3.6, 3.7_

  - [x] 4.2 Write property test for target type resolution from context (Property 3)
    - **Property 3: Target type resolution from context**
    - **Validates: Requirements 3.1, 3.2, 3.3, 3.4, 3.5**
    - Create `Test/Mist.Tests/Semantics/TargetTypedNewResolutionPropertyTests.cs`
    - Generate programs with target-typed new in various typed contexts (field, local, return, argument, assignment)
    - Verify expression type on the AST node matches the declared target type
    - In-process (compile with shared driver, inspect semantic model)

  - [x] 4.3 Write property test for generic type preservation (Property 4)
    - **Property 4: Generic type preservation**
    - **Validates: Requirements 5.3**
    - Create `Test/Mist.Tests/Semantics/TargetTypedNewGenericPropertyTests.cs`
    - Generate programs with generic target types (e.g., `List<int>`, `Pair<string>`)
    - Verify the resolved expression type includes fully-constructed type arguments
    - In-process (compile with shared driver, inspect semantic model)

- [x] 5. Type checker — validation and diagnostics
  - [x] 5.1 Add type checker validation for target-typed new expressions
    - Add validation in `Source/Mist/Analysis/Builtin/TypeCheckerAnalyzer.cs` for both node types
    - Emit `MIST_TARGET_NEW_WITH_VAR (historical)` when used in a `var` declaration context
    - Emit `MIST_TARGET_NEW_NO_CONTEXT (historical)` when expression type is ErrorType and no target available
    - Emit `MIST_TARGET_NEW_INTERFACE (historical)` when target type is an interface
    - Emit `MIST_TARGET_NEW_TRAIT (historical)` when target type is a trait
    - Reuse existing `MIST_ABSTRACT_INSTANTIATION (historical)` for abstract classes
    - Reuse existing constructor resolution logic for argument/parameter type matching
    - Define new diagnostic IDs in the appropriate diagnostics file
    - _Requirements: 4.1, 4.2, 4.3, 4.4, 4.5, 4.6, 5.1, 5.2, 5.3, 5.4_

  - [x] 5.2 Write unit tests for type checker diagnostics
    - Create `Test/Mist.Tests/Semantics/TargetTypedNewDiagnosticTests.cs`
    - Test `var x = new()` → `MIST_TARGET_NEW_WITH_VAR (historical)`
    - Test `new()` in expression statement → `MIST_TARGET_NEW_NO_CONTEXT (historical)`
    - Test interface target → `MIST_TARGET_NEW_INTERFACE (historical)`
    - Test trait target → `MIST_TARGET_NEW_TRAIT (historical)`
    - Test abstract class target → `MIST_ABSTRACT_INSTANTIATION (historical)`
    - Test constructor argument count mismatch → existing error
    - In-process (compile with shared driver, inspect diagnostics)
    - _Requirements: 4.3, 4.4, 4.5, 4.6, 5.1, 5.2_

  - [x] 5.3 Write property test for constructor validation (Property 6)
    - **Property 6: Constructor validation**
    - **Validates: Requirements 4.1, 4.2**
    - Create `Test/Mist.Tests/Semantics/TargetTypedNewConstructorPropertyTests.cs`
    - Generate types with varying constructor arities and argument combinations
    - Verify target-typed new accepts if and only if a matching accessible constructor exists
    - In-process (compile with shared driver, inspect diagnostics)

- [x] 6. Checkpoint - Ensure semantic model and type checker work
  - Ensure all tests pass, ask the user if questions arise.

- [x] 7. C# emitter — fully-typed output
  - [x] 7.1 Implement emitter cases for target-typed new expression nodes
    - Add cases for `TargetTypedNewExpressionPrimaryNode` and `TargetTypedNewExpressionNoArgsPrimaryNode` in `Source/Mist/Backends/CSharpTranspiler/CSharpEmitter.cs`
    - Resolve the expression type from the semantic model
    - Emit `new TypeName(args)` using `CSharpTypeMap.GetTypeName` for the resolved type
    - Emit property initializer block if present: `new TypeName(args) { Prop = value }`
    - For `TargetTypedNewExpressionNoArgsPrimaryNode` — emit `new TypeName() { ... }`
    - _Requirements: 6.1, 6.2, 6.3_

  - [x] 7.2 Write property test for emission equivalence (Property 5)
    - **Property 5: Emission equivalence**
    - **Validates: Requirements 6.1, 6.2, 6.3**
    - Create `Test/Mist.Tests/Backend/TargetTypedNewEmissionPropertyTests.cs`
    - Generate pairs of programs: one using `T x = new(args)` and one using `T x = new T(args)`
    - Compile both in-process and compare emitted C# for the relevant lines
    - In-process (compile with shared driver, compare emitter output)

- [x] 8. Sample application
  - [x] 8.1 Create the TargetTypedNew sample project
    - Create `samples/TargetTypedNew/Program.mist` containing the proof code verbatim from Requirement 8
    - The sample uses Allman braces and `var` where appropriate (per sample style policy)
    - The proof code is immutable — copy it exactly as specified in the requirements
    - Create `samples/TargetTypedNew/TargetTypedNew.mistproj` project file
    - _Requirements: 8.1, 8.2, 8.3, 8.4, 8.5, 8.7_

  - [x] 8.2 Integrate into AllFeatures sample
    - Add `TargetTypedNew.Program.RunDemo()` call to `samples/AllFeatures/Program.mist`
    - Add the project reference to the AllFeatures project file
    - _Requirements: 8.6_

  - [x] 8.3 Update sample baselines
    - Run the sample and verify output matches expected output from Requirement 8
    - Update `samples/__baselines__/` if the project uses baseline-based verification
    - _Requirements: 8.7_

- [x] 9. Checkpoint - Ensure sample compiles and produces correct output
  - Ensure all tests pass, ask the user if questions arise.

- [x] 10. Book documentation
  - [x] 10.1 Create book snippets for target-typed new
    - Create `book/snippets/object-model/target-typed-new/` directory
    - Create `.mist` snippet files demonstrating: syntax, supported contexts, generic types, and error cases
    - Create corresponding empty `.cs` files for each snippet
    - Run `dotnet run --project tools/SnippetEmitter` to generate the emitted C# output
    - _Requirements: 9.3_

  - [x] 10.2 Write the book chapter for target-typed new expressions
    - Create `book/src/object-model/target-typed-new.md`
    - Document syntax, supported contexts (field, local, return, argument, assignment), constructor resolution, generic type inference, and error cases
    - Use `{{#include}}` references with tabbed format for all code examples
    - _Requirements: 9.1, 9.2_

  - [x] 10.3 Update book SUMMARY.md
    - Add `- [Target-Typed New](object-model/target-typed-new.md)` to the Object Model section of `book/src/SUMMARY.md`
    - _Requirements: 9.4_

- [x] 11. TextMate grammar and feature tracking
  - [x] 11.1 Verify TextMate grammar handles target-typed new
    - Confirm `Editor/mist-syntax-highlighting/syntaxes/mist.tmLanguage.json` already highlights `new` regardless of what follows it
    - The existing `\\bnew\\b` pattern covers `new(...)` — no changes expected
    - If changes are needed, update the grammar to ensure `new` is highlighted as `keyword.operator.new.mist` when followed directly by `(`
    - _Requirements: 7.1, 7.2_

  - [x] 11.2 Update CSHARP_FEATURES.md
    - Mark `Target-typed new()` as ✅ Implemented in the `Expressions — Object Creation` section
    - Update the `Summary` table at the bottom with new implementation counts
    - _Requirements: 10.1, 10.2_

- [x] 12. Final checkpoint - Ensure all tests pass and build has zero warnings
  - Ensure all tests pass, ask the user if questions arise.
  - Verify the build completes with zero warnings.

## Notes

- Tasks marked with `*` are optional and can be skipped for faster MVP
- Each task references specific requirements for traceability
- Checkpoints ensure incremental validation
- Property tests validate universal correctness properties from the design document
- Unit tests validate specific examples and edge cases
- All property tests (1, 2, 3, 4, 5, 6) are **in-process** — they use the shared `CompilerDriver` to compile and inspect the semantic model or emitted output without spawning external processes
- The proof code in Requirement 8 is **immutable** — copy verbatim; if it doesn't compile, fix the compiler
- Sample app uses Allman braces and `var` for locals where type is obvious from context
- Book snippets use the compiled snippet policy — `.mist` + `.cs` pairs processed by SnippetEmitter

## Task Dependency Graph

```json
{
  "waves": [
    { "id": 0, "tasks": ["1.1"] },
    { "id": 1, "tasks": ["1.2", "2.1"] },
    { "id": 2, "tasks": ["2.2", "4.1"] },
    { "id": 3, "tasks": ["4.2", "4.3", "5.1"] },
    { "id": 4, "tasks": ["5.2", "5.3", "7.1"] },
    { "id": 5, "tasks": ["7.2", "8.1", "11.1"] },
    { "id": 6, "tasks": ["8.2", "8.3", "10.1"] },
    { "id": 7, "tasks": ["10.2", "10.3", "11.2"] }
  ]
}
```
