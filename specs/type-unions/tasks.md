# Implementation Plan: Type Unions

## Overview

This plan implements TypeScript-style type unions (`int | string`) across the full Mist compiler pipeline: grammar, lexer, parser, pretty-printer, semantic model, type checker, C# emitter, syntax highlighting, sample app, and documentation. Each task builds incrementally on the previous, wiring components together as they are completed.

## Tasks

- [x] 1. Add PIPE token and UnionTypeRef grammar rule
  - [x] 1.1 Add the `PIPE` token to `Source/Mist/Grammar/Mist.storm`
    - Declare `token PIPE = "|";` before `LOGICAL_OR` so maximal-munch selects `||` over `| |`
    - _Requirements: 1.1, 1.2, 1.3_
  - [x] 1.2 Add `UnionTypeRef` and `SingleTypeRef` rules to the grammar
    - Add `UnionTypeRef` as the first alternative in the `TypeReference` rule
    - Define `rule UnionTypeRef = first:SingleTypeRef (PIPE rest:SingleTypeRef)+;`
    - Define `rule SingleTypeRef` mirroring `TypeReference` without the union alternative, plus `ParenthesizedTypeRef`
    - _Requirements: 2.1, 2.2, 2.3, 2.4, 2.5_
  - [x] 1.3 Rebuild the solution and verify the Storm source generator produces `UnionTypeRefNode` and `SingleTypeRefNode` AST types
    - Ensure zero warnings after grammar changes
    - _Requirements: 2.4_

- [x] 2. Extend the lexer and parser for union types
  - [x] 2.1 Verify lexer produces PIPE tokens correctly
    - Write a focused unit test confirming single `|` produces PIPE and `||` produces LOGICAL_OR
    - _Requirements: 1.2, 1.3_
  - [x] 2.2 Write property test for lexer maximal munch (Property 1)
    - **Property 1: Lexer Maximal Munch — PIPE vs LOGICAL_OR**
    - Generate random strings containing `|` and `||`; verify PIPE vs LOGICAL_OR token production
    - In-process lexer, no process spawning
    - **Validates: Requirements 1.2, 1.3**
  - [x] 2.3 Verify parser produces UnionTypeRefNode for union type annotations
    - Write unit tests parsing `int | string`, `int | string | bool`, `int[] | string`, `(int | string)[]`
    - Confirm constituent count and ordering in the AST
    - _Requirements: 2.1, 2.2, 2.3, 2.4, 3.1, 3.2, 3.3, 3.4, 3.5_
  - [x] 2.4 Write property test for union type parsing (Property 2)
    - **Property 2: Union Type Parsing Correctness**
    - Generate random valid union type strings with N constituents; verify AST has exactly N elements in source order
    - In-process parser, no process spawning
    - **Validates: Requirements 2.2, 2.3, 2.4, 3.1, 3.2, 3.3, 3.4, 3.5**

- [x] 3. Extend the pretty-printer for union types
  - [x] 3.1 Add `UnionTypeRef` case to `MistPrettyPrinter.EmitTypeReference`
    - Handle `UnionTypeRefTypeReferenceNode` by joining constituents with ` | `
    - Add `EmitSingleTypeRef` helper method for the `SingleTypeRef` node variants
    - _Requirements: 4.1, 4.2, 4.3, 4.4_
  - [x] 3.2 Write property test for pretty-printer formatting (Property 3)
    - **Property 3: Pretty-Printer Formatting**
    - Generate random UnionTypeRefNode ASTs; verify output has exactly one space before and after each `|`
    - In-process pretty-printer, no process spawning
    - **Validates: Requirements 4.1, 4.2, 4.3, 4.4**
  - [x] 3.3 Write property test for pretty-printer round-trip (Property 4)
    - **Property 4: Pretty-Printer Round-Trip**
    - Generate valid Mist source with union types; parse → print → parse → compare ASTs
    - In-process parser + pretty-printer, no process spawning
    - **Validates: Requirements 4.5**

- [x] 4. Checkpoint — Grammar, lexer, parser, and pretty-printer
  - Ensure all tests pass, ask the user if questions arise.

- [x] 5. Implement the UnionType semantic model class
  - [x] 5.1 Create `UnionType` class in `Source/Mist/Semantics/Model/Types/`
    - Implement `IType` with `Constituents`, `DisplayName`, `Kind` properties
    - Canonical ordering: sort constituents by `DisplayName`
    - Structural equality: same constituents (regardless of order) = same type
    - _Requirements: 5.1, 5.6_
  - [x] 5.2 Extend `SemanticModelBuilder` to resolve `UnionTypeRefNode` to `UnionType`
    - Resolve each constituent type reference
    - Flatten nested unions (inline constituents of nested UnionType)
    - Deduplicate by DisplayName equality
    - Reduce single-constituent result to the type itself
    - Handle `ErrorType` for unresolved constituents
    - _Requirements: 5.1, 5.2, 5.3, 5.4, 5.5, 5.6_
  - [x] 5.3 Write property test for semantic model resolution (Property 5)
    - **Property 5: Semantic Model Resolution with Flattening and Deduplication**
    - Generate nested/duplicate union type references; verify flattening and deduplication
    - In-process semantic model builder, no process spawning
    - **Validates: Requirements 5.1, 5.2, 5.3, 5.4, 5.6**
  - [x] 5.4 Write property test for union type order equivalence (Property 6)
    - **Property 6: Union Type Order Equivalence**
    - Generate permutations of the same type set; verify all resolve to the same UnionType
    - In-process semantic model builder, no process spawning
    - **Validates: Requirements 6.5**

- [x] 6. Extend the type checker for union types
  - [x] 6.1 Implement assignability rules for union types in `TypeCheckerAnalyzer`
    - Constituent → union: valid
    - Union → wider union (all constituents present): valid
    - Union → narrower union (missing constituents): error MIST_TC_0055
    - Non-constituent → union: error MIST_TC_0050
    - Union → non-union without narrowing: error MIST_TC_0051
    - Order-independent equality for same-constituent unions
    - _Requirements: 6.1, 6.2, 6.3, 6.4, 6.5, 6.6_
  - [x] 6.2 Implement member access validation on union types
    - Allow access if member exists on ALL constituents with compatible signatures
    - Result type: union of member return types (or single type if all identical)
    - Emit MIST_TC_0052 if member missing on any constituent
    - _Requirements: 7.1, 7.2, 7.3_
  - [x] 6.3 Implement pattern matching validation for union types
    - Type pattern with constituent type: valid, introduce variable with constituent type
    - Type pattern with non-constituent: warning MIST_TC_0053
    - Var pattern: introduce variable with full union type
    - Degenerate union warning MIST_TC_0054
    - _Requirements: 8.1, 8.2, 8.3, 8.4, 12.1, 12.5_
  - [x] 6.4 Write property tests for type checker (Properties 7–12)
    - **Property 7: Constituent Assignability**
    - **Property 8: Non-Constituent Rejection**
    - **Property 9: Common Member Access**
    - **Property 10: Missing Member Rejection**
    - **Property 11: Pattern Matching Narrows to Constituent Type**
    - **Property 12: Non-Constituent Pattern Warning**
    - In-process type checker, no process spawning
    - **Validates: Requirements 6.1, 6.2, 6.3, 6.4, 7.1, 7.2, 7.3, 8.1, 8.2, 8.3, 12.2, 12.3, 12.4, 12.5**

- [x] 7. Checkpoint — Semantic model and type checker
  - Ensure all tests pass, ask the user if questions arise.

- [x] 8. Implement C# emitter support for union types
  - [x] 8.1 Implement helper type generation in the emitter
    - Generate `Union_{Type1}_{Type2}` struct with `_tag`, `_value`, `Is*`, `As*` properties, and implicit conversion operators
    - Naming convention: constituent C# type names sorted alphabetically, joined by `_`
    - Deduplicate: maintain dictionary keyed by helper type name
    - Emit all helper types into a single `UnionHelpers.cs` file
    - _Requirements: 9.1, 9.2, 9.3, 9.4, 9.5, 9.6_
  - [x] 8.2 Emit union type usage (variables, parameters, return types, fields)
    - Replace union type references with the generated helper type name
    - Rely on implicit conversion operators for constituent-to-union assignment
    - _Requirements: 10.1, 10.2, 10.3, 10.4, 10.5_
  - [x] 8.3 Emit pattern matching on union types
    - `is T t` on union → check `Is{T}` property, extract via `As{T}`
    - `is var v` on union → bind the helper type instance directly
    - _Requirements: 10.6, 11.1, 11.2, 11.3_
  - [x] 8.4 Write property test for helper type structural correctness (Property 13)
    - **Property 13: Helper Type Structural Correctness**
    - Generate random union types with N constituents; verify emitted helper struct has correct tag, Is*, As*, and implicit operators
    - In-process compilation via shared CompilerDriver, no per-iteration process spawning
    - **Validates: Requirements 9.1, 9.2, 9.3, 9.4, 9.5, 9.6**
  - [x] 8.5 Write property test for emitted C# semantic equivalence (Property 14)
    - **Property 14: Emitted C# Semantic Equivalence**
    - Batched pattern: generate N scenarios, build single Mist program exercising all N, compile once (in-process), run once (1 process spawn), compare N output lines
    - Process spawns: ≤1 per test run (dotnet exec on compiled DLL)
    - **Validates: Requirements 10.1, 10.2, 10.3, 10.4, 10.5, 10.6, 11.1, 11.2, 11.3**

- [x] 9. Checkpoint — Emitter complete
  - Ensure all tests pass, ask the user if questions arise.

- [x] 10. Add syntax highlighting for union types
  - [x] 10.1 Update `Editor/mist-syntax-highlighting/syntaxes/mist.tmLanguage.json`
    - Add pattern matching `|` in type contexts with scope `keyword.operator.type.union.mist`
    - Use lookbehind/lookahead to distinguish from logical OR `||`
    - _Requirements: 13.1, 13.2_

- [x] 11. Create the TypeUnions sample application
  - [x] 11.1 Create `Samples/TypeUnions/` directory with `mist.toml` and `Program.mist`
    - Demonstrate: union-typed variables, parameters, return types, type arguments, pattern matching
    - Expose `public static void RunDemo()` method with `Console.WriteLine` output
    - _Requirements: 15.1, 15.2, 15.3, 15.4, 15.5, 15.6, 15.7, 15.8_
  - [x] 11.2 Integrate into AllFeatures
    - Add `[[references]] path = "../TypeUnions"` to `Samples/AllFeatures/mist.toml`
    - Add `TypeUnions.Program.RunDemo()` call in `Samples/AllFeatures/Program.mist`
    - _Requirements: 15.9, 15.10_

- [x] 12. Update CSHARP_FEATURES.md documentation
  - Add `Type Unions` entry marking it as implemented
  - Update the Summary table counts
  - _Requirements: 14.1, 14.2_

- [x] 13. Add error diagnostics unit tests
  - Write unit tests verifying diagnostic messages for:
    - MIST_TC_0050: non-constituent assigned to union
    - MIST_TC_0051: union assigned to non-union without narrowing
    - MIST_TC_0052: member not present on all constituents
    - MIST_TC_0053: pattern type not a constituent (warning)
    - MIST_TC_0054: degenerate union (warning)
    - MIST_TC_0055: union-to-union with incompatible constituents
    - MIST_TC for unresolved constituent type
  - _Requirements: 12.1, 12.2, 12.3, 12.4, 12.5, 12.6_

- [x] 14. Final checkpoint — Full integration
  - Ensure all tests pass (including AllFeatures integration), zero warnings, ask the user if questions arise.

## Notes

- Tasks marked with `*` are optional and can be skipped for faster MVP
- Each task references specific requirements for traceability
- Checkpoints ensure incremental validation
- Property tests validate universal correctness properties from the design document
- All property tests use FsCheck with `MaxTest = 100` minimum
- All property tests use in-process compilation (shared `CompilerDriver`) — no per-iteration process spawning
- The emitter property test (Property 14) uses the batched pattern: 1 compile, 1 run, N output lines
- Zero warnings policy applies to all tasks
