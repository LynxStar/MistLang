# Future Work

Tracked issues and improvements that don't belong to an active spec but should be addressed.

---

## Compiler Gaps

### Auto-await does not fire on async lambda/Func invocation in sync context

**Discovered:** 2026-06-01 (first-class-functions spec)

**Symptom:** In a sync context, calling an `async` method auto-unwraps `Task<T>` to `T`. But invoking an async *lambda* (or any `Func`-typed variable whose return type is `Task<T>`) does NOT auto-unwrap — you get `Task<T>` back.

```mist
public static async string AsyncMethod(int id) { return $"method-{id}"; }

public static void Main(string[] args)
{
    var asyncLambda = async (int id) => $"lambda-{id}";

    var methodResult = AsyncMethod(1);   // auto-unwrapped → string ✅
    var lambdaResult = asyncLambda(2);   // NOT unwrapped → Task<string> ❌
}
```

**Root cause:** The auto-await logic in `SemanticModelBuilder.ResolvePostfixSuffix` only fires for method symbols with `IsAsync` or `NeedsCSharpAsync`. Func-typed invocations go through the `IsFuncType` path which returns `GetFuncReturnType(funcType)` directly — it never checks whether the return type is `Task<T>` and should be unwrapped in a sync context.

**Fix:** In the Func invocation path (when `IsFuncType(targetType)` is true), after extracting the return type, check: if `!_isInAsyncContext && IsStandardTaskType(returnType)`, unwrap to the inner type (same as auto-await on method calls).

**Impact:** Medium. Affects any code that invokes async lambdas in sync context without explicit `await`.

---

### Scope-exit await not emitted for Task variables in sync context

**Discovered:** 2026-06-01 (first-class-functions spec)

**Symptom:** When a `Task<T>` value is stored in a variable in a sync context (e.g., captured via `async` functor or from an async lambda invocation), the live task tracking records it, but the emitter never generates scope-exit await code. The Task may never complete.

```mist
public static void Main(string[] args)
{
    var asyncLambda = async (int id) => $"lambda-{id}";
    var result = asyncLambda(2);  // Task<string> — tracked as live task
    // Scope exits here — but no await is emitted
}
```

**Emitted C#:**
```csharp
public static void Main(string[] args)
{
    var asyncLambda = async (int id) => $"lambda-{id}";
    var result = asyncLambda(2);
    // No await — Main is not async, live task is abandoned
}
```

**Root cause:** The `NeedsAsyncPropagationPass` only marks a method as `NeedsCSharpAsync` when it *calls* an async method. Storing a `Task<T>` in a variable (without calling an async method directly) doesn't trigger the propagation. Even though the semantic model's live task tracking records the variable, the emitter's scope-exit await logic only runs for methods that are already `async` — because you can't emit `await` in a non-async method.

**Relationship to auto-unwrap gap:** If the auto-unwrap gap above is fixed (lambda invocations auto-unwrap like method calls), this issue becomes moot for the lambda case — `var result = asyncLambda(2)` would give `string` directly. But the issue persists for the `async` functor case: `var task = async SyncMethod()` intentionally captures a `Task<T>` and the scope-exit await should ensure it completes.

**Fix options:**
1. Extend `NeedsAsyncPropagationPass` to also trigger when a method contains live task variables (any `Task<T>` local from the async functor).
2. For sync methods with live tasks, emit scope-exit as `.GetAwaiter().GetResult()` instead of `await` (blocking, but ensures completion).
3. Alternatively, treat the `async` functor in sync context as always triggering `NeedsCSharpAsync` on the containing method (since scope-exit await requires `async`).

**Impact:** Medium-high. Affects structured concurrency guarantees — live tasks in sync contexts may silently not complete.

---

### Scope-exit await not emitted at all (even in async context)

**Discovered:** 2026-06-01 (first-class-functions spec investigation)
**Pre-existing since:** structured-concurrency spec implementation

**Symptom:** The semantic model's live task tracking records `Task<T>` variables, but the emitter never generates scope-exit `await` statements at the end of the containing block — not in sync context, and not in async context either. The `StructuredConcurrency` sample's `DemoDiscardedAsyncScopeExit` method claims "scope-exit will await both tasks" in a comment, but the emitted C# just drops the tasks on the floor:

```mist
// Mist source (samples/StructuredConcurrency/Program.mist)
public static async void DemoDiscardedAsyncScopeExit()
{
    Helpers.LogAsync("task-a");
    Helpers.LogAsync("task-b");
    Console.WriteLine("  scope-exit will await both tasks");
}
```

```csharp
// Emitted C# — no scope-exit await generated
public static async System.Threading.Tasks.Task DemoDiscardedAsyncScopeExit()
{
    Helpers.LogAsync("task-a");
    Helpers.LogAsync("task-b");
    System.Console.WriteLine("  scope-exit will await both tasks");
}
```

**Root cause:** The emitter does not read the `LiveTaskInfo` data from the semantic model to generate scope-exit await statements. The tracking infrastructure exists in the semantic model but the emitter pass that would consume it and emit `await __task_N;` at block boundaries is either missing or incomplete.

**Additional concern — nested Tasks:** Even when scope-exit await is implemented, `await nested` on a `Task<Task<string>>` only unwraps one layer. The inner `Task<string>` is still unawaited. Structured concurrency should guarantee *all* layers complete. Options:
1. Recursive unwrap: `await await nested` (or a generated loop for arbitrary nesting)
2. Flatten at the type level: prevent `Task<Task<T>>` from being constructible (the `async` functor on an already-`Task<T>` value could return `Task<T>` instead of `Task<Task<T>>`)
3. Emit a helper like `await TaskExtensions.UnwrapAll(nested)` that recursively awaits

**Impact:** High. This is a core structured concurrency guarantee violation — "all tasks created in a scope complete before the scope exits" is not enforced.

---
