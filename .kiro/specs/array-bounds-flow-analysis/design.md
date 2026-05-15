# Design Document

## Overview

This design adds flow analysis to the Mist compiler so that array index expressions can be statically proven to satisfy the `ArrayBounds` refinement constraint (`>= 0 and < Length`) defined on `Standard.Array<TValue, Length>`. The approach extends the existing `ConditionConstraint` narrowing infrastructure with symbolic bounds, adds a `Length` property to `ArrayType`, and wires arrays into the unified indexer refinement validation path.

#[[file:Source/Standard/Bootstrap/Array.mist]]
#[[file:Source/Mist/Semantics/Model/Types/ArrayType.cs]]
#[[file:Source/Mist/Analysis/Builtin/RefinementSolver.cs]]

## Architecture

### Component Interactions

```
┌─────────────────────────────────────────────────────────────────────┐
│                     SemanticModelBuilder                             │
│  - Creates ArrayType with concrete/symbolic Length                   │
│  - Resolves .Length to concrete int or symbolic identity             │
└──────────────────────────────┬──────────────────────────────────────┘
                               │ produces ArrayType(elementType, length)
                               ▼
┌─────────────────────────────────────────────────────────────────────┐
│                     TypeCheckerAnalyzer                              │
│                                                                     │
│  CheckForStatement ──► InjectInductionNarrowing ──► _narrowingContext│
│       │                                                    │        │
│       ▼                                                    ▼        │
│  CheckIndexSuffix ──► ValidateIndexerParameterRefinement            │
│       │                    │                                        │
│       │                    ├─ Case 1: Literal → Evaluate predicate  │
│       │                    ├─ Case 2: RefinementType → Implies      │
│       │                    ├─ Case 3: Narrowing → ConstraintsImply  │
│       │                    └─ Case 4: Reject                        │
│       │                                                             │
└───────┼─────────────────────────────────────────────────────────────┘
        │
        ▼
┌─────────────────────────────────────────────────────────────────────┐
│                     RefinementSolver / ConstraintsImplyPredicate     │
│  - Extended ConditionConstraint with symbolic variant                │
│  - Tautology: symbolic < X implies < X when same identity           │
└─────────────────────────────────────────────────────────────────────┘
```

### Key Design Decisions

1. **Extend `ArrayType` rather than replace it** — `ArrayType` gains an optional `Length` property. This preserves backward compatibility with the CSharp transpiler and all existing code that pattern-matches on `ArrayType`.

2. **Extend `ConditionConstraint` to support symbolic bounds** — Rather than creating a parallel constraint system, we add a `SymbolicId` field to `ConditionConstraint`. When `SymbolicId` is non-null, the constraint references a symbolic Length identity rather than a concrete `Value`.

3. **Reuse the existing `ValidateIndexerParameterRefinement` path** — Arrays are wired into the same validation logic as custom indexers. The `ArrayType` exclusion in `CheckIndexSuffix` is removed.

4. **For-loop narrowing is injected before the body check** — Same pattern as `if`-statement narrowing: constraints are pushed into `_narrowingContext` before the body, and popped after.

## Components and Interfaces

### ArrayType (Modified)

**File:** `Source/Mist/Semantics/Model/Types/ArrayType.cs`

The `ArrayType` class is extended with optional `KnownLength` and `SymbolicLengthId` properties. It retains its existing single-argument constructor for backward compatibility and adds two new constructors for concrete and symbolic lengths.

**Interface contract:** Any code that constructs an `ArrayType` from a `new T[N]` expression with a literal `N` MUST use the concrete-length constructor. Code that constructs from array literals MUST pass the element count. Code that constructs from unsized parameter declarations MUST use the symbolic-length constructor with a unique ID.

### ConditionConstraint (Modified)

**File:** `Source/Mist/Analysis/Builtin/TypeCheckerAnalyzer.cs` (nested record struct)

Extended with an optional `SymbolicId` field. When non-null, the constraint represents a bound relative to a symbolic Length identity rather than a concrete integer value.

**Interface contract:** All existing code that creates `ConditionConstraint` values continues to pass `SymbolicId: null` (the default). Only the new for-loop narrowing code and the array bounds extraction code produce symbolic constraints.

### ForLoopInductionNarrower (New logic in TypeCheckerAnalyzer)

**File:** `Source/Mist/Analysis/Builtin/TypeCheckerAnalyzer.cs`

A new private method `InjectForLoopNarrowing` (called from `CheckForStatement`) that:
1. Extracts the induction variable name and initial value from the for-loop initializer
2. Extracts the upper bound from the for-loop condition (concrete or symbolic)
3. Checks for reassignment in the loop body
4. Injects constraints into `_narrowingContext`

**Interface contract:** Returns the variable name and constraints injected (or null if narrowing was skipped), so the caller can remove them after the body check.

### ArrayIndexRefinementValidator (Modified logic in TypeCheckerAnalyzer)

**File:** `Source/Mist/Analysis/Builtin/TypeCheckerAnalyzer.cs`

The existing `CheckIndexSuffix` method is modified to remove the `ArrayType` exclusion. A new private method `ValidateArrayIndexRefinement` handles the array-specific case by constructing the appropriate `ArrayBounds` predicate (concrete or symbolic) and validating the index argument against it using the same 3-case logic as `ValidateIndexerParameterRefinement`.

**Interface contract:** Emits the same diagnostic codes as custom indexer validation. Returns without emitting if the index is provably safe.

### SymbolicConstraintImplication (Modified logic in TypeCheckerAnalyzer)

**File:** `Source/Mist/Analysis/Builtin/TypeCheckerAnalyzer.cs`

The existing `ConditionConstraintImplies` method is extended with a symbolic tautology check: if both source and target have the same `SymbolicId` and the same `Op`, implication holds.

**Interface contract:** Returns `true` for symbolic tautologies, `false` for mismatched symbols. Falls through to existing concrete logic when both constraints are non-symbolic.

## Data Models

### ArrayType Extension

```csharp
public sealed class ArrayType : IType
{
    public IType ElementType { get; }
    
    /// <summary>
    /// The array's Length. Null means unknown (unsized parameter, non-literal new T[expr]).
    /// When non-null and SymbolicLengthId is null, this is a concrete length.
    /// </summary>
    public long? KnownLength { get; }
    
    /// <summary>
    /// A unique identity for symbolic (unknown) lengths. Non-null when the array
    /// has an unknown length that can still be referenced symbolically (e.g., from
    /// a parameter declaration). Two references to the same parameter's .Length
    /// share the same SymbolicLengthId.
    /// </summary>
    public int? SymbolicLengthId { get; }
    
    public string DisplayName => KnownLength.HasValue 
        ? $"{ElementType.DisplayName}[{KnownLength.Value}]" 
        : $"{ElementType.DisplayName}[]";
    public TypeKind Kind => TypeKind.Class;

    // Existing constructor preserved for backward compat (unknown length)
    public ArrayType(IType elementType) 
    { 
        ElementType = elementType; 
        KnownLength = null;
        SymbolicLengthId = null;
    }
    
    // New constructor for concrete length
    public ArrayType(IType elementType, long knownLength) 
    { 
        ElementType = elementType; 
        KnownLength = knownLength;
        SymbolicLengthId = null;
    }
    
    // New constructor for symbolic length
    public ArrayType(IType elementType, int symbolicLengthId) 
    { 
        ElementType = elementType; 
        KnownLength = null;
        SymbolicLengthId = symbolicLengthId;
    }
}
```

### ConditionConstraint Extension

```csharp
private enum ConditionRelOp { Gte, Lte, Gt, Lt }

/// <summary>
/// A relational fact proven about a variable. Either concrete (Value is meaningful,
/// SymbolicId is null) or symbolic (SymbolicId is non-null, Value is ignored).
/// </summary>
private readonly record struct ConditionConstraint(
    ConditionRelOp Op, 
    long Value, 
    int? SymbolicId = null);
```

### Symbolic Length ID Generation

A static counter in `SemanticModelBuilder` (or `TypeCheckerAnalyzer`) generates unique IDs:

```csharp
private int _nextSymbolicLengthId = 1;

private int AllocateSymbolicLengthId() => _nextSymbolicLengthId++;
```

Each unsized array parameter declaration gets a unique ID. When `.Length` is accessed on that array, the resolved type carries the same symbolic ID.

## Key Algorithms

### Algorithm 1: For-Loop Induction Variable Narrowing

In `CheckForStatement`, after checking the initializer and condition but before checking the body:

```
1. Extract induction variable name from initializer:
   - ForInitializerVariableDeclarationForInitializerNode → variable name
   - ForInitializerVarDeclarationForInitializerNode → variable name
   
2. Extract lower bound from initializer value:
   - If initializer is a literal integer N → lower bound = Gte(N)
   - Otherwise → skip narrowing
   
3. Extract upper bound from condition:
   - Navigate condition AST: Expression → Assignment → LogicalOr → ... → Relational
   - If condition is `varName < expr`:
     a. If expr is a literal N → upper bound = Lt(N)
     b. If expr is `identifier.Length` and identifier's type is ArrayType:
        - If ArrayType.KnownLength is set → upper bound = Lt(KnownLength)
        - If ArrayType.SymbolicLengthId is set → upper bound = Lt(symbolic: SymbolicLengthId)
        - Otherwise → allocate new SymbolicLengthId, upper bound = Lt(symbolic: id)
     c. Otherwise → skip narrowing
   - If condition is `varName <= expr`: same logic but Lte instead of Lt
   
4. Check for reassignment in body:
   - Walk the body AST looking for assignments targeting the induction variable
   - If found → skip narrowing
   
5. Inject constraints into _narrowingContext:
   - _narrowingContext[varName] = [lowerBound, upperBound]
   
6. Check the loop body
   
7. Remove constraints:
   - _narrowingContext.Remove(varName)
```

### Algorithm 2: Array Indexer Refinement Validation

Modified `CheckIndexSuffix` — remove the `ArrayType` exclusion:

```
1. Check the index expression (existing)
2. Validate index type is int-like (existing)
3. Validate refinement constraint (NEW for arrays):
   - If targetType is ArrayType:
     a. Resolve the ArrayBounds predicate for this specific array:
        - If KnownLength is set → predicate is ">= 0 and < KnownLength"
        - If SymbolicLengthId is set → predicate is ">= 0 and < symbolic(id)"
        - Otherwise → allocate symbolic id, predicate is ">= 0 and < symbolic(id)"
     b. Validate index argument against this predicate using the same 3-case logic:
        - Case 1 (literal): Evaluate predicate directly (only works for concrete Length)
        - Case 2 (narrowing context): Check if _narrowingContext[varName] implies predicate
        - Case 3 (refinement type): Check if argument's RefinementType implies predicate
        - Case 4: Emit diagnostic
   - If targetType is ClassType/StructType/ConstructedType:
     a. Existing ValidateIndexerParameterRefinement logic (unchanged)
```

### Algorithm 3: Symbolic Constraint Implication

Extended `ConditionConstraintImplies` to handle symbolic bounds:

```
ConditionConstraintImplies(source, target):
  // Both symbolic with same ID and same operator → tautology
  if source.SymbolicId != null && target.SymbolicId != null:
    if source.SymbolicId == target.SymbolicId && source.Op == target.Op:
      return true
    else:
      return false  // different symbols or different ops → cannot prove
  
  // One symbolic, one concrete → cannot prove (unless we add range analysis later)
  if source.SymbolicId != null || target.SymbolicId != null:
    return false
  
  // Both concrete → existing logic
  return existingConcreteImplication(source, target)
```

### Algorithm 4: `.Length` Resolution

In `SemanticModelBuilder.ResolveMemberAccess` (or equivalent), when resolving `.Length` on an `ArrayType`:

```
1. If ArrayType.KnownLength is set:
   - Return a type that carries the concrete value (e.g., a RefinementType `int KnownLength`
     or simply record the constant for later use)
   - For narrowing purposes: when the result of .Length is used as a for-loop bound,
     the TypeChecker extracts the concrete value from the ArrayType directly
     
2. If ArrayType.SymbolicLengthId is set:
   - The TypeChecker recognizes this as a symbolic reference when extracting for-loop bounds
   
3. Otherwise (truly unknown):
   - Allocate a new SymbolicLengthId for this array, store it, and use it for bounds
```

Note: The `.Length` resolution for the *type system* still returns `int` (the runtime type). The symbolic/concrete tracking is used only by the TypeChecker's flow analysis, not by the type system itself.

## Modifications to Existing Code

### SemanticModelBuilder Changes

| Location | Change |
|----------|--------|
| `ArrayCreationExpressionPrimaryNode` handler | Extract literal size from `arrayCreation.Size`, pass to `new ArrayType(elementType, knownLength)` |
| `ArrayLiteralExpressionPrimaryNode` handler | Count elements, pass to `new ArrayType(elementType, count)` |
| `EmptyArrayLiteralExpressionPrimaryNode` handler | Use `new ArrayType(elementType, 0)` |
| Array type reference resolution (parameters) | Use `new ArrayType(elementType)` (unknown) or `new ArrayType(elementType, symbolicId)` |

### TypeCheckerAnalyzer Changes

| Location | Change |
|----------|--------|
| `ConditionConstraint` record | Add `int? SymbolicId = null` field |
| `ConditionConstraintImplies` | Add symbolic tautology check at the top |
| `ConstraintsImplyPredicate` | Handle symbolic constraints in predicate extraction |
| `CheckIndexSuffix` | Remove `targetType is not ArrayType` guard; add array-specific refinement validation |
| `CheckForStatement` | Add induction variable narrowing injection before body check |
| `ExtractPredicateConstraints` | Handle predicates that reference dependent type parameters (produce symbolic constraints) |

### RefinementSolver / RefinementPredicate Changes

| Location | Change |
|----------|--------|
| `RefinementPredicate.ExtractConstraints` | When a relational pattern references a non-literal expression (identifier), check if it resolves to a DependentTypeParameterType and produce a symbolic constraint |
| `ConstraintPairImplies` | Add a `ConstraintKind.Symbolic` case that checks identity equality |

## Diagnostic IDs

No new diagnostic IDs are needed. The existing refinement validation diagnostics apply:

- `ParameterRefinementViolation` — literal fails predicate, or non-provable argument
- `RefinementImplicationFailure` — refinement type doesn't imply target

## Edge Cases

1. **`for (int i = 0; i < 100; i = i + 1)` with `arr[i]` where arr has unknown length** — The upper bound `100` is concrete but doesn't match the array's symbolic Length. The compiler cannot prove `< 100` implies `< symbolicLength`. This correctly rejects.

2. **`for (int i = 0; i < arr.Length; i = i + 1)` with `arr[i]`** — The upper bound is `arr.Length` which resolves to the same symbolic ID as the array's Length parameter. The tautology `< X implies < X` accepts this.

3. **`arr[0]` on an array with unknown length** — The literal `0` satisfies `>= 0` but cannot prove `< symbolicLength` (we don't know Length > 0). This requires either: (a) the array has a concrete known length > 0, or (b) the array's Length type has a constraint `>= 1`. For `new int[5]`, Length is 5 so `0 < 5` is provable. For parameter arrays, this would reject unless guarded.

4. **Nested `d[i][j]`** — The outer index `i` is validated against `d`'s bounds. The result type of `d[i]` is an `ArrayType` (the inner array). The inner index `j` is validated against that inner array's bounds independently.

5. **Induction variable reassigned in body** — `for (int i = 0; i < n; i = i + 1) { i = 5; arr[i]; }` — The reassignment invalidates narrowing, so `arr[i]` is rejected.

## Testing Strategy

- Unit tests for `ConditionConstraint` symbolic implication logic
- Unit tests for `ArrayType` construction with concrete/symbolic lengths
- Integration tests via the sample apps (all existing samples must pass)
- Dedicated `ArrayBoundsFlowAnalysis` sample exercising every pattern

## Correctness Properties

### Property 1: Soundness

If the compiler accepts an array index expression, the index is guaranteed to be within bounds at runtime (assuming no unsafe code or reflection). The compiler never accepts an index it cannot prove safe.

**Validates: Requirements 2.3, 2.4**

### Property 2: Completeness (best-effort)

The compiler accepts the common idiom `for (int i = 0; i < arr.Length; i = i + 1) { arr[i] }` without requiring any annotation. It also accepts literal indices on arrays with known concrete lengths. It does NOT attempt to prove arbitrary arithmetic expressions.

**Validates: Requirements 3.1, 3.2, 3.4**

### Property 3: Monotonicity of narrowing

Once a constraint is injected into `_narrowingContext`, it remains valid for the entire scope it covers. Reassignment of the narrowed variable invalidates the constraint immediately.

**Validates: Requirements 3.7, 3.8, 3.9**

### Property 4: Symbolic identity correctness

Two symbolic constraints imply each other if and only if they reference the same `SymbolicLengthId` AND use the same relational operator. Different IDs never imply each other, even if they happen to represent the same runtime value.

**Validates: Requirements 4.2, 4.3, 4.4**

### Property 5: No false negatives on existing code

All existing sample apps that currently compile must continue to compile after this change (with the targeted modifications described in Requirement 6).

**Validates: Requirements 6.1, 6.2, 6.3, 6.7, 6.8, 6.9**

## Error Handling

1. **Unrecognized for-loop patterns:** When the for-loop initializer, condition, or iterator doesn't match a recognized pattern, the compiler silently skips induction narrowing. No diagnostic is emitted — the loop simply doesn't get the optimization, and any array access inside will be validated without narrowing (potentially rejected).

2. **Non-literal array sizes:** When `new T[expr]` uses a non-literal expression, the array gets an unknown length. Array accesses on such arrays require either a for-loop with `.Length` as the bound (symbolic tautology) or explicit refinement type annotations.

3. **Cascading diagnostics:** When an array index is rejected, only one diagnostic is emitted per index expression. The existing `ValidateIndexerParameterRefinement` pattern (break after first parameter check) prevents cascading.

4. **Stdlib not loaded:** If `Standard.Array` cannot be resolved (e.g., in unit tests without stdlib), the array bounds validation is skipped gracefully. The existing fallback pattern in `SemanticModelBuilder` (try stdlib, catch `StdlibMissingException`, fall through) is preserved.

