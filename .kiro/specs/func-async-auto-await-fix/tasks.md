# Implementation Plan

## Overview

Fix three interrelated defects in the structured concurrency pipeline: (1) Func auto-unwrap missing in sync context, (2) NeedsAsyncPropagationPass not seeding from async functor expressions, (3) recursive drop chaining not implemented in the emitter. Validated by a new AsyncDropChaining sample and property-based tests.

## Tasks

- [x] 1. Fix Defect 1: Func auto-unwrap in SemanticModelBuilder
  - **Property 1: Bug Condition** - Func Invocation Returns Task Instead of Unwrapped Type in Sync Context
  - **CRITICAL**: This test MUST FAIL on unfixed code - failure confirms the bug exists
  - **DO NOT attempt to fix the test or the code when it fails**
  - **NOTE**: This test encodes the expected behavior - it will validate the fix when it passes after implementation
  - **GOAL**: Surface counterexamples that demonstrate the Func invocation path does not auto-unwrap Task<T> to T in sync contexts
  - **Scoped PBT Approach**: Scope the property to Func-typed invocations with Task<T> return types in non-async methods
  - Test that invoking a Func whose return type is Task<T> in a sync context resolves to Task<T> instead of T (from Bug Condition: `expr IS FuncInvocation AND expr.funcType.returnType IS Task<T> AND NOT isInAsyncContext(expr)`)
  - Expected behavior after fix: expression type resolves to T (unwrapped), matching direct async method call behavior
  - Run test on UNFIXED code - expect FAILURE (confirms the bug: Func invocations return raw Task<T>)
  - Document counterexamples: e.g., `asyncLambda("test")` resolves to `Task<string>` instead of `string`
  - Mark task complete when test is written, run, and failure is documented
  - _Requirements: 1.1, 2.1_

- [x] 2. Fix Defect 2: NeedsAsyncPropagationPass seeding from async functor expressions
  - **Property 1: Bug Condition** - Async Functor Locals Do Not Seed NeedsCSharpAsync
  - **CRITICAL**: This test MUST FAIL on unfixed code - failure confirms the bug exists
  - **DO NOT attempt to fix the test or the code when it fails**
  - **NOTE**: This test encodes the expected behavior - it will validate the fix when it passes after implementation
  - **GOAL**: Surface counterexamples that demonstrate sync methods with only async functor locals are not marked NeedsCSharpAsync
  - **Scoped PBT Approach**: Scope to sync methods containing `async SyncMethod()` expressions but no direct async calls or explicit await
  - Test that a sync method with only async functor expressions (producing Task<T> locals) has NeedsCSharpAsync = false (from Bug Condition: `NOT method.isAsync AND method.body.contains(AsyncFunctorExpression) AND NOT method.body.contains(DirectAsyncMethodCall) AND NOT method.body.contains(ExplicitAwait)`)
  - Expected behavior after fix: NeedsCSharpAsync = true, enabling valid scope-exit await emission
  - Run test on UNFIXED code - expect FAILURE (confirms the gap: NeedsCSharpAsync is not set)
  - Document counterexamples: e.g., `void Foo() { var task = async SyncMethod(); }` lacks async keyword in emitted C#
  - Mark task complete when test is written, run, and failure is documented
  - _Requirements: 1.2, 2.2_

- [x] 3. Fix Defect 3: Recursive drop chaining in the emitter
  - **Property 1: Bug Condition** - Drop Returns Droppable Type But Chain Does Not Continue
  - **CRITICAL**: This test MUST FAIL on unfixed code - failure confirms the bug exists
  - **DO NOT attempt to fix the test or the code when it fails**
  - **NOTE**: This test encodes the expected behavior - it will validate the fix when it passes after implementation
  - **GOAL**: Surface counterexamples that demonstrate unassigned droppable Drop() returns are not recursively dropped
  - **Scoped PBT Approach**: Scope to auto-drop sites where Drop() returns a linear/relevant type with its own Drop()
  - Test that emitted finally clauses contain only single-layer drop without chaining (from Bug Condition: `dropSite IS AutoDropEmission AND dropSite.dropReturnType.hasDrop = true AND dropSite.dropReturnType.discipline IN {Linear, Relevant} AND NOT dropSite.returnValueCapturedByUser`)
  - Expected behavior after fix: chained drop emission — e.g., `await (await nested);` for Task<Task<T>>, or `await localName.Drop();` for custom type returning Task
  - Affects four emitter methods: EmitDroppableLocalWithWrapping, EmitDiscardedTaskWithWrapping, EmitRemainingFieldDropsIfManualDrop, EmitWithBlockDropCall
  - Run test on UNFIXED code - expect FAILURE (confirms single-layer emission)
  - Document counterexamples: e.g., `Task<Task<int>>` local emits `await nested;` without chaining inner Task
  - Mark task complete when test is written, run, and failure is documented
  - _Requirements: 1.3, 2.3, 2.4, 2.5_

- [x] 4. Write preservation property tests (BEFORE implementing fixes)
  - **Property 2: Preservation** - Existing Auto-Await, Func, Propagation, and Drop Behaviors
  - **IMPORTANT**: Follow observation-first methodology
  - Observe: direct async method call in sync context auto-unwraps Task<T> → T on unfixed code
  - Observe: Func invocation in async context returns Task<T> (no unwrap) on unfixed code
  - Observe: Func invocation with non-Task return type (e.g., Func<int, string>) returns string on unfixed code
  - Observe: methods with direct async calls or explicit await get NeedsCSharpAsync = true on unfixed code
  - Observe: simple linear/relevant Drop() returning void emits single `localName.Drop();` on unfixed code
  - Observe: Task<T> local at scope exit emits `await localName;` on unfixed code
  - Observe: explicit capture `var x = v.Drop()` does NOT chain — x gets its own scope-exit drop on unfixed code
  - Write property-based tests capturing all observed preservation behaviors:
    - For all direct async method calls in sync context: expression type = T (Preservation Req 3.2)
    - For all Func invocations in async context: expression type = Task<T> (Preservation Req 3.1)
    - For all Func invocations with non-Task return: expression type = Func return type (Preservation Req 3.3)
    - For all methods with existing async seeds: NeedsCSharpAsync = true (Preservation Req 3.4)
    - For all simple drops (void or non-droppable return): single-layer emission unchanged (Preservation Req 3.8)
    - For all explicit captures: no recursive chaining (Preservation Req 3.7)
  - Verify all preservation tests PASS on UNFIXED code
  - _Requirements: 3.1, 3.2, 3.3, 3.4, 3.5, 3.6, 3.7, 3.8_

- [x] 5. Implement all three defect fixes

  - [x] 5.1 Implement Defect 1 fix: Func auto-unwrap in SemanticModelBuilder
    - In `ResolvePostfixSuffixSkipArguments` (~line 20520): after `var returnType = GetFuncReturnType(funcCt);`, add: if `!_isInAsyncContext && IsStandardTaskType(returnType)`, then `returnType = UnwrapTaskType(returnType);`
    - In inline `ResolvePostfixSuffix` (~line 20860): identical change — after obtaining returnType from GetFuncReturnType, apply same async-unwrap check
    - _Bug_Condition: isBugCondition(input) where expr IS FuncInvocation AND returnType IS Task<T> AND NOT isInAsyncContext_
    - _Expected_Behavior: expressionType = UnwrapTaskType(funcReturnType) in sync context_
    - _Preservation: Async-context Func invocations continue returning Task<T>; non-Task Func returns unchanged_
    - _Requirements: 1.1, 2.1, 3.1, 3.3_

  - [x] 5.2 Implement Defect 2 fix: NeedsAsyncPropagationPass seeding
    - In `WalkMethodBody` in `NeedsAsyncPropagationPass.cs`: add a case for `AsyncFunctorExpressionUnaryNode` (or the AST node representing `async expr`) that sets `needsAsync = true`
    - This is seed rule 4: async functor expressions in a method body produce Task-typed locals requiring scope-exit await
    - _Bug_Condition: isBugCondition(input) where method has AsyncFunctorExpression but no other async seeds_
    - _Expected_Behavior: method.NeedsCSharpAsync = true_
    - _Preservation: Existing seeds (declared async, sync-calls-async, explicit await) continue working_
    - _Requirements: 1.2, 2.2, 3.4_

  - [x] 5.3 Implement Defect 3 fix: Recursive drop chaining in emitter
    - Extract helper method `EmitChainedDropExpression(IType type, string baseExpr)` that:
      - Resolves the type's DropReturnType
      - If DropReturnType is null or not droppable → emit just `baseExpr`
      - If DropReturnType is task-like → emit `await ({baseExpr})` and recurse with inner T
      - If DropReturnType is droppable but not task-like → emit `({baseExpr}).Drop()` and recurse
      - Returns the full chained expression string
    - Apply recursive chaining in `EmitDroppableLocalWithWrapping` finally clause
    - Apply recursive chaining in `EmitDiscardedTaskWithWrapping` finally clause
    - Apply recursive chaining in `EmitRemainingFieldDropsIfManualDrop`
    - Apply recursive chaining in `EmitWithBlockDropCall`
    - Key invariant: explicit capture (`var x = v.Drop()`) does NOT trigger recursion — x gets its own scope-exit drop as a named local
    - _Bug_Condition: isBugCondition(input) where Drop() returns droppable type, unassigned_
    - _Expected_Behavior: chained drop expression emitted recursively until non-droppable_
    - _Preservation: void-returning Drop() and non-droppable returns terminate chain; explicit captures not affected_
    - _Requirements: 1.3, 2.3, 2.4, 2.5, 3.7, 3.8_

  - [x] 5.4 Verify bug condition exploration tests now pass
    - **Property 1: Expected Behavior** - All Three Defects Resolved
    - **IMPORTANT**: Re-run the SAME tests from tasks 1, 2, 3 - do NOT write new tests
    - The tests from tasks 1-3 encode the expected behavior
    - When these tests pass, they confirm the expected behavior is satisfied for all three defects
    - Run bug condition exploration tests from tasks 1, 2, 3
    - **EXPECTED OUTCOME**: Tests PASS (confirms bugs are fixed)
    - _Requirements: 2.1, 2.2, 2.3_

  - [x] 5.5 Verify preservation tests still pass
    - **Property 2: Preservation** - Existing Behaviors Unchanged
    - **IMPORTANT**: Re-run the SAME tests from task 4 - do NOT write new tests
    - Run preservation property tests from task 4
    - **EXPECTED OUTCOME**: Tests PASS (confirms no regressions)
    - Confirm all preservation tests still pass after all three fixes (no regressions)
    - _Requirements: 3.1, 3.2, 3.3, 3.4, 3.5, 3.6, 3.7, 3.8_

- [x] 6. Create AsyncDropChaining sample app
  - Create `samples/AsyncDropChaining/mist.toml` with `name = "AsyncDropChaining"`
  - Create `samples/AsyncDropChaining/Program.mist` with the verification sample from bugfix.md
  - The sample exercises all three defects and would fail if any fix is broken:
    - Sections 1-2: Func auto-unwrap in sync context (Defect 1) — lambdaResult would be Task<string> if broken
    - Section 3: Async functor scope-exit (Defect 2) — emitted C# would have await in non-async method
    - Sections 4-5: Recursive drop chaining (Defect 3) — inner tasks would be silently abandoned
    - Sections 6-7: Preservation baselines (non-recursive drop, explicit capture)
  - Add `AsyncDropChaining.Program.RunDemo()` call to `samples/AllFeatures/Program.mist`
  - Add `AsyncDropChaining = { path = "../AsyncDropChaining" }` reference to `samples/AllFeatures/mist.toml`
  - Verify the sample compiles and produces the expected output from the bugfix requirements document
  - _Requirements: 2.1, 2.2, 2.3, 2.4, 2.5, 3.7, 3.8_

- [x] 7. Add property-based tests for all three defects
  - Create test class(es) in `Test/Mist.Tests/` covering all three defects with property-based tests
  - **Zero warnings policy**: ensure no build warnings from new test code
  - **Test process spawning policy**: use in-process compilation via shared CompilerDriver — no per-iteration process spawning
  - **Batching**: if end-to-end execution is needed, batch all scenarios into a single compile+run cycle
  - Tests must cover:
    - Defect 1: Generate Func types with varying Task<T> return types across sync/async contexts; verify type resolution
    - Defect 2: Generate method bodies with varying async functor, direct async, and explicit await combinations; verify NeedsCSharpAsync
    - Defect 3: Generate type hierarchies with varying Drop() return types (void, non-droppable, Task<T>, custom linear); verify emitted drop chains are correct and terminate
  - Preservation properties:
    - Generate non-bug-condition inputs; verify emitted output is identical pre/post fix
    - Direct async calls still unwrap, async-context Func stays Task<T>, non-Task Func unchanged
    - Simple drops still emit single layer, explicit captures not affected
  - _Requirements: 1.1, 1.2, 1.3, 2.1, 2.2, 2.3, 2.4, 2.5, 3.1, 3.2, 3.3, 3.4, 3.5, 3.6, 3.7, 3.8_

- [x] 8. Update TODO.md to remove resolved items
  - Remove "Auto-await does not fire on async lambda/Func invocation in sync context" section (resolved by Defect 1 fix)
  - Remove "Scope-exit await not emitted for Task variables in sync context" section (resolved by Defect 2 fix)
  - Remove "Scope-exit await not emitted at all (even in async context)" section (resolved by Defects 2+3 fix — async functor seeding + recursive drop chaining address the core issue)
  - Verify the remaining TODO.md content is still accurate and no dangling references exist
  - _Requirements: 2.1, 2.2, 2.3_

- [x] 9. Checkpoint - Ensure all tests pass
  - Run full test suite: `dotnet test` on the solution
  - Verify zero build warnings (zero-warnings policy)
  - Verify AsyncDropChaining sample compiles and produces expected output
  - Verify AllFeatures sample still compiles and runs (regression check on all other samples)
  - Verify no existing tests are broken by the changes
  - Ensure all tests pass, ask the user if questions arise.

## Task Dependency Graph

```json
{
  "waves": [
    {
      "name": "Exploration & Preservation Tests",
      "tasks": ["1", "2", "3", "4"]
    },
    {
      "name": "Implementation",
      "tasks": ["5"]
    },
    {
      "name": "Sample & Property Tests",
      "tasks": ["6", "7", "8"]
    },
    {
      "name": "Checkpoint",
      "tasks": ["9"]
    }
  ]
}
```

## Notes

- Tasks 1-4 MUST be completed before task 5 (exploration + preservation tests run on unfixed code)
- Task 5 implements all three fixes and re-runs tests from tasks 1-4 to verify
- Task 6 (sample) and task 7 (PBT) depend on the fixes being in place
- Task 8 removes resolved TODO items after the fixes are validated
- Task 9 is the final checkpoint ensuring everything passes together
- Zero warnings policy: no build warnings allowed in any new code
- Test process spawning: use in-process compilation via shared CompilerDriver, never spawn 100+ processes
- Sample app integrity: AsyncDropChaining must genuinely exercise all three fixes and would fail if any fix is broken

## Standard pipeline completion closeout (2026-10-03)

Async functors over promoted synchronous methods use scheduled async lambdas that await completion,
including immediate promoted argument expressions. Deferred bodies do not add async modifiers.
AsyncLiftDecisionTests (Tier 1) and ExtensionAsyncPromotionTests (Tier 2) hold these lowering decisions;
AsyncPromotionRuntimeBoundaryTests holds delayed completion/drop behavior at the Tier 4 runtime seam.
Extension/local scope obligations and canonical generic callee identities participate in promotion.
Synchronous callable and abstract boundaries retain their contracts through completion adapters;
absent optional receivers skip invocation before awaiting. Getter/constructor policy remains open
under emitter register order 170.
