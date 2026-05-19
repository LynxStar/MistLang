# Requirements Document

## Introduction

Mist adopts a structured concurrency model where the entire program executes within a task scheduler. There is no function coloring — all functions are "green" and can participate in concurrent execution. Concurrency is expressed through an `async` modifier on methods (which causes them to return a `Task<T>` implicitly) and an `async` functor (which lifts any synchronous method into a scheduled task). The `await` functor unwraps a `Task<T>` to its inner value. Automatic unwrapping rules differ between synchronous and asynchronous contexts: synchronous contexts implicitly await discarded tasks, while asynchronous contexts await them on scope exit. No task can outlive the lifetime of its parent, enforcing structured concurrency guarantees at the language level.

`Task<T>` is a promise — it represents work that may be running, completed, or faulted. It can be awaited multiple times; each await simply accesses the result once complete. Structured concurrency guarantees that every task completes before its parent scope exits, so there are no dangling or forgotten tasks.

## Glossary

- **Task_Monad**: The `Task<T>` generic type representing a promise of eventual completion. A Task may be running, completed, or faulted. `Task<Void>` represents a task with no result. A Task can be awaited multiple times — each await returns the same result.
- **Task_Scheduler**: The runtime component that manages scheduling and execution of all tasks. The entire program executes within the Task_Scheduler.
- **Async_Method**: A method declared with the `async` modifier. The compiler automatically wraps its return type in Task_Monad. Invocation of an Async_Method constitutes a yield point.
- **Sync_Context**: A method body that is not marked `async`. Within a Sync_Context, async method calls are automatically awaited and execution order within the method is guaranteed sequential. The method may still yield to the scheduler during auto-awaits, but other tasks' execution is invisible to the developer — their code proceeds in order.
- **Async_Context**: A method body that is marked `async`. Within an Async_Context, the developer controls ordering via explicit `await` and discarded tasks are awaited on scope exit.
- **Async_Functor**: The `async` keyword used as a prefix functor on a method invocation to lift a synchronous method call into a scheduled task, returning `Task<T>`.
- **Await_Functor**: The `await` keyword used as a prefix functor on an expression of type `Task<T>` to suspend until the task completes and unwrap the result to type `T`. Awaiting an already-completed task returns the result immediately.
- **Yield_Point**: A point in execution where the Task_Scheduler may suspend the current task and schedule other work. Yield points occur in any context (sync or async) when an async method is invoked or the async functor is used. In a sync context, the method resumes after the yield and continues sequentially.
- **Discarded_Task**: A `Task<T>` value that is not assigned to a variable or explicitly awaited at the call site.
- **Scope_Exit_Await**: The implicit await of all Discarded_Tasks that occurs when an Async_Context's scope ends, ensuring structured concurrency.
- **Structured_Concurrency**: The guarantee that no task can be orphaned. Every task must either be explicitly awaited, implicitly awaited at scope exit, or returned to a parent scope where the same rules apply.
- **Compiler**: The Mist compiler pipeline including parsing, semantic analysis, and code generation.
- **TextMate_Grammar**: The TextMate grammar file at `Editor/mist-syntax-highlighting/syntaxes/mist.tmLanguage.json` providing syntax highlighting for `.mist` files.

## Requirements

### Requirement 1: Task Intrinsic Type

**User Story:** As a Mist developer, I want a `Task<T>` type available in the standard library with utility methods, so that I can represent concurrent computations as promises and coordinate them.

#### Acceptance Criteria

1. THE Standard_Library SHALL declare `Task` as an `intrinsic public class` with one unconstrained type parameter `TResult` in the `Standard` namespace, in a file at `Source/Standard/Bootstrap/Task.mist`.
2. WHEN a program includes `using Standard;` and references `Task<T>` as a type in variable declarations, parameter types, or return types (e.g., `Task<int>`, `Task<void>`), THE Compiler SHALL resolve the type without producing any diagnostics.
3. THE Compiler SHALL resolve `Task<T>` through the stdlib type resolution path (loaded from `.mist` source by StdlibLoader), producing a symbol with `SymbolOrigin.Source` and `IsBackendBacked = true`.
4. THE CSharp_Type_Map SHALL contain an entry mapping `Standard.Task` to `System.Threading.Tasks.Task` so that the C# backend emits the correct CLR type reference.
5. A `Task<T>` SHALL be awaitable multiple times. Each `await` on a completed task SHALL return the same result without re-executing the work.
6. THE Task type SHALL declare an intrinsic static method `Yield` with no parameters and return type `Task`, which creates an explicit yield point allowing the scheduler to run other pending tasks.
7. THE Task type SHALL declare an intrinsic static method `Delay` with an `Int32` parameter (milliseconds) and return type `Task`, which creates a task that completes after the specified duration.
8. THE Task type SHALL declare an intrinsic static method `WhenAll` with a parameter of type `Task<T>[]` and return type `Task<T[]>`, which creates a task that completes when all input tasks have completed, returning their results in order.
9. THE Task type SHALL declare an intrinsic static method `WhenAny` with a parameter of type `Task<T>[]` and return type `Task<T>`, which creates a task that completes when any one of the input tasks has completed, returning that task's result.
10. THE Standard_Library SHALL declare a type alias `Task` (non-generic) as shorthand for `Task<void>` in the same file, so that developers can write `Task` instead of `Task<void>` for tasks with no result.

### Requirement 2: Async Method Declaration

**User Story:** As a Mist developer, I want to declare methods with the `async` modifier, so that the compiler automatically wraps their return type in `Task<T>` and callers can choose when to await the result.

#### Acceptance Criteria

1. WHEN a method is declared with the `async` modifier and a return type `T` (where `T` is not `void`), THE Compiler SHALL treat the method's effective return type as `Task<T>` in the semantic model without requiring the developer to write `Task<T>` explicitly.
2. WHEN a method is declared with `async` and return type `void`, THE Compiler SHALL treat the effective return type as `Task<Void>`.
3. THE `async` modifier SHALL be valid on instance methods, static methods, and top-level functions.
4. THE Compiler SHALL report a diagnostic error when `async` is combined with the `intrinsic` modifier on the same method.
5. THE Compiler SHALL report a diagnostic error when `async` is applied to a constructor or operator declaration.
6. IF a method is declared with `async` and the developer explicitly writes `Task<T>` as the return type, THEN THE Compiler SHALL report a diagnostic error indicating that `async` methods must not specify `Task<T>` explicitly.
7. THE `async` modifier SHALL be valid on interface method signatures and trait method signatures, with the same `Task<T>` wrapping semantics applied to the declared return type.

### Requirement 3: Async Functor (Lifting Synchronous Methods)

**User Story:** As a Mist developer, I want to use `async` as a prefix functor on any method invocation, so that I can lift a synchronous call into a scheduled task.

#### Acceptance Criteria

1. WHEN the `async` keyword precedes a method invocation expression, THE Compiler SHALL wrap the invocation in a Task_Monad and schedule it with the Task_Scheduler.
2. WHEN `async` is applied to a method with return type `T`, THE resulting expression SHALL have type `Task<T>`.
3. WHEN `async` is applied to a method with return type `void`, THE resulting expression SHALL have type `Task<Void>`.
4. THE Async_Functor SHALL be applicable to any method invocation expression — including instance methods, static methods, and top-level function calls — regardless of whether that method is itself declared `async`.
5. WHEN `async` is applied to an Async_Method in a Sync_Context, THE resulting expression SHALL have type `Task<T>` (the async functor cancels the auto-unwrap that would normally occur in a sync context).
6. WHEN `async` is applied to an Async_Method in an Async_Context, THE resulting expression SHALL have type `Task<Task<T>>` (double-wrapping, since the method already returns `Task<T>` in an async context).
7. IF the `async` keyword precedes an expression that is not a method invocation (e.g., a variable reference, literal, or property access), THEN THE Compiler SHALL report a diagnostic error indicating that the async functor requires a method invocation operand.

### Requirement 4: Await Functor (Unwrapping Tasks)

**User Story:** As a Mist developer, I want to use `await` as a prefix functor on a `Task<T>` expression, so that I can suspend execution until the task completes and obtain the result value.

#### Acceptance Criteria

1. WHEN the `await` keyword precedes an expression of type `Task<T>`, THE Compiler SHALL treat the resulting expression as having type `T`.
2. WHEN `await` is applied to an expression of type `Task<Void>`, THE resulting expression SHALL have type `void`.
3. IF `await` is applied to an expression whose type is not `Task<T>`, THEN THE Compiler SHALL report a diagnostic error indicating that `await` requires an operand of type `Task<T>` and showing the actual type of the operand.
4. EACH use of `await` SHALL constitute a Yield_Point where the Task_Scheduler may suspend the current task.
5. THE `await` keyword SHALL be valid in both Sync_Context and Async_Context method bodies.
6. WHEN `await` is applied to a `Task<T>` that has already completed, THE result SHALL be returned immediately without suspending.

### Requirement 5: Automatic Await in Synchronous Context

**User Story:** As a Mist developer, I want async method calls to be automatically awaited in synchronous contexts, so that sequential execution order is preserved without requiring explicit `await` calls.

#### Acceptance Criteria

1. WHEN an Async_Method is invoked in a Sync_Context, THE Compiler SHALL implicitly await the returned task, unwrapping the result to type `T` regardless of whether the result is assigned, passed as an argument, or discarded.
2. WHEN an Async_Method is invoked in a Sync_Context and the result is assigned to a variable, THE variable SHALL have type `T` (the unwrapped type), not `Task<T>`.
3. THE only way to obtain a `Task<T>` value in a Sync_Context SHALL be via the Async_Functor (`async` prefix on a method invocation).
4. WHILE in a Sync_Context, THE Compiler SHALL ensure all statements execute in sequential order regardless of whether they invoke async or synchronous methods.

### Requirement 6: Discarded Task Handling in Asynchronous Context

**User Story:** As a Mist developer, I want discarded tasks in async contexts to be awaited on scope exit, so that structured concurrency is enforced and I have freedom to order concurrent operations.

#### Acceptance Criteria

1. WHEN an Async_Method is invoked in an Async_Context and the result is not assigned or awaited, THE Compiler SHALL track the returned task as a Discarded_Task and defer its await until the enclosing block scope exits (Scope_Exit_Await).
2. WHEN multiple Discarded_Tasks exist in the same block scope of an Async_Context, THE Compiler SHALL await all of them concurrently at that block's scope exit before execution continues past the block.
3. WHEN an Async_Method is invoked in an Async_Context and the result is explicitly awaited, THE task SHALL complete at the await point (not deferred to scope exit).
4. WHEN an Async_Method is invoked in an Async_Context and the result is assigned to a variable that is never subsequently awaited within the same block scope, THE Compiler SHALL treat it as a Discarded_Task and await it at scope exit.

### Requirement 7: Structured Concurrency Guarantee

**User Story:** As a Mist developer, I want the guarantee that no task is orphaned, so that I can reason about task lifetimes and avoid dangling concurrent work.

#### Acceptance Criteria

1. A `Task<T>` SHALL NOT be orphaned. Every task must either be explicitly awaited, implicitly awaited at scope exit, or explicitly returned from the method.
2. WHEN an Async_Context method returns, ALL tasks spawned within that method that are not returned SHALL have completed (both explicitly awaited and implicitly awaited at scope exit).
3. WHEN a Sync_Context method returns, ALL tasks spawned within that method that are not returned SHALL have completed (due to immediate implicit await or scope-exit await).
4. IF a `Task<T>` local variable in any context (sync or async) is not explicitly awaited before scope exit and is not returned, THEN THE Compiler SHALL insert an implicit await for that task at scope exit.
5. THE Compiler SHALL report a diagnostic error when a `Task<T>` value is stored in a field, property, or collection, since this would allow the task to become orphaned.
6. A `Task<T>` MAY be returned from a method, transferring ownership to the caller's scope where it will be subject to the same structured concurrency rules.
7. A `Task<T>` MAY be passed as a method argument. The callee's scope is nested within the caller's scope, so the task remains governed by the caller's structured concurrency guarantee.

### Requirement 8: Yield Points on Async Method Invocation

**User Story:** As a Mist developer, I want every invocation of an async method to be a yield point, so that the scheduler can interleave work and prevent starvation.

#### Acceptance Criteria

1. WHEN an Async_Method is invoked in any context (sync or async), THE Task_Scheduler SHALL be given control, allowing it to run other pending tasks.
2. WHEN the Async_Functor is applied to a method invocation in any context, THE Task_Scheduler SHALL be given control, allowing it to run other pending tasks.
3. WHILE other tasks are enqueued in the Task_Scheduler, WHEN a Yield_Point is reached, THE Task_Scheduler SHALL execute at least one pending task before resuming the yielding task.
4. IF no other tasks are enqueued in the Task_Scheduler when a Yield_Point is reached, THEN THE Task_Scheduler SHALL resume the yielding task without delay.
5. IN a Sync_Context, yield points SHALL NOT affect the sequential execution order visible to the developer — the method resumes after the yield and proceeds to the next statement in order.

### Requirement 9: Task Scheduling Semantics

**User Story:** As a Mist developer, I want tasks to be scheduled through the task scheduler, so that concurrent work is managed centrally and structured concurrency is enforced.

#### Acceptance Criteria

1. WHEN a task is created (via Async_Method invocation or Async_Functor), THE Task_Scheduler SHALL synchronously enqueue the task for execution before returning the Task_Monad to the caller.
2. IF the Task_Scheduler has execution capacity available at the time a task is scheduled, THEN THE Task_Scheduler SHALL begin executing the task without waiting for an explicit await.
3. IF the Task_Scheduler does not have execution capacity available at the time a task is scheduled, THEN THE Task_Scheduler SHALL retain the task in its queue and begin executing it when capacity becomes available.

### Requirement 10: C# Backend Task Type Emission

**User Story:** As a compiler developer, I want the C# backend to emit `ValueTask<T>` or `Task<T>` based on usage analysis, so that generated code gets optimal performance without the Mist developer needing to choose.

#### Acceptance Criteria

1. WHEN the Compiler determines that a `Task<T>` is awaited at most once and is never assigned to a variable that is read more than once, THE C# backend SHALL emit `ValueTask<T>` for that method's return type.
2. WHEN the Compiler determines that a `Task<T>` is awaited multiple times, or is assigned to a variable that is read from multiple await sites, THE C# backend SHALL emit `Task<T>` for that method's return type.
3. WHEN the Compiler determines that a `Task<T>` is passed as a method argument or returned from the enclosing method, THE C# backend SHALL emit `Task<T>` for that method's return type.
4. THE choice between `ValueTask<T>` and `Task<T>` in emitted C# SHALL be transparent to the Mist developer — the Mist type system always presents `Task<T>` regardless of what the backend emits.
5. THE C# backend SHALL emit the appropriate `async`/`await` keywords in the generated C# code to match the Mist concurrency semantics.
6. WHEN the Mist type is `Task<Void>` and the task value is not used (only awaited for completion), THE C# backend SHALL emit the non-generic `Task` type rather than `Task<Void>`, following the same lowering pattern used for `Standard.Void` to C# `void`.

### Requirement 11: Entry Point Task Scheduler Integration

**User Story:** As a Mist developer, I want the program entry point to execute within the task scheduler, so that async operations are available from the start of the program.

#### Acceptance Criteria

1. THE Compiler SHALL generate code that initializes the Task_Scheduler before invoking the program entry point (`Main` method) and shuts down the Task_Scheduler after the entry point and all its child tasks have completed.
2. WHEN the entry point method is declared `async`, THE Compiler SHALL schedule it as the root task within the Task_Scheduler and the program SHALL not exit until the root task completes.
3. WHEN the entry point method is not declared `async`, THE Compiler SHALL still initialize the Task_Scheduler so that async methods invoked transitively within Main can be scheduled and executed.
4. WHEN the entry point method is declared `async` with return type `int`, THE program exit code SHALL be the unwrapped integer value returned by the root task upon completion.

### Requirement 12: Grammar and Parser Support

**User Story:** As a compiler developer, I want the Mist grammar to support `async` and `await` keywords in the correct syntactic positions, so that the parser can produce correct AST nodes for concurrency constructs.

#### Acceptance Criteria

1. THE Grammar SHALL define `async` as a keyword token in `keywords.storm` with the declaration `token ASYNC = "async";`.
2. THE Grammar SHALL define `await` as a keyword token in `keywords.storm` with the declaration `token AWAIT = "await";`.
3. THE Grammar SHALL include `ASYNC as AsyncModifier` as an alternative in the `Modifier` rule, allowing `async` to appear in the same positions as `public`, `static`, and other existing modifiers.
4. THE Grammar SHALL include an `ASYNC operand:Unary as AsyncExpression` alternative in the `Unary` rule, so that `async` may precede any unary expression syntactically. The constraint that the operand must be a method invocation is a semantic check, not a parse-time restriction.
5. THE Grammar SHALL include an `AWAIT operand:Unary as AwaitExpression` alternative in the `Unary` rule, so that `await` may precede any unary expression syntactically. The constraint that the operand must be of type `Task<T>` is a semantic check, not a parse-time restriction.
6. THE `async` and `await` unary alternatives SHALL bind at the same precedence level as existing unary operators (`!`, `-`, and cast expressions) in the `Unary` rule.
7. THE Parser SHALL produce three distinct AST node types: `AsyncModifier` (from the Modifier rule), `AsyncExpression` (from the Unary rule), and `AwaitExpression` (from the Unary rule), each distinguishable by node kind in the parse tree.

### Requirement 13: TextMate Grammar Update

**User Story:** As a Mist developer using VS Code, I want `async` and `await` keywords to be syntax-highlighted correctly, so that I get visual cues for concurrency constructs.

#### Acceptance Criteria

1. THE TextMate_Grammar SHALL recognize `async` as a storage modifier keyword (scope `storage.modifier.mist`) when it appears as a complete token bounded by word boundaries.
2. THE TextMate_Grammar SHALL recognize `await` as a control keyword (scope `keyword.control.mist`) when it appears as a complete token bounded by word boundaries.
3. IF `async` or `await` appears as a substring of a larger identifier (e.g., `asyncHelper`, `awaiting`, `reawait`), THEN THE TextMate_Grammar SHALL NOT assign a keyword scope to the substring.

### Requirement 14: Sample Application

**User Story:** As a Mist developer, I want a comprehensive sample application demonstrating structured concurrency, so that I can see every supported usage pattern in one place and verify correctness.

#### Acceptance Criteria

1. A sample project SHALL exist at `samples/StructuredConcurrency/Program.mist` with a corresponding `mist.toml` where `name = "StructuredConcurrency"`.
2. The sample SHALL demonstrate every syntax variant and usage pattern defined in the preceding requirements, including but not limited to: Task type references, async method declaration, await in async context, multi-await on the same task, discarded tasks in sync context (implicit await), discarded tasks in async context (scope-exit await), async functor on sync methods, async functor on async methods, await functor unwrapping, sequential ordering in sync context, concurrent ordering in async context, and entry point async support.
3. Each demonstrated permutation SHALL print a unique labeled prefix to stdout (e.g., `[async-method-declaration]`, `[await-in-async]`, `[multi-await]`, `[discarded-sync-implicit-await]`, `[discarded-async-scope-exit]`, `[async-functor-sync]`, `[async-functor-async]`, `[await-unwrap]`, `[sequential-sync]`, `[concurrent-async]`, `[entry-point-async]`) so that correctness of each pattern is independently observable.
4. The sample SHALL expose a `public static void RunDemo()` method in `StructuredConcurrency.Program` that executes all demonstrations.
5. `samples/AllFeatures/Program.mist` SHALL call `StructuredConcurrency.Program.RunDemo()` to include the feature in the full-suite sample run, and `samples/AllFeatures/mist.toml` SHALL include a `[[references]]` entry with `path = "../StructuredConcurrency"`.
6. The sample SHALL compile and run without errors or warnings using the Mist compiler.

### Requirement 15: Book Documentation

**User Story:** As a Mist developer, I want a chapter in the Mist book documenting structured concurrency, so that I can learn the concurrency model, understand sync vs async contexts, and see practical examples.

#### Acceptance Criteria

1. A chapter SHALL exist at `book/src/advanced/structured-concurrency.md` covering the structured concurrency model.
2. THE chapter SHALL be listed in `book/src/SUMMARY.md` under the "Advanced Features" section.
3. THE chapter SHALL explain the following concepts with code examples: the `async` modifier on methods, the `async` functor for lifting methods into tasks, the `await` functor for unwrapping tasks, auto-unwrap behavior in sync contexts, discarded task handling and scope-exit await in async contexts, the structured concurrency guarantee (no orphaned tasks), yield points and scheduler behavior, and multi-await on completed tasks.
4. THE chapter SHALL include a comparison table showing behavior differences between sync and async contexts.
5. Code snippets in the chapter SHALL have corresponding compilable `.mist` files in `book/snippets/advanced/` so they can be validated by the build.
