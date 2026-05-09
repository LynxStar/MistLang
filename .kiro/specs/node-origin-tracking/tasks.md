# Implementation Plan: Node Origin Tracking

## Overview

This plan replaces the span-containment heuristic for resolving AST node source locations with a direct `NodeOrigin` property on every `SyntaxNode`. The implementation proceeds bottom-up: define the type hierarchy, emit the property from the source generator, assign origins during parsing, build the `DiagnosticHelper` utility, then migrate consumers and remove the old heuristic methods.

## Tasks

- [x] 1. Define NodeOrigin type hierarchy
  - [x] 1.1 Create NodeOrigin records in `Source/Mist/Common/SourceText/`
    - Create a new file `NodeOrigin.cs` in `Source/Mist/Common/SourceText/`
    - Define `abstract record NodeOrigin` in namespace `Mist.Common.Text`
    - Define `sealed record ParsedOrigin(SourceText Source) : NodeOrigin`
    - Define `sealed record SyntheticOrigin(NodeOrigin DerivedFrom, string Reason) : NodeOrigin` with `ArgumentException` guard for empty/null `Reason`
    - Define `sealed record TestOrigin : NodeOrigin` as a singleton with `public static readonly TestOrigin Instance = new()` and private constructor
    - _Requirements: 1.1, 1.2, 1.3, 1.4, 1.5_

  - [x] 1.2 Write property test for NodeOrigin constructor invariants
    - **Property 1: NodeOrigin Constructor Invariants**
    - **Validates: Requirements 1.2, 1.3**
    - Create `Test/Mist.Tests/Common/SourceText/NodeOriginConstructionPropertyTests.cs`
    - Use FsCheck to generate random `SourceText` instances and verify `ParsedOrigin.Source` is reference-equal
    - Generate random `NodeOrigin` + non-empty strings and verify `SyntheticOrigin` fields
    - Verify `SyntheticOrigin` throws `ArgumentException` for empty reason

  - [x] 1.3 Write unit tests for NodeOrigin edge cases
    - Create `Test/Mist.Tests/Common/SourceText/NodeOriginTests.cs`
    - Test `SyntheticOrigin` with empty reason throws `ArgumentException`
    - Test `TestOrigin.Instance` is a singleton (reference equality)
    - _Requirements: 1.3, 1.4_

- [x] 2. Emit Origin property on SyntaxNode base class
  - [x] 2.1 Modify `AstEmitter.EmitRuntimeTypes()` to emit Origin property
    - In `Source/Storm.SourceGenerator/Emit/AstEmitter.cs`, inside the `SyntaxNode` class block
    - Add `writer.WriteLine("public Mist.Common.Text.NodeOrigin? Origin { get; set; }");` after the existing `Span` property
    - Ensure the generated code compiles with zero warnings (nullable annotation is intentional for transition period)
    - _Requirements: 2.1, 2.2, 2.3_

  - [x] 2.2 Write unit test for AstEmitter Origin property emission
    - Create `Test/Mist.Tests/Common/SourceText/AstEmitterOriginPropertyTests.cs`
    - Call `AstEmitter.EmitRuntimeTypes()` and assert the output contains `NodeOrigin? Origin { get; set; }`
    - _Requirements: 2.1_

  - [x] 2.3 Write property test for Origin property round-trip
    - **Property 2: Origin Property Round-Trip**
    - **Validates: Requirements 2.2**
    - Create `Test/Mist.Tests/Common/SourceText/OriginPropertyRoundTripTests.cs`
    - Use FsCheck to generate random `NodeOrigin` variants, assign to concrete `SyntaxNode` subclasses, read back and verify equality

- [x] 3. Checkpoint - Ensure all tests pass
  - Ensure all tests pass, ask the user if questions arise.

- [x] 4. Create DiagnosticHelper utility
  - [x] 4.1 Implement `DiagnosticHelper.ResolveLocation` in `Source/Mist/Common/Diagnostics/`
    - Create a new file `DiagnosticHelper.cs` in `Source/Mist/Common/Diagnostics/`
    - Implement `public static (string Path, LinePositionSpan Location, string? SyntheticContext) ResolveLocation(Storm.Runtime.SyntaxNode node)`
    - Handle `null` origin same as `TestOrigin` — return `("<unknown>", default, null)`
    - Handle `ParsedOrigin` — return `(source.Path, source.ToLinePositionSpan(node.Span), null)` with bounds check on `node.Span`
    - Handle `SyntheticOrigin` — chain through `DerivedFrom` until `ParsedOrigin` or `TestOrigin` is found, accumulate reason strings into `SyntheticContext`
    - Add max-depth guard (100) for circular chain protection
    - If `node.Span` exceeds `SourceText.Contents.Length`, return path but `default` location
    - _Requirements: 4.1, 4.2, 4.3, 4.4_

  - [x] 4.2 Write property test for ParsedOrigin location resolution
    - **Property 4: ParsedOrigin Resolves to Correct Location**
    - **Validates: Requirements 4.1**
    - Create `Test/Mist.Tests/Common/SourceText/DiagnosticLocationResolutionPropertyTests.cs`
    - Generate random `SourceText` + random valid spans within content bounds
    - Verify `ResolveLocation` returns path equal to `ParsedOrigin.Source.Path` and correct `LinePositionSpan`

  - [x] 4.3 Write property test for SyntheticOrigin chain resolution
    - **Property 5: SyntheticOrigin Chain Resolves to Terminal ParsedOrigin**
    - **Validates: Requirements 4.2, 7.3**
    - Create `Test/Mist.Tests/Common/SourceText/SyntheticChainResolutionPropertyTests.cs`
    - Generate random chain depths (1–10), random intermediate reasons, random terminal `ParsedOrigin`
    - Verify `ResolveLocation` returns the terminal `ParsedOrigin`'s path and location

  - [x] 4.4 Write property test for overlapping span resolution
    - **Property 6: Overlapping Spans Resolve Correctly via Origin**
    - **Validates: Requirements 4.4**
    - Create `Test/Mist.Tests/Common/SourceText/OverlappingSpanResolutionPropertyTests.cs`
    - Generate N files with deliberately overlapping span ranges
    - Verify `ResolveLocation` returns the correct file path based on `Origin`, not span containment

  - [x] 4.5 Write property test for TestOrigin safety
    - **Property 7: TestOrigin Diagnostic Creation Never Throws**
    - **Validates: Requirements 6.2**
    - Create `Test/Mist.Tests/Common/SourceText/TestOriginSafetyPropertyTests.cs`
    - Generate random `SourceSpan` values (including zero-length, max int)
    - Verify `ResolveLocation` returns `("<unknown>", default, null)` without throwing

- [x] 5. Assign ParsedOrigin in MistParser
  - [x] 5.1 Add origin assignment to `MistParser.Parse()`
    - In `Source/Mist/Parsing/MistParser.cs`, after the Storm-generated parser returns `root`
    - Add a null check for `tokens.Source` — if null, report `MIST_PARSE_ERROR` diagnostic and return `ParseResult` with null `Root`
    - If `root` is not null, walk the tree recursively and set `Origin = new ParsedOrigin(tokens.Source)` on every node
    - Add private static method `AssignOrigin(Storm.Runtime.SyntaxNode node, ParsedOrigin origin)` that sets `node.Origin = origin` and recurses into `node.Children()`
    - _Requirements: 3.1, 3.2, 3.3_

  - [x] 5.2 Write property test for parser origin assignment
    - **Property 3: Parser Assigns Correct ParsedOrigin to All Nodes**
    - **Validates: Requirements 3.1, 3.2**
    - Create `Test/Mist.Tests/Parsing/ParserOriginAssignmentPropertyTests.cs`
    - Use FsCheck to generate valid Mist source files (use existing `MistProgramGenerator` or similar)
    - Parse in-process via `MistParser`, walk all nodes, verify every node has `ParsedOrigin` with reference-equal `SourceText`
    - No process spawning — use in-process `MistParser` directly

- [x] 6. Checkpoint - Ensure all tests pass
  - Ensure all tests pass, ask the user if questions arise.

- [x] 7. Migrate TypeCheckerAnalyzer to use Origin
  - [x] 7.1 Replace `MakeDiagnostic` span-containment logic with `DiagnosticHelper.ResolveLocation`
    - In `Source/Mist/Analysis/Builtin/TypeCheckerAnalyzer.cs`
    - Locate the `MakeDiagnostic` and `MakeDiagnosticWarning` private methods
    - Replace the span-containment loop (iterating ASTs to find the file) with a call to `DiagnosticHelper.ResolveLocation(node)`
    - Use the returned `Path` for `CompilationUnitPath` and `Location` for the diagnostic's `Location`
    - Remove any parameters that passed AST collections solely for span-containment lookup (if applicable)
    - Ensure zero warnings after the change
    - _Requirements: 5.3, 5.4_

  - [x] 7.2 Write unit test for TypeChecker multi-file correct attribution
    - Create `Test/Mist.Tests/Analysis/TypeCheckerOriginAttributionTests.cs`
    - Compile multiple files with overlapping spans in-process
    - Verify diagnostics point to the correct file paths
    - _Requirements: 4.4, 5.3_

- [x] 8. Migrate SemanticModelBuilder to use Origin
  - [x] 8.1 Replace `FindSourceTextForNode` / `FindSourceTextByContainment` call sites with `node.Origin` access
    - In `Source/Mist/Semantics/Model/SemanticModelBuilder.cs`
    - At each call site of `FindSourceTextForNode`, `FindSourceTextByContainment`, `FindSourceTextForClassDecl`, and `FindSourceTextForSymbol`:
      - Replace with `DiagnosticHelper.ResolveLocation(node)` or direct pattern match on `node.Origin` (e.g., `((ParsedOrigin)node.Origin).Source` for text extraction)
    - Ensure all diagnostic creation uses the origin-based path and location
    - _Requirements: 5.1, 5.2_

  - [x] 8.2 Remove `FindSourceTextForNode`, `FindSourceTextByContainment`, `FindSourceTextForClassDecl`, and `FindSourceTextForSymbol` methods
    - Delete the private static methods from `SemanticModelBuilder.cs`
    - Verify no remaining references to these methods exist in the codebase
    - Ensure zero warnings after removal
    - _Requirements: 5.1, 5.2_

  - [x] 8.3 Write integration test verifying span-containment methods are removed
    - Create `Test/Mist.Tests/Semantics/SemanticModelBuilderNoSpanContainmentTests.cs`
    - Use reflection to verify `SemanticModelBuilder` does not contain methods named `FindSourceTextByContainment` or `FindSourceTextForNode`
    - _Requirements: 5.2_

- [x] 9. Final checkpoint - Ensure all tests pass
  - Ensure all tests pass, ask the user if questions arise.

## Notes

- Tasks marked with `*` are optional and can be skipped for faster MVP
- Each task references specific requirements for traceability
- Checkpoints ensure incremental validation
- Property tests validate universal correctness properties from the design document
- Unit tests validate specific examples and edge cases
- All property tests use FsCheck with minimum 100 iterations
- No process spawning in any test — all tests use in-process compilation via `MistParser` and `CompilerDriver`
- The `Origin` property is nullable (`NodeOrigin?`) to maintain backward compatibility during the transition period
- Zero-warnings policy applies to all changes

## Task Dependency Graph

```json
{
  "waves": [
    { "id": 0, "tasks": ["1.1"] },
    { "id": 1, "tasks": ["1.2", "1.3", "2.1"] },
    { "id": 2, "tasks": ["2.2", "2.3", "4.1"] },
    { "id": 3, "tasks": ["4.2", "4.3", "4.4", "4.5", "5.1"] },
    { "id": 4, "tasks": ["5.2", "7.1"] },
    { "id": 5, "tasks": ["7.2", "8.1"] },
    { "id": 6, "tasks": ["8.2"] },
    { "id": 7, "tasks": ["8.3"] }
  ]
}
```
