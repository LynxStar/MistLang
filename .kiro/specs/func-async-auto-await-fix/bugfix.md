# Bugfix Requirements Document

## Introduction

Three related defects in the structured concurrency and drop systems:

1. Invoking an async lambda or any `Func`-typed variable whose return type is `Task<T>` in a sync context does not auto-unwrap the result to `T`. Direct async method calls correctly auto-unwrap via the existing auto-await logic, but the Func invocation path bypasses this check entirely.

2. `NeedsAsyncPropagationPass` does not seed from async functor expressions (`var task = async SyncMethod()`), causing the containing method to lack the `async` keyword needed for emitted scope-exit `await` in try/finally blocks.

3. When a scope-exit auto-drop fires and `Drop()` returns a droppable value that is not assigned to a variable, the returned value is not itself dropped. The drop obligation should chain recursively: if `Drop()` returns a droppable type, that value immediately triggers its own drop, continuing until reaching a non-droppable type. This is the general mechanism that solves `Task<Task<T>>` — `await` only unwraps one layer, but the unassigned `Task<T>` result triggers its own scope-exit drop (`await`), and so on.

## Bug Analysis

### Current Behavior (Defect)

1.1 WHEN an async lambda (or Func variable with return type `Task<T>`) is invoked in a sync context THEN the system returns `Task<T>` as the expression type instead of unwrapping to `T`

1.2 WHEN a sync method contains only async functor expressions (`var task = async SyncMethod()`) producing `Task<T>` locals (without direct async method calls or explicit await) THEN the system does not mark the method as `NeedsCSharpAsync`, causing invalid emitted C# (bare `await` in a non-async method's try/finally scope-exit)

1.3 WHEN a scope-exit auto-drop fires on a type whose `Drop()` returns a droppable type and the return value is not captured into a variable THEN the returned value is silently discarded without triggering its own drop obligation, violating structured concurrency guarantees for nested tasks and the general recursive drop invariant

### Expected Behavior (Correct)

2.1 WHEN an async lambda (or Func variable with return type `Task<T>`) is invoked in a sync context THEN the system SHALL unwrap `Task<T>` to `T` as the expression type, consistent with direct async method call auto-await behavior

2.2 WHEN a sync method contains `Task<T>`-typed locals produced by async functor expressions THEN the system SHALL mark the method as `NeedsCSharpAsync` so that scope-exit `await` statements in try/finally blocks emit valid C#

2.3 WHEN a scope-exit auto-drop fires and `Drop()` returns a droppable type that is not assigned to a variable THEN the system SHALL immediately drop the returned value, chaining recursively until reaching a non-droppable return type or void

2.4 The recursive drop chaining SHALL apply uniformly to all auto-drop sites: scope-exit drops for named locals, discarded task wrapping, synthetic field drops, and with-block drops

2.5 `await` SHALL continue to unwrap only one layer — the recursive behavior comes from the drop obligation on the unassigned return value, not from await itself

### Unchanged Behavior (Regression Prevention)

3.1 WHEN an async lambda (or Func variable with return type `Task<T>`) is invoked in an async context THEN the system SHALL CONTINUE TO return `Task<T>` as the expression type (no auto-unwrap in async context)

3.2 WHEN a direct async method is called in a sync context THEN the system SHALL CONTINUE TO unwrap `Task<T>` to `T` via the existing auto-await logic

3.3 WHEN a Func variable with a non-Task return type (e.g., `Func<int, string>`) is invoked THEN the system SHALL CONTINUE TO return the Func's return type directly without modification

3.4 WHEN a sync method directly calls an async method THEN the system SHALL CONTINUE TO mark it as `NeedsCSharpAsync` through the existing propagation mechanism

3.5 WHEN a discarded async method call occurs in a block THEN the system SHALL CONTINUE TO emit scope-exit await via the `DiscardedTaskInfo` try/finally wrapping mechanism

3.6 WHEN a named `Task<T>` local (from direct assignment of an async method call) exists in a method already marked `NeedsCSharpAsync` THEN the system SHALL CONTINUE TO emit `await localName;` in the scope-exit try/finally block

3.7 WHEN a user explicitly captures the return value of `Drop()` into a variable (e.g., `var inner = outer.Drop()`) THEN the system SHALL NOT trigger recursive drop on that return value — the user has taken ownership

3.8 WHEN `Drop()` returns void or a non-droppable type THEN no recursive chaining SHALL occur (base case)

## Verification Sample

The following sample SHALL compile and produce the expected output when all three defects are fixed. It will be added as `samples/AsyncDropChaining/Program.mist`.

### Source

```mist
// AsyncDropChaining — Proves async Func auto-unwrap, async propagation, and recursive drop chaining.

using Standard;

namespace AsyncDropChaining;

// ─── Helpers ─────────────────────────────────────────────────────────────────

public class Helpers
{
    public static async string FetchAsync(string label)
    {
        Console.WriteLine($"  FetchAsync({label}) executed");
        return $"result-{label}";
    }

    public static int ComputeSync(int x)
    {
        return x * 2;
    }

    public static async int SlowCompute(int x)
    {
        return x + 100;
    }
}

// ─── Custom linear type with value-returning Drop (for recursive drop demo) ──

public linear class ResourceHandle
{
    string Name;

    public ResourceHandle(string name)
    {
        this.Name = name;
    }

    // Drop() returns a string — non-droppable, so recursion stops here
    public string Drop()
    {
        Console.WriteLine($"  ResourceHandle({Name}).Drop() called");
        return Name;
    }
}

// ─── Linear wrapper whose Drop returns a droppable type (recursive drop) ─────

public linear class TaskBox
{
    Task<string> Inner;

    public TaskBox(Task<string> inner)
    {
        this.Inner = inner;
    }

    // Drop() returns Task<string> — which is itself droppable (relevant, has Drop)
    // The recursive drop rule means: TaskBox.Drop() fires → returns Task<string> →
    // not assigned → triggers Task<string>.Drop() → await → returns string → not droppable → done
    public Task<string> Drop()
    {
        Console.WriteLine("  TaskBox.Drop() called, yielding inner Task");
        return Inner;
    }
}

// ─── Entry point ─────────────────────────────────────────────────────────────

public class Program
{
    public static void Main(string[] args)
    {
        RunDemo();
    }

    public static void RunDemo()
    {
        Console.WriteLine("=== AsyncDropChaining Sample ===");

        // ─── 1. Func auto-unwrap in sync context (Defect 1.1) ───────────
        // PROOF: async lambda invoked in sync context auto-unwraps Task<string> → string
        Console.WriteLine("--- 1. Func auto-unwrap in sync context ---");
        var asyncLambda = async (string label) => $"lambda-{label}";
        var lambdaResult = asyncLambda("test");
        // PROOF: lambdaResult is string, not Task<string>
        Console.WriteLine($"  lambdaResult={lambdaResult}");
        Console.WriteLine($"  type is string (not Task): {lambdaResult == "lambda-test"}");

        // ─── 2. Func auto-unwrap with async body (Defect 1.1) ────────────
        // PROOF: async lambda calling async method, invoked in sync context
        Console.WriteLine("--- 2. Async lambda with async body ---");
        var asyncFetch = async (string id) => await Helpers.FetchAsync(id);
        var fetchResult = asyncFetch("abc");
        // PROOF: fetchResult is string
        Console.WriteLine($"  fetchResult={fetchResult}");

        // ─── 3. Async functor in sync method (Defect 1.2) ────────────────
        // PROOF: method containing only async functor locals gets NeedsCSharpAsync
        // The scope-exit drop (await) in try/finally requires the method to be async
        Console.WriteLine("--- 3. Async functor scope-exit ---");
        DemoAsyncFunctorScopeExit();
        Console.WriteLine("  async functor scope-exit completed");

        // ─── 4. Recursive drop — Task<Task<T>> (Defect 1.3) ─────────────
        // PROOF: nested task is fully awaited through recursive drop chaining
        Console.WriteLine("--- 4. Recursive drop Task<Task<T>> ---");
        DemoNestedTaskDrop();
        Console.WriteLine("  nested task fully resolved");

        // ─── 5. Recursive drop — custom type returning droppable (Defect 1.3) ──
        // PROOF: TaskBox.Drop() returns Task<string>, which gets its own drop (await)
        Console.WriteLine("--- 5. Custom recursive drop ---");
        DemoCustomRecursiveDrop();
        Console.WriteLine("  custom recursive drop completed");

        // ─── 6. Non-recursive drop — Drop() returns non-droppable (3.8) ──
        // PROOF: ResourceHandle.Drop() returns string — no further chaining
        Console.WriteLine("--- 6. Non-recursive drop baseline ---");
        {
            var handle = new ResourceHandle("baseline");
            Console.WriteLine($"  handle created");
            // scope-exit: handle.Drop() fires, returns string, string is not droppable, done
        }
        Console.WriteLine("  non-recursive drop done");

        // ─── 7. Explicit capture prevents recursive drop (3.7) ───────────
        // PROOF: user captures Drop() return value — no automatic recursive drop
        Console.WriteLine("--- 7. Explicit capture stops recursion ---");
        {
            var box = new TaskBox(async Helpers.FetchAsync("captured"));
            var innerTask = box.Drop();
            // PROOF: innerTask is Task<string>, user owns it, await it manually
            var innerResult = await innerTask;
            Console.WriteLine($"  manually awaited: {innerResult}");
        }

        Console.WriteLine("=== AsyncDropChaining Done ===");
    }

    // Exercises defect 1.2: sync method with only async functor expression
    public static void DemoAsyncFunctorScopeExit()
    {
        var task = async Helpers.ComputeSync(21);
        Console.WriteLine($"  async functor produced Task, value will be: 42");
        // scope-exit: task is Task<int> (linear), drop fires → await task → returns 42 (not droppable, done)
    }

    // Exercises defect 1.3: Task<Task<T>> recursive drop
    public static void DemoNestedTaskDrop()
    {
        // async functor on an async method in sync context: captures Task<int> into Task<Task<int>>
        var nested = async Helpers.SlowCompute(5);
        Console.WriteLine("  nested Task<Task<int>> created");
        // scope-exit: nested is Task<Task<int>> (relevant)
        //   drop fires → await nested → returns Task<int> (droppable!)
        //   recursive drop fires → await inner → returns 105 (not droppable, done)
    }

    // Exercises defect 1.3: custom linear type whose Drop returns a droppable
    public static void DemoCustomRecursiveDrop()
    {
        var box = new TaskBox(async Helpers.FetchAsync("boxed"));
        Console.WriteLine("  TaskBox created with inner async task");
        // scope-exit: box is linear, drop fires
        //   box.Drop() → returns Task<string> (relevant, has Drop)
        //   not assigned → recursive drop fires → await → returns "result-boxed" (not droppable, done)
    }
}
```

### Expected Output

```
=== AsyncDropChaining Sample ===
--- 1. Func auto-unwrap in sync context ---
  lambdaResult=lambda-test
  type is string (not Task): True
--- 2. Async lambda with async body ---
  FetchAsync(abc) executed
  fetchResult=result-abc
--- 3. Async functor scope-exit ---
  async functor produced Task, value will be: 42
  async functor scope-exit completed
--- 4. Recursive drop Task<Task<T>> ---
  nested Task<Task<int>> created
  nested task fully resolved
--- 5. Custom recursive drop ---
  TaskBox created with inner async task
  FetchAsync(boxed) executed
  TaskBox.Drop() called, yielding inner Task
  custom recursive drop completed
--- 6. Non-recursive drop baseline ---
  handle created
  ResourceHandle(baseline).Drop() called
  non-recursive drop done
--- 7. Explicit capture stops recursion ---
  FetchAsync(captured) executed
  TaskBox.Drop() called, yielding inner Task
  manually awaited: result-captured
=== AsyncDropChaining Done ===
```

### What This Sample Proves

| Section | Exercises | Would fail if broken |
|---------|-----------|---------------------|
| 1 | Defect 1.1 — Func auto-unwrap | `lambdaResult` would be `Task<string>`, comparison would fail |
| 2 | Defect 1.1 — Func auto-unwrap with async body | `fetchResult` would be `Task<string>` |
| 3 | Defect 1.2 — NeedsAsyncPropagationPass gap | Emitted C# would have `await` in non-async method → compile error |
| 4 | Defect 1.3 — Recursive drop on `Task<Task<T>>` | Inner task would never complete / be silently dropped |
| 5 | Defect 1.3 — Custom type recursive drop | `Task<string>` returned by `TaskBox.Drop()` would be abandoned |
| 6 | Regression 3.8 — Non-droppable return stops recursion | Baseline: string return from Drop() doesn't chain |
| 7 | Regression 3.7 — Explicit capture prevents recursion | User takes ownership, no automatic re-drop |
