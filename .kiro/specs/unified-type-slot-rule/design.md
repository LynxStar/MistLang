# Design Document

## Overview

This design collapses three grammar rules for associated type slot declarations into a single unified rule, then propagates that change through the compiler pipeline: node generation, member dispatch, ProgramModel builders, legacy SemanticModelBuilder, and the pretty-printer.

The key insight is that all three existing rules share identical structure — they differ only in whether modifiers are present (zero vs one-or-more) and whether a constraint clause follows the name. A single rule with `Modifier*` and an optional constraint clause covers all cases.

## Grammar Changes

### Unified Rule

```storm
rule AssociatedTypeSlotDeclaration : HasModifiers = modifiers:Modifier* TYPE name:IDENTIFIER
    typeParams:TypeParameterList?
    constraintClause:(COLON constraints:ConstraintList slotWhereConstraints:WhereClause*)?
    SEMICOLON;
```

The `: HasModifiers` trait causes the Storm generator to implement `IHasModifiersNode` on `AssociatedTypeSlotDeclarationNode`, enabling `ModifierBuilder.Build(node, diagnostics)`.

### Rules to Remove

- `UnconstrainedAssociatedTypeSlotDeclaration` — subsumed (zero modifiers, no constraint clause)
- `ModifiedAssociatedTypeSlotDeclaration` — subsumed (one-or-more modifiers, optional constraint clause)
- `ModifiedTypeHead` — no longer needed as a shared prefix rule

### MemberDeclaration Restructuring

The current `MemberDeclaration` rule uses an inline block referencing `ModifiedTypeHead` to share the `Modifier+ TYPE IDENTIFIER TypeParameterList?` prefix between `MemberTypeAliasDeclaration` and `ModifiedAssociatedTypeSlotDeclaration`. It also has two standalone alternatives for `AssociatedTypeSlotDeclaration` (constrained) and `UnconstrainedAssociatedTypeSlotDeclaration`.

The new structure uses an inline block that shares `Modifier* TYPE IDENTIFIER TypeParameterList?` and differentiates on the token after the prefix:
- `EQUAL` → `MemberTypeAliasDeclaration` (type alias)
- `COLON` or `SEMICOLON` → `AssociatedTypeSlotDeclaration` (type slot)

```storm
rule MemberDeclaration
    = constructor:ConstructorDeclaration as ConstructorMember
    | method:MethodDeclaration           as MethodMember
    | operator:OperatorDeclaration       as OperatorMember
    | castOperator:CastOperatorDeclaration as CastOperatorMember
    | property:PropertyDeclaration       as PropertyMember
    | indexer:IndexerDeclaration         as IndexerMember
    | defaultBlock:DefaultBlockDeclaration as DefaultBlockMember
    | field:FieldDeclaration             as FieldMember
    | nestedType:TypeDeclaration         as NestedTypeMember
    | inline modifiers:Modifier* TYPE name:IDENTIFIER typeParams:TypeParameterList?: {
          memberTypeAlias:MemberTypeAliasDeclaration as MemberTypeAliasMember,
          associatedType:AssociatedTypeSlotDeclaration as AssociatedTypeMember,
      }
    ;
```

The inline block captures the shared prefix. `MemberTypeAliasDeclaration` continues with `EQUAL`, while `AssociatedTypeSlotDeclaration` continues with `COLON` or `SEMICOLON`. This is unambiguous under PEG ordered-choice because the first differentiating token (`EQUAL` vs `COLON`/`SEMICOLON`) is within the 3-lookahead window after the shared prefix ends.

### MemberTypeAliasDeclaration Adjustment

`MemberTypeAliasDeclaration` currently defines `modifiers:Modifier+` at the start of its own rule. Since modifiers are now captured by the inline prefix, the rule body for the inline alternative starts after the prefix. The rule definition itself changes to `Modifier*` to accommodate zero modifiers:

```storm
rule MemberTypeAliasDeclaration = modifiers:Modifier* TYPE name:IDENTIFIER
    typeParams:TypeParameterList?
    EQUAL type:RefinableTypeExpression
    constraints:WhereClause*
    SEMICOLON;
```

Note: The inline block handles prefix sharing transparently — the generated node will still have the `Modifiers` array populated by the inline prefix.

## Generated Node Changes

After the grammar change, the Storm source generator will produce:

- `AssociatedTypeSlotDeclarationNode` with fields: `Modifiers`, `Name`, `TypeParams`, `Constraints` (nullable), `SlotWhereConstraints` (nullable), implementing `IHasModifiersNode`
- The following generated types are removed:
  - `UnconstrainedAssociatedTypeSlotDeclarationNode`
  - `ModifiedAssociatedTypeSlotDeclarationNode`
  - `ModifiedTypeHeadNode`
  - `UnconstrainedAssociatedTypeMemberMemberDeclarationNode`
  - `ModifiedAssociatedTypeMemberMemberDeclarationNode`

The `MemberDeclaration` alternatives collapse from the current set to:
- `AssociatedTypeMemberMemberDeclarationNode` (the one unified alternative)

## ProgramModel Changes

### TypeSlotBuilder

Collapse three methods into one:

```csharp
internal sealed class TypeSlotBuilder
{
    private readonly BuilderContext context;

    public TypeSlotBuilder(BuilderContext context)
    {
        this.context = context;
    }

    public void Resolve(AssociatedTypeSlotDeclarationNode slotDecl, AbstractType typeDef)
    {
        var name = slotDecl.Name.Lexeme;
        var modifiers = ModifierBuilder.Build(slotDecl, context.Diagnostics);

        var slot = new TypeSlot(name, [], modifiers);
        typeDef.TypeSlotsList.Add(slot);
    }
}
```

Key changes:
- Single `Resolve` method (no longer static — needs `context` for diagnostics)
- Uses `ModifierBuilder.Build(slotDecl, diagnostics)` via the `IHasModifiersNode` generic overload
- When modifiers array is empty/default, `ModifierBuilder.Build` returns `default` MemberModifiers (matching old unconstrained behavior)

### AbstractTypeBuilder

The member dispatch switch simplifies from three cases to one:

```csharp
case AssociatedTypeMemberMemberDeclarationNode assocType:
    typeSlotBuilder.Resolve(assocType.AssociatedType, typeDef);
    break;
```

### ConcreteTypeBuilder

The three cases for "type slot not allowed on concrete" collapse to one:

```csharp
case AssociatedTypeMemberMemberDeclarationNode:
    Context.Diagnostics.ReportAtNode(ProgramModelDiagnostics.TypeSlotNotAllowedOnConcrete, member, null);
    break;
```

## Legacy SemanticModelBuilder Changes

### Abstract Member Dispatch

The three cases collapse. The logic needs to handle:
1. If modifiers are present → emit `MIST_AT_0005` diagnostic (modifiers not permitted on slots in old system)
2. If no modifiers + has constraints → register constrained slot (old `RegisterAssociatedTypeSlotDeclaration` logic)
3. If no modifiers + no constraints → register unconstrained slot (old `RegisterUnconstrainedAssociatedTypeSlotDeclaration` logic)

```csharp
case AssociatedTypeMemberMemberDeclarationNode associatedTypeMember:
    RegisterAssociatedTypeSlotDeclaration(associatedTypeMember.AssociatedType, abstractType, typeScope, symbolTable, sourceText, diagnostics);
    break;
```

### Unified Registration Method

Merge the two registration methods into one that handles both constrained and unconstrained cases. The logic is nearly identical — the only difference is that the constrained path has a `Constraints` field. Since the unified node makes `Constraints` nullable (via the optional constraint clause), the unified method checks for null:

```csharp
private static void RegisterAssociatedTypeSlotDeclaration(
    AssociatedTypeSlotDeclarationNode slotDecl,
    IType declaringType,
    Scope typeScope,
    SymbolTable symbolTable,
    SourceText? sourceText,
    DiagnosticBag diagnostics)
{
    // Emit diagnostic if modifiers are present (not yet supported in old system).
    if(!slotDecl.Modifiers.IsDefaultOrEmpty)
    {
        EmitModifiersOnAssociatedTypeSlotDiagnostic(slotDecl, sourceText, diagnostics);
    }

    var slotName = slotDecl.Name.Lexeme;
    var slotSpan = ToMistSourceSpan(slotDecl.Name.Span);
    var typeParams = ExtractTypeParameters(slotDecl.TypeParams, slotName, sourceText, diagnostics);
    var slotScope = new Scope(parent: typeScope);

    if(typeParams.Count > 0 && slotDecl.TypeParams is not null)
    {
        foreach(var tp in typeParams)
        {
            var (tpName, tpOrdinal) = GetTypeParameterNameAndOrdinal(tp);
            var tpSpan = GetTypeParameterSpan(slotDecl.TypeParams, tpOrdinal);
            var tpSymbol = new Symbol(tpName, SymbolKind.Class, SymbolOrigin.UserCode, slotScope, tp, tpSpan);
            slotScope.AddMember(tpSymbol);
        }
    }

    var slotType = new AssociatedTypeSlotType(
        displayName: slotName,
        memberScope: slotScope,
        containingNamespace: declaringType switch
        {
            AbstractType it => it.ContainingNamespace,
            _ => null
        },
        declaringType: declaringType,
        constraints: new TypeParameterConstraints(),
        typeParameters: typeParams);

    var slotSymbol = new Symbol(slotName, SymbolKind.TypeAlias, SymbolOrigin.UserCode, typeScope, slotType, slotSpan)
    {
        TypeParameters = typeParams
    };
    typeScope.AddMember(slotSymbol);
    symbolTable.Register(slotSymbol, slotDecl);
}
```

### EmitModifiersOnAssociatedTypeSlotDiagnostic

Updated to accept the unified `AssociatedTypeSlotDeclarationNode`:

```csharp
private static void EmitModifiersOnAssociatedTypeSlotDiagnostic(
    AssociatedTypeSlotDeclarationNode slotDecl,
    SourceText? sourceText,
    DiagnosticBag diagnostics)
{
    if(slotDecl.Modifiers.IsDefaultOrEmpty)
    {
        return;
    }

    var firstModifier = slotDecl.Modifiers[0];
    diagnostics.Add(MakeDiagnosticWithLocation(
        CommonDiagnostics.ModifiersOnAssociatedTypeSlot.Id.Value,
        DiagnosticSeverity.Error,
        "Modifiers are not permitted on associated type slot declarations",
        firstModifier,
        sourceText));
}
```

### DetectDuplicateAssociatedTypeSlots

Simplifies from two cases to one:

```csharp
switch(member)
{
    case AssociatedTypeMemberMemberDeclarationNode assoc:
        slotName = assoc.AssociatedType.Name.Lexeme;
        declarationNode = assoc.AssociatedType;
        break;
    default:
        continue;
}
```

### Where-Clause Resolution (Pass1_ResolveConstraints)

The existing code at line ~6939 already checks `declaringNode is not AssociatedTypeSlotDeclarationNode slotDecl`. This remains valid since the unified node retains the same type name. The access to `slotDecl.Constraints` and `slotDecl.SlotWhereConstraints` still works because the unified node still has these fields (now nullable). The code must add a null check:

```csharp
if(declaringNode is not AssociatedTypeSlotDeclarationNode slotDecl)
{
    continue;
}

// Only resolve constraints if the constraint clause is present.
if(slotDecl.Constraints is null)
{
    continue;
}
```

The pattern-match expression at line ~7651 similarly needs a null guard for `SlotWhereConstraints`.

## PrettyPrinter Changes

Collapse three emission methods into one:

```csharp
private void EmitAssociatedTypeSlotDeclaration(AssociatedTypeSlotDeclarationNode slotDecl)
{
    var modifiers = EmitModifiers(slotDecl.Modifiers);
    var name = slotDecl.Name.Lexeme;
    var typeParams = EmitTypeParameterList(slotDecl.TypeParams);

    if(slotDecl.Constraints is not null)
    {
        var constraints = EmitConstraintList(slotDecl.Constraints);
        var whereClauses = EmitConstraintClauses(slotDecl.SlotWhereConstraints);
        AppendLine($"{modifiers}type {name}{typeParams} : {constraints}{whereClauses};");
    }
    else
    {
        AppendLine($"{modifiers}type {name}{typeParams};");
    }
}
```

The dispatch in the member switch collapses to:

```csharp
case AssociatedTypeMemberMemberDeclarationNode associatedTypeMember:
    EmitAssociatedTypeSlotDeclaration(associatedTypeMember.AssociatedType);
    break;
```

## Test Changes

### AssociatedTypeDisambiguationPropertyTests

Property 2a currently asserts on `AssociatedTypeMemberMemberDeclarationNode` — this name stays the same. However, the test accesses `.AssociatedType` which remains the field name. This test should continue working without changes to the assertion logic.

If the test also validates `UnconstrainedAssociatedTypeMemberMemberDeclarationNode`, those assertions need updating to expect `AssociatedTypeMemberMemberDeclarationNode` with a null constraint clause.

## Ordering and Dependencies

1. **Grammar change first** — modify `Mist.storm` (unified rule, inline block, remove old rules)
2. **Build** — the Storm source generator regenerates node types; this will produce compile errors in all downstream code
3. **Fix ProgramModel** — `TypeSlotBuilder`, `AbstractTypeBuilder`, `ConcreteTypeBuilder`
4. **Fix SemanticModelBuilder** — unified dispatch and registration
5. **Fix PrettyPrinter** — unified emission
6. **Fix tests** — update any assertions on old node types
7. **Full build + test run** — verify no regressions

## Risks and Mitigations

- **Inline block syntax**: The exact inline syntax must match what Storm supports. The current grammar already uses `inline ModifiedTypeHead: { ... }` so the pattern is established. The new form uses inline with an anonymous prefix rather than a named rule reference.
- **Modifier* on MemberTypeAliasDeclaration**: Currently requires `Modifier+`. With the inline prefix providing `Modifier*`, a modifier-less type alias inside a type body becomes parseable. This may or may not be desirable semantically, but the grammar will accept it — validation can reject it at the semantic level if needed.
- **Null checks on Constraints/SlotWhereConstraints**: Multiple call sites assume these fields are non-null (the old constrained rule always had them). The unified node makes them nullable, so all access sites must be audited for null safety.
