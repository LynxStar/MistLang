# Design Document: Readonly Locals

## Overview

This document describes the technical design for adding `readonly` as a modifier on local variable declarations in the Mist programming language. The feature is compile-time only — it prevents reassignment to readonly locals during type checking and emits standard C# locals with no runtime overhead.

## Architecture

The feature touches four compiler layers in sequence:

1. **Grammar (Parser)** — Accept `readonly` before variable declarations
2. **Semantic Model (Pass 2)** — Record `ExplicitReadonly` status on local symbols
3. **Type Checker (Analysis)** — Reject assignment to readonly locals
4. **C# Emitter (Backend)** — No changes needed (emit normal locals)

Plus two tooling updates:
5. **TextMate Grammar** — Already handled (readonly is in the `storage.modifier.mist` pattern)
6. **Sample Application** — New sample project

## Detailed Design

### 1. Grammar Changes (`Source/Mist/Grammar/Mist.storm`)

The `VariableDeclarationStatement` rule currently accepts an optional `OwnershipSpecifier` before the type or `var` keyword. We need to add an optional `READONLY` modifier before the ownership specifier.

**Current grammar:**
```
rule VariableDeclarationStatement
    = ownershipSpecifier:OwnershipSpecifier? type:TypeReference name:IDENTIFIER EQUAL initializer:Expression SEMICOLON  as TypedVariableDeclaration
    | VAR LEFT_PARENTHESIS firstName:IDENTIFIER (COMMA restNames:IDENTIFIER)* RIGHT_PARENTHESIS EQUAL initializer:Expression SEMICOLON  as VarTupleDeconstruction
    | ownershipSpecifier:OwnershipSpecifier? VAR name:(IDENTIFIER | DISCARD) EQUAL initializer:Expression SEMICOLON     as InferredVariableDeclaration
    | LEFT_PARENTHESIS first:TupleDeconstructionElement COMMA rest:TupleDeconstructionElement (COMMA additional:TupleDeconstructionElement)* RIGHT_PARENTHESIS EQUAL initializer:Expression SEMICOLON  as TypedTupleDeconstruction
    ;
```

**New grammar:**
```
rule VariableDeclarationStatement
    = readonlyModifier:READONLY? ownershipSpecifier:OwnershipSpecifier? type:TypeReference name:IDENTIFIER EQUAL initializer:Expression SEMICOLON  as TypedVariableDeclaration
    | readonlyModifier:READONLY? VAR LEFT_PARENTHESIS firstName:IDENTIFIER (COMMA restNames:IDENTIFIER)* RIGHT_PARENTHESIS EQUAL initializer:Expression SEMICOLON  as VarTupleDeconstruction
    | readonlyModifier:READONLY? ownershipSpecifier:OwnershipSpecifier? VAR name:(IDENTIFIER | DISCARD) EQUAL initializer:Expression SEMICOLON     as InferredVariableDeclaration
    | readonlyModifier:READONLY? LEFT_PARENTHESIS first:TupleDeconstructionElement COMMA rest:TupleDeconstructionElement (COMMA additional:TupleDeconstructionElement)* RIGHT_PARENTHESIS EQUAL initializer:Expression SEMICOLON  as TypedTupleDeconstruction
    ;
```

Each alternative gains an optional `readonlyModifier:READONLY?` at the front. The `readonly` keyword already exists as a token (`READONLY`). The ordering is: `readonly` → ownership specifier → type/var → name → `=` → initializer → `;`.

**Disambiguation note:** The `READONLY` token already appears in the `ReadonlyBlockStatement` rule (`READONLY body:Block`). The parser can disambiguate because:
- `readonly {` → ReadonlyBlockStatement (READONLY followed by LEFT_BRACE)
- `readonly var` / `readonly owned` / `readonly int` → VariableDeclarationStatement (READONLY followed by VAR, OwnershipSpecifier, or TypeReference)

Since `Statement` tries `ReadonlyBlockStatement` before `VariableDeclarationStatement` in the alternatives list, and `ReadonlyBlockStatement` requires `READONLY Block` (i.e., READONLY followed by LEFT_BRACE), the parser will correctly fall through to `VariableDeclarationStatement` when READONLY is followed by anything other than LEFT_BRACE.

**Wait** — actually looking at the grammar more carefully, the `Statement` rule lists `readonlyBlock:ReadonlyBlockStatement` before `variableDeclaration:VariableDeclarationStatement`. Storm parsers use ordered choice (PEG-style), so `ReadonlyBlockStatement = READONLY body:Block` will be tried first. Since `Block = LEFT_BRACE ...`, the parser will only match `ReadonlyBlockStatement` when READONLY is followed by LEFT_BRACE. If READONLY is followed by VAR, a type name, or an ownership keyword, the `ReadonlyBlockStatement` alternative will fail and the parser will try `VariableDeclarationStatement` next.

However, there's a subtlety: the `VariableDeclarationStatement` rule's `TypedVariableDeclaration` alternative starts with `readonlyModifier:READONLY? ownershipSpecifier:OwnershipSpecifier?` and then `type:TypeReference`. The `OwnershipSpecifier` rule includes `READONLY REF` as `ReadonlyRefSpecifier`. This means `readonly ref var x = ...` could be ambiguous — is `readonly` the local modifier or the start of a `readonly ref` ownership specifier?

**Resolution:** The grammar places `readonlyModifier` before `ownershipSpecifier`. When the user writes `readonly ref var x = expr;`, the parser will consume `readonly` as the `readonlyModifier` token, then try to match `ownershipSpecifier` starting at `ref`. Since `REF` alone matches `RefSpecifier`, this parses as: readonly modifier + ref ownership specifier + var + name + initializer. This is the correct interpretation: the variable is both readonly (cannot be reassigned) and a ref local.

For `readonly owned var x = expr;`, the parser consumes `readonly` as the modifier, then `owned` matches `OwnedSpecifier`, then `var` matches the VAR keyword.

### 2. Semantic Model Changes (`Source/Mist/Semantics/Model/SemanticModelBuilder.cs`)

In the `ResolveVariableDeclaration` method (around line 12900), after creating the `Symbol` for each variable declaration variant, check if the `readonlyModifier` token is present and set `ReadonlyStatus = ReadonlyStatus.ExplicitReadonly`.

**For TypedVariableDeclaration:**
```csharp
case TypedVariableDeclarationVariableDeclarationStatementNode typed:
{
    // ... existing code to resolve type, initializer, create varSymbol ...
    
    // NEW: Set readonly status if the readonly modifier is present.
    if (typed.ReadonlyModifier is not null)
    {
        varSymbol.ReadonlyStatus = ReadonlyStatus.ExplicitReadonly;
    }
    
    blockScope.AddMember(varSymbol);
    symbolTable.Register(varSymbol, varDecl);
    break;
}
```

**For InferredVariableDeclaration:**
```csharp
case InferredVariableDeclarationVariableDeclarationStatementNode inferred:
{
    // ... existing code to resolve initializer, create varSymbol ...
    
    // NEW: Set readonly status if the readonly modifier is present.
    if (inferred.ReadonlyModifier is not null)
    {
        varSymbol.ReadonlyStatus = ReadonlyStatus.ExplicitReadonly;
    }
    
    blockScope.AddMember(varSymbol);
    symbolTable.Register(varSymbol, varDecl);
    break;
}
```

**For VarTupleDeconstruction:**
```csharp
case VarTupleDeconstructionVariableDeclarationStatementNode varTuple:
{
    // ... existing code ...
    
    // NEW: Set readonly status on all introduced symbols.
    if (varTuple.ReadonlyModifier is not null)
    {
        firstSymbol.ReadonlyStatus = ReadonlyStatus.ExplicitReadonly;
    }
    blockScope.AddMember(firstSymbol);
    symbolTable.Register(firstSymbol, varDecl);

    foreach (var restName in varTuple.RestNames)
    {
        var restSymbol = new Symbol(...);
        if (varTuple.ReadonlyModifier is not null)
        {
            restSymbol.ReadonlyStatus = ReadonlyStatus.ExplicitReadonly;
        }
        blockScope.AddMember(restSymbol);
        symbolTable.Register(restSymbol, varDecl);
    }
    break;
}
```

**For TypedTupleDeconstruction:**
```csharp
case TypedTupleDeconstructionVariableDeclarationStatementNode typedTuple:
{
    // ... existing code ...
    
    void RegisterDeconstructionElement(TupleDeconstructionElementNode element)
    {
        // ... existing code to create sym ...
        
        // NEW: Set readonly status on each element symbol.
        if (typedTuple.ReadonlyModifier is not null)
        {
            sym.ReadonlyStatus = ReadonlyStatus.ExplicitReadonly;
        }
        blockScope.AddMember(sym);
        symbolTable.Register(sym, varDecl);
    }
    // ... rest unchanged ...
    break;
}
```

### 3. Type Checker Changes (`Source/Mist/Analysis/Builtin/TypeCheckerAnalyzer.cs`)

Add a new check in the assignment validation logic. The check should run early in the assignment handling (in the `CheckAssignment` or equivalent method that processes `Assignment` nodes with a non-null `Value`).

**New diagnostic ID:** `MIST_READONLY_LOCAL_0001`

**Check location:** In the assignment handling code (around line 4820), after the existing ref-readonly checks, add:

```csharp
// ── readonly-locals feature: readonly local reassignment prohibition ──
CheckReadonlyLocalReassignment(assignment, model, diagnostics);
```

**New method:**
```csharp
private void CheckReadonlyLocalReassignment(
    AssignmentNode assignment,
    ISemanticModel model,
    DiagnosticBag diagnostics)
{
    if (assignment.Value is null) return;
    
    // Resolve the assignment target to a symbol.
    var targetSymbol = TryResolveSymbolFromExpression(assignment.Target, model);
    if (targetSymbol is null) return;
    
    // Only check local variables with ExplicitReadonly status.
    if (targetSymbol.Kind != SymbolKind.LocalVariable) return;
    if (targetSymbol.ReadonlyStatus != ReadonlyStatus.ExplicitReadonly) return;
    
    diagnostics.Add(MakeDiagnostic(
        "MIST_READONLY_LOCAL_0001",
        $"Cannot reassign readonly local variable '{targetSymbol.Name}'",
        assignment,
        model));
}
```

**Ref argument check:** In the method that validates `RefArgument` nodes, add a check that the expression being passed by ref is not a readonly local:

```csharp
// When processing RefArgument nodes:
var argSymbol = TryResolveSymbolFromExpression(refArg.Expression, model);
if (argSymbol is not null 
    && argSymbol.Kind == SymbolKind.LocalVariable 
    && argSymbol.ReadonlyStatus == ReadonlyStatus.ExplicitReadonly)
{
    diagnostics.Add(MakeDiagnostic(
        "MIST_READONLY_LOCAL_0001",
        $"Cannot pass readonly local variable '{argSymbol.Name}' by mutable reference",
        refArg,
        model));
}
```

### 4. Readonly Inference Engine Interaction (`Source/Mist/Semantics/ReadonlyInferenceEngine.cs`)

The `ReadonlyInferenceEngine` already skips symbols with `ReadonlyStatus.ExplicitReadonly` (line 86-89):
```csharp
if (symbol.ReadonlyStatus is ReadonlyStatus.ExplicitReadonly
    or ReadonlyStatus.ImplicitReadonly)
{
    continue;
}
```

This means readonly locals will be correctly skipped by inference — no changes needed.

The `ReadonlyAnalyzer` already permits local variable assignments in readonly contexts (base rule 4, line 1242-1245):
```csharp
case AssignmentTargetKind.LocalVariable:
    // Local variable assignment — always permitted (base rule 4).
    return;
```

This needs to be updated to check if the local is explicitly readonly:
```csharp
case AssignmentTargetKind.LocalVariable:
    // Local variable assignment — permitted unless the local is explicitly readonly.
    // (base rule 4 still applies for non-readonly locals)
    return;
```

Actually, the readonly local check is handled by the TypeCheckerAnalyzer (section 3 above), not the ReadonlyAnalyzer. The ReadonlyAnalyzer's base rule 4 permits local variable *declarations and assignments* in readonly contexts — this is about whether the readonly *context* blocks the assignment. The readonly *local* check is orthogonal and handled separately. So no changes to ReadonlyAnalyzer are needed.

### 5. C# Emitter Changes

No changes needed. The C# emitter already emits local variables as standard `var` or typed declarations. The `readonly` modifier is a Mist-only compile-time concept. C# does not have `readonly` on local variables (only on fields and `ref readonly` locals, which are a different concept).

### 6. TextMate Grammar Changes

The `readonly` keyword is already included in the `storage.modifier.mist` pattern:
```json
"match": "\\b(public|private|static|intrinsic|implicit|explicit|readonly|mutable|owned|frozen|ref|borrowed)\\b"
```

Since `readonly` before a local variable declaration is already matched by this pattern (it's a word boundary match), no TextMate grammar changes are needed. The existing pattern will correctly highlight `readonly` in `readonly var x = 7;` as `storage.modifier.mist`.

The only consideration is ensuring `readonly` as a substring of an identifier (e.g., `readonlyValue`) is not matched — the `\b` word boundary anchors already handle this correctly.

### 7. Sample Application

Create `samples/ReadonlyLocals/Program.mist` and `samples/ReadonlyLocals/mist.toml` demonstrating all syntax variants. Add a reference from `samples/AllFeatures/mist.toml` and a call to `ReadonlyLocals.Program.RunDemo()` in `samples/AllFeatures/Program.mist`.

## Data Flow

```
Source: "readonly var x = 7;"
    │
    ▼
Parser (Storm grammar)
    │  Produces: InferredVariableDeclarationNode with ReadonlyModifier token present
    ▼
SemanticModelBuilder (Pass 2)
    │  Creates Symbol(name="x", kind=LocalVariable, ReadonlyStatus=ExplicitReadonly)
    ▼
TypeCheckerAnalyzer (Analysis pass)
    │  On "x = 10;": detects target symbol has ExplicitReadonly → emits MIST_READONLY_LOCAL_0001
    ▼
CSharpTranspilerBackend (Emission)
       Emits: "var x = 7;" (no readonly in C#)
```

## Error Messages

| Scenario | Diagnostic ID | Message |
|----------|--------------|---------|
| Simple reassignment | MIST_READONLY_LOCAL_0001 | Cannot reassign readonly local variable 'x' |
| Ref argument | MIST_READONLY_LOCAL_0001 | Cannot pass readonly local variable 'x' by mutable reference |

## Testing Strategy

1. **Parser tests** — Verify that `readonly var x = 7;`, `readonly int x = 7;`, `readonly var (a, b) = expr;`, `readonly (int a, string b) = expr;`, and `readonly owned var x = expr;` all parse correctly with the ReadonlyModifier token present.
2. **Round-trip tests** — Verify parse → print → parse produces equivalent trees for all readonly local variants.
3. **Semantic model tests** — Verify that symbols created from readonly local declarations have `ReadonlyStatus.ExplicitReadonly`.
4. **Type checker tests** — Verify that reassignment to readonly locals produces the expected diagnostic, and that non-readonly locals remain assignable.
5. **Integration test** — The sample application compiles and runs without errors.

## Files to Modify

| File | Change |
|------|--------|
| `Source/Mist/Grammar/Mist.storm` | Add `readonlyModifier:READONLY?` to all 4 VariableDeclarationStatement alternatives |
| `Source/Mist/Semantics/Model/SemanticModelBuilder.cs` | Set `ReadonlyStatus.ExplicitReadonly` on local symbols when modifier is present |
| `Source/Mist/Analysis/Builtin/TypeCheckerAnalyzer.cs` | Add readonly local reassignment check and ref argument check |
| `Editor/mist-syntax-highlighting/syntaxes/mist.tmLanguage.json` | No changes needed (already handled) |
| `samples/ReadonlyLocals/Program.mist` | New file — sample application |
| `samples/ReadonlyLocals/mist.toml` | New file — project config |
| `samples/AllFeatures/Program.mist` | Add `ReadonlyLocals.Program.RunDemo()` call |
| `samples/AllFeatures/mist.toml` | Add `[[references]] path = "../ReadonlyLocals"` |
