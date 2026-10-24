# Implementation Plan: Disposable-Drop Interop

## Overview

This plan implements automatic IDisposable-to-Drop interop for the Mist compiler. CLR types implementing `System.IDisposable` are recognized as droppable during type projection, enabling seamless use in `with` blocks. The emitter emits `.Dispose()` instead of `.Drop()` for these types. The implementation touches four compiler stages: type model (new property), type projection (detection), synthetic drop inference (exclusion), and C# emission (dispatch).

## Tasks

- [x] 1. Type model — Add `IsDisposableDrop` property
  - [x] 1.1 Add `IsDisposableDrop` property to `ClassType`
    - Add `public bool IsDisposableDrop { get; internal set; }` to `ClassType.cs` after the existing `HasDrop` / `DropReturnType` properties
    - Include XML doc comment explaining the property's purpose
    - Default value is `false` (no initialization needed for value types)
    - _Requirements: 1.2, 2.1_

  - [x] 1.2 Add `IsDisposableDrop` property to `StructType`
    - Add `public bool IsDisposableDrop { get; internal set; }` to `StructType.cs` after the existing `HasDrop` / `DropReturnType` properties
    - Include XML doc comment explaining the property's purpose
    - _Requirements: 1.2, 2.1_

- [x] 2. Type projection — Detect IDisposable and set flags
  - [x] 2.1 Add IDisposable detection helper to `TypeProjector`
    - Add a `private static bool ImplementsIDisposable(Type clrType)` helper method
    - Use `clrType.GetInterfaces().Any(i => i.FullName == "System.IDisposable")` to detect IDisposable (covers transitive implementation via `GetInterfaces()`)
    - Wrap in try/catch for corrupted assembly metadata — return `false` on exception (consistent with existing `IsRefStruct` pattern)
    - _Requirements: 1.1, 1.4_

  - [x] 2.2 Add explicit Drop method detection helper to `TypeProjector`
    - Add a `private static bool HasExplicitDropMethod(Type clrType)` helper method
    - Check for a public parameterless instance method named "Drop" on the CLR type
    - This is needed to determine precedence: explicit Drop wins over IDisposable
    - _Requirements: 2.2_

  - [x] 2.3 Integrate IDisposable detection into `ProjectClass`
    - After the `ProjectInterfaces` call and synthesize steps in `ProjectClass`, add IDisposable detection logic
    - If `ImplementsIDisposable(clrType)` is true AND `HasExplicitDropMethod(clrType)` is false: set `classType.HasDrop = true` and `classType.IsDisposableDrop = true`
    - If `ImplementsIDisposable(clrType)` is true AND `HasExplicitDropMethod(clrType)` is true: set `classType.HasDrop = true` and `classType.IsDisposableDrop = false`
    - If `ImplementsIDisposable(clrType)` is false: leave both flags unchanged
    - _Requirements: 1.1, 1.2, 1.3, 1.5, 2.2, 6.1, 6.2_

  - [x] 2.4 Integrate IDisposable detection into `ProjectStruct`
    - After the `ProjectInterfaces` call and synthesize steps in `ProjectStruct`, add IDisposable detection logic (same logic as 2.3)
    - _Requirements: 1.1, 1.2, 1.3, 1.5, 2.2, 6.1, 6.2_

  - [x] 2.5 Write unit tests for TypeProjector IDisposable detection
    - Test: `System.IO.MemoryStream` → `HasDrop=true`, `IsDisposableDrop=true`
    - Test: `System.String` → `HasDrop=false`, `IsDisposableDrop=false`
    - Test: `System.IO.Stream` (abstract, transitive IDisposable) → `HasDrop=true`, `IsDisposableDrop=true`
    - Test: generic IDisposable type → flags set correctly on generic definition
    - Test: Type with only `IAsyncDisposable` → `HasDrop=false`
    - Test: Type with both `IAsyncDisposable` and `IDisposable` → recognized via IDisposable
    - Add tests to `Test/Mist.Tests/Backend/TypeProjectorTests.cs`
    - _Requirements: 1.1, 1.2, 1.3, 1.4, 1.5, 6.1, 6.2_

- [x] 3. Synthetic drop inference — Exclude IDisposable fields
  - [x] 3.1 Modify `SyntheticDropInferenceEngine.IsDroppable` to exclude `IsDisposableDrop` types
    - Add cases at the top of the `switch` in `IsDroppable`:
      - `case ClassType ct when ct.IsDisposableDrop: return false;`
      - `case StructType st when st.IsDisposableDrop: return false;`
    - This prevents IDisposable CLR fields from triggering synthetic drop cascading on containing Mist types
    - _Requirements: 5.1, 5.2, 5.3_

  - [x] 3.2 Write property test for synthetic drop inference exclusion
    - **Property 2: Synthetic drop inference exclusion**
    - Generate random Mist type configurations with N fields, each having random `(HasDrop, IsDisposableDrop)` settings
    - Run `IsDroppable` filter logic directly (in-process, no process spawning)
    - Assert: no field with `IsDisposableDrop=true` appears in the synthesized drop field list
    - Assert: all fields with `HasDrop=true` AND `IsDisposableDrop=false` DO appear
    - Minimum 100 FsCheck iterations
    - **Validates: Requirements 5.1, 5.2, 5.3**

  - [x] 3.3 Write unit tests for synthetic drop inference exclusion
    - Test: Mist type with only an IDisposable CLR field → no synthetic Drop synthesized
    - Test: Mist type with IDisposable CLR field + explicit Mist Drop field → only explicit field appears in synthetic drop list
    - Test: Mist type with multiple fields, mix of IDisposable and explicit drops → correct field list
    - _Requirements: 5.1, 5.2, 5.3_

- [x] 4. Checkpoint - Ensure type model and inference tests pass
  - Ensure all tests pass, ask the user if questions arise.

- [x] 5. C# emitter — Emit `.Dispose()` for IDisposable types
  - [x] 5.1 Add `IsDisposableDrop` helper method to `CSharpEmitter`
    - Add a `private static bool IsDisposableDrop(IType type)` helper method
    - Resolve through `ConstructedType` and `TypeAliasType` to check the flag on the underlying definition:
      ```csharp
      private static bool IsDisposableDrop(IType type) => type switch
      {
          ConstructedType ct => IsDisposableDrop(ct.GenericDefinition),
          TypeAliasType alias => IsDisposableDrop(alias.UnderlyingType),
          ClassType c => c.IsDisposableDrop,
          StructType s => s.IsDisposableDrop,
          _ => false,
      };
      ```
    - _Requirements: 3.1_

  - [x] 5.2 Modify `EmitChainedDropExpression` to dispatch `.Dispose()` vs `.Drop()`
    - In the non-task-like branch, replace `$"{baseExpr}.Drop()"` with:
      ```csharp
      currentExpr = IsDisposableDrop(type)
          ? $"{baseExpr}.Dispose()"
          : $"{baseExpr}.Drop()";
      ```
    - In the while-loop chaining branch (non-task-like), apply same logic for the `dropResultType`
    - _Requirements: 3.1, 3.2, 3.4_

  - [x] 5.3 Verify fallback paths in `EmitDroppableLocalWithWrapping`
    - The fallback string `$"{localName}.Drop()"` path fires only when `declType` is null (which won't happen for properly-projected CLR types)
    - Add a code comment explaining why no change is needed for the null-declType fallback
    - _Requirements: 3.2_

  - [x] 5.4 Write property test for emitter dispatch correctness
    - **Property 1: Emitter dispatch correctness**
    - Generate random type configurations: `(HasDrop=true, IsDisposableDrop: bool)`
    - For each, construct a minimal `ClassType`/`StructType` with appropriate flags
    - Call `EmitChainedDropExpression` (or test the `IsDisposableDrop` helper + string building logic)
    - Assert: output contains `.Dispose()` iff `IsDisposableDrop=true`; output contains `.Drop()` iff `IsDisposableDrop=false`
    - In-process testing, no process spawning. Minimum 100 FsCheck iterations
    - **Validates: Requirements 2.3, 3.1, 3.2**

  - [x] 5.5 Write unit tests for emitter dispatch
    - Test: type with `IsDisposableDrop=true` → emitted string contains `.Dispose()`
    - Test: type with `IsDisposableDrop=false`, `HasDrop=true` → emitted string contains `.Drop()`
    - Test: `ConstructedType` wrapping an `IsDisposableDrop=true` definition → `.Dispose()` emitted
    - Test: `TypeAliasType` wrapping an `IsDisposableDrop=true` type → `.Dispose()` emitted
    - _Requirements: 3.1, 3.2, 3.4_

- [x] 6. Checkpoint - Ensure emitter tests pass
  - Ensure all tests pass, ask the user if questions arise.

- [x] 7. Interop fixture and integration tests
  - [x] 7.1 Add IDisposable fixture types to `Mist.Tests.InteropFixtures`
    - Create `DisposableFixtures.cs` in `Test/Mist.Tests.InteropFixtures/`
    - Add fixture types:
      - `SimpleDisposable` — implements `IDisposable` directly
      - `TransitiveDisposable` — implements an interface that extends `IDisposable`
      - `DisposableWithDrop` — implements `IDisposable` AND has a public parameterless `Drop()` method
      - `AsyncOnlyDisposable` — implements only `IAsyncDisposable`
      - `BothDisposables` — implements both `IDisposable` and `IAsyncDisposable`
      - `GenericDisposable<T>` — generic type implementing `IDisposable`
    - _Requirements: 1.4, 2.2, 6.1, 6.2_

  - [x] 7.2 Write integration tests for end-to-end IDisposable with-block compilation
    - Test: Mist code using `SimpleDisposable` in a `with` block compiles and emitted C# contains `.Dispose()`
    - Test: Mist code using `DisposableWithDrop` in a `with` block emits `.Drop()` (not `.Dispose()`)
    - Test: Mist type implementing IDisposable without Drop() is NOT usable in `with` block
    - Uses batched compilation pattern (1 compile, 1 run per test)
    - _Requirements: 3.3, 7.1, 7.2, 7.3_

- [x] 8. Sample application
  - [x] 8.1 Create sample project at `samples/DisposableDropInterop/Program.mist`
    - Include the proof code from Requirement 8 verbatim
    - Implement `public static void RunDemo()` entry point
    - Cover all permutations: IDisposable CLR type in with-block, with-declaration form, explicit Drop precedence, Mist type with IDisposable but no Drop
    - Verify expected stdout output matches the specification exactly
    - _Requirements: 8.1, 8.2, 8.3, 8.5_

  - [x] 8.2 Create `samples/DisposableDropInterop/DisposableDropInterop.mistproj` project file
    - Configure project file referencing the standard library
    - _Requirements: 8.5_

  - [x] 8.3 Integrate into `samples/AllFeatures/Program.mist`
    - Add `DisposableDropInterop.Program.RunDemo()` call to the AllFeatures orchestrator
    - Verify AllFeatures sample compiles and runs without errors
    - _Requirements: 8.4_

- [x] 9. Checkpoint - Ensure sample compiles and runs with expected output
  - Ensure all tests pass, ask the user if questions arise.

- [x] 10. Book documentation
  - [x] 10.1 Add "IDisposable Interop" section to `book/src/advanced/drop-semantics.md`
    - Add new section after the existing "Interaction with Ownership" section
    - Explain how CLR types implementing `IDisposable` are automatically droppable in `with` blocks
    - Explain `.Dispose()` emission vs `.Drop()` emission
    - Explain precedence: explicit `Drop()` wins over IDisposable
    - Explain that Mist types implementing IDisposable are NOT automatically droppable
    - _Requirements: 9.1, 9.2, 9.3, 9.4_

  - [x] 10.2 Create compiled book snippets
    - Create `.mist` snippet files in `book/snippets/advanced/drop-semantics/` demonstrating:
      - CLR IDisposable type in a `with` block (with emitted C# showing `try/finally` + `.Dispose()`)
      - Type with both `Drop()` and `IDisposable` showing `Drop()` is emitted
    - Generate corresponding `.cs` files via SnippetEmitter tool
    - _Requirements: 9.2, 9.3, 9.5_

- [x] 11. Final checkpoint - Ensure all tests pass and samples run correctly
  - Ensure all tests pass, ask the user if questions arise.

## Notes

- Tasks marked with `*` are optional and can be skipped for faster MVP
- Each task references specific requirements for traceability
- Checkpoints ensure incremental validation
- Property tests validate universal correctness properties from the design document
- Unit tests validate specific examples and edge cases
- The compiler is written in C# — all implementation tasks target C# code
- Property-based tests use FsCheck (already in the project) with minimum 100 iterations
- Both property tests operate in-process without spawning external processes (testing pure logic of emitter helper and inference filter)
- Integration tests that compile and run Mist programs follow the batched pattern (1 compile, 1 run)
- The proof code in Requirement 8 is immutable — it must appear verbatim in the sample app
- No new diagnostics are introduced; existing drop analyzer diagnostics apply unchanged
- No TextMate grammar update needed — this feature introduces no new keywords or syntax

## Task Dependency Graph

```json
{
  "waves": [
    { "id": 0, "tasks": ["1.1", "1.2"] },
    { "id": 1, "tasks": ["2.1", "2.2"] },
    { "id": 2, "tasks": ["2.3", "2.4"] },
    { "id": 3, "tasks": ["2.5", "3.1"] },
    { "id": 4, "tasks": ["3.2", "3.3", "5.1"] },
    { "id": 5, "tasks": ["5.2", "5.3"] },
    { "id": 6, "tasks": ["5.4", "5.5", "7.1"] },
    { "id": 7, "tasks": ["7.2", "8.1", "8.2"] },
    { "id": 8, "tasks": ["8.3", "10.1"] },
    { "id": 9, "tasks": ["10.2"] }
  ]
}
```
