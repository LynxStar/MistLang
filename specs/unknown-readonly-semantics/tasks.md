# Implementation Tasks

## Task 1: Add `IsClosedGenericWithUnknown` helper to ReadonlyAnalyzer

- [x] Add `IsClosedGenericWithUnknown(IType?)` static method to `ReadonlyAnalyzer`
- [x] Add `ContainsUnknownTypeArgument(ConstructedType)` recursive helper
- [x] Add `ContainsOpenTypeParameter(ConstructedType)` recursive helper
- [x] Returns `true` only when the type is a `ConstructedType` with at least one `UnknownType` argument (at any depth) AND no `TypeParameterType` arguments (at any depth)
- [x] Unit test: `List<unknown>` → true
- [x] Unit test: `Map<string, unknown>` → true
- [x] Unit test: `Optional<List<unknown>>` → true (nested)
- [x] Unit test: `List<int>` → false
- [x] Unit test: `List<T>` → false (open type parameter)
- [x] Unit test: `Map<T, unknown>` → false (has open type parameter)

**Requirements:** 2.2, 2.3, 8.1–8.5

## Task 2: Add fifth analysis pass for Closed_Generic_With_Unknown access

- [x] Add `AnalyzeClosedGenericWithUnknownAccess(ISymbol, ISemanticModel, DiagnosticBag)` method
- [x] Add walk methods mirroring the constrained-type-param pattern: `WalkBlockForClosedUnknownViolations`, `WalkStatementForClosedUnknownViolations`, `WalkNodeForClosedUnknownViolations`
- [x] Add `CheckClosedUnknownPostfix(PostfixNode, ISemanticModel, DiagnosticBag)` — checks if target type is `IsClosedGenericWithUnknown`, then validates each suffix
- [x] Add `CheckClosedUnknownAssignment(AssignmentNode, ISemanticModel, DiagnosticBag)` — checks if assignment target goes through a `Closed_Generic_With_Unknown` receiver
- [x] Register the fifth pass in `Analyze()` after the existing fourth pass
- [x] Add `ClosedUnknownNonReadonlyAccess` diagnostic ID constant (`MIST_TC_0062`)

**Requirements:** 2.1, 2.4, 4.1–4.6

## Task 3: Implement suffix validation logic

- [x] `MethodCallSuffix`: resolve symbol, check `ReadonlyStatus` — emit MIST_TC_0062 if not readonly
- [x] `MemberAccessSuffix` (field): always permit reads; emit MIST_TC_0062 for field writes
- [x] `MemberAccessSuffix` (property/indexer): check `ReadonlyStatus` of getter — emit MIST_TC_0062 if not readonly
- [x] `IndexSuffix` (read): check indexer getter's `ReadonlyStatus` — emit MIST_TC_0062 if not readonly
- [x] `IndexSuffix` (write): always emit MIST_TC_0062 (setters are never callable in readonly context)
- [x] Property setter calls: always emit MIST_TC_0062
- [x] Diagnostic message format: `"Cannot call non-readonly {kind} '{name}' on '{typeDisplayName}': the type is treated as readonly because it contains an 'unknown' type argument."`
- [x] Ensure subsequent suffixes after the first are checked against the result type (not the original receiver type) — if the result type is also a `Closed_Generic_With_Unknown`, continue enforcing

**Requirements:** 2.1, 4.1–4.3, 4.5

## Task 4: Ensure variable reassignment is not blocked

- [x] Verify that the fifth pass only fires on `PostfixNode` suffixes and `AssignmentNode` targets that go through member access — bare variable reassignment (`myList = expr`) does not trigger the check
- [x] Unit test: `List<unknown> xs = new List<int>(); xs = new List<string>();` — no MIST_TC_0062
- [x] Unit test: passing `List<unknown>` as argument to a method — no MIST_TC_0062 at the call site

**Requirements:** 3.1–3.5

## Task 5: Diagnostic differentiation from MIST_RO family

- [x] Verify that when a member access on a `Closed_Generic_With_Unknown` is also inside a readonly method body, only MIST_TC_0062 fires (not both MIST_TC_0062 and MIST_RO_0002)
- [x] Add logic: if the receiver is `Closed_Generic_With_Unknown`, the fifth pass emits MIST_TC_0062; the first pass (readonly method body) should not double-emit for the same access
- [x] Approach: the first pass already only fires for members that are themselves readonly — if the enclosing method is not readonly, pass 1 doesn't fire. If it IS readonly, pass 1 would fire MIST_RO_0002 for the same call. Add a guard: if the receiver is `Closed_Generic_With_Unknown`, skip the MIST_RO emission (let MIST_TC_0062 take precedence)
- [x] Unit test: non-readonly method calling `xs.Add(1)` where `xs: List<unknown>` → only MIST_TC_0062
- [x] Unit test: readonly method calling `xs.Add(1)` where `xs: List<unknown>` → only MIST_TC_0062 (not MIST_RO_0002)

**Requirements:** 4.4, 4.6

## Task 6: Preserve `SubstituteClosedUnknownInResultType` in TypeChecker

- [x] Identify all call sites of `SubstituteClosedUnknownInResultType` in `TypeCheckerAnalyzer.cs`
- [x] Ensure this logic is NOT removed during the reachability cleanup — it handles result-type substitution (reading a `T`-typed field on `List<unknown>` yields `unknown`), which is orthogonal to the readonly enforcement
- [x] Preserve `GetClosedUnknownTypeParameters` if it is used by the substitution logic (or inline it)
- [x] Unit test: reading a `T`-typed field on `List<unknown>` still resolves to type `unknown`

**Requirements:** 11.3 (backward compat for accepted programs)

## Task 7: Remove ReachabilityAnalyzer and IReachabilityMap

- [x] Delete `Source/Mist/Semantics/Model/ReachabilityAnalyzer.cs`
- [x] Delete `Source/Mist/Semantics/Contracts/IReachabilityMap.cs`
- [x] Remove `ReachabilityMap` property from `ISemanticModel` interface
- [x] Remove `ReachabilityMap` property from `SemanticModel` class
- [x] Remove `reachabilityMap` constructor parameter from `SemanticModel`
- [x] Remove `ReachabilityAnalyzer.Analyze()` call from `SemanticModelBuilder.Build()`
- [x] Remove `ReachabilityAnalyzer.Empty` usage (default for tests)
- [x] Fix all compilation errors from the removal
- [x] Build with zero warnings

**Requirements:** 1.1–1.6

## Task 8: Remove TypeChecker member-filtering logic

- [x] Remove `FilterMethodOnClosedUnknownReceiver` method
- [x] Remove `FilterMemberAccessOnClosedUnknownReceiver` method
- [x] Remove `FilterIndexerOnClosedUnknownReceiver` method
- [x] Remove `FormatFilteredMemberMessage` method
- [x] Remove `UnknownClosedGenericMemberFiltered` diagnostic constant
- [x] Remove all call sites of the above methods in the TypeChecker's postfix/assignment analysis
- [x] Preserve `GetClosedUnknownTypeParameters` and `SubstituteClosedUnknownInResultType` (needed for type resolution)
- [x] Build with zero warnings

**Requirements:** 1.5, 1.6

## Task 9: Remove old reachability tests

- [x] Delete `Test/Mist.Tests/Semantics/ReachabilityAnalyzerFieldPropertyIndexerTests.cs`
- [x] Delete `Test/Mist.Tests/Semantics/ReachabilityAnalyzerMethodConstructorTests.cs`
- [x] Delete `Test/Mist.Tests/Semantics/ReachabilityAnalyzerFixedPointPropertyTests.cs`
- [x] Delete `Test/Mist.Tests/Semantics/ReachabilityMapIntegrationTests.cs`
- [x] Delete `Test/Mist.Tests/Analysis/UnknownMemberFilteringTests.cs`
- [x] Build with zero warnings

**Requirements:** 1.7

## Task 10: Write replacement tests for readonly-based filtering

- [x] Test: method call on `List<unknown>` where method is `Mutable` → MIST_TC_0062
- [x] Test: method call on `List<unknown>` where method is `InferredReadonly` → no diagnostic
- [x] Test: method call on `List<int>` (concrete) → no diagnostic
- [x] Test: property read on `List<unknown>` where getter is `InferredReadonly` → no diagnostic
- [x] Test: property setter on `List<unknown>` → MIST_TC_0062
- [x] Test: field read on `List<unknown>` → no diagnostic
- [x] Test: field write on `List<unknown>` → MIST_TC_0062
- [x] Test: self-call inside declaring body (`this.Add(x)` inside `List<T>`) → no diagnostic (open generic)
- [x] Test: nested unknown (`Optional<List<unknown>>`) → MIST_TC_0062 on non-readonly access
- [x] Test: variable reassignment `List<unknown> xs = ...; xs = ...;` → no diagnostic
- [x] Test: `Map<T, unknown>` inside generic body → no diagnostic (open type parameter)
- [x] Test: `Map<string, unknown>` inside generic body → MIST_TC_0062 on non-readonly access (independently closed)

**Requirements:** 2, 3, 4, 8, 11

## Task 11: Verify readonly inference compatibility with standard library generics

- [x] Compile the existing standard library (`Source/Standard/`) with the new system
- [x] Identify any member that was previously `DoesNotWriteT` but is now `Mutable` under readonly inference
- [x] For each discrepancy: add explicit `readonly` annotation to the standard library member OR adjust the member's implementation
- [x] Document any changes made
- [x] All existing sample apps must still compile and run

**Requirements:** 5.1–5.6, 11.1–11.5

## Task 12: Interaction with readonly parameters

- [x] Unit test: passing `List<unknown>` to a `readonly` parameter → no error at call site
- [x] Unit test: passing `List<unknown>` to a non-`readonly` parameter → no error at call site
- [x] Unit test: inside callee with `List<unknown>` parameter, calling non-readonly method → MIST_TC_0062
- [x] Verify that pass 3 (readonly parameters) and pass 5 (closed-unknown) do not double-emit when a parameter is both `readonly` and `Closed_Generic_With_Unknown`

**Requirements:** 9.1–9.4

## Task 13: Interaction with covariant widening and return types

- [x] Unit test: `List<unknown> xs = new List<int>(); xs.Add(1);` → MIST_TC_0062 on Add
- [x] Unit test: method returning `List<unknown>` — caller accessing non-readonly member on result → MIST_TC_0062
- [x] Unit test: `GetList().Count` where `GetList()` returns `List<unknown>` and `Count` is readonly → no diagnostic
- [x] Unit test: generic method inferred as `unknown` at call site → MIST_TC_0062 on non-readonly access

**Requirements:** 7.1–7.4

## Task 14: Interaction with pattern narrowing

- [x] Unit test: `List<unknown> xs = ...; if (xs is List<int> li) { li.Add(1); }` → no diagnostic inside narrowed branch
- [x] Unit test: `unknown x = ...; if (x is string s) { s.Length; }` → no diagnostic
- [x] Verify existing pattern-narrowing rejection tests still pass (narrowing TO `unknown` or TO `Closed_Generic_With_Unknown` is rejected)

**Requirements:** 6.1–6.4

## Task 15: Sample application

- [x] Create `samples/UnknownReadonlySemantics/mist.toml` with project name
- [x] Create `samples/UnknownReadonlySemantics/Program.mist` with `RunDemo()` entry point
- [x] Demonstrate: readonly member access on `List<unknown>` (e.g., `Count`)
- [x] Demonstrate: variable reassignment of `List<unknown>` to different list
- [x] Demonstrate: pattern narrowing from `List<unknown>` to `List<int>` and calling `Add`
- [x] Demonstrate: covariant widening from `List<int>` to `List<unknown>`
- [x] Demonstrate: passing `List<unknown>` to method with readonly parameter
- [x] Demonstrate: open generic type body retaining full access
- [x] Each section prints labeled output
- [x] Add `UnknownReadonlySemantics.Program.RunDemo()` call to `samples/AllFeatures/Program.mist`
- [x] Add `[[references]]` entry to `samples/AllFeatures/mist.toml`
- [x] Compile and run with zero errors/warnings

**Requirements:** 10.1–10.5

## Task 16: Full integration verification

- [x] Run `dotnet build` on the full solution — zero warnings
- [x] Run `dotnet test` on the full test suite — all pass
- [x] Run all sample apps — all compile and produce expected output
- [x] Verify the AllFeatures sample still passes end-to-end

**Requirements:** 1.6, 11.3
