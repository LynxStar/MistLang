# Design: Grammar Type Declaration Unification

## Overview

This design unifies the separate `ClassDeclaration`, `StructDeclaration`, and `AbstractDeclaration` grammar rules into a single `TypeBodyDeclaration` rule, and consolidates all type-introducing declarations (class, struct, abstract, enum, newtype, type alias) under one `TypeDeclaration` alternation. The `AbstractMemberDeclaration` rule is removed — its members fold into the existing `MemberDeclaration` rule. Semantic validation moves to the builder/analyzer passes.

The key insight: abstract methods are just regular `MethodDeclaration` nodes where `MethodBody = SEMICOLON as MethodNoBody`. Abstract properties are regular `PropertyDeclaration` nodes with `AutoGetAccessor`/`AutoSetAccessor` variants. There is no syntactic difference between a "concrete" member and an "abstract" member — the distinction is purely semantic (whether the enclosing type is declared with the `abstract` keyword).

## Grammar Design

### TypeKeyword Rule

A new variant rule distinguishes the three body-bearing type kinds:

```storm
rule TypeKeyword = CLASS as ClassKeyword | STRUCT as StructKeyword | ABSTRACT as AbstractKeyword;
```

This produces three generated node types: `ClassKeywordTypeKeywordNode`, `StructKeywordTypeKeywordNode`, `AbstractKeywordTypeKeywordNode`.

### TypeBodyDeclaration Rule

A single unified rule replaces `ClassDeclaration`, `StructDeclaration`, and `AbstractDeclaration`:

```storm
rule TypeBodyDeclaration = attributes:AttributeList* modifiers:Modifier* keyword:TypeKeyword name:IDENTIFIER
    typeParams:TypeParameterList?
    baseClause:(COLON baseList:BaseList)?
    constraints:WhereClause*
    LEFT_BRACE members:MemberDeclaration* RIGHT_BRACE;
```

All three type kinds now share the same member rule (`MemberDeclaration*`). The parser no longer restricts which members appear in which type kind — that responsibility moves to semantic passes.

### TypeDeclaration Rule

The `TypeDeclaration` alternation now covers all type-introducing declarations:

```storm
rule TypeDeclaration
    = typeBody:TypeBodyDeclaration as BodyType
    | enumDeclaration:EnumDeclaration as EnumType
    | newtypeDeclaration:NewtypeDeclaration as NewtypeType
    | typeAliasDeclaration:TypeAliasDeclaration as TypeAliasType
    ;
```

`TypeBodyDeclaration` is tried first (PEG ordered choice). Enum, newtype, and type alias are absorbed into this alternation so that a single `TypeDeclaration` reference covers all named-type introductions.

### TopLevelItem Rule (Simplified)

```storm
@memo
rule TopLevelItem
    = namespaceDeclaration:NamespaceDeclaration as NamespaceItem
    | typeDeclaration:TypeDeclaration           as TypeItem
    | extensionDeclaration:ExtensionBlockDeclaration as ExtensionItem
    | withDeclaration:WithDeclaration           as WithItem
    | usingDeclaration:UsingDeclaration         as UsingItem
    | statement:Statement                       as StatementItem
    ;
```

The separate `NewtypeItem` and `TypeAliasItem` alternations are removed — they are now reached through `TypeDeclaration`.

### NamespaceBodyItem Rule (Simplified)

```storm
rule NamespaceBodyItem
    = typeDeclaration:TypeDeclaration              as NamespaceTypeItem
    | extensionDeclaration:ExtensionBlockDeclaration as NamespaceExtensionItem
    | namespaceDeclaration:NamespaceDeclaration    as NamespaceNamespaceItem
    ;
```

The separate `NamespaceNewtypeItem` and `NamespaceTypeAliasItem` alternations are removed.

### MemberDeclaration Unification

The existing `MemberDeclaration` rule already contains all necessary variants:

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
    | memberTypeAlias:MemberTypeAliasDeclaration as MemberTypeAliasMember
    | associatedType:AssociatedTypeSlotDeclaration as AssociatedTypeMember
    | unconstrainedAssociatedType:UnconstrainedAssociatedTypeSlotDeclaration as UnconstrainedAssociatedTypeMember
    ;
```

No changes needed here. The members that `AbstractMemberDeclaration` previously contained map as follows:

| AbstractMemberDeclaration variant | Covered by |
|---|---|
| `AbstractMethodDeclaration` (signature + semicolon) | `MethodDeclaration` with `MethodNoBody` body variant |
| `AbstractPropertyDeclaration` (abstract accessors) | `PropertyDeclaration` with `AutoGetAccessor`/`AutoSetAccessor` |
| `AbstractDefaultMethodMember` (method with body) | `MethodDeclaration` with `MethodBlockBody` |
| Associated type slots | `AssociatedTypeMember` / `UnconstrainedAssociatedTypeMember` |
| Nested types | `NestedTypeMember` |

### Removed Rules

| Rule | Replacement |
|---|---|
| `ClassDeclaration` | `TypeBodyDeclaration` with `ClassKeyword` |
| `StructDeclaration` | `TypeBodyDeclaration` with `StructKeyword` |
| `AbstractDeclaration` | `TypeBodyDeclaration` with `AbstractKeyword` |
| `AbstractMemberDeclaration` | Members folded into `MemberDeclaration` |
| `AbstractMethodDeclaration` | `MethodDeclaration` with `MethodNoBody` |
| `AbstractPropertyDeclaration` | `PropertyDeclaration` with auto accessors |
| `AbstractAccessorDeclaration` | Existing `AccessorDeclaration` variants |

## AST Node Changes

### Before

The source generator produced three separate declaration node hierarchies:

```
TypeDeclarationNode
├── ClassTypeTypeDeclarationNode  → .ClassDeclaration (ClassDeclarationNode)
│     └── .Name, .Modifiers, .BaseList, .Members (MemberDeclarationNode[])
├── StructTypeTypeDeclarationNode → .StructDeclaration (StructDeclarationNode)
│     └── .Name, .Modifiers, .BaseList, .Members (MemberDeclarationNode[])
├── AbstractTypeTypeDeclarationNode → .AbstractDeclaration (AbstractDeclarationNode)
│     └── .Name, .Modifiers, .BaseList, .Members (AbstractMemberDeclarationNode[])
└── EnumTypeTypeDeclarationNode → .EnumDeclaration (EnumDeclarationNode)
```

### After

All three body-bearing types collapse to one:

```
TypeDeclarationNode
├── BodyTypeTypeDeclarationNode → .TypeBody (TypeBodyDeclarationNode)
│     └── .Keyword (TypeKeywordNode), .Name, .Modifiers, .BaseList, .Members (MemberDeclarationNode[])
├── EnumTypeTypeDeclarationNode → .EnumDeclaration (EnumDeclarationNode)
├── NewtypeTypeTypeDeclarationNode → .NewtypeDeclaration (NewtypeDeclarationNode)
└── TypeAliasTypeTypeDeclarationNode → .TypeAliasDeclaration (TypeAliasDeclarationNode)
```

Where `TypeKeywordNode` has three variants:
- `ClassKeywordTypeKeywordNode`
- `StructKeywordTypeKeywordNode`
- `AbstractKeywordTypeKeywordNode`

All three previously separate paths collapse to one. Consumers read `.TypeBody.Keyword` to determine the kind.

### Access Patterns

| What you need | Before | After |
|---|---|---|
| Type name | `ct.ClassDeclaration.Name.Lexeme` | `body.TypeBody.Name.Lexeme` |
| Modifiers | `ct.ClassDeclaration.Modifiers` | `body.TypeBody.Modifiers` |
| Base list | `ct.ClassDeclaration.BaseList` | `body.TypeBody.BaseList` |
| Members | `ct.ClassDeclaration.Members` | `body.TypeBody.Members` |
| Type kind | `switch (node) { case ClassType... }` | `switch (body.TypeBody.Keyword) { case ClassKeyword... }` |

## Component Impact

### TypeBuilder (Phase 1 — Type Registration)

Currently switches on three node types to create type shells:

```csharp
// Before
switch (typeDecl)
{
    case ClassTypeTypeDeclarationNode ct:
        return new ClassDefinition(ct.ClassDeclaration.Name.Lexeme, ...);
    case StructTypeTypeDeclarationNode st:
        return new StructDefinition(st.StructDeclaration.Name.Lexeme, ...);
    case AbstractTypeTypeDeclarationNode at:
        return new AbstractType(at.AbstractDeclaration.Name.Lexeme, ...);
}
```

After unification:

```csharp
// After
switch (typeDecl)
{
    case BodyTypeTypeDeclarationNode body:
        var name = body.TypeBody.Name.Lexeme;
        return body.TypeBody.Keyword switch
        {
            ClassKeywordTypeKeywordNode => new ClassDefinition(name, ...),
            StructKeywordTypeKeywordNode => new StructDefinition(name, ...),
            AbstractKeywordTypeKeywordNode => new AbstractType(name, ...),
            _ => throw new InvalidOperationException(...)
        };
    case EnumTypeTypeDeclarationNode et:
        return new EnumDefinition(et.EnumDeclaration.Name.Lexeme, ...);
    case NewtypeTypeTypeDeclarationNode nt:
        return new NewtypeDefinition(nt.NewtypeDeclaration.Name.Lexeme, ...);
    case TypeAliasTypeTypeDeclarationNode ta:
        return new TypeAliasDefinition(ta.TypeAliasDeclaration.Name.Lexeme, ...);
}
```

The `TopLevelItem` dispatch also simplifies — the `NewtypeItemTopLevelItemNode` and `TypeAliasItemTopLevelItemNode` cases are removed since newtypes and type aliases are now reached through `TypeItemTopLevelItemNode` → `TypeDeclarationNode`. The `RegisterType` method handles them via the `NewtypeTypeTypeDeclarationNode` and `TypeAliasTypeTypeDeclarationNode` cases.

Similarly, `ProcessNamespaceBody` loses its separate newtype/alias branches.

### ConcreteTypeBuilder (Phase 2 — Class/Struct)

Currently unwraps through two separate node types:

```csharp
// Before
var members = typeDef.DeclarationNode switch
{
    ClassTypeTypeDeclarationNode ct => ct.ClassDeclaration.Members,
    StructTypeTypeDeclarationNode st => st.StructDeclaration.Members,
    _ => throw ...
};
```

After: single path through the unified node:

```csharp
// After
var body = (BodyTypeTypeDeclarationNode)typeDef.DeclarationNode;
var members = body.TypeBody.Members;
var modifiers = body.TypeBody.Modifiers;
```

The builder no longer needs to distinguish class vs. struct at the member-access level. The keyword variant is only needed for type-kind-specific validation, which is already handled during Phase 1 registration.

### AbstractTypeBuilder (Phase 2 — Abstract)

Currently iterates over `AbstractMemberDeclarationNode` entries:

```csharp
// Before
if (typeDef.DeclarationNode is not AbstractTypeTypeDeclarationNode at)
    throw ...;

foreach (var member in at.AbstractDeclaration.Members)  // AbstractMemberDeclarationNode
    ResolveMember(member, typeDef);
```

After: iterates over `MemberDeclarationNode` entries (same type as concrete):

```csharp
// After
var body = (BodyTypeTypeDeclarationNode)typeDef.DeclarationNode;

foreach (var member in body.TypeBody.Members)  // MemberDeclarationNode
    ResolveMember(member, typeDef);
```

The member dispatch switch stays the same but works with `MemberDeclarationNode` variants instead of `AbstractMemberDeclarationNode` variants. An abstract method is now identified as a `MethodMemberMemberDeclarationNode` whose `Method.Body` is `MethodNoBodyMethodBodyNode`.

### BaseTypeResolver

Currently switches on three node types to extract `.BaseList`:

```csharp
// Before
var baseList = typeDecl switch
{
    ClassTypeTypeDeclarationNode ct => ct.ClassDeclaration.BaseList,
    StructTypeTypeDeclarationNode st => st.StructDeclaration.BaseList,
    AbstractTypeTypeDeclarationNode at => at.AbstractDeclaration.BaseList,
    _ => null
};
```

After: one path:

```csharp
// After
var baseList = typeDecl switch
{
    BodyTypeTypeDeclarationNode body => body.TypeBody.BaseList,
    _ => null
};
```

### MemberTypeDefinition.DeclarationNode

The property remains typed as `TypeDeclarationNode` — this is the alternation rule's node type. `BodyTypeTypeDeclarationNode` is a subclass of `TypeDeclarationNode`, so the stash still works. Consumers cast to `BodyTypeTypeDeclarationNode` when they need to access `.TypeBody`.

### MistPrettyPrinter

Currently has separate formatting methods for class/struct/abstract:

```csharp
// Before
case ClassTypeTypeDeclarationNode ct:
    PrintClassDeclaration(ct.ClassDeclaration);
case StructTypeTypeDeclarationNode st:
    PrintStructDeclaration(st.StructDeclaration);
case AbstractTypeTypeDeclarationNode at:
    PrintAbstractDeclaration(at.AbstractDeclaration);
```

After: one method that emits the keyword based on variant:

```csharp
// After
case BodyTypeTypeDeclarationNode body:
    PrintTypeBodyDeclaration(body.TypeBody);

void PrintTypeBodyDeclaration(TypeBodyDeclarationNode typeBody)
{
    PrintModifiers(typeBody.Modifiers);
    var keyword = typeBody.Keyword switch
    {
        ClassKeywordTypeKeywordNode => "class",
        StructKeywordTypeKeywordNode => "struct",
        AbstractKeywordTypeKeywordNode => "abstract",
        _ => throw ...
    };
    Write(keyword);
    Write(" ");
    Write(typeBody.Name.Lexeme);
    // ... shared structure: type params, base clause, constraints, members
}
```

### CSharpEmitter

Currently dispatches to separate emit methods:

```csharp
// Before
case ClassTypeTypeDeclarationNode ct:  EmitClass(ct.ClassDeclaration);
case StructTypeTypeDeclarationNode st: EmitStruct(st.StructDeclaration);
case AbstractTypeTypeDeclarationNode at: EmitAbstract(at.AbstractDeclaration);
```

After: one emit method with keyword-based dispatch for the C# output keyword:

```csharp
// After
case BodyTypeTypeDeclarationNode body:
    var csharpKeyword = body.TypeBody.Keyword switch
    {
        ClassKeywordTypeKeywordNode => "class",
        StructKeywordTypeKeywordNode => "struct",
        AbstractKeywordTypeKeywordNode => "interface",
        _ => throw ...
    };
    EmitTypeBody(body.TypeBody, csharpKeyword);
```

### SemanticModelBuilder (Legacy)

Same pattern as above: replace three-way dispatch with keyword check on the unified node. This is the largest file impact due to its size, but the transformation is mechanical.

### Test Files

Tests that construct AST nodes must be updated:

```csharp
// Before
var node = new ClassDeclarationNode { Name = ..., Modifiers = ..., Members = ... };

// After
var node = new TypeBodyDeclarationNode
{
    Keyword = new ClassKeywordTypeKeywordNode(),
    Name = ...,
    Modifiers = ...,
    Members = ...
};
```

Pattern applies across ~30+ test files.

## Design Constraints

1. **ProgramModel hierarchy unchanged.** The `ClassDefinition`, `StructDefinition`, `AbstractType`, `EnumDefinition`, `NewtypeDefinition`, and `TypeAliasDefinition` classes remain as-is. The grammar simplification only affects the AST layer — how we parse and extract information from syntax trees.

2. **PEG ordered choice.** The Storm grammar is PEG-based. `TypeBodyDeclaration` must be tried before `EnumDeclaration` in the `TypeDeclaration` alternation because `enum` declarations also start with optional attributes and modifiers. The `TypeKeyword` at position 3 disambiguates.

3. **Source generator rebuild.** The Storm source generator regenerates all node types when the grammar changes. A clean build is required after grammar modifications. Incremental builds will fail with missing-type errors until the generator runs.

4. **Zero warnings policy.** All code must compile warning-free after changes. Unused node type references (e.g., `AbstractMemberDeclarationNode`) must be removed from all switch statements and using directives.

5. **Round-trip preservation.** Parsing then pretty-printing must produce equivalent source. The printer must emit the correct keyword token matching the original source.

## Migration Strategy

### Phase 1: Grammar Modification

1. Add `TypeKeyword` rule
2. Add `TypeBodyDeclaration` rule
3. Modify `TypeDeclaration` to include `BodyType`, `NewtypeType`, `TypeAliasType` alternations
4. Simplify `TopLevelItem` (remove `NewtypeItem`, `TypeAliasItem`)
5. Simplify `NamespaceBodyItem` (remove `NamespaceNewtypeItem`, `NamespaceTypeAliasItem`)
6. Remove `ClassDeclaration`, `StructDeclaration`, `AbstractDeclaration`
7. Remove `AbstractMemberDeclaration`, `AbstractMethodDeclaration`, `AbstractPropertyDeclaration`, `AbstractAccessorDeclaration`
8. Build to regenerate AST nodes

### Phase 2: Fix Compilation Errors (Wave by Wave)

The order is dictated by dependency — each wave depends on the previous:

1. **TypeBuilder** — Phase 1 registration must compile first since it's the entry point for type creation. Update `CreateTypeDefinition`, `GetMembers`, `RegisterNestedTypes`, `Build`, `ProcessNamespaceBody`.

2. **ConcreteTypeBuilder + AbstractTypeBuilder + BaseTypeResolver** — Phase 2 builders depend on the stashed `DeclarationNode` from Phase 1. Update member access and modifier extraction.

3. **PrettyPrinter** — Formatting must be updated to handle the unified node.

4. **CSharpEmitter** — Backend emission updated to dispatch on keyword variant.

5. **SemanticModelBuilder** — Legacy system updated with the same mechanical transformation.

6. **Test files** — Update AST construction in ~30+ test files.

### Phase 3: Verification

1. Full build with zero warnings
2. Run complete test suite — zero regressions
3. Verify round-trip property: parse → print → parse produces equivalent AST
4. Verify sample programs produce identical compiled output
