# Design Document: Ownership Field Assignment Check

## Overview

This design addresses a bug where the `OwnershipAnalyzer` does not enforce lending compatibility when assigning to a field with an explicit ownership tier. Currently, `AnalyzeAssignment` checks for moves, non-storability, and mutation blocking on field assignments, but it does not verify that the source variable's tier is compatible with the target field's declared tier.

The fix adds a lending compatibility check in `AnalyzeAssignment` that fires when:
1. The assignment target is a field access (detected via `IsFieldOrIndexAccess`)
2. The resolved field symbol has `SymbolKind.Field` and a non-`Unrestricted` `OwnershipTier`
3. The RHS resolves to a symbol (not a literal or constructor)
4. The RHS variable's declared type is not a value type

The check reuses the existing `IsLendingCompatible` matrix and emits the same diagnostic IDs (`MIST_OWN_INCOMPATIBLE_LEND` and `MIST_OWN_UNRESTRICTED_TO_REF`) already used for parameter lending.

## Architecture

The fix is entirely contained within `OwnershipAnalyzer.AnalyzeAssignment`. No new classes, interfaces, or infrastructure are needed.

```
AnalyzeAssignment (existing method)
├── [existing] Check RHS for use-after-move
├── [existing] Resolve LHS/RHS symbols
├── [existing] Handle owned variable re-assignment (Live restore)
├── [existing] Handle owned-to-owned move
├── [existing] Non-storability check (ref/frozen ref → field)
├── [NEW] Field assignment lending compatibility check  ← this fix
└── [existing] Mutation blocking check (LentFrozen/LentRef)
```

The new check is inserted after the existing non-storability check and before the mutation blocking check. This ordering ensures:
- Non-storable refs are rejected first (they can't be stored at all)
- Lending compatibility is checked next (the source tier must be compatible with the field's tier)
- Mutation blocking is checked last (the receiver must not be frozen/lent)

## Components and Interfaces

### Modified Method: `AnalyzeAssignment`

The only method that changes. A new block is added after the non-storability check:

```csharp
// Field assignment lending compatibility check: if the assignment target is a
// field access and the field has a non-Unrestricted ownership tier, verify that
// the source variable's tier is compatible with the field's tier.
// Requirements: 1.1, 1.2, 1.3, 1.4, 1.5, 1.6, 2.1, 2.2, 2.3, 2.4, 3.1, 3.2
CheckFieldAssignmentLendingCompatibility(
    assignment.Target, assignment.Value, state, model, diagnostics);
```

### New Private Method: `CheckFieldAssignmentLendingCompatibility`

```csharp
/// <summary>
/// Checks lending compatibility when assigning to a field with a non-Unrestricted
/// ownership tier. Emits MIST_OWN_UNRESTRICTED_TO_REF or MIST_OWN_INCOMPATIBLE_LEND
/// if the source variable's tier cannot provide the field's required tier.
/// </summary>
private static void CheckFieldAssignmentLendingCompatibility(
    SyntaxNode target,
    AssignmentNode value,
    MethodFlowState state,
    ISemanticModel model,
    DiagnosticBag diagnostics)
```

**Algorithm:**

1. Check `IsFieldOrIndexAccess(target)` — if not a field access, return early.
2. Unwrap the expression chain to find the `PostfixNode`, then get the last suffix as `MemberAccessSuffixPostfixSuffixNode`.
3. Resolve the field symbol by determining the receiver type via `model.GetTypeOf()` and looking up the member name on the type's `MemberScope`.
4. If the symbol is null, or not `SymbolKind.Field`, or its `OwnershipTier` is `Unrestricted` — return early.
5. Resolve the RHS to a symbol via `TryResolveAssignmentValueSymbol(value, model)`.
6. If the RHS symbol is null — return early (literal, constructor, or unresolvable expression).
7. Check if the RHS variable's declared type is a value type — if so, return early.
8. Determine the source tier from the RHS symbol (default to `Unrestricted` if not a `Symbol`).
9. If source tier equals target tier — return early (always compatible).
10. Call `IsLendingCompatible(sourceTier, targetTier)` — if compatible, return early.
11. Emit the appropriate diagnostic:
    - If `sourceTier == Unrestricted` and `targetTier` is `Ref`, `FrozenRef`, or `Owned`: emit `MIST_OWN_UNRESTRICTED_TO_REF`
    - Otherwise: emit `MIST_OWN_INCOMPATIBLE_LEND`

### Field Symbol Resolution Pattern

The method resolves the field symbol by looking up the member name on the receiver's type (since `GetDeclaredSymbol` does not map `MemberAccessSuffixPostfixSuffixNode` nodes):

```csharp
// Navigate to the PostfixNode and get the last suffix
var postfix = FindPostfixNode(target);
if (postfix is null || postfix.Suffix.Length == 0)
    return;

var lastSuffix = postfix.Suffix[^1];
if (lastSuffix is not MemberAccessSuffixPostfixSuffixNode memberAccess)
    return;

var memberName = memberAccess.Name.Lexeme;

// Determine the receiver type
IType? receiverType;
if (postfix.Suffix.Length == 1)
    receiverType = model.GetTypeOf(postfix.Target);
else
    receiverType = model.GetTypeOf(postfix.Suffix[^2]);

if (receiverType is null)
    return;

// Unwrap ConstructedType to get the generic definition's member scope
var lookupType = receiverType is ConstructedType ct ? ct.GenericDefinition : receiverType;

ISymbol? fieldSymbol = null;
if (lookupType is ClassType classType)
{
    ClassType? current = classType;
    while (current is not null)
    {
        fieldSymbol = current.MemberScope.Lookup(memberName);
        if (fieldSymbol is not null)
            break;
        current = current.BaseType as ClassType;
    }
}
else if (lookupType is StructType structType)
{
    fieldSymbol = structType.MemberScope.Lookup(memberName);
}

if (fieldSymbol is not Symbol fieldSym)
    return;

if (fieldSym.Kind != SymbolKind.Field)
    return;

if (fieldSym.OwnershipTier == OwnershipTier.Unrestricted)
    return;
```

### Diagnostic Messages

The diagnostic messages follow the same pattern as `CheckLendingCompatibility` for parameters, adapted for field context:

- **MIST_OWN_UNRESTRICTED_TO_REF**: `"Cannot assign unrestricted variable '{name}' to a '{tier}' field. Unrestricted references cannot provide the guarantees required by '{tier}'."`
- **MIST_OWN_INCOMPATIBLE_LEND**: `"Cannot assign '{sourceTier}' variable '{name}' to a '{targetTier}' field. The '{sourceTier}' tier cannot provide '{targetTier}' access."`

## Data Models

No new data models are introduced. The fix operates on existing types:

- `OwnershipTier` enum (Unrestricted, ReadonlyRef, FrozenRef, Ref, Owned)
- `ISymbol` / `Symbol` with `Kind`, `OwnershipTier`, `DeclaredType`
- `MemberAccessSuffixPostfixSuffixNode` (existing AST node)
- `PostfixNode` (existing AST node)

## Error Handling

- **Unresolvable field symbol**: If the receiver type cannot be determined or the member lookup returns null (e.g., due to earlier type errors), the check is silently skipped. This matches the existing pattern in `CheckFieldAssignmentMutationBlocking`.
- **Unresolvable RHS symbol**: If the RHS cannot be resolved to a symbol (literals, constructor calls, complex expressions), the check is skipped. This matches the existing `CheckLendingCompatibility` behavior.
- **Non-field member access**: If the resolved symbol is a property or method (not `SymbolKind.Field`), the check is skipped.

## Correctness Properties

*A property is a characteristic or behavior that should hold true across all valid executions of a system — essentially, a formal statement about what the system should do. Properties serve as the bridge between human-readable specifications and machine-verifiable correctness guarantees.*

### Property 1: Field assignment lending compatibility matrix

*For any* (source_tier, target_tier) pair where target_tier is non-Unrestricted, and for both `this.Field` and `obj.Field` receiver forms, the compiler SHALL emit a lending diagnostic if and only if the pair is NOT in the permitted lending compatibility set. When the source is Unrestricted and the target is Owned/Ref/FrozenRef, the diagnostic SHALL be `MIST_OWN_UNRESTRICTED_TO_REF`. For other incompatible pairs, the diagnostic SHALL be `MIST_OWN_INCOMPATIBLE_LEND`. Compatible pairs SHALL produce no lending diagnostic.

**Validates: Requirements 1.1, 1.2, 1.3, 1.4, 1.5, 1.6, 2.3, 2.4**

### Property 2: Value type exemption on field assignment

*For any* ownership tier combination (source_tier, target_tier) where the RHS variable's declared type is a value type, the compiler SHALL NOT emit a lending compatibility diagnostic for the field assignment, regardless of tier incompatibility.

**Validates: Requirements 3.1**

## Testing Strategy

### Property-Based Tests

Both properties use the shared `CompilerDriver` for in-process compilation (no process spawning per iteration). The test generates all meaningful tier combinations, builds a single batched Mist program containing all scenarios, compiles once, and verifies diagnostics line-by-line.

**Test class**: `OwnershipFieldAssignmentLendingPropertyTests`

- **Property 1 generator**: Enumerate all 20 (source_tier × target_tier) pairs for non-Unrestricted targets (4 source tiers × 4 non-Unrestricted target tiers, plus Unrestricted source × 4 targets = 20 pairs), crossed with 2 receiver forms (this.Field, obj.Field) = 40 scenarios.
- **Property 2 generator**: Enumerate all 20 tier pairs with a value-type RHS = 20 scenarios.

Since the input space is finite and small (60 total scenarios), the test can exhaustively cover all combinations in a single compile-and-check pass rather than using random generation.

### Example-Based Tests

- Unresolvable field (assign to non-existent field) → no ownership diagnostic
- Literal/constructor RHS → no ownership diagnostic
- Diagnostic message content verification (variable name, tier names present)
- Diagnostic location points to assignment expression
