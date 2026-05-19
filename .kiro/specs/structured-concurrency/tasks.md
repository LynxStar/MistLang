# Implementation Plan: Structured Concurrency

## Overview

This plan implements structured concurrency for the Mist language, progressing through the compiler pipeline: grammar → parsing → semantic model → type checking → C# emission → editor support → sample app → documentation. Each task builds incrementally on the previous, ensuring no orphaned code.

## Tasks

- [ ] 1. Grammar and keyword definitions
  - [ ] 1.1 Add `async` and `await` keyword tokens to `keywords.storm`
    - Add `token ASYNC = "async";` and `token AWAIT = "await";` to `Source/Mist/Grammar/keywords.storm`
    - _Requirements: 12.1, 12.2_

  - [ ] 1.2 Add `AsyncModifier` to the `Modifier` rule in `Mist.storm`
    - Add `| ASYNC as AsyncModifier` alternative to the existing `Modifier` rule
    - _Requirements: 12.3_

  - [ ] 1.3 Add `AsyncExpression` and `AwaitExpression` to the `Unary` rule in `Mist.storm`
    - Add `| ASYNC operand:Unary as AsyncExpression` and `| AWAIT operand:Unary as AwaitExpression` alternatives to the `Unary` rule at the same precedence as `!` and `-`
    - _Requirements: 12.4, 12.5, 12.6_

- [ ] 2. Pretty-printer and parser verification
  - [ ] 2.1 Add `AsyncExpression` and `AwaitExpression` cases to `MistPrettyPrinter.cs`
    - Handle `AsyncExpressionUnaryNode` emitting `async {operand}` and `AwaitExpressionUnaryNode` emitting `await {operand}` in `EmitUnary`
    - Handle `AsyncModifierModifierNode` in `EmitModifiers`
    - _Requirements: 12.7_

  - [ ]* 2.2 Write property test for async/await syntax round-trip
    - **Property 1: Async/Await Syntax Round-Trip**
    - **Validates: Requirements 12.3, 12.4, 12.5, 12.6, 12.7**

  - [ ]* 2.3 Write example-based parsing tests for async/await
    - Test `AsyncModifier` on methods, `AsyncExpression` on invocations, `AwaitExpression` on expressions
    - Test precedence: `async foo() + 1` parses as `(async foo()) + 1`
    - _Requirements: 12.4, 12.5, 12.6_

- [ ] 3. Standard library Task type declaration
  - [ ] 3.1 Create `Source/Standard/Bootstrap/Task.mist` with the `Task<TResult>` intrinsic class
    - Declare `intrinsic public class Task<TResult>` in the `Standard` namespace
    - Add intrinsic static methods: `Yield()`, `Delay(Int32)`, `WhenAll(Task<TResult>[])`, `WhenAny(Task<TResult>[])`
    - Add `type Task = Task<void>;` alias
    - _Requirements: 1.1, 1.6, 1.7, 1.8, 1.9, 1.10_

  - [ ] 3.2 Add `Standard.Task` → `System.Threading.Tasks.Task` mapping to `CSharpTypeMap.cs`
    - Add entry to `BackingTypes` dictionary
    - Extend `GetTypeName` to handle `ConstructedType` with `Standard.Task` generic definition (Task<T> → Task<T>/ValueTask<T>, Task<Void> → Task)
    - _Requirements: 1.3, 1.4, 10.6_

- [ ] 4. Checkpoint - Ensure all tests pass
  - Ensure all tests pass, ask the user if questions arise.

- [ ] 5. Semantic model: async method return type wrapping
  - [ ] 5.1 Implement async method return type wrapping in `SemanticModelBuilder.cs`
    - When a method has the `async` modifier, construct `Task<T>` as the effective return type using the stdlib `Task` class
    - For `void` return type, construct `Task<Void>`
    - Record both `DeclaredReturnType` (T) and `EffectiveReturnType` (Task<T>) on the method symbol
    - _Requirements: 2.1, 2.2, 2.3_

  - [ ]* 5.2 Write property test for async method return type wrapping
    - **Property 2: Async Method Return Type Wrapping**
    - **Validates: Requirements 2.1, 2.2**

- [ ] 6. Semantic model: context-sensitive expression type resolution
  - [ ] 6.1 Implement auto-unwrap for async method calls in sync contexts
    - In sync context (method without `async` modifier), async method invocations resolve to type `T` (not `Task<T>`)
    - The only way to get `Task<T>` in a sync context is via the `async` functor
    - _Requirements: 5.1, 5.2, 5.3, 5.4_

  - [ ] 6.2 Implement `async` functor expression type resolution
    - `async` on method with return type `T` → expression type `Task<T>`
    - `async` on async method in sync context → expression type `Task<T>` (cancels auto-unwrap)
    - `async` on async method in async context → expression type `Task<Task<T>>` (double-wrap)
    - _Requirements: 3.1, 3.2, 3.3, 3.4, 3.5, 3.6_

  - [ ] 6.3 Implement `await` functor expression type resolution
    - `await` on `Task<T>` → expression type `T`
    - `await` on `Task<Void>` → expression type `void`
    - Valid in both sync and async contexts
    - _Requirements: 4.1, 4.2, 4.5_

  - [ ]* 6.4 Write property test for async functor expression type
    - **Property 3: Async Functor Expression Type**
    - **Validates: Requirements 3.1, 3.2, 3.3, 3.5**

  - [ ]* 6.5 Write property test for await functor unwrap type
    - **Property 4: Await Functor Unwrap Type**
    - **Validates: Requirements 4.1, 4.2**

  - [ ]* 6.6 Write property test for auto-unwrap in sync context
    - **Property 5: Auto-Unwrap in Sync Context**
    - **Validates: Requirements 5.1, 5.2, 5.3**

- [ ] 7. Checkpoint - Ensure all tests pass
  - Ensure all tests pass, ask the user if questions arise.

- [ ] 8. Type checker: diagnostic rules
  - [ ] 8.1 Implement `MIST_TC_0070`: async + intrinsic incompatibility diagnostic
    - Report error when a method has both `async` and `intrinsic` modifiers
    - _Requirements: 2.4_

  - [ ] 8.2 Implement `MIST_TC_0071`: async on constructor/operator diagnostic
    - Report error when `async` is applied to a constructor or operator declaration
    - _Requirements: 2.5_

  - [ ] 8.3 Implement `MIST_TC_0072`: async method with explicit Task<T> return type diagnostic
    - Report error when an async method explicitly declares `Task<T>` as its return type
    - _Requirements: 2.6_

  - [ ] 8.4 Implement `MIST_TC_0073`: async functor on non-invocation operand diagnostic
    - Report error when `async` precedes an expression that is not a method invocation
    - _Requirements: 3.7_

  - [ ] 8.5 Implement `MIST_TC_0074`: await on non-Task<T> operand diagnostic
    - Report error when `await` is applied to an expression whose type is not `Task<T>`, including the actual type in the message
    - _Requirements: 4.3_

  - [ ] 8.6 Implement `MIST_TC_0075`: Task<T> storage restriction diagnostic
    - Report error when `Task<T>` is stored in a field, property, or collection
    - Allow `Task<T>` in local variables, method parameters, and return types
    - _Requirements: 7.5, 7.6, 7.7_

  - [ ] 8.7 Implement `MIST_TC_0076`: double-wrap warning for async functor on async method in async context
    - Report warning when `async` functor is applied to an async method in an async context, producing `Task<Task<T>>`
    - _Requirements: 3.6_

  - [ ]* 8.8 Write property test for async-intrinsic incompatibility
    - **Property 8: Async-Intrinsic Incompatibility**
    - **Validates: Requirements 2.4**

  - [ ]* 8.9 Write property test for async functor requires invocation operand
    - **Property 9: Async Functor Requires Invocation Operand**
    - **Validates: Requirements 3.7**

  - [ ]* 8.10 Write property test for await requires Task operand
    - **Property 10: Await Requires Task Operand**
    - **Validates: Requirements 4.3**

  - [ ]* 8.11 Write property test for Task storage restriction
    - **Property 7: Task Storage Restriction**
    - **Validates: Requirements 7.5**

- [ ] 9. Semantic model: discarded task tracking and scope-exit await
  - [ ] 9.1 Implement live task tracking per block scope
    - Track tasks that are created but not awaited or returned in async contexts
    - Track `Task<T>` locals obtained via `async` functor in sync contexts that are not awaited before scope exit
    - Mark tasks as consumed when explicitly awaited or returned
    - Store `LiveTaskInfo` in the semantic model for the emitter to consume
    - _Requirements: 6.1, 6.2, 6.3, 6.4, 7.1, 7.2, 7.3, 7.4_

  - [ ]* 9.2 Write property test for scope-exit await insertion
    - **Property 6: Scope-Exit Await Insertion**
    - **Validates: Requirements 6.1, 6.4, 7.1, 7.2, 7.4**

- [ ] 10. Checkpoint - Ensure all tests pass
  - Ensure all tests pass, ask the user if questions arise.

- [ ] 11. C# emitter: needs-async propagation analysis
  - [ ] 11.1 Implement needs-async propagation pass
    - Mark methods declared `async` in Mist as needing C# `async`
    - Mark sync methods that directly invoke async methods (triggering auto-unwrap) as needing C# `async`
    - Mark sync methods that use explicit `await` as needing C# `async`
    - Propagate transitively over the call graph as a fixed-point computation
    - _Requirements: 10.5, 5.1_

- [ ] 12. C# emitter: async/await emission
  - [ ] 12.1 Implement async method signature emission
    - Emit `async Task<T>` or `async ValueTask<T>` for async methods based on usage analysis
    - Emit `async Task` for `Task<Void>` methods where value is not used
    - Emit C# `async` keyword on methods that need it (from needs-async analysis)
    - _Requirements: 10.1, 10.2, 10.3, 10.4, 10.5, 10.6_

  - [ ] 12.2 Implement await functor emission
    - Emit `await {expression}` for `AwaitExpression` nodes
    - _Requirements: 4.1, 4.4_

  - [ ] 12.3 Implement auto-unwrap emission in sync contexts
    - Emit `await {asyncMethodCall}` for async method calls in sync contexts
    - Adjust enclosing method signature to `async Task<T>` in C# (via needs-async)
    - _Requirements: 5.1, 5.4_

  - [ ] 12.4 Implement async functor emission
    - For sync method: emit `Task.Run(() => method())` 
    - For async method in sync context (cancelling auto-unwrap): emit direct call without await
    - _Requirements: 3.1, 3.4, 3.5_

  - [ ] 12.5 Implement scope-exit await emission
    - Emit `await Task.WhenAll(...)` at block scope exit for all live tasks
    - Generate synthetic variable names (`__task_0`, `__task_1`) for discarded tasks in async contexts
    - Handle both sync and async contexts with un-awaited `Task<T>` locals
    - _Requirements: 6.1, 6.2, 6.4, 7.2, 7.3, 7.4_

  - [ ] 12.6 Implement usage analysis pass for ValueTask vs Task selection
    - Analyze each method: task awaited at most once and not multi-read → `ValueTask<T>`
    - Task awaited multiple times, passed as argument, or returned → `Task<T>`
    - Conservative default: emit `Task<T>` unless single-await pattern is proven
    - _Requirements: 10.1, 10.2, 10.3, 10.4_

  - [ ]* 12.7 Write property test for ValueTask vs Task emission
    - **Property 11: ValueTask vs Task Emission**
    - **Validates: Requirements 10.1, 10.2, 10.3**

  - [ ]* 12.8 Write property test for Task-Void lowering
    - **Property 12: Task-Void Lowers to Non-Generic Task**
    - **Validates: Requirements 10.6**

- [ ] 13. C# emitter: entry point integration
  - [ ] 13.1 Implement entry point task scheduler emission
    - Emit `async Task Main` or `async Task<int> Main` for async entry points
    - Emit `async Task Main` for non-async entry points that transitively call async methods
    - Rely on .NET's built-in async Main support and thread pool as the task scheduler
    - _Requirements: 11.1, 11.2, 11.3, 11.4_

- [ ] 14. Checkpoint - Ensure all tests pass
  - Ensure all tests pass, ask the user if questions arise.

- [ ] 15. TextMate grammar update
  - [ ] 15.1 Add `async` and `await` to the TextMate grammar
    - Add `async` to the storage modifier pattern (`storage.modifier.mist`)
    - Add `await` to the control keyword pattern (`keyword.control.mist`)
    - Both use `\b` word boundaries to prevent matching substrings of larger identifiers
    - _Requirements: 13.1, 13.2, 13.3_

- [ ] 16. Sample application
  - [ ] 16.1 Create `samples/StructuredConcurrency/mist.toml` with `name = "StructuredConcurrency"`
    - _Requirements: 14.1_

  - [ ] 16.2 Create `samples/StructuredConcurrency/Program.mist` with all concurrency demonstrations
    - Implement `public static void RunDemo()` method
    - Demonstrate: Task type references, async method declaration, await in async context, multi-await on same task, discarded tasks in sync context (implicit await), discarded tasks in async context (scope-exit await), async functor on sync methods, async functor on async methods, await functor unwrapping, sequential ordering in sync context, concurrent ordering in async context, entry point async support
    - Each permutation prints a unique labeled prefix (e.g., `[async-method-declaration]`, `[await-in-async]`, `[multi-await]`, etc.)
    - _Requirements: 14.1, 14.2, 14.3, 14.4, 14.6_

  - [ ] 16.3 Integrate into AllFeatures sample
    - Add `StructuredConcurrency.Program.RunDemo()` call to `samples/AllFeatures/Program.mist`
    - Add `[[references]]` entry with `path = "../StructuredConcurrency"` to `samples/AllFeatures/mist.toml`
    - _Requirements: 14.5_

- [ ] 17. Book documentation
  - [ ] 17.1 Create `book/src/advanced/structured-concurrency.md` chapter
    - Explain: async modifier, async functor, await functor, auto-unwrap in sync contexts, discarded task handling and scope-exit await in async contexts, structured concurrency guarantee, yield points and scheduler behavior, multi-await on completed tasks
    - Include comparison table of sync vs async context behavior
    - _Requirements: 15.1, 15.3, 15.4_

  - [ ] 17.2 Add chapter to `book/src/SUMMARY.md` under "Advanced Features"
    - _Requirements: 15.2_

  - [ ] 17.3 Create compilable book snippets in `book/snippets/advanced/`
    - Create `.mist` files corresponding to code examples in the chapter
    - _Requirements: 15.5_

- [ ] 18. Final checkpoint - Ensure all tests pass
  - Ensure all tests pass, ask the user if questions arise.

## Notes

- Tasks marked with `*` are optional and can be skipped for faster MVP
- Each task references specific requirements for traceability
- Checkpoints ensure incremental validation
- Property tests validate universal correctness properties from the design document
- Unit tests validate specific examples and edge cases
- All property tests run in-process using compiler APIs (no process spawning per iteration) per the test process spawning policy
- The zero-warnings policy applies to all new code — `MIST_TC_0076` is intentionally a warning, not an error

## Task Dependency Graph

```json
{
  "waves": [
    { "id": 0, "tasks": ["1.1"] },
    { "id": 1, "tasks": ["1.2", "1.3"] },
    { "id": 2, "tasks": ["2.1", "3.1", "15.1"] },
    { "id": 3, "tasks": ["2.2", "2.3", "3.2"] },
    { "id": 4, "tasks": ["5.1"] },
    { "id": 5, "tasks": ["5.2", "6.1", "6.2", "6.3"] },
    { "id": 6, "tasks": ["6.4", "6.5", "6.6", "8.1", "8.2", "8.3", "8.4", "8.5", "8.6", "8.7"] },
    { "id": 7, "tasks": ["8.8", "8.9", "8.10", "8.11", "9.1"] },
    { "id": 8, "tasks": ["9.2", "11.1"] },
    { "id": 9, "tasks": ["12.1", "12.2", "12.3", "12.4", "12.5", "12.6"] },
    { "id": 10, "tasks": ["12.7", "12.8", "13.1"] },
    { "id": 11, "tasks": ["16.1", "17.1", "17.2"] },
    { "id": 12, "tasks": ["16.2", "17.3"] },
    { "id": 13, "tasks": ["16.3"] }
  ]
}
```
