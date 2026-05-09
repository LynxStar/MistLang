# Design Document: Newtypes

## Overview

Newtypes introduce nominally distinct type cloning to the Mist programming language. A newtype creates a distinct type that is a structural clone of its source type — equivalent to copy-pasting the source type's definition and renaming it. The syntax reuses the existing `type` keyword: `type Name = new SourceType;`. The clone includes all member categories unconditionally: fields, properties, methods, constructors, operators, cast operators, and indexers. No filtering or transformation is performed.

The implementation spans the full compiler pipeline:
1. **Grammar** — A single Storm grammar rule for `NewtypeDeclaration`, added as a variant to `TopLevelItem` and `NamespaceBodyItem`
2. **Parser** — AST node generation via Storm source generator
3. **Pretty Printer** — Single-line formatting: `type {name} = new {source_type};`
4. **Semantic Model Builder** — Type registration with a new `SymbolKind.Newtype` and unconditional member cloning from the source type
5. **Type Checker** — Nominal distinctness enforcement (explicit cast required for conversion)
6. **C# Transpiler** — Code generation: class→class, struct→struct, primitive→readonly struct wrapper
7. **TextMate Grammar** — Syntax highlighting for VS Code

### Design Rationale

- **Reuse of `type` keyword**: Newtypes are conceptually related to type aliases (both introduce a name for a type). Using `type Name = new SourceType;` makes the distinction clear — `new` signals cloning rather than aliasing.
- **Unconditional cloning**: All members are cloned without filtering. This keeps the feature simple and predictable. If the source type gains new members, the newtype automatically includes them.
- **Nominal distinctness**: The newtype is a completely separate type in the type system. This prevents accidental substitution and enables domain-specific type safety (e.g., `type UserId = new int;` cannot be confused with `type OrderId = new int;`).
- **Primitive wrapping via readonly struct**: Primitives have no members to clone, so the transpiler emits a lightweight readonly struct wrapper with explicit conversion operators. This preserves value semantics while enforcing nominal distinctness.

## Architecture

The newtype feature integrates into the existing compiler pipeline without introducing new phases:

```mermaid
graph LR
    A[Source Text] --> B[Lexer]
    B --> C[Parser]
    C --> D[AST with NewtypeDeclaration nodes]
    D --> E[SemanticModelBuilder]
    E --> F[Member Cloning]
    F --> G[NewtypeType registered in Semantic Model]
    G --> H[Type Checker]
    H --> I[CSharp Transpiler]
    I --> J[C# Source Output]
```

### Pipeline Integration Points

| Phase | Action |
|-------|--------|
| Grammar | New `NewtypeDeclaration` rule; variants added to `TopLevelItem` and `NamespaceBodyItem` |
| Parser | Storm-generated; no hand-written code needed |
| Pretty Printer | New `EmitNewtypeDeclaration` method in `MistPrettyPrinter` |
| SemanticModelBuilder Pass 1 | `RegisterNewtypeDeclaration` — registers symbol, clones members |
| SemanticModelBuilder Pass 2 | `ResolveNewtypeDeclaration` — resolves source type reference |
| Type Checker | `IsDirectlyAssignable` updated to reject newtype↔source assignments |
| C# Transpiler | `EmitNewtypeDeclaration` — emits class/struct/wrapper based on source category |

## Components and Interfaces

### Grammar Rule

A single new rule added to `Mist.storm`:

```
rule NewtypeDeclaration = TYPE name:IDENTIFIER EQUAL NEW sourceType:TypeReference SEMICOLON;
```

Added as variants:
```
rule TopLevelItem
    = ...existing variants...
    | newtypeDeclaration:NewtypeDeclaration as NewtypeItem
    ;

rule NamespaceBodyItem
    = ...existing variants...
    | newtypeDeclaration:NewtypeDeclaration as NamespaceNewtypeItem
    ;
```

The Storm source generator will produce a `NewtypeDeclarationNode` AST class with properties:
- `Name` (Token — the IDENTIFIER)
- `SourceType` (TypeReferenceNode — the source type)

### Pretty Printer

A new method `EmitNewtypeDeclaration(NewtypeDeclarationNode)` in `MistPrettyPrinter`:

```csharp
private void EmitNewtypeDeclaration(NewtypeDeclarationNode newtypeDecl)
{
    var name = newtypeDecl.Name.Lexeme;
    var sourceType = EmitTypeReference(newtypeDecl.SourceType);
    AppendLine($"type {name} = new {sourceType};");
}
```

Called from `EmitCompilationUnit` (for top-level items) and `EmitNamespaceBodyItem` (for namespace-scoped items) via pattern matching on the new `NewtypeItemTopLevelItemNode` and `NamespaceNewtypeItemNamespaceBodyItemNode` variants.

### Semantic Model Builder

#### Symbol Registration (Pass 1)

A new `RegisterNewtypeDeclaration` method:

```csharp
private static void RegisterNewtypeDeclaration(
    NewtypeDeclarationNode newtypeDecl,
    Scope parentScope,
    SymbolTable symbolTable,
    SourceText? sourceText,
    DiagnosticBag diagnostics)
{
    var name = newtypeDecl.Name.Lexeme;
    var span = ToMistSourceSpan(newtypeDecl.Name.Span);

    // Check for duplicate name in scope
    foreach (var member in parentScope.Members)
    {
        if (member.Name == name)
        {
            diagnostics.Add(MakeDiagnosticWithLocation(...));
            return;
        }
    }

    var symbol = new Symbol(
        name,
        SymbolKind.Newtype,
        SymbolOrigin.UserCode,
        parentScope,
        declaredType: null,  // Set in Pass 2
        span);

    parentScope.AddMember(symbol);
    symbolTable.Register(symbol, newtypeDecl);
}
```

#### Type Resolution and Member Cloning (Pass 2)

A new `ResolveNewtypeDeclaration` method:

```csharp
private static void ResolveNewtypeDeclaration(
    NewtypeDeclarationNode newtypeDecl,
    Scope fileScope,
    SymbolTable symbolTable,
    SourceText? sourceText,
    DiagnosticBag diagnostics)
{
    var name = newtypeDecl.Name.Lexeme;
    var symbol = FindSymbolInScope(fileScope, name);

    // Resolve source type
    var sourceType = ResolveTypeReference(newtypeDecl.SourceType, fileScope, symbolTable, ...);
    if (sourceType is ErrorType)
    {
        symbol.DeclaredType = ErrorType.Instance;
        return;
    }

    // Create NewtypeType and clone all members
    var newtypeType = new NewtypeType(name, sourceType);
    CloneAllMembers(sourceType, newtypeType, symbolTable);
    symbol.DeclaredType = newtypeType;
}
```

Member cloning copies all member symbols from the source type's scope into the newtype's scope:
- Fields → cloned as-is
- Properties → cloned with all accessors
- Methods → cloned with signatures preserved
- Constructors → cloned (name updated to newtype name)
- Operators → cloned as-is
- Cast operators → cloned as-is
- Indexers → cloned as-is

### SymbolKind Extension

```csharp
public enum SymbolKind
{
    // ... existing variants ...
    Newtype,
}
```

### NewtypeType

A new type class implementing `IType`:

```csharp
public class NewtypeType : IType
{
    public string Name { get; }
    public IType SourceType { get; }
    public IScope MemberScope { get; }

    public NewtypeType(string name, IType sourceType)
    {
        Name = name;
        SourceType = sourceType;
        MemberScope = new Scope(name);
    }
}
```

### Type Checker Updates

The `IsDirectlyAssignable` method is updated to reject assignments between:
- Newtype → Source type (without explicit cast)
- Source type → Newtype (without explicit cast)
- Newtype A → Newtype B (even if they share the same source)

The `CheckCastExpression` method is updated to allow explicit casts between a newtype and its source type.

Member access on a newtype instance resolves against the newtype's own member scope (populated during cloning), not the source type's scope.

### C# Transpiler

A new `EmitNewtypeDeclaration` method with three strategies based on source type category:

#### Strategy 1: Source is a Class

Emit a C# class with the newtype name containing all cloned members:

```csharp
// Mist: type OrderId = new EntityBase;
// C# output:
public class OrderId
{
    // All members from EntityBase, cloned
}
```

#### Strategy 2: Source is a Struct

Emit a C# struct with the newtype name containing all cloned members:

```csharp
// Mist: type Vector2D = new Point;
// C# output:
public struct Vector2D
{
    // All members from Point, cloned
}
```

#### Strategy 3: Source is a Primitive

Emit a C# readonly struct wrapping the primitive value with explicit conversion operators:

```csharp
// Mist: type UserId = new int;
// C# output:
public readonly struct UserId
{
    public readonly int Value;

    public UserId(int value)
    {
        Value = value;
    }

    public static explicit operator UserId(int value) => new UserId(value);
    public static explicit operator int(UserId id) => id.Value;
}
```

### TextMate Grammar

Update `Editor/mist-syntax-highlighting/syntaxes/mist.tmLanguage.json`:

1. Add a pattern to recognize `new` after `=` in type declaration context with scope `keyword.operator.new.mist`
2. Ensure the identifier after `type` in a newtype declaration gets scope `entity.name.type.newtype.mist`

This can be achieved by adding a new pattern in the `type-declarations` group that matches the full `type Name = new ...` pattern before the existing type alias pattern.

## Data Models

### AST Nodes (Storm-generated)

```
NewtypeDeclarationNode
├── Name: Token (IDENTIFIER)
└── SourceType: TypeReferenceNode
```

The `TopLevelItemNode` discriminated union gains a new variant:
```
NewtypeItemTopLevelItemNode
└── NewtypeDeclaration: NewtypeDeclarationNode
```

The `NamespaceBodyItemNode` discriminated union gains a new variant:
```
NamespaceNewtypeItemNamespaceBodyItemNode
└── NewtypeDeclaration: NewtypeDeclarationNode
```

### Semantic Model Types

```
NewtypeType : IType
├── Name: string
├── SourceType: IType
└── MemberScope: IScope (contains cloned members)
```

### Symbol Table Entry

```
Symbol
├── Name: "{newtype_name}"
├── Kind: SymbolKind.Newtype
├── Origin: SymbolOrigin.UserCode
├── ContainingScope: (enclosing namespace or global scope)
├── DeclaredType: NewtypeType
└── DeclarationSpan: (source location)
```

## Correctness Properties

*A property is a characteristic or behavior that should hold true across all valid executions of a system — essentially, a formal statement about what the system should do. Properties serve as the bridge between human-readable specifications and machine-verifiable correctness guarantees.*

### Property 1: Parsing produces correct AST structure

*For any* valid identifier and valid type reference, parsing `type {id} = new {typeRef};` SHALL produce a NewtypeDeclaration AST node whose `Name` lexeme equals the identifier and whose `SourceType` matches the type reference.

**Validates: Requirements 1.1**

### Property 2: Missing semicolon produces diagnostic

*For any* valid newtype declaration source text, removing the trailing semicolon and parsing SHALL produce at least one diagnostic error.

**Validates: Requirements 1.3**

### Property 3: Nominal distinctness (bidirectional non-assignability)

*For any* newtype and its source type, assigning a value of the source type to a variable of the newtype (or vice versa) without an explicit cast SHALL produce a type mismatch diagnostic error.

**Validates: Requirements 2.1, 2.3, 2.4**

### Property 4: Cross-newtype non-assignability

*For any* two distinct newtypes that share the same source type, assigning a value of one newtype to a variable of the other SHALL produce a type mismatch diagnostic error.

**Validates: Requirements 2.5**

### Property 5: Member access resolves on cloned set

*For any* newtype whose source type has at least one accessible member, accessing that member on a newtype instance SHALL resolve successfully (no "member not found" error).

**Validates: Requirements 2.2**

### Property 6: Symbol registration with correct kind

*For any* valid NewtypeDeclaration, after semantic model building, the symbol table SHALL contain a symbol with `SymbolKind.Newtype` for the declared name in the enclosing scope.

**Validates: Requirements 3.1**

### Property 7: Member cloning completeness

*For any* source type with N members (across all categories: fields, properties, methods, constructors, operators, cast operators, indexers), the newtype's member scope SHALL contain exactly N cloned members.

**Validates: Requirements 3.2**

### Property 8: Pretty-printer round-trip

*For any* valid newtype declaration source text, parsing then pretty-printing then parsing then pretty-printing SHALL produce identical text in both pretty-print passes.

**Validates: Requirements 4.1, 4.2**

### Property 9: Transpilation preserves type category

*For any* newtype whose source is a class, the transpiled C# output SHALL be a class declaration; for any newtype whose source is a struct, the transpiled C# output SHALL be a struct declaration.

**Validates: Requirements 5.1, 5.2**

### Property 10: Transpiled C# compiles without errors

*For any* valid newtype declaration (with a resolvable source type), the transpiled C# output SHALL compile without errors.

**Validates: Requirements 5.4**

## Error Handling

| Condition | Diagnostic ID | Severity | Message |
|-----------|--------------|----------|---------|
| Missing `;` after newtype declaration | `MIST_PARSE_ERROR` | Error | `Expected ';' at end of newtype declaration` |
| Invalid token after `new` (not a type reference) | `MIST_PARSE_ERROR` | Error | `Expected type reference after 'new'` |
| Duplicate newtype name in scope | `MIST_DUPLICATE_SYMBOL` | Error | `Type '{name}' is already defined in this scope` |
| Undefined source type | `MIST_UNRESOLVED_TYPE` | Error | `Type '{name}' could not be found` |
| Source→Newtype assignment without cast | `MIST_TYPE_MISMATCH` | Error | `Cannot convert type '{source}' to '{newtype}' without an explicit cast` |
| Newtype→Source assignment without cast | `MIST_TYPE_MISMATCH` | Error | `Cannot convert type '{newtype}' to '{source}' without an explicit cast` |
| Cross-newtype assignment | `MIST_TYPE_MISMATCH` | Error | `Cannot convert type '{newtypeA}' to '{newtypeB}'` |

**Error recovery strategy**: When the source type is unresolved, the newtype symbol is assigned `ErrorType` so that downstream phases (type checker, transpiler) do not produce cascading errors. The newtype is still registered in the symbol table to prevent "undefined type" errors on subsequent references to the newtype name.

## Testing Strategy

### Unit Tests (Example-Based)

- **Parser disambiguation**: `type Foo = Bar;` parses as TypeAliasDeclaration (not newtype)
- **Parser error cases**: Invalid token after `new`, missing semicolon with specific position verification
- **Semantic errors**: Duplicate name, undefined source type
- **Primitive transpilation**: Each primitive type (int, string, bool, float, double, decimal, char, byte, short, long) produces correct readonly struct wrapper
- **TextMate grammar**: Verify scopes are assigned correctly for newtype syntax
- **Sample baseline**: Compile and run sample, compare output to baseline

### Property-Based Tests

Property-based tests use FsCheck to verify universal properties across generated inputs. Each test runs a minimum of 100 iterations.

**Library**: FsCheck (already used in the project)

**Test configuration**:
- Minimum 100 iterations per property
- Each test tagged with: `Feature: newtypes, Property {N}: {description}`

**Process spawning note**: Properties 3, 4, 5, 9, and 10 require compilation. Per the test process spawning policy, these use the **batching pattern**: generate all scenarios upfront, build a single Mist program containing all scenarios, compile once via the shared `CompilerDriver` instance (in-process), and run once. Output lines are compared per-scenario.

**Properties to implement**:
1. Parse round-trip (Property 8) — pure, no process spawn needed
2. Parse produces correct AST (Property 1) — pure, no process spawn needed
3. Missing semicolon diagnostic (Property 2) — pure, no process spawn needed
4. Nominal distinctness (Property 3) — batched compilation
5. Cross-newtype non-assignability (Property 4) — batched compilation
6. Member access resolution (Property 5) — batched compilation
7. Symbol registration (Property 6) — in-process semantic model build
8. Member cloning completeness (Property 7) — in-process semantic model build
9. Transpilation category preservation (Property 9) — in-process transpilation
10. Transpiled C# compiles (Property 10) — batched, 1 process spawn per batch

### Integration Tests

- **Sample compilation**: Compile `samples/Newtypes/Program.mist` and verify zero exit code
- **Baseline comparison**: Run sample and diff output against `samples/__baselines__/Newtypes.txt`
- **AllFeatures integration**: Verify `AllFeatures` sample still compiles and runs after adding Newtypes call
