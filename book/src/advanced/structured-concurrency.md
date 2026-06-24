# Structured Concurrency

Mist's concurrency model is built on a simple guarantee: no task can outlive its parent scope. Every concurrent computation is either explicitly awaited, implicitly awaited at scope exit, or returned to a caller where the same rules apply. There is no function coloring — all methods participate in the same scheduler, and the distinction between sync and async contexts determines only *when* tasks are awaited, not *whether* they can be created.

The entire program executes within a task scheduler. Concurrency is expressed through a small set of composable primitives:

- **`async` modifier** — declares a method whose return type is implicitly wrapped in `Task<T>`
- **`async` functor** — lifts any method invocation into a scheduled task
- **`await` functor** — unwraps a `Task<T>` to its inner value `T`

## Sync vs Async Contexts

The behavior of async method calls depends on the context they appear in. A method body is a **sync context** unless the method is declared `async`, in which case it is an **async context**.

| Context | Async method call behavior | `Task<T>` obtainable via |
|---|---|---|
| Sync Context | Auto-unwrap to `T` (implicit await) | `async` functor only |
| Async Context | Returns `Task<T>` (explicit control) | Direct call or `async` functor |

This distinction gives you sequential-by-default behavior in sync code while providing full concurrency control in async code — without requiring different function signatures or call-site annotations.

## The `async` Modifier

The `async` modifier on a method declaration tells the compiler to wrap the return type in `Task<T>` automatically. You write the inner type; the compiler handles the wrapping.

{{#tabs }}
{{#tab name="Mist" }}
```mist
{{#include ../../snippets/advanced/structured-concurrency/async-modifier.mist}}
```
{{#endtab }}
{{#tab name="Emitted C#" }}
```csharp
{{#include ../../snippets/advanced/structured-concurrency/async-modifier.cs}}
```
{{#endtab }}
{{#endtabs }}

The `async` modifier is valid on instance methods, static methods, top-level functions, abstract type method signatures, and trait method signatures. It cannot be combined with `intrinsic`, and it cannot be applied to constructors or operators.

An async method must not explicitly declare `Task<T>` as its return type — the compiler wraps it for you. Writing `async Task<int> Compute()` is a compile-time error.

## The `async` Functor

The `async` keyword can also be used as a prefix functor on any method invocation. This lifts the call into a scheduled task, returning `Task<T>` regardless of whether the method itself is declared `async`.

{{#tabs }}
{{#tab name="Mist" }}
```mist
{{#include ../../snippets/advanced/structured-concurrency/async-functor.mist}}
```
{{#endtab }}
{{#tab name="Emitted C#" }}
```csharp
{{#include ../../snippets/advanced/structured-concurrency/async-functor.cs}}
```
{{#endtab }}
{{#endtabs }}

The `async` functor requires a method invocation as its operand. Applying it to a variable, literal, or property access is a compile-time error.

In a sync context, the `async` functor is the *only* way to obtain a `Task<T>` value — it cancels the auto-unwrap that would normally occur. In an async context, applying `async` to an already-async method produces `Task<Task<T>>` (double-wrapping), which is almost certainly unintended and produces a compiler warning.

## The `await` Functor

The `await` keyword is a prefix functor that unwraps a `Task<T>` to its inner value `T`. It suspends the current task until the awaited task completes.

{{#tabs }}
{{#tab name="Mist" }}
```mist
{{#include ../../snippets/advanced/structured-concurrency/await-functor.mist}}
```
{{#endtab }}
{{#tab name="Emitted C#" }}
```csharp
{{#include ../../snippets/advanced/structured-concurrency/await-functor.cs}}
```
{{#endtab }}
{{#endtabs }}

`await` is valid in both sync and async contexts. Applying `await` to an expression that is not of type `Task<T>` is a compile-time error.

## Auto-Unwrap in Sync Contexts

In a sync context (a method without the `async` modifier), calling an async method automatically awaits the result. The developer writes sequential code; the scheduler handles yielding transparently.

{{#tabs }}
{{#tab name="Mist" }}
```mist
{{#include ../../snippets/advanced/structured-concurrency/auto-unwrap.mist}}
```
{{#endtab }}
{{#tab name="Emitted C#" }}
```csharp
{{#include ../../snippets/advanced/structured-concurrency/auto-unwrap.cs}}
```
{{#endtab }}
{{#endtabs }}

Notice that `result` has type `int`, not `Task<int>`. The auto-unwrap is implicit — you don't need to write `await`. Execution within the sync method is guaranteed sequential: each statement completes before the next begins, even when calling async methods.

The only way to obtain a `Task<T>` in a sync context is via the `async` functor. This gives you an escape hatch when you need to capture a task for later coordination.

## Discarded Tasks and Scope-Exit Await

In an async context, calling an async method returns `Task<T>` — it does not auto-unwrap. If you don't assign or await the result, the task becomes a *discarded task*. Discarded tasks are automatically awaited when the enclosing block scope exits.

{{#tabs }}
{{#tab name="Mist" }}
```mist
{{#include ../../snippets/advanced/structured-concurrency/scope-exit-await.mist}}
```
{{#endtab }}
{{#tab name="Emitted C#" }}
```csharp
{{#include ../../snippets/advanced/structured-concurrency/scope-exit-await.cs}}
```
{{#endtab }}
{{#endtabs }}

Both `FetchData()` and `SendNotification()` run concurrently. At the end of `ProcessAll`, the compiler inserts an implicit `await Task.WhenAll(...)` to ensure both tasks complete before the method returns.

This also applies to `Task<T>` variables that are assigned but never explicitly awaited within the same scope — they are treated as discarded and awaited at scope exit.

## The Structured Concurrency Guarantee

Mist enforces a strict rule: **no task can be orphaned**. Every task must either be:

1. Explicitly awaited with `await`
2. Implicitly awaited at scope exit (discarded task handling)
3. Returned from the method (transferring ownership to the caller's scope)

This means `Task<T>` cannot be stored in fields, properties, or collections — doing so would allow a task to escape its structured scope. The compiler reports an error if you attempt this.

```mist
{{#include ../../snippets/advanced/structured-concurrency/no-orphans.mist}}
```

A `Task<T>` can be passed as a method argument because the callee's scope is nested within the caller's scope — the task remains governed by the caller's structured concurrency guarantee.

## Yield Points and Scheduler Behavior

Every invocation of an async method — whether called directly or via the `async` functor — is a *yield point*. At a yield point, the task scheduler gains control and may run other pending tasks before resuming the current one.

{{#tabs }}
{{#tab name="Mist" }}
```mist
{{#include ../../snippets/advanced/structured-concurrency/yield-points.mist}}
```
{{#endtab }}
{{#tab name="Emitted C#" }}
```csharp
{{#include ../../snippets/advanced/structured-concurrency/yield-points.cs}}
```
{{#endtab }}
{{#endtabs }}

In a sync context, yield points don't affect the visible execution order — the method resumes after the yield and proceeds to the next statement sequentially. Other tasks may execute during the yield, but this is invisible to the developer's code.

If no other tasks are enqueued when a yield point is reached, the scheduler resumes the current task immediately without delay. You can also create an explicit yield point using `Task.Yield()` to give other tasks a chance to run.

## Multi-Await on Completed Tasks

A `Task<T>` can be awaited multiple times. Each `await` on a completed task returns the same result immediately without re-executing the work. This is useful when multiple parts of your code need the same computed value.

{{#tabs }}
{{#tab name="Mist" }}
```mist
{{#include ../../snippets/advanced/structured-concurrency/multi-await.mist}}
```
{{#endtab }}
{{#tab name="Emitted C#" }}
```csharp
{{#include ../../snippets/advanced/structured-concurrency/multi-await.cs}}
```
{{#endtab }}
{{#endtabs }}

The computation runs once. Both `first` and `second` receive the same value. This makes `Task<T>` a safe, shareable promise — you can pass it to multiple consumers without worrying about duplicate execution.
