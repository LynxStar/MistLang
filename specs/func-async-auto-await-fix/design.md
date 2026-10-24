# Func/Async Auto-Await and Recursive Drop Chaining Bugfix Design

## Overview

Three interrelated defects in the compiler's structured concurrency pipeline produce invalid C# output when async lambdas (Func-typed variables) are invoked in sync contexts, when sync methods contain only async functor expressions, and when `Drop()` returns a droppable type that isn't captured. The fix targets the semantic model's Func invocation path, the `NeedsAsyncPropagationPass` seeding logic, and the emitter's auto-drop code generation — all with minimal surface area to avoid regressions on the well-established direct-async-call and simple-drop paths.

## Glossary

- **Bug_Condition (C)**: The set of inputs that trigger one of the three defects — Func invocations returning Task in sync context, async functor locals without NeedsCSharpAsync propagation, or unassigned droppable Drop() return values
- **Property (P)**: The desired behavior — Func auto-unwrap matches direct async call behavior, async functor methods get the `async` keyword, and unassigned droppable Drop() returns are recursively dropped
- **Preservation**: Existing behaviors that must remain unchanged — async-context Func invocations, direct async method auto-unwrap, non-Task Func returns, existing propagation seeds, discarded task wrapping, explicit capture ownership
- **`SemanticModelBuilder`**: The file `Source/Mist/Semantics/Model/SemanticModelBuilder.cs` that resolves expression types during semantic analysis
- **`NeedsAsyncPropagationPass`**: The file `Source/Mist/Backends/CSharpTranspiler/NeedsAsyncPropagationPass.cs` that computes which methods need the C# `async` keyword
- **`CSharpEmitter`**: The file `Source/Mist/Backends/CSharpTranspiler/CSharpEmitter.cs` that generates C# source from the Mist AST
- **`_isInAsyncContext`**: Thread-static flag in SemanticModelBuilder that suppresses auto-unwrap inside async method/lambda bodies
- **`IsStandardTaskType`**: Helper that identifies `Standard.Task<T>` constructed types
- **`UnwrapTaskType`**: Helper that extracts `T` from `Task<T>`
- **`IsTaskLikeType`**: Emitter helper identifying Task/ValueTask for await-vs-Drop emission
- **`DropReturnType`**: Property on ClassType/StructType storing the return type of the type's `Drop()` method (null = void)
- **`ShouldAutoInsertScopeExitDrop`**: Emitter predicate that identifies linear/relevant types with Drop() for scope-exit wrapping

## Bug Details

### Bug Condition

The bug manifests across three distinct code paths that share a common theme: the compiler fails to fully honor the structured concurrency invariants when the standard "direct async method call" path is not taken.

**Formal Specification:**
```
FUNCTION isBugCondition(input)
  INPUT: input of type CompilationUnit
  OUTPUT: boolean
  
  // Defect 1: Func invocation in sync context with Task return type
  LET funcInvocationsInSyncContext = findAll(input, expr =>
    expr IS FuncInvocation
    AND expr.funcType.returnType IS Task<T>
    AND NOT isInAsyncContext(expr)
  )
  
  // Defect 2: Sync method with async functor locals but no other async seeds
  LET unseedeMethods = findAll(input, method =>
    NOT method.isAsync
    AND method.body.contains(AsyncFunctorExpression)
    AND NOT method.body.contains(DirectAsyncMethodCall)
    AND NOT method.body.contains(ExplicitAwait)
  )
  
  // Defect 3: Auto-drop where Drop() returns a droppable type, unassigned
  LET unchainedDrops = findAll(input, dropSite =>
    dropSite IS AutoDropEmission
    AND dropSite.dropReturnType IS NOT NULL
    AND dropSite.dropReturnType.hasDrop = true
    AND dropSite.dropReturnType.discipline IN {Linear, Relevant}
    AND NOT dropSite.returnValueCapturedByUser
  )
  
  RETURN funcInvocationsInSyncContext.count > 0
         OR unseedeMethods.count > 0
         OR unchainedDrops.count > 0
END FUNCTION
```

### Examples

- **Defect 1**: `var asyncLambda = async (string s) => s; var result = asyncLambda("x");` — in a sync method, `result` is typed as `Task<string>` instead of `string`
- **Defect 1**: `Func<int, Task<int>> f = async (x) => x * 2; var v = f(5);` — `v` should be `int`, gets `Task<int>`
- **Defect 2**: `void Foo() { var task = async SyncMethod(21); }` — method lacks `async` keyword, scope-exit `await task;` in try/finally is invalid C#
- **Defect 3**: `Task<Task<int>> nested = ...; // scope-exit: await nested → Task<int>, but that Task<int> is silently discarded`
- **Defect 3**: `TaskBox box = ...; // scope-exit: box.Drop() → Task<string>, but Task<string> is silently discarded`
- **Non-bug (3.7)**: `var inner = outer.Drop();` — user captures return, no recursive chaining

## Expected Behavior

### Preservation Requirements

**Unchanged Behaviors:**
- Async-context Func invocations must continue returning `Task<T>` (no unwrap when `_isInAsyncContext` is true)
- Direct async method calls in sync context must continue unwrapping via the existing `IsAsync` check + `UnwrapTaskType` path
- Non-Task Func return types (e.g., `Func<int, string>`) must continue returning the Func's return type directly
- Existing NeedsAsyncPropagationPass seeds (declared async, sync-calls-async, explicit await) must continue working
- Discarded task wrapping via `DiscardedTaskInfo` / `EmitDiscardedTaskWithWrapping` must continue functioning
- Named `Task<T>` locals already handled by `TryGetScopeExitDropLocalName` must continue emitting `await localName;`
- Explicit capture of Drop() return value (`var x = v.Drop()`) must NOT trigger recursive drop — user owns the value
- `Drop()` returning void or a non-droppable type must terminate the chain (base case)
- `ShouldAutoInsertScopeExitDrop` logic must continue correctly identifying linear/relevant types

**Scope:**
All inputs that do NOT match the three defect conditions should be completely unaffected. This includes:
- All direct async method calls (already handled by `targetSymbol is Symbol { IsAsync: true }` path)
- All Func invocations in async context (gated by `_isInAsyncContext`)
- All non-Func invocations (gated by `IsFuncType` check)
- All methods already correctly seeded by rules 1-3 of NeedsAsyncPropagationPass
- All simple drops where Drop() returns void or a non-droppable type

## Hypothesized Root Cause

Based on the bug description and code analysis, the root causes are:

1. **Defect 1 — Missing async-unwrap in Func invocation path**: The `ResolvePostfixSuffixSkipArguments` (line ~20520) and inline `ResolvePostfixSuffix` (line ~20860) methods both have:
   ```csharp
   if (IsFuncType(targetType) && targetType is ConstructedType funcCt)
   {
       var returnType = GetFuncReturnType(funcCt);
       expressionTypes[suffix] = returnType;
       return returnType;
   }
   ```
   This returns the raw `Task<T>` without checking `_isInAsyncContext` or calling `UnwrapTaskType`. The direct-async-call path (around line 20492) does check these conditions, but the Func path was added separately and doesn't replicate the logic.

2. **Defect 2 — Incomplete NeedsAsyncPropagationPass seeding**: The pass seeds from three sources: (1) `method.IsAsync`, (2) sync methods calling async methods, (3) explicit `AwaitExpressionUnaryNode`. But `async SyncMethod()` (async functor expression) produces a `Task<T>` local without going through any of these seed paths. The local triggers `ShouldAutoInsertScopeExitDrop` → scope-exit `await` in the emitter, but the containing method never gets `NeedsCSharpAsync = true`, so the emitted C# has `await` in a non-async method.

3. **Defect 3 — Single-layer drop emission**: All four auto-drop sites (`EmitDroppableLocalWithWrapping`, `EmitDiscardedTaskWithWrapping`, `EmitRemainingFieldDropsIfManualDrop`, `EmitWithBlockDropCall`) emit exactly one drop action: either `await localName;` or `localName.Drop();`. None check whether the *result* of that drop is itself droppable. For `Task<Task<T>>`: `await nested` yields `Task<T>`, which is itself relevant with Drop — but that value is discarded. For custom types: `box.Drop()` yields `Task<string>`, which is relevant — but that value is discarded.

4. **Interaction between defects**: Defect 2 is only observable when defect 1 or async functor expressions create Task-typed locals. Defect 3 amplifies defect 2 by making even correctly-typed nested tasks silently lose their inner values.

## Correctness Properties

Property 1: Bug Condition - Func Auto-Unwrap in Sync Context

_For any_ Func invocation in a sync context where the Func's return type is `Task<T>` (or `ValueTask<T>`), the fixed semantic model SHALL resolve the invocation expression's type to `T` (not `Task<T>`), consistent with direct async method call auto-await behavior.

**Validates: Requirements 2.1**

Property 2: Bug Condition - NeedsAsyncPropagationPass Seeding from Async Functor Locals

_For any_ sync method whose body contains an async functor expression (`async SyncMethod()`) producing a `Task<T>`-typed local, and no other async seeds (no direct async calls, no explicit await), the fixed pass SHALL mark the method as `NeedsCSharpAsync = true` so that scope-exit `await` in try/finally emits valid C#.

**Validates: Requirements 2.2**

Property 3: Bug Condition - Recursive Drop Chaining

_For any_ auto-drop site where `Drop()` returns a type that is itself droppable (linear or relevant with HasDrop) and the return value is not captured by the user, the fixed emitter SHALL emit chained drop operations recursively until reaching a non-droppable or void return type.

**Validates: Requirements 2.3, 2.4, 2.5**

Property 4: Preservation - Func Invocation in Async Context

_For any_ Func invocation inside an async context (async method or async lambda body), the fixed code SHALL produce the same result type as the original code — `Task<T>` without unwrapping — preserving async-context semantics.

**Validates: Requirements 3.1**

Property 5: Preservation - Direct Async Method Call

_For any_ direct async method call in a sync context, the fixed code SHALL continue to unwrap `Task<T>` to `T` via the existing `IsAsync` symbol check path, producing the same behavior as the original code.

**Validates: Requirements 3.2, 3.4**

Property 6: Preservation - Non-Task Func Returns and Explicit Capture

_For any_ Func invocation with a non-Task return type, the fixed code SHALL return the Func's return type unchanged. And _for any_ explicit capture of a Drop() return value into a variable, the fixed code SHALL NOT trigger recursive drop chaining.

**Validates: Requirements 3.3, 3.7, 3.8**

## Fix Implementation

### Changes Required

Assuming our root cause analysis is correct:

**File**: `Source/Mist/Semantics/Model/SemanticModelBuilder.cs`

**Function**: `ResolvePostfixSuffixSkipArguments` (~line 20520)

**Specific Changes**:
1. **Add async-unwrap after GetFuncReturnType**: After `var returnType = GetFuncReturnType(funcCt);`, insert a check: if `!_isInAsyncContext && IsStandardTaskType(returnType)`, then `returnType = UnwrapTaskType(returnType);`. This mirrors the direct-async-call unwrap logic.

**Function**: inline `ResolvePostfixSuffix` (~line 20860)

2. **Same async-unwrap logic**: Identical change in the second Func invocation path — after obtaining `returnType` from `GetFuncReturnType`, check for Task unwrap in sync context.

---

**File**: `Source/Mist/Backends/CSharpTranspiler/NeedsAsyncPropagationPass.cs`

**Function**: `WalkMethodBody`

**Specific Changes**:
3. **Detect AsyncFunctorExpressionUnaryNode**: Add a case for `AsyncFunctorExpressionUnaryNode` (or the AST node representing `async expr`) that sets `needsAsync = true`. This is seed rule 4: async functor expressions in a method body produce Task-typed locals that require scope-exit await.

4. **Detect Task/ValueTask-typed variable declarations from async functors**: When encountering a variable declaration whose inferred type is `Task<T>` or `ValueTask<T>` (produced by an async functor), mark `needsAsync = true`. This handles the case where the async functor node itself might not be directly visible but its result flows into a local that triggers scope-exit await.

---

**File**: `Source/Mist/Backends/CSharpTranspiler/CSharpEmitter.cs`

**Function**: `EmitDroppableLocalWithWrapping`

**Specific Changes**:
5. **Recursive drop emission**: Replace the single `await localName;` or `localName.Drop();` in the finally clause with a recursive emission helper. The helper:
   - If `isTaskLike`: emit `await localName;` — but then check if the Task's inner type (from `DropReturnType` of the constructed type's generic definition) is itself droppable. If so, emit `await (await localName);` (nested await for `Task<Task<T>>`), continuing until the inner type is non-droppable.
   - If not task-like: emit `localName.Drop();` — but then check if `DropReturnType` of the local's type is droppable. If so, chain: e.g., `await localName.Drop();` when Drop returns Task, or `localName.Drop().Drop();` when Drop returns another linear type with Drop.
   - Recursion terminates when `DropReturnType` is null (void) or the return type is not linear/relevant with HasDrop.

**Function**: `EmitDiscardedTaskWithWrapping`

6. **Same recursive logic**: Apply the same chaining in the discarded task finally clause. Currently emits `await __task_N;` — needs to chain if the inner type is droppable.

**Function**: `EmitRemainingFieldDropsIfManualDrop`

7. **Same recursive logic**: For each field drop, apply chaining when the field's `DropReturnType` yields a droppable type.

**Function**: `EmitWithBlockDropCall`

8. **Same recursive logic**: Apply chaining for with-block drops when the bound type's Drop returns a droppable value.

9. **Key invariant**: The recursive chaining MUST NOT fire when the user explicitly captures the Drop() return value (`var x = v.Drop()`). This is already naturally handled because explicit captures go through the variable declaration path (the return value gets its OWN scope-exit drop as a named local), not through the auto-drop emission in the finally clause. The recursion only applies to the *emitted* finally-clause code where the return value has no variable binding.

10. **Implementation approach for chaining**: Extract a helper method (e.g., `EmitChainedDropExpression(IType type, string baseExpr)`) that:
    - Resolves the type's DropReturnType
    - If DropReturnType is null or not droppable → emit just `baseExpr`
    - If DropReturnType is task-like → emit `await ({baseExpr})` and recurse with the inner T
    - If DropReturnType is droppable but not task-like → emit `({baseExpr}).Drop()` and recurse
    - Returns the full chained expression string

## Testing Strategy

### Validation Approach

The testing strategy follows a two-phase approach: first, surface counterexamples that demonstrate the bug on unfixed code, then verify the fix works correctly and preserves existing behavior.

### Exploratory Bug Condition Checking

**Goal**: Surface counterexamples that demonstrate the bug BEFORE implementing the fix. Confirm or refute the root cause analysis. If we refute, we will need to re-hypothesize.

**Test Plan**: Write unit tests that compile Mist source containing each defect scenario, inspect the semantic model's expression types and NeedsCSharpAsync flags, and verify the emitted C# output. Run these tests on the UNFIXED code to observe failures.

**Test Cases**:
1. **Func Auto-Unwrap Test**: Compile a sync method that invokes an async lambda; assert the expression type is `Task<string>` (proving defect exists on unfixed code — will fail after fix, which is expected)
2. **NeedsAsyncPropagationPass Gap Test**: Compile a sync method with only `var task = async SyncMethod();`; assert `NeedsCSharpAsync` is false (proving the gap on unfixed code)
3. **Recursive Drop Missing Test**: Compile a method with a `Task<Task<int>>` local at scope exit; assert emitted C# contains only single `await` (proving single-layer emission on unfixed code)
4. **Custom Type Recursive Drop Test**: Compile a linear type whose Drop() returns Task<string>; assert emitted finally clause doesn't chain (proving defect on unfixed code)

**Expected Counterexamples**:
- Expression type for Func invocation in sync context is `Task<T>` instead of `T`
- `NeedsCSharpAsync` flag is false for methods containing only async functor locals
- Emitted finally clause contains `await nested;` without chaining for inner Task
- Possible causes confirmed: missing unwrap in Func path, missing seed rule, single-emission in finally

### Fix Checking

**Goal**: Verify that for all inputs where the bug condition holds, the fixed function produces the expected behavior.

**Pseudocode:**
```
FOR ALL input WHERE isBugCondition(input) DO
  model := buildSemanticModel_fixed(input)
  emitted := emitCSharp_fixed(model)
  
  // Defect 1: Func type resolution
  IF input.hasFuncInvocationInSyncContext THEN
    ASSERT expressionType(funcInvocation) = UnwrapTaskType(funcReturnType)
  END IF
  
  // Defect 2: NeedsAsync propagation
  IF input.hasSyncMethodWithAsyncFunctorOnly THEN
    ASSERT method.NeedsCSharpAsync = true
  END IF
  
  // Defect 3: Recursive drop
  IF input.hasDropReturningDroppable THEN
    ASSERT emitted.finallyClause CONTAINS chainedDropExpression
  END IF
END FOR
```

### Preservation Checking

**Goal**: Verify that for all inputs where the bug condition does NOT hold, the fixed function produces the same result as the original function.

**Pseudocode:**
```
FOR ALL input WHERE NOT isBugCondition(input) DO
  ASSERT buildSemanticModel_original(input).expressionTypes 
       = buildSemanticModel_fixed(input).expressionTypes
  ASSERT emitCSharp_original(input) = emitCSharp_fixed(input)
END FOR
```

**Testing Approach**: Property-based testing is recommended for preservation checking because:
- It generates many test cases automatically across the input domain
- It catches edge cases that manual unit tests might miss
- It provides strong guarantees that behavior is unchanged for all non-buggy inputs

**Test Plan**: Observe behavior on UNFIXED code first for non-bug inputs (direct async calls, non-Task Func, async-context Func, simple drops), then write property-based tests capturing that behavior.

**Test Cases**:
1. **Direct Async Call Preservation**: Verify direct async method calls in sync context continue unwrapping Task<T> → T after the fix
2. **Async Context Func Preservation**: Verify Func invocations inside async methods/lambdas continue returning Task<T> unchanged
3. **Non-Task Func Preservation**: Verify Func<int, string> invocations return string unchanged
4. **Existing NeedsAsync Seeds Preservation**: Verify methods with direct async calls or explicit await still get NeedsCSharpAsync
5. **Simple Drop Preservation**: Verify types with void-returning Drop() emit single `localName.Drop();` unchanged
6. **Explicit Capture Preservation**: Verify `var x = v.Drop()` does not trigger recursive chaining — x gets its own scope-exit drop as a named local

### Unit Tests

- Test Func invocation type resolution for async lambda in sync context (unwraps to T)
- Test Func invocation type resolution for async lambda in async context (stays Task<T>)
- Test Func invocation type resolution for non-Task return type (unchanged)
- Test NeedsAsyncPropagationPass with async functor expression only (seeds correctly)
- Test NeedsAsyncPropagationPass with Task-typed variable declarations (seeds correctly)
- Test emitted drop chaining for Task<Task<T>> (double await)
- Test emitted drop chaining for custom type → Task (Drop() then await)
- Test emitted drop termination for non-droppable Drop() return type
- Test explicit capture does not chain

### Property-Based Tests

- Generate random Func type signatures (with and without Task return types) across async/sync contexts; verify type resolution correctness
- Generate random method bodies with varying combinations of async calls, async functors, and explicit awaits; verify NeedsCSharpAsync is set correctly
- Generate random type hierarchies with varying Drop() return types; verify emitted drop chains are correct and terminate
- Generate random programs matching non-bug conditions; verify emitted output is identical pre/post fix

### Integration Tests

- Compile and run the `AsyncDropChaining` verification sample end-to-end
- Verify the sample produces the exact expected output
- Verify existing samples continue to compile and produce their expected output (regression)
