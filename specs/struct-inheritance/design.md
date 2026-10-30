# Design Document: Struct Inheritance

#[[file:requirements.md]]

## Overview

Struct inheritance adds structural composition to Mist value types. A derived struct contains all fields from its base struct plus its own. The feature reuses the existing `:` syntax and parser grammar (the `interfaceList` slot on `StructDeclaration` already accepts a `BaseList`). The semantic model builder distinguishes the first entry as a base struct (if it resolves to a `StructType`) from interfaces. The type checker adds struct-subtype assignability (derived→base is implicit), and the C# emitter flattens inherited fields and synthesizes implicit conversion operators.

No grammar changes are required — the existing `StructDeclaration` rule already parses `: BaseList` identically to how `ClassDeclaration` does.

---

## Architecture

### Component Interaction Flow

```
Source: struct Point3D : Point { int Z; }
         │
         ▼
┌─────────────────┐
│     Parser      │  Already parses `: Point` into interfaceList BaseList
└────────┬────────┘
         │ AST: StructDeclarationNode with Interfaces = BaseList[Point]
         ▼
┌─────────────────────────────┐
│  SemanticModelBuilder       │
│  Pass1_ResolveBaseTypes     │  NEW: resolve struct base types (mirrors class logic)
│  Pass1_ResolveInterfaceLists│  MODIFIED: skip first entry if already consumed as base struct
└────────┬────────────────────┘
         │ StructType.BaseType = StructType(Point)
         │ StructType.MemberScope includes inherited fields
         ▼
┌─────────────────────────────┐
│  TypeCheckerAnalyzer        │
│  IsAssignable()             │  MODIFIED: add struct base-type chain walk
│  ImplementsInterface()      │  MODIFIED: walk struct base chain for interface checks
└────────┬────────────────────┘
         │
         ▼
┌─────────────────────────────┐
│  CSharpEmitter              │
│  EmitStructDeclaration()    │  MODIFIED: emit flattened ancestor fields
│  EmitImplicitConversions()  │  NEW: synthesize implicit operators
│  EmitStructConstructor()    │  MODIFIED: inline base field assignments
└─────────────────────────────┘
```

---

## Detailed Design

### 1. StructType Model Change

**File:** `Source/Mist/Semantics/Model/Types/StructType.cs`

Add a `BaseType` property mirroring `ClassType.BaseType`:

```csharp
/// <summary>
/// The base struct type, if this struct extends another struct.
/// Null for structs with no base struct.
/// </summary>
public IType? BaseType { get; internal set; }
```

Add a helper to collect the full ancestor chain:

```csharp
/// <summary>
/// Returns all ancestor struct types in order from immediate parent to root.
/// </summary>
public IReadOnlyList<StructType> GetAncestorChain()
{
    var ancestors = new List<StructType>();
    var current = BaseType as StructType;
    while (current is not null)
    {
        ancestors.Add(current);
        current = current.BaseType as StructType;
    }
    return ancestors;
}
```

_Requirements: 2.1, 2.5_

---

### 2. Semantic Model Builder — Base Type Resolution

**File:** `Source/Mist/Semantics/Model/SemanticModelBuilder.cs`

#### 2a. New Pass: `Pass1_ResolveStructBaseTypes`

Insert after `Pass1_ResolveBaseTypes` (which handles classes). Mirrors the class logic:

```csharp
private static void Pass1_ResolveStructBaseTypes(
    SymbolTable symbolTable,
    ImmutableArray<Storm.Runtime.SyntaxNode> asts,
    MistCompilation compilation,
    DiagnosticBag diagnostics)
{
    foreach (var symbol in symbolTable.AllSymbols)
    {
        if (symbol.Kind != SymbolKind.Struct || symbol.DeclaredType is not StructType structType)
            continue;

        var declaringNode = symbolTable.GetDeclaringNode(symbol);
        if (declaringNode is not StructDeclarationNode structDecl)
            continue;

        if (structDecl.Interfaces is null)
            continue;

        var entries = ExtractBaseListEntries(structDecl.Interfaces);
        if (entries.Count == 0)
            continue;

        var firstEntry = entries[0];
        var firstName = firstEntry.Name.Lexeme;
        var parentScope = symbol.ContainingScope;
        var resolvedSymbol = parentScope.Lookup(firstName);

        if (resolvedSymbol?.DeclaredType is StructType resolvedStructType)
        {
            // First entry is a struct → record as base type
            var typeArgs = firstEntry.TypeArgs;
            structType.BaseType = ResolveGenericTypeReference(
                resolvedStructType, typeArgs, firstName,
                structType, structDecl, asts, compilation, diagnostics);
        }
        // If it's not a struct, leave it for ResolveStructInterfaceList to handle as interface
    }

    // Cycle detection pass
    DetectStructInheritanceCycles(symbolTable, diagnostics);
}
```

_Requirements: 1.2, 2.1, 2.2, 2.3_

#### 2b. Cycle Detection

```csharp
private static void DetectStructInheritanceCycles(
    SymbolTable symbolTable,
    DiagnosticBag diagnostics)
{
    const int MaxDepth = 64;

    foreach (var symbol in symbolTable.AllSymbols)
    {
        if (symbol.Kind != SymbolKind.Struct || symbol.DeclaredType is not StructType structType)
            continue;
        if (structType.BaseType is null)
            continue;

        var visited = new HashSet<string> { structType.DisplayName };
        var current = structType.BaseType as StructType;
        var depth = 0;

        while (current is not null)
        {
            depth++;
            if (depth > MaxDepth)
            {
                // Emit max depth exceeded diagnostic
                structType.BaseType = null;
                break;
            }
            if (!visited.Add(current.DisplayName))
            {
                // Cycle detected
                diagnostics.Add(/* MIST_STRUCT_CIRCULAR_INHERITANCE */);
                structType.BaseType = null;
                break;
            }
            current = current.BaseType as StructType;
        }
    }
}
```

_Requirements: 2.4, 2.5, 2.6_

#### 2c. Modify `ResolveStructInterfaceList`

Skip the first entry if `structType.BaseType` is already set (consumed as base struct):

```csharp
private static void ResolveStructInterfaceList(...)
{
    // ... existing preamble ...

    var entries = ExtractBaseListEntries(structDecl.Interfaces);
    if (entries.Count == 0) return;

    // If Pass1_ResolveStructBaseTypes consumed the first entry as a base struct, skip it.
    var startIndex = structType.BaseType is not null ? 1 : 0;

    // Check for multiple struct types in the list
    for (var i = startIndex; i < entries.Count; i++)
    {
        var name = entries[i].Name.Lexeme;
        var resolved = parentScope.Lookup(name);
        if (resolved?.DeclaredType is StructType)
        {
            // Emit MIST_STRUCT_MULTIPLE_INHERITANCE error
            continue;
        }
    }

    // Resolve remaining entries as interfaces (existing logic)
    var interfaceEntries = entries.Skip(startIndex);
    // ... existing ResolveInterfaceEntries call ...
}
```

_Requirements: 1.3, 1.5_

#### 2d. Populate MemberScope with Inherited Members

After base type resolution, inject inherited public members into the derived struct's scope:

```csharp
private static void PopulateInheritedMembers(StructType structType)
{
    if (structType.BaseType is not StructType baseStruct)
        return;

    // Walk the ancestor chain and add public members to the derived scope
    var current = baseStruct;
    while (current is not null)
    {
        foreach (var member in current.MemberScope.GetAllMembers())
        {
            if (member.IsPublic && !structType.MemberScope.Contains(member.Name))
            {
                structType.MemberScope.AddInherited(member);
            }
        }
        current = current.BaseType as StructType;
    }
}
```

_Requirements: 2.7, 3.2, 3.4, 3.6_

#### 2e. Field Hiding Detection

During Pass 2 field resolution, check for name collisions with ancestor fields:

```csharp
// In ResolveFieldDeclaration for structs:
if (structType.BaseType is StructType baseForHiding)
{
    var ancestorField = FindFieldInAncestors(baseForHiding, fieldName);
    if (ancestorField is not null)
    {
        diagnostics.Add(/* MIST_STRUCT_FIELD_HIDING */);
    }
}
```

_Requirements: 3.3_

---

### 3. Type Checker — Assignability

**File:** `Source/Mist/Analysis/Builtin/TypeCheckerAnalyzer.cs`

#### 3a. Struct Subtype Assignability in `IsAssignable`

Add after the existing class subtype check (around line 12643):

```csharp
// Struct subtype assignability — walk the inheritance chain.
// A derived struct is assignable to any of its ancestor structs.
if (source is StructType sourceStruct && target is StructType)
{
    var current = sourceStruct.BaseType as StructType;
    while (current is not null)
    {
        if (ReferenceEquals(current, target) || current.DisplayName == target.DisplayName)
            return true;
        current = current.BaseType as StructType;
    }
}
```

For `ConstructedType` wrapping a `StructType`, unwrap and check the underlying definition's ancestor chain with matching type arguments.

_Requirements: 4.1, 4.2, 4.3, 4.4, 4.5, 4.6, 5.1, 5.2, 5.3, 5.4_

#### 3b. Interface Implementability via Ancestors

Modify `ImplementsInterface` to walk the struct base chain:

```csharp
case StructType structType:
    if (InterfaceListContains(structType.Interfaces, targetInterface))
        return true;
    // NEW: walk struct base chain
    var currentStruct = structType.BaseType as StructType;
    while (currentStruct is not null)
    {
        if (InterfaceListContains(currentStruct.Interfaces, targetInterface))
            return true;
        currentStruct = currentStruct.BaseType as StructType;
    }
    return false;
```

_Requirements: 3.6 (interface access through ancestors)_

#### 3c. Constructor Validation

In the constructor validation pass, when a derived struct constructor has a `: base(...)` chain:
- Resolve the base struct's constructors
- Verify argument count and types match
- If no `: base(...)` and base has no parameterless constructor, emit error

_Requirements: 7.2, 7.3, 7.4_

---

### 4. C# Emitter — Field Flattening and Conversion Operators

**File:** `Source/Mist/Backends/CSharpTranspiler/CSharpEmitter.cs`

#### 4a. Emit Flattened Fields

In `EmitStructDeclaration`, before emitting the struct's own members, emit all ancestor fields:

```csharp
// Emit flattened ancestor fields (root-first order)
if (symbol?.DeclaredType is StructType derivedStruct && derivedStruct.BaseType is not null)
{
    var ancestors = derivedStruct.GetAncestorChain();
    ancestors.Reverse(); // root-first

    foreach (var ancestor in ancestors)
    {
        foreach (var field in ancestor.MemberScope.GetFields())
        {
            EmitFieldDeclaration(field); // preserves name, type, access modifier
        }
    }
}
```

_Requirements: 6.1, 6.5_

#### 4b. Emit Implicit Conversion Operators

After emitting members, emit conversion operators for each ancestor:

```csharp
private void EmitStructImplicitConversions(StructType derivedStruct, string derivedName)
{
    var current = derivedStruct.BaseType as StructType;
    while (current is not null)
    {
        var baseName = CSharpTypeMap.GetTypeName(current);
        AppendLine($"public static implicit operator {baseName}({derivedName} value)");
        AppendLine("{");
        _indentLevel++;
        AppendLine($"return new {baseName}");
        AppendLine("{");
        _indentLevel++;
        foreach (var field in GetAllFieldsForType(current))
        {
            AppendLine($"{field.Name} = value.{field.Name},");
        }
        _indentLevel--;
        AppendLine("};");
        _indentLevel--;
        AppendLine("}");

        current = current.BaseType as StructType;
    }
}
```

_Requirements: 6.2, 6.3, 6.4_

#### 4c. Emit Constructor with Inlined Base Assignments

When a derived struct constructor has `: base(args)`, emit the base constructor's field assignments inline at the start of the constructor body:

```csharp
// Instead of emitting `: base(args)` (invalid for C# structs),
// inline the base constructor's logic as field assignments.
if (hasBaseChain && baseConstructor is not null)
{
    // Map base constructor parameters to field assignments
    foreach (var (param, field) in baseConstructor.ParameterFieldMappings)
    {
        AppendLine($"this.{field.Name} = {EmitExpression(arg)};");
    }
}
```

_Requirements: 7.5_

#### 4d. No C# Inheritance Clause

The emitter already does not emit `: BaseStruct` for structs. The interface list emission continues to work — it only emits interfaces (and trait interfaces), not the base struct.

_Requirements: 6.4_

---

### 5. Pretty Printer

**File:** `Source/Mist/Parsing/MistPrettyPrinter.cs`

The pretty printer already handles `StructDeclaration` with an `interfaceList` using `EmitBaseList`. Since the grammar doesn't change (the base struct is just the first entry in the same `BaseList`), the pretty printer requires no modification — it already emits `: Type1, Type2, ...` preserving order.

Verify round-trip with a property test.

_Requirements: 8.1, 8.2, 8.3, 8.4, 8.5_

---

### 6. Diagnostic IDs

New diagnostic IDs to add to `SemanticModelBuilder`:

| ID | Severity | Condition |
|----|----------|-----------|
| `MIST_STRUCT_CIRCULAR_INHERITANCE (historical)` | Error | Cycle detected in struct inheritance chain |
| `MIST_STRUCT_MAX_DEPTH (historical)` | Error | Inheritance chain exceeds 64 levels |
| `MIST_STRUCT_MULTIPLE_INHERITANCE (historical)` | Error | More than one struct in base list |
| `MIST_STRUCT_FIELD_HIDING (historical)` | Error | Derived field name collides with ancestor field |
| `MIST_STRUCT_INVALID_BASE (historical)` | Error | Base type is not a struct (class/interface used as base) |
| `MIST0147` | Error | Base struct requires constructor args but none provided |
| `MIST_STRUCT_BASE_CTOR_MISMATCH (historical)` | Error | Arguments don't match any base constructor |

_Requirements: 1.3, 2.2, 2.3, 2.4, 2.6, 3.3, 7.3, 7.4_

---

### 7. Constraint System Update

**File:** `Source/Mist/Semantics/Model/SemanticModelBuilder.cs`

Remove or relax `MIST_CONSTRAINT_0005 (historical)` ("Struct cannot be used as a base class constraint"). With struct inheritance, a struct used as a constraint means "T must be this struct or a struct that extends it." The existing constraint validation needs updating:

- If the constraint type is a `StructType` and the constrained parameter is also constrained with `struct`, allow it as a base-struct constraint (analogous to base-class constraints on class-constrained parameters).
- If the constraint type is a `StructType` but the parameter has a `class` constraint, emit the existing incompatibility error.

_Requirements: 4.4 (transitive assignability applies to constrained contexts)_

---

### 8. Private Member Visibility

The `PopulateInheritedMembers` step only injects **public** members into the derived scope. Private members remain inaccessible — the type checker's existing "undefined member" diagnostic handles attempts to access them. No additional logic needed.

_Requirements: 3.5_

---

### 9. TextMate Grammar

**File:** `Editor/mist-syntax-highlighting/syntaxes/mist.tmLanguage.json`

The existing struct declaration pattern already handles `: InterfaceName` highlighting. Since the grammar doesn't change (base struct uses the same syntax position), the TextMate grammar should already highlight the base type name correctly. Verify that the pattern captures type names after `:` with the appropriate scope.

_Requirements: 11.1, 11.2, 11.3_

---

### 10. Sample Application

**File:** `samples/StructInheritance/Program.mist`

Demonstrates:
- Single-level inheritance (`Point3D : Point`)
- Multi-level inheritance (`ColorPoint3D : Point3D : Point`)
- Base constructor chaining (`: base(x, y)`)
- Derived-to-base implicit conversion (assignment, parameter, return)
- Field access on derived instances
- Method access on derived instances
- Generic struct inheritance (`TypedPoint<T> : Point`)

**File:** `samples/StructInheritance/mist.toml`
```toml
name = "StructInheritance"
```

**File:** `samples/AllFeatures/Program.mist` — add `StructInheritance.Program.RunDemo()` call.

_Requirements: 9.1, 9.2, 9.3, 9.4, 9.5_

---

### 11. Book Documentation

**File:** `book/src/object-model/structs.md`

Update the comparison table:
- Change "No inheritance" → "Single inheritance (field composition, no virtual dispatch)"
- Add a section explaining struct inheritance with a Tab_Block showing Mist source and flattened C# output
- Explain that both class and struct inheritance use static dispatch
- Note the behavioral difference: class inheritance = runtime subtype (reference), struct inheritance = compile-time composition (value copy)

_Requirements: 10.1, 10.2, 10.3, 10.4_

---

## Key Design Decisions

1. **No grammar change** — The existing `interfaceList` slot on `StructDeclaration` already accepts a `BaseList`. The semantic model builder distinguishes struct-typed entries from interface-typed entries during resolution.

2. **Mirrors class pattern** — The implementation follows the same Pass1_ResolveBaseTypes → Pass1_ResolveInterfaceLists → IsAssignable chain walk pattern used for classes, minimizing new code paths.

3. **Field flattening at emit time** — The Mist semantic model maintains the inheritance relationship (for type checking and member resolution), but the emitted C# has no inheritance. Fields are copied and conversion operators are synthesized.

4. **Implicit conversion direction** — Derived→Base is implicit (safe subset copy). Base→Derived is rejected (missing fields). This matches the numeric widening analogy discussed in requirements.

5. **Max depth 64** — Prevents infinite loops from undetected cycles or pathological chains. Matches a reasonable practical limit.

6. **Private members stay private** — Only public members are inherited into the derived scope. Private members exist in the flattened C# output but are not accessible from Mist source in the derived struct.

---

## Components and Interfaces

### Modified Components

| Component | File | Change |
|-----------|------|--------|
| `StructType` | `Source/Mist/Semantics/Model/Types/StructType.cs` | Add `BaseType` property and `GetAncestorChain()` helper |
| `SemanticModelBuilder` | `Source/Mist/Semantics/Model/SemanticModelBuilder.cs` | Add `Pass1_ResolveStructBaseTypes`, modify `ResolveStructInterfaceList`, add cycle detection, add field hiding check, add inherited member population |
| `TypeCheckerAnalyzer` | `Source/Mist/Analysis/Builtin/TypeCheckerAnalyzer.cs` | Add struct subtype walk in `IsAssignable`, extend `ImplementsInterface` for struct ancestors, add constructor chain validation |
| `CSharpEmitter` | `Source/Mist/Backends/CSharpTranspiler/CSharpEmitter.cs` | Emit flattened ancestor fields, synthesize implicit conversion operators, inline base constructor assignments |
| `MistPrettyPrinter` | `Source/Mist/Parsing/MistPrettyPrinter.cs` | No change needed (already handles BaseList on structs) |
| `ConstraintResolution` | `Source/Mist/Semantics/Model/SemanticModelBuilder.cs` | Relax `MIST_CONSTRAINT_0005 (historical)` to allow struct base-type constraints |

### Interfaces

No new public interfaces are introduced. The existing `IType` interface is unchanged — `StructType` gains an internal-set property following the same pattern as `ClassType.BaseType`.

The `IScope` interface may need an `AddInherited(ISymbol)` method if it doesn't already support injecting members from external sources. If the scope implementation uses a dictionary, inherited members are added with a flag distinguishing them from declared members.

---

## Data Models

### StructType (updated)

```
StructType
├── DisplayName: string
├── Kind: TypeKind.Struct
├── MemberScope: IScope (includes inherited public members)
├── ContainingNamespace: string?
├── BaseType: IType?              ← NEW
├── Interfaces: IReadOnlyList<IType>
├── TypeParameters: IReadOnlyList<IType>
├── IsBackendBacked: bool
├── IsReadonly: bool
├── IsOwnedType: bool
├── Defaultability: DefaultabilityStatus
├── DefaultValues: IReadOnlyDictionary<string, DefaultFieldValue>?
├── SatisfiedTraits: List<IType>
└── GetAncestorChain(): IReadOnlyList<StructType>  ← NEW
```

### Emitted C# Structure (for `struct Point3D : Point`)

```
// Mist source:
// struct Point { int X; int Y; Point(int x, int y) { X = x; Y = y; } }
// struct Point3D : Point { int Z; Point3D(int x, int y, int z) : base(x, y) { Z = z; } }

// Emitted C#:
public struct Point
{
    public int X;
    public int Y;
    public Point(int x, int y) { X = x; Y = y; }
}

public struct Point3D
{
    public int X;  // flattened from Point
    public int Y;  // flattened from Point
    public int Z;  // own field

    public Point3D(int x, int y, int z)
    {
        X = x;  // inlined from base constructor
        Y = y;  // inlined from base constructor
        Z = z;
    }

    public static implicit operator Point(Point3D value)
    {
        return new Point { X = value.X, Y = value.Y };
    }
}
```

### Diagnostic Model

| ID | Severity | Message Template |
|----|----------|-----------------|
| `MIST_STRUCT_CIRCULAR_INHERITANCE (historical)` | Error | `Struct '{name}' has a circular inheritance chain.` |
| `MIST_STRUCT_MAX_DEPTH (historical)` | Error | `Struct '{name}' exceeds the maximum inheritance depth of 64.` |
| `MIST_STRUCT_MULTIPLE_INHERITANCE (historical)` | Error | `Struct '{name}' cannot inherit from multiple structs. Only single struct inheritance is supported.` |
| `MIST_STRUCT_FIELD_HIDING (historical)` | Error | `Field '{field}' in struct '{derived}' hides field '{field}' inherited from '{ancestor}'.` |
| `MIST_STRUCT_INVALID_BASE (historical)` | Error | `Struct '{name}' cannot inherit from '{base}' because it is not a struct.` |
| `MIST0147` | Error | `Struct '{base}' requires constructor arguments. Add ': base(...)' to the constructor of '{derived}'.` |
| `MIST_STRUCT_BASE_CTOR_MISMATCH (historical)` | Error | `No constructor on struct '{base}' accepts the provided arguments.` |

---

## Correctness Properties

### Property 1: Ancestor Chain Acyclicity

The cycle detection pass guarantees that `GetAncestorChain()` terminates for all valid programs.

**Validates: Requirements 2.4, 2.5, 2.6**

### Property 2: Field Flattening Completeness

Every field in every ancestor appears exactly once in the emitted C# struct, in root-first order.

**Validates: Requirements 6.1, 6.5**

### Property 3: Implicit Conversion Value Preservation

For every field `F` in base struct `B`, if `derived.F == v` before conversion, then `((B)derived).F == v` after conversion.

**Validates: Requirements 6.2, 6.3**

### Property 4: Assignability Asymmetry

`IsAssignable(Derived, Base)` returns true; `IsAssignable(Base, Derived)` returns false. No code path allows base→derived without an explicit construction.

**Validates: Requirements 4.1, 4.4, 5.1, 5.4**

### Property 5: No Runtime Inheritance

The emitted C# never contains `: BaseStruct` on a struct declaration. All inheritance is resolved at compile time.

**Validates: Requirements 6.4**

### Property 6: Member Scope Consistency

After `PopulateInheritedMembers`, the derived struct's `MemberScope` contains all public members from all ancestors, and field name uniqueness is enforced.

**Validates: Requirements 2.7, 3.2, 3.3**

---

## Error Handling

| Scenario | Handling |
|----------|----------|
| Base type not found | Emit `MIST_STRUCT_INVALID_BASE (historical)`, set `BaseType = null`, continue compilation |
| Base type is a class | Emit `MIST_STRUCT_INVALID_BASE (historical)`, set `BaseType = null`, continue |
| Circular inheritance | Emit `MIST_STRUCT_CIRCULAR_INHERITANCE (historical)`, set `BaseType = null` on the offending struct, break the cycle |
| Depth > 64 | Emit `MIST_STRUCT_MAX_DEPTH (historical)`, set `BaseType = null`, stop chain walk |
| Multiple structs in base list | Emit `MIST_STRUCT_MULTIPLE_INHERITANCE (historical)`, use only the first struct as base |
| Field name collision | Emit `MIST_STRUCT_FIELD_HIDING (historical)`, skip adding the duplicate to scope |
| Missing base constructor args | Emit `MIST0147`, emit constructor without base assignments |
| Wrong base constructor args | Emit `MIST_STRUCT_BASE_CTOR_MISMATCH (historical)`, emit constructor without base assignments |

All error paths set the model to a consistent state (null BaseType or skipped members) so downstream passes don't crash on incomplete data.

---

## Testing Strategy

### Unit Tests

1. **SemanticModelBuilder tests** — Verify `StructType.BaseType` is populated correctly for single-level, multi-level, and generic inheritance. Verify all diagnostic IDs fire for error cases (cycles, invalid base, field hiding, multiple inheritance).

2. **TypeCheckerAnalyzer tests** — Verify `IsAssignable` returns true for derived→base (direct and transitive) and false for base→derived. Verify interface implementability walks the struct ancestor chain.

3. **CSharpEmitter tests** — Snapshot tests comparing emitted C# output for structs with inheritance against expected output (flattened fields, implicit operators, inlined constructors).

4. **Pretty printer round-trip** — Property test: generate random struct declarations with inheritance, pretty-print, re-parse, pretty-print again, assert identical output.

### Integration Tests

5. **Sample application** — `samples/StructInheritance/Program.mist` compiles and runs, producing expected labeled output. Verified by the AllFeatures orchestrator.

6. **Negative tests** — Programs that should fail to compile (base→derived assignment, circular inheritance, field hiding) produce the expected diagnostics and no emitted output.

### Existing Test Regression

7. **Constraint tests** — Update `ConstraintResolutionTests.StructAsBaseClass_EmitsMIST_CONSTRAINT_0005` to reflect the relaxed behavior (struct-as-constraint is now valid when the parameter has a `struct` constraint).
