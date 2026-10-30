# Technical Design: Unknown Readonly Semantics

## Overview

This design replaces the `ReachabilityAnalyzer` (write-tracking) system with a readonly-type-context approach. When a variable's resolved type is a `Closed_Generic_With_Unknown`, the `ReadonlyAnalyzer` treats member access on that variable as though the type were readonly. This leverages the existing readonly infrastructure instead of maintaining a parallel member-filtering mechanism.

The change touches three main areas:
1. **Removal** of `ReachabilityAnalyzer`, `IReachabilityMap`, and the TypeChecker's member-filtering logic
2. **Addition** of a new analysis pass in `ReadonlyAnalyzer` that detects `Closed_Generic_With_Unknown` receivers and enforces readonly rules
3. **Diagnostic migration** from the TypeChecker's `MIST_TC_0062 (historical)` to the ReadonlyAnalyzer, preserving the diagnostic code but changing the message

## Architecture

### Current Flow (Being Removed)

```
SemanticModelBuilder.Build()
  → ReachabilityAnalyzer.Analyze(symbolTable, expressionTypes)
  → IReachabilityMap stored on SemanticModel
  → TypeCheckerAnalyzer consults IReachabilityMap via:
      - FilterMethodOnClosedUnknownReceiver()
      - FilterMemberAccessOnClosedUnknownReceiver()
      - FilterIndexerOnClosedUnknownReceiver()
  → Emits MIST_TC_0062 when WritesT member is accessed
```

### New Flow

```
SemanticModelBuilder.Build()
  → ReadonlyInferenceEngine.RunInference(model)  [already exists]
  → ReadonlyAnalyzer.Analyze(model, diagnostics)
      → Existing passes (readonly methods, fields, params, type-param constraints)
      → NEW: Fifth pass — AnalyzeClosedGenericWithUnknownAccess()
          - Walks all member bodies
          - For each PostfixNode, checks if receiver type is Closed_Generic_With_Unknown
          - If yes, applies readonly context rules to member access
          - Emits MIST_TC_0062 (not MIST_RO_*) for violations
```

## Component Design

### 1. Type Detection: `IsClosedGenericWithUnknown(IType)`

A helper method on `ReadonlyAnalyzer` that determines whether a type qualifies for the readonly type context:

```csharp
/// Returns true if the type is a ConstructedType with at least one
/// type argument that is UnknownType (directly or transitively through
/// nested type arguments), AND no type argument is an unsubstituted
/// TypeParameterType (open type parameters mean the type is not yet closed).
private static bool IsClosedGenericWithUnknown(IType? type)
{
    if (type is not ConstructedType ct)
        return false;
    return ContainsUnknownTypeArgument(ct) && !ContainsOpenTypeParameter(ct);
}

private static bool ContainsUnknownTypeArgument(ConstructedType ct)
{
    foreach (var arg in ct.TypeArguments)
    {
        if (arg is UnknownType)
            return true;
        if (arg is ConstructedType nested && ContainsUnknownTypeArgument(nested))
            return true;
    }
    return false;
}

private static bool ContainsOpenTypeParameter(ConstructedType ct)
{
    foreach (var arg in ct.TypeArguments)
    {
        if (arg is TypeParameterType)
            return true;
        if (arg is ConstructedType nested && ContainsOpenTypeParameter(nested))
            return true;
    }
    return false;
}
```

This satisfies:
- **Req 2.2**: Detects unknown at any depth
- **Req 2.3**: Nested generics like `Optional<List<unknown>>` are caught
- **Req 8.1–8.3**: Open type parameters (`Foo<T>`) are excluded
- **Req 8.5**: `Map<T, unknown>` is excluded (has open type parameter)

### 2. Fifth Analysis Pass: `AnalyzeClosedGenericWithUnknownAccess`

Follows the same structural pattern as the existing fourth pass (`AnalyzeReadonlyConstrainedTypeParamAccess`):

```csharp
// Fifth pass: check for operations on values whose type is a
// Closed_Generic_With_Unknown. When a variable's resolved type contains
// unknown at any type argument position (and no open type parameters),
// all member access is treated as if the receiver is in a readonly context.
foreach (var symbol in model.AllSymbols)
{
    if (symbol.Origin != SymbolOrigin.UserCode)
        continue;

    switch (symbol.Kind)
    {
        case SymbolKind.Method:
        case SymbolKind.Property:
        case SymbolKind.Indexer:
        case SymbolKind.Constructor:
            AnalyzeClosedGenericWithUnknownAccess(symbol, model, diagnostics);
            break;
    }
}
```

The walk logic mirrors `WalkBlockForConstrainedTypeParamViolations` but checks `IsClosedGenericWithUnknown(receiverType)` instead of `IsReadonlyConstrainedTypeParameter(targetType)`.

### 3. Violation Checking: `CheckClosedUnknownPostfix`

When a `PostfixNode`'s target type is a `Closed_Generic_With_Unknown`, each suffix is checked:

| Suffix Type | Check | Diagnostic |
|---|---|---|
| `MethodCallSuffix` | Symbol's `ReadonlyStatus` must be readonly | MIST_TC_0062 (historical) |
| `MemberAccessSuffix` (property/indexer) | Symbol's `ReadonlyStatus` must be readonly | MIST_TC_0062 (historical) |
| `MemberAccessSuffix` (field read) | Always permitted | — |
| `IndexSuffix` (read) | Indexer getter's `ReadonlyStatus` must be readonly | MIST_TC_0062 (historical) |
| `IndexSuffix` (write) | Always prohibited | MIST_TC_0062 (historical) |

Assignment checking (`CheckClosedUnknownAssignment`):
- Field assignment on a `Closed_Generic_With_Unknown` receiver → MIST_TC_0062 (historical)
- Property/indexer setter on a `Closed_Generic_With_Unknown` receiver → MIST_TC_0062 (historical)

### 4. Diagnostic Messages

The new diagnostic uses `MIST_TC_0062 (historical)` (same code as the old reachability-based filter) but with an updated message format:

```
Cannot call non-readonly method 'Add' on 'List<unknown>': the type is treated as
readonly because it contains an 'unknown' type argument.
```

```
Cannot access non-readonly property 'Items' on 'Map<string, unknown>': the type is
treated as readonly because it contains an 'unknown' type argument.
```

This is distinct from the `MIST_RO_* (historical scheme)` family used for general readonly context violations (Req 4.4, 4.6).

### 5. Interaction with Existing Passes

The new fifth pass is **additive** — it does not modify the behavior of passes 1–4:

- **Pass 1** (readonly member bodies): Unchanged. A method marked `readonly` inside a generic type still has its body checked for readonly violations.
- **Pass 2** (readonly field assignments): Unchanged.
- **Pass 3** (readonly parameters): Unchanged. A parameter of type `List<unknown>` gets the readonly type context from pass 5, not from pass 3 (unless the parameter is also explicitly `readonly`, in which case both apply with equivalent effect per Req 9.4).
- **Pass 4** (readonly-constrained type params): Unchanged. `where T : readonly` is a different mechanism from the unknown-type-context.

### 6. Readonly Inference Compatibility

The `ReadonlyInferenceEngine` already classifies members of generic types using the same local analysis rules as non-generic types (Req 5.1, 5.6). No changes are needed to the inference engine because:

- A method like `List<T>.Add(T x)` that assigns `this.item = x` writes to a field → classified `Mutable`
- A property like `List<T>.Count { get; }` that only reads → classified `InferredReadonly`
- This naturally produces the same accept/reject set as the old `WritesT`/`DoesNotWriteT` classification (Req 11)

### 7. Variable Reassignment

The readonly type context applies only to **member access**, not to the variable binding. The `ReadonlyAnalyzer`'s fifth pass only fires on `PostfixNode` suffixes and `AssignmentNode` targets that go through a `Closed_Generic_With_Unknown` receiver. A bare assignment like `myList = new List<string>()` does not involve member access on the receiver and is handled by the TypeChecker's existing assignability rules (Req 3).

### 8. Pattern Narrowing

When a variable is narrowed via pattern matching (e.g., `items is List<int> li`), the narrowed binding `li` has type `List<int>`, which is not a `Closed_Generic_With_Unknown`. The `IsClosedGenericWithUnknown` check on `li`'s type returns `false`, so no readonly context is applied (Req 6.1, 6.4).

### 9. Removal Scope

Files to delete:
- `Source/Mist/Semantics/Model/ReachabilityAnalyzer.cs`
- `Source/Mist/Semantics/Contracts/IReachabilityMap.cs`

Files to modify:
- `Source/Mist/Semantics/Model/SemanticModel.cs` — Remove `ReachabilityMap` property and constructor parameter
- `Source/Mist/Semantics/Contracts/ISemanticModel.cs` — Remove `ReachabilityMap` from interface
- `Source/Mist/Semantics/Model/SemanticModelBuilder.cs` — Remove `ReachabilityAnalyzer.Analyze()` call
- `Source/Mist/Analysis/Builtin/TypeCheckerAnalyzer.cs` — Remove `FilterMethodOnClosedUnknownReceiver`, `FilterMemberAccessOnClosedUnknownReceiver`, `FilterIndexerOnClosedUnknownReceiver`, `GetClosedUnknownTypeParameters`, `SubstituteClosedUnknownInResultType`, `FormatFilteredMemberMessage`, and the `UnknownClosedGenericMemberFiltered` diagnostic constant
- `Source/Mist/Analysis/Builtin/ReadonlyAnalyzer.cs` — Add fifth pass

Test files to delete/replace:
- `Test/Mist.Tests/Semantics/ReachabilityAnalyzerFieldPropertyIndexerTests.cs`
- `Test/Mist.Tests/Semantics/ReachabilityAnalyzerMethodConstructorTests.cs`
- `Test/Mist.Tests/Semantics/ReachabilityAnalyzerFixedPointPropertyTests.cs`
- `Test/Mist.Tests/Semantics/ReachabilityMapIntegrationTests.cs`
- `Test/Mist.Tests/Analysis/UnknownMemberFilteringTests.cs`

## Critical Design Considerations

### Backward Compatibility (Req 11)

The old system classified members as `WritesT` if they wrote to T-typed storage. The new system classifies members as `Mutable` if they write to any field or call non-readonly methods. These are **not identical** — the old system was T-specific (a method writing to a non-T field was `DoesNotWriteT`), while the new system is global (any field write makes the method `Mutable`).

However, in practice the sets align for the standard library generics because:
- Methods that write to T-typed storage also write to fields (making them `Mutable`)
- Methods that only read T-typed storage only read fields (making them `InferredReadonly`)

The risk case is a method that writes to a **non-T** field but never touches T-typed storage. Under the old system this was `DoesNotWriteT` (accessible on `Foo<unknown>`). Under the new system this is `Mutable` (inaccessible on `Foo<unknown>`). This is actually **more conservative** (safer) — it blocks more operations, not fewer. Per Req 11.4, this is not a soundness hole. Per Req 11.3, if any standard library member becomes newly inaccessible, we resolve it by adding an explicit `readonly` annotation.

### SubstituteClosedUnknownInResultType

The existing `SubstituteClosedUnknownInResultType` helper in the TypeChecker substitutes `unknown` for type parameters in the result type of member access on `Closed_Generic_With_Unknown` receivers (e.g., reading a `T`-typed field on `List<unknown>` yields `unknown`). This logic is **not part of the readonly enforcement** — it's part of type resolution. It must be preserved in the TypeChecker even after the reachability removal. The readonly pass only gates whether the access is permitted; the TypeChecker still determines the result type.

### Diagnostic Code Ownership

`MIST_TC_0062 (historical)` moves from the TypeChecker to the ReadonlyAnalyzer. Since both analyzers run in the same pipeline and the old filter logic is removed, there is no risk of double-emission. The ReadonlyAnalyzer declares a new `DiagnosticId` constant for this code.
