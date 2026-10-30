# Design Document: Newtypes

## Overview

Newtypes introduce nominally distinct type cloning to the Mist programming language. A newtype creates a distinct type that is a structural clone of its source type — equivalent to copy-pasting the source type's definition and renaming it. The syntax reuses the existing `type` keyword: `type Name = new SourceType;`. The clone includes all member categories unconditionally: fields, properties, methods, constructors, operators, cast operators, and indexers. No filtering or transformation is performed.

The implementation spans the full compiler pipeline:
1. **Grammar** — A single Storm grammar rule for `NewtypeDeclaration`, added as a variant to `TopLevelItem` and `NamespaceBodyItem`
2. **Parser** — AST node generation via Storm source generator
3. **Pretty Printer** — Single-line formatting: `type {name} = new {source_type};`
4. **Semantic Model Builder** — Type registration with a new `SymbolKind.Newtype` and unconditional member cloning from the source type
5. **Type Checker** — Nominal distinctness: a value enters a newtype only by a cast or a literal in a visible slot, and leaves it for a copyable source implicitly
6. **C# Transpiler** — Code generation: the renamed clone as a class or struct, an intrinsic source wrapped, and the conversions each source admits
7. **TextMate Grammar** — Syntax highlighting for VS Code

**Revision, 2026-09-13.** Requirements 2, 3.2 and 3.5–3.8, 5, 9 and 10, the Identity and Conversions and C# Transpiler sections below, and Properties 3, 4, 11 and 12 were revised by the language owner's decision. The classic pipeline implements the earlier revision, in which a newtype and its source reach each other only by an explicit cast, a primitive newtype is a wrapper with explicit operators both ways, and a cast between a class or struct newtype and its source does not compile. The experimental pipeline implements this one.

### Design Rationale

- **Reuse of `type` keyword**: Newtypes are conceptually related to type aliases (both introduce a name for a type). Using `type Name = new SourceType;` makes the distinction clear — `new` signals cloning rather than aliasing.
- **Unconditional cloning**: All members are cloned without filtering. This keeps the feature simple and predictable. If the source type gains new members, the newtype automatically includes them.
- **Nominal distinctness**: The newtype is a completely separate type in the type system. This prevents accidental substitution and enables domain-specific type safety (e.g., `type UserId = new int;` cannot be confused with `type OrderId = new int;`).
- **A clone, never a view**: Mist cannot change an object's type, so a newtype is not a lens over a source value the way a base type is. A Jaguar is built from Tiger's declaration and shares nothing with Tiger at runtime, which is why a conversion between them must copy.
- **Intent at entry, freedom at exit**: What a newtype guards is a raw value entering its domain, so entry needs a cast, or a literal written where the slot's type is in sight. Leaving costs nothing where a copy cannot be told from the original, the way an enum reaches its backing value. A call argument is not a visible slot: the parameter's type sits in another declaration, and swapped literals are exactly the mistake a newtype exists to catch.
- **A class copies by its own rules**: A mutable class has no copy Mist could define for it -- shallow or deep, private state, owned fields -- so it converts only when it implements `IClonable` of itself, and the conversion is its own `Clone`.

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

### Identity and Conversions

**Building the clone (Requirement 3.2).** A newtype is built by binding its Source_Type's declaration a second time, as a class or struct named after the newtype, under a scope that sits between the clone's own type scope and the Source_Type's lexical parent. That scope answers the Source_Type's simple name with the newtype, and, for a constructed source, each type parameter of the definition with the instance's argument. Everything Requirement 3.2 lists then follows from ordinary binding: a `Tiger` field, a `Tiger` parameter, `new Tiger(...)` and `this` inside a body all resolve to `Jaguar`, and the clone's base list is Tiger's, Renamed. A declaration outside the node -- a `with` block, an extension block -- never reaches the clone, since nothing binds it for the clone (3.5).

The clone reuses the Source_Type's syntax, so a mistake in Tiger's body would be reported once for Tiger and once for Jaguar at the same place. The clone's diagnostics are compared with the source's and only those the source does not report are kept, anchored at the newtype declaration and naming the member (3.6).

**An intrinsic source (3.8, 5.2).** The clone of an intrinsic declaration is intrinsic, its members the Renamed contract, and it carries the synthesized `Value` field.

**Conversions (2.4, 2.5).** A newtype over a Copyable_Source is within its source for the conversion relation, the way a numeric widening is: implicit, lossless, and ranked, so `Take(int)` is applicable to an `Age` and `Take(Age)` beats it as the narrower target. A cast to such a newtype calls a conversion function whose parameter is the source, recorded on the cast as an enum's lookup is, so the operand is judged and lowered as an argument. Newtype to newtype over one source is the composition of the two and needs no rule of its own.

**Never its source (2.1).** The copy above is read as containment so that assignability, overload ranking and a
lossless leave all see it, and that reading is the one thing a type test must not inherit: a test asks what a
value IS, and the copy is a second value of another type. So `is`, a type pattern, a switch arm and `as` all ask
the relation WITHOUT the copy, and a newtype against its source -- or a source against its newtype, or either
against a type only the copy reaches -- is refused where the static type shows it, exactly as a source that
defines no copy already was. The two spellings refuse in different stages, `as` at construction because the
classification is what gives the expression its type, the rest at Stage 5, and they say the same thing. A value
whose static type cannot show it -- one boxed into `unknown` -- is not refused and answers false when the
program runs (5.3), which is the same rule reaching the same verdict a step later.

**A copy that does not hold (2.8).** A slot whose type names the Source_Type is that slot's Renamed type in the clone -- `Node<T>? next` is `Chain<T>? next` -- so the field-by-field copy has no pair of slots to copy between. Construction compares the clone's slots with the source's, applying the source construction's arguments, and where the first pair disagrees it routes to the clonable path instead: a source implementing `IClonable<Source_Type>` converts through Requirement 10, which is where such a copy belongs, since only the class author knows whether the slot is shared or cloned. A source that does not is left with neither conversion, the disagreeing slot and both its types recorded on the clone, and the refusal is owed at both kinds of position -- a cast and an implicit one -- because both directions are missing; one rule reports it from either analyzer.

**Literals (9).** Assignability is told whether the position it judges is one Requirement 9.2 names, and admits a Newtype_Literal there alone. An argument position never is.

**A clonable class (10).** Two explicit cast operators are synthesized on the newtype when the Source_Type names `IClonable` of itself in its own base list and declares the `Clone`: the newtype's Renamed `Clone` body for the source to the newtype, the Source_Type's `Clone` body for the reverse. Each body is bound again under a scope that answers `this` and every instance member from the operand's type, and a static member or a type name from the body's own declaration, so `(Jaguar)tiger` reads `tiger`'s state, constructs a `Jaguar`, and counts in `Jaguar`'s statics. That scope also carries the conversion itself, which is what lets two of the body's spellings mean here what they meant where it was written (10.4): a call to the same `Clone` on a value of the operand's type IS this conversion, replacing the call so the value crosses once and answers the result type, `?.` included and lifting the result back to optional; and a type test naming the result type -- `is`, a switch arm, the negation of either -- tests for the operand's type, since the value being tested never is the result type and a test that could only fail would let a deep copy truncate in silence. Everything else is unchanged, so a field the body shares rather than clones fails to bind and refuses the cast, naming it. The operator exists either way; whether its body holds is judged where a cast calls it. Stage 5 runs every rule over each such body on its own, withholding what they find, and the first error -- the binder's, or a rule's other than accessibility, which 10.2 waives -- is the reason a cast calling the operator is refused, naming the member at the error's location (10.4). A class that declares no `IClonable` of itself produces neither operator, and a cast is refused as defining no copy (10.3).

### C# Transpiler

Three strategies based on source type category. None lowers a newtype to its source, whose runtime type would then answer a type test the newtype must fail (Requirement 5.3).

#### Strategy 1: Source is a Class

The Renamed_Clone is an ordinary class, emitted as every class is, its base list, members and bodies coming from binding. A clonable source adds the two synthesized conversions (Requirement 10):

```csharp
// Mist: class Tiger : Cat, IClonable<Tiger> { ... }   type Jaguar = new Tiger;
// C# output:
public class Jaguar : Cat, IClonable<Jaguar>
{
    // Tiger's members and bodies, Renamed
    public static explicit operator Jaguar(Tiger @this) { /* Jaguar.Clone's body */ }
    public static explicit operator Tiger(Jaguar @this) { /* Tiger.Clone's body */ }
}
```

The operator's parameter stands for `this`, and every implicit member in the body is written through it or through its declaring type, a bare name resolving against `Jaguar` in C#. A member C# refuses from `Jaguar` -- one private to `Tiger`, a protected one read through a `Tiger` -- is reached through an `UnsafeAccessor` declared beside the operator, and an object initializer naming one runs its entries through a helper against the constructed value. Only an operator some cast calls is emitted, since one no cast calls may hold a body that does not type.

#### Strategy 2: Source is a Struct, or a Readonly Class

As Strategy 1, with the copy conversions of Requirements 2.4 and 2.5: an implicit conversion to the source and an explicit one from it, each copying every field. A source whose copy cannot be field for field (2.8) emits Strategy 1's conversions instead, or none.

```csharp
// Mist: type Vector2D = new Point;
// C# output:
public struct Vector2D
{
    // Point's members and bodies, Renamed
    public static implicit operator Point(Vector2D value) { /* copy each field */ }
    public static explicit operator Vector2D(Point value) { /* copy each field */ }
}
```

#### Strategy 3: Source is an Intrinsic Standard Type

A struct wrapping the backing value, supplying each Renamed intrinsic member by forwarding to the backing value's and wrapping a result of the source type:

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

    public static UserId operator +(UserId left, UserId right) => new UserId(left.Value + right.Value);
    // ... every other Renamed member of Int32

    public static implicit operator int(UserId id) => id.Value;
    public static explicit operator UserId(int value) => new UserId(value);
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

### Property 3: Entry needs intent

*For any* newtype and any value of another type -- its source type or another newtype over the same source among them -- using the value where the newtype is expected without a cast SHALL produce a type mismatch diagnostic error, unless the value is a Newtype_Literal in a position Requirement 9.2 names.

**Validates: Requirements 2.6, 9.2, 9.3**

### Property 4: A copyable newtype leaves freely and is never its source

*For any* newtype over a Copyable_Source, a value of the newtype SHALL be accepted where the source type is expected, and a type test of that value against the source type SHALL fail, both statically and when the program runs.

**Validates: Requirements 2.1, 2.4, 5.3**

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

### Property 11: The clone is renamed

*For any* member of a source type that takes or answers the source type, the newtype's member of the same name SHALL take or answer the newtype, and *for any* base type or abstract base of the source type, a newtype value SHALL be assignable to it.

**Validates: Requirements 2.2, 2.3, 3.2**

### Property 12: A class conversion copies

*For any* newtype over a mutable class implementing `IClonable` of itself, a cast in either direction SHALL produce a value that is not the operand's object and whose observable state is what the operand's `Clone` would produce.

**Validates: Requirements 10.1**

## Error Handling

| Condition | Diagnostic ID | Severity | Message |
|-----------|--------------|----------|---------|
| Missing `;` after newtype declaration | `MIST0122` | Error | `Expected ';' at end of newtype declaration` |
| Invalid token after `new` (not a type reference) | `MIST0122` | Error | `Expected type reference after 'new'` |
| Duplicate newtype name in scope | `MIST_DUPLICATE_SYMBOL (historical)` | Error | `Type '{name}' is already defined in this scope` |
| Undefined source type | `MIST_UNRESOLVED_TYPE (historical)` | Error | `Type '{name}' could not be found` |
| Source→Newtype use without cast | `MIST_TYPE_MISMATCH (historical)` | Error | `Cannot convert type '{source}' to '{newtype}' without an explicit cast` |
| Newtype→Source use without cast, mutable class source | `MIST_TYPE_MISMATCH (historical)` | Error | `Cannot convert type '{newtype}' to '{source}'` |
| Cross-newtype use without cast | `MIST_TYPE_MISMATCH (historical)` | Error | `Cannot convert type '{newtypeA}' to '{newtypeB}' without an explicit cast` |
| Newtype over an unsupported kind of type | `MIST0419` | Error | `A newtype over '{source}' is not supported: a newtype clones a class or a struct declared in Mist.` |
| A member of the renamed clone does not bind | `MIST0418` | Error | `'{newtype}' cannot clone '{source}', whose member '{member}' does not hold for '{newtype}': {reason}` |
| Cast between a mutable class source and its newtype, no `IClonable` of itself | `MIST0152` | Error | `Cannot cast '{from}' to '{to}'. '{source}' defines no copy to convert with; ...` |
| Cast whose `Clone` body does not hold with the operand as `this` | `MIST0154` | Error | `Cannot cast '{from}' to '{to}'. The cast runs '{type}.Clone' with a '{receiver}' as this, and it does not hold at '{member}': {reason}` |

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
