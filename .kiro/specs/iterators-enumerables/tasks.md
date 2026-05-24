# Implementation Plan: Iterators and Enumerables

## Overview

This plan implements first-class iterator/enumerable support in Mist through two standard library interfaces (`IEnumerator<TElement>` and `IEnumerable<TElement>`), upgrades the `Array` intrinsic to implement `IEnumerable<TValue>`, enhances the type checker to validate `foreach` collection expressions, maps the interfaces to their C# `System.Collections.Generic` counterparts in the emitter, updates the book, and adds a sample application.

## Tasks

- [x] 1. Define Standard Library Interfaces
  - [x] 1.1 Create `Source/Standard/IEnumerator.mist` with the `IEnumerator<TElement>` interface
    - Define `interface IEnumerator<TElement>` in the `Standard` namespace
    - Declare `bool MoveNext()` method
    - Declare `TElement Current { get; }` property
    - _Requirements: 1.1, 1.2, 1.3_

  - [x] 1.2 Create `Source/Standard/IEnumerable.mist` with the `IEnumerable<TElement>` interface
    - Define `interface IEnumerable<TElement>` in the `Standard` namespace
    - Declare associated type slot `type Enumerator : IEnumerator<TElement>`
    - Declare `Enumerator GetEnumerator()` method
    - _Requirements: 2.1, 2.2, 2.3_

  - [x] 1.3 Update `Source/Standard/Bootstrap/Array.mist` to implement `IEnumerable<TValue>`
    - Add `: IEnumerable<TValue>` to the Array class declaration
    - Add intrinsic nested `struct Enumerator : IEnumerator<TValue>` with `MoveNext()` and `Current`
    - Add intrinsic `Enumerator GetEnumerator()` method
    - _Requirements: 5.1, 5.2, 5.3, 5.4_

- [x] 2. Checkpoint - Ensure stdlib files parse correctly
  - Ensure all tests pass, ask the user if questions arise.

- [x] 3. Emitter: Map Mist Iterator Interfaces to C# Counterparts
  - [x] 3.1 Add `IEnumerator` and `IEnumerable` entries to `CSharpTypeMap`
    - Add `["Standard.IEnumerator"] = "System.Collections.Generic.IEnumerator"` to `BackingTypes`
    - Add `["Standard.IEnumerable"] = "System.Collections.Generic.IEnumerable"` to `BackingTypes`
    - _Requirements: 4.1, 4.2_

  - [x] 3.2 Update `CSharpEmitter` to handle `IEnumerable`/`IEnumerator` interface emission without the associated-type-slot-lifted type argument
    - When emitting a type that implements `IEnumerable<TElement>` or `IEnumerator<TElement>`, recognize these as special CLR interfaces and emit them using the standard C# generic form (e.g., `System.Collections.Generic.IEnumerable<int>`) without appending the slot-lifted `Enumerator` type argument
    - The existing `EmitInterfaceWithAssociatedTypeArgs` method lifts associated type slots to extra type parameters — this must be suppressed for `IEnumerable` and `IEnumerator` specifically
    - _Requirements: 4.1, 4.2, 4.5_

  - [x] 3.3 Write property test for emitter interface mapping (Property 6)
    - **Property 6: Emitter maps Mist iterator interfaces to C# System.Collections.Generic counterparts**
    - Generate random types implementing `IEnumerable<TElement>` or `IEnumerator<TElement>`, compile in-process, and verify emitted C# contains `System.Collections.Generic.IEnumerable<T>` / `System.Collections.Generic.IEnumerator<T>` and that foreach is emitted as plain C# `foreach`
    - **Validates: Requirements 4.1, 4.2, 4.3, 4.4, 4.5**

- [x] 4. Type Checker: Foreach Semantic Validation
  - [x] 4.1 Implement `FindIEnumerableImplementation` helper in `TypeCheckerAnalyzer`
    - Add a helper method that searches a type's interface list for `IEnumerable<TElement>`
    - For `ArrayType`: return the element type directly (arrays always implement `IEnumerable<ElementType>`)
    - For other types: iterate interfaces, unwrap `ConstructedType`, match against `IEnumerable` by name + namespace, extract `TElement` from the first type argument
    - Return `null` if not found
    - _Requirements: 3.1, 5.5_

  - [x] 4.2 Enhance `CheckForeachStatement` to validate collection implements `IEnumerable<TElement>`
    - Resolve the collection expression's type
    - Call `FindIEnumerableImplementation` to get `TElement`
    - If not found: emit diagnostic `MIST_TC_FOREACH_NOT_ENUMERABLE` with the resolved type name, bind iteration variable to `ErrorType` for error recovery
    - If found: bind the iteration variable to `TElement`
    - _Requirements: 3.1, 3.2, 3.3_

  - [x] 4.3 Add explicit type annotation validation to `CheckForeachStatement`
    - If the foreach declares an explicit element type, validate assignability from `TElement` to the declared type
    - If not assignable: emit diagnostic `MIST_TC_FOREACH_TYPE_MISMATCH`
    - If assignable: bind the iteration variable to the declared type
    - _Requirements: 3.4, 3.5_

  - [x] 4.4 Write property test for foreach over IEnumerable types (Property 3)
    - **Property 3: Foreach over IEnumerable types compiles with correct element type inference**
    - Generate random types implementing `IEnumerable<TElement>` (including arrays), compile foreach statements in-process, verify zero diagnostics and correct element type resolution
    - **Validates: Requirements 3.1, 3.3, 5.5**

  - [x] 4.5 Write property test for foreach over non-IEnumerable types (Property 4)
    - **Property 4: Foreach over non-IEnumerable types produces a diagnostic**
    - Generate random types that do NOT implement `IEnumerable<TElement>`, compile foreach statements in-process, verify `MIST_TC_FOREACH_NOT_ENUMERABLE` diagnostic is emitted
    - **Validates: Requirements 3.2**

  - [x] 4.6 Write property test for explicit element type annotation (Property 5)
    - **Property 5: Explicit element type annotation compatibility**
    - Generate foreach statements with explicit type annotations (both compatible and incompatible), compile in-process, verify correct diagnostic behavior
    - **Validates: Requirements 3.4, 3.5**

- [x] 5. Checkpoint - Ensure type checker and emitter tests pass
  - Ensure all tests pass, ask the user if questions arise.

- [x] 6. Interface Implementation Validation
  - [x] 6.1 Write property test for valid IEnumerable/IEnumerator implementations (Property 1)
    - **Property 1: Valid IEnumerable/IEnumerator implementations are accepted**
    - Generate types that correctly implement `IEnumerable<TElement>` with proper nested `Enumerator` types, compile in-process, verify zero interface-related diagnostics
    - **Validates: Requirements 1.4, 2.4, 2.5**

  - [x] 6.2 Write property test for invalid IEnumerator/IEnumerable implementations (Property 2)
    - **Property 2: Invalid IEnumerator/IEnumerable implementations produce diagnostics**
    - Generate types that declare `IEnumerator<TElement>` or `IEnumerable<TElement>` but are missing required members, compile in-process, verify at least one diagnostic error is emitted
    - **Validates: Requirements 1.5, 2.4, 2.5**

- [x] 7. Book Documentation Update
  - [x] 7.1 Update `book/src/basics/control-flow.md` foreach section
    - Remove the existing "Note" and "Design intent" blockquotes
    - Update the foreach description to state that the collection expression must implement `IEnumerable<TElement>`
    - Add a new tabbed snippet section demonstrating foreach over a user-defined `IEnumerable<TElement>` type
    - _Requirements: 6.1, 6.2, 6.3_

  - [x] 7.2 Create book snippet for user-defined enumerable foreach
    - Create `book/snippets/basics/control-flow/foreach-enumerable.mist` — a complete compilable program demonstrating foreach over a user-defined `IEnumerable<TElement>` type
    - Create an empty `book/snippets/basics/control-flow/foreach-enumerable.cs` placeholder
    - Run `dotnet run --project tools/SnippetEmitter` to generate the emitted C# output
    - _Requirements: 6.3, 6.4_

- [x] 8. Sample Application
  - [x] 8.1 Create `samples/Iterators/Program.mist` with the proof code verbatim
    - Copy the proof code from Requirement 7 exactly as specified (immutable — do not modify)
    - Uses Allman (C#-style) braces throughout
    - Includes `Range` class with struct `Enumerator`, `Words` class with class `Enumerator`
    - Includes `Program.RunDemo()` exercising array foreach, struct enumerator, class enumerator, and explicit type annotation
    - _Requirements: 7.1, 7.2, 7.3, 7.4, 7.6_

  - [x] 8.2 Verify sample matches proof code and produces expected output
    - Compile and run `samples/Iterators/Program.mist` using the Mist compiler
    - Compare actual stdout output against the expected output specified in Requirement 7 — must match exactly line-for-line
    - Verify that `samples/Iterators/Program.mist` contains every `// PROOF:` comment from the requirement's proof code block
    - If any PROOF line is missing, the sample was modified and the task FAILS — do not proceed
    - If actual output differs from expected output, the implementation is incomplete — do not proceed
    - _Requirements: 7.2, 7.3, 7.6_

  - [x] 8.3 Update `samples/AllFeatures/Program.mist` to call `Iterators.Program.RunDemo()`
    - Add `Iterators.Program.RunDemo();` call in the appropriate position in the AllFeatures orchestrator
    - _Requirements: 7.5_

- [x] 9. Final Checkpoint - Full integration verification
  - Ensure all tests pass, ask the user if questions arise.

## Notes

- Tasks marked with `*` are optional and can be skipped for faster MVP
- Each task references specific requirements for traceability
- Checkpoints ensure incremental validation
- Property tests validate universal correctness properties from the design document
- Unit tests validate specific examples and edge cases
- The proof code in Requirement 7 is immutable — implementation must make it compile exactly as written
- All new sample code uses Allman (C#-style) braces
- The emitter's `EmitForeachStatement` method remains unchanged — it already emits valid C# `foreach`
- Process spawning policy: property tests use in-process compilation via `CompilerDriver` with a single batched `dotnet` run for output verification

## Task Dependency Graph

```json
{
  "waves": [
    { "id": 0, "tasks": ["1.1", "1.2"] },
    { "id": 1, "tasks": ["1.3", "3.1"] },
    { "id": 2, "tasks": ["3.2", "4.1"] },
    { "id": 3, "tasks": ["4.2", "3.3"] },
    { "id": 4, "tasks": ["4.3", "4.4", "4.5"] },
    { "id": 5, "tasks": ["4.6", "6.1", "6.2"] },
    { "id": 6, "tasks": ["7.1", "7.2", "8.1"] },
    { "id": 7, "tasks": ["8.2"] },
    { "id": 8, "tasks": ["8.3"] }
  ]
}
```
