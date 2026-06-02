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
