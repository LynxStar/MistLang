# Design Document: Dependent Type Refinement Constraints

## Overview

This feature extends the Mist compiler in four coordinated areas:

1. **Grammar extension** — The `TypeParameter` rule is extended to accept identifiers (not just primitive keywords) as dependent type constraints, enabling `Length:ZeroOrPositiveInt` to parse as a `DependentTypeParameter` node.

2. **Semantic resolution** — The `SemanticModelBuilder` gains the ability to resolve identifier constraints by looking up the referenced type alias, validating it is a refinement type with an allowed primitive base type, and extracting that base type for the `DependentTypeParameterType`.

3. **Parser completeness checking** — `MistParser.cs` detects when the Storm-generated parser fails to consume all tokens, emitting `MIST_PARSE_INCOMPLETE` to surface silent parse failures immediately.

4. **Workaround removal** — With stdlib files parsing fully, the synthetic `EnsureSpanAliasesRegistered` workaround, the `SpanType` constructor special case, and the `FindIEnumerableImplementation` fast-paths for Array/Span are removed.

The net result: `Array.mist` and `Span.mist` parse completely, their types are registered from the parsed AST, and the dedicated `ArrayType`/`SpanType` IType representations continue to handle use-site resolution, bounds checking, and type substitution as before.

## Architecture

The changes span four compiler pipeline stages:

```
Grammar/Storm → MistParser.cs → SemanticModelBuilder → TypeCheckerAnalyzer
     │                │                  │                      │
     │                │                  │                      │
  TypeParameter    Completeness      Resolve identifier      Remove SpanType ctor
  rule accepts     check: _pos <     constraint to           and foreach fast-paths
  IDENTIFIER       tokens.Length     refinement type base
  constraint       → diagnostic
```

**Key architectural invariant:** The dedicated `ArrayType` and `SpanType` IType representations remain untouched. What changes is how these types are *registered* (from parsed declarations instead of synthetic workarounds) and how *specific behaviors* (constructor validation, foreach element-type resolution) are wired — from hardcoded special-case code to standard member-scope/interface lookup.

## Components and Interfaces

### 1. Grammar Change (`Mist.storm`)

**Current rule (lines 148–152):**

```storm
rule TypeParameter
    = name:IDENTIFIER COLON valueType:PrimitiveType  as DependentTypeParameter
    | name:IDENTIFIER                                 as RegularTypeParameter
    ;
```

**New rule:**

```storm
rule TypeParameter
    = name:IDENTIFIER COLON valueType:PrimitiveType           as DependentTypeParameter
    | name:IDENTIFIER COLON constraintName:IDENTIFIER         as NamedConstraintDependentTypeParameter
    | name:IDENTIFIER                                          as RegularTypeParameter
    ;
```

**Design decisions:**

- **New alternative name (`NamedConstraintDependentTypeParameter`):** A distinct node type is introduced rather than overloading `DependentTypeParameter`. The `NamedConstraintDependentTypeParameterTypeParameterNode` stores the constraint as an `IDENTIFIER` token (field: `ConstraintName`) rather than a `PrimitiveType` node (field: `ValueType`). This preserves type safety — downstream code matching on `DependentTypeParameterTypeParameterNode` continues unchanged with its guaranteed `ValueType: PrimitiveTypeNode` field.

- **Alternative ordering (PEG disambiguation):** The parser tries alternatives in declaration order. `PrimitiveType` matches reserved keywords (`int`, `bool`, `uint`, etc.) that lex to distinct token kinds — never `IDENTIFIER`. So `Name:int` matches alt 1 (keyword after colon), `Name:ZeroOrPositiveInt` matches alt 2 (IDENTIFIER after colon), and `Name` alone matches alt 3. No ambiguity exists because primitive keywords and identifiers are distinct tokens in the lexer.

- **Reuse of `DependentTypeParameter` was rejected:** Overloading a single node to hold either `PrimitiveTypeNode` or `Token` would require nullable fields, violating the Storm AST's fully-typed node contract.

**Impact on `TypeParameterNodeExtensions.cs`:** The `GetName()` extension method must add a case for `NamedConstraintDependentTypeParameterTypeParameterNode`:

```csharp
public static Token GetName(this TypeParameterNode node)
{
    return node switch
    {
        DependentTypeParameterTypeParameterNode dep => dep.Name,
        NamedConstraintDependentTypeParameterTypeParameterNode named => named.Name,
        RegularTypeParameterTypeParameterNode reg => reg.Name,
        _ => throw new InvalidOperationException(...)
    };
}
```

**Impact on `MistPrettyPrinter.cs`:** The pretty-printer must handle the new node:

```csharp
NamedConstraintDependentTypeParameterTypeParameterNode named =>
    $"{named.Name.Lexeme}:{named.ConstraintName.Lexeme}",
```

### 2. Parser Completeness Check (`MistParser.cs`)

After `ParseCompilationUnit` returns successfully (non-null root), we check whether the parser consumed all default-channel tokens.

**Exposing parser position:** The Storm source generator emits `_pos` as a `[ThreadStatic] private static int` inside `Mist.Generated.MistParser`. To expose it to `MistParser.cs`, the source generator (`ParserEmitter.cs`, line ~74) will emit an additional public static property:

```csharp
public static int LastPosition => _pos;
```

This is the minimal-surface change — a single read-only property added to the generated parser class.

**Completeness check implementation in `MistParser.Parse()`:**

```csharp
// After successful parse, check completeness.
if (root is not null)
{
    var lastPos = Mist.Generated.MistParser.LastPosition;
    var defaultTokens = stormTokenStream.DefaultChannelTokens;
    
    if (lastPos < defaultTokens.Length)
    {
        var unconsumedToken = defaultTokens[lastPos];
        var location = new LinePositionSpan(
            new LinePosition(unconsumedToken.Line, unconsumedToken.Column),
            new LinePosition(unconsumedToken.Line, unconsumedToken.Column));
        
        diagnostics.Add(new Diagnostic(
            new DiagnosticId("MIST_PARSE_INCOMPLETE"),
            DiagnosticSeverity.Error,
            PipelineStage.Parsing,
            $"Parser did not consume all input. Unparseable content begins at line {unconsumedToken.Line}.",
            Location: location,
            CompilationUnitPath: tokens.Source.Path,
            AnalyzerName: null));
    }
}
```

**Diagnostic specification:**

| Field | Value |
|-------|-------|
| ID | `MIST_PARSE_INCOMPLETE` |
| Severity | Error |
| Stage | Parsing |
| Message | "Parser did not consume all input. Unparseable content begins at line {line}." |
| Location | Line/column of the first unconsumed token |
| File path | The source file being parsed |

### 3. Semantic Resolution of Identifier Constraints (`SemanticModelBuilder`)

The `ExtractTypeParameters` method (around line 3998) currently handles only `DependentTypeParameterTypeParameterNode`. A new branch is added for `NamedConstraintDependentTypeParameterTypeParameterNode`:

```csharp
else if (node is NamedConstraintDependentTypeParameterTypeParameterNode namedNode)
{
    var constraintName = namedNode.ConstraintName.Lexeme;
    
    // Step 1: Look up the constraint name in the current resolution scope.
    var constraintSymbol = LookupTypeByName(constraintName, currentScope);
    
    if (constraintSymbol == null)
    {
        // Constraint type not found — emit diagnostic.
        diagnostics.Add(MakeDiagnosticWithLocation(
            /* new diagnostic ID */ ...,
            DiagnosticSeverity.Error,
            $"Constraint type '{constraintName}' could not be found.",
            namedNode, sourceText));
        typeParams.Add(new DependentTypeParameterType(name, ordinal, ErrorType.Instance));
        return;
    }
    
    // Step 2: Verify the symbol resolves to a RefinementType.
    IType baseType;
    if (constraintSymbol.DeclaredType is RefinementType refinement)
    {
        baseType = refinement.BaseType;
    }
    else if (constraintSymbol.DeclaredType is TypeAliasType alias 
             && alias.UnderlyingType is RefinementType aliasRefinement)
    {
        baseType = aliasRefinement.BaseType;
    }
    else
    {
        diagnostics.Add(MakeDiagnosticWithLocation(
            /* diagnostic */ ...,
            DiagnosticSeverity.Error,
            $"Constraint '{constraintName}' must be a refinement type with a primitive base type.",
            namedNode, sourceText));
        typeParams.Add(new DependentTypeParameterType(name, ordinal, ErrorType.Instance));
        return;
    }
    
    // Step 3: Validate the base type is in Allowed_Dependent_Primitives.
    var baseKeyword = GetPrimitiveKeyword(baseType);
    if (!AllowedDependentTypeAnnotations.Contains(baseKeyword))
    {
        // Emit MIST_DT_0001 — base type not in allowed set.
        diagnostics.Add(MakeDiagnosticWithLocation(
            DependentTypeDisallowedAnnotation.Value,
            DiagnosticSeverity.Error,
            $"Type '{baseKeyword}' (base type of '{constraintName}') cannot be used as a dependent type parameter. Only primitive integral types, bool, and char are allowed.",
            namedNode, sourceText));
        typeParams.Add(new DependentTypeParameterType(name, ordinal, ErrorType.Instance));
        return;
    }
    
    // Step 4: Create DependentTypeParameterType with the resolved base type.
    var depType = new DependentTypeParameterType(name, ordinal, baseType);
    // Store the refinement constraint for potential future validation use.
    depType.RefinementConstraint = constraintSymbol.DeclaredType as RefinementType;
    typeParams.Add(depType);
}
```

**Scope resolution timing:** This resolution happens during type parameter registration (Pass 1). For the stdlib case, `ZeroOrPositiveInt` is declared as a type alias *before* the `Array` class declaration in `Array.mist`. Since type aliases are registered in a top-to-bottom pass over the AST within the same file, the alias is available when the class's type parameters are processed. For `Span.mist`, `ZeroOrPositiveInt` is declared in `Array.mist` which is processed first (files are sorted alphabetically: `Array.mist` < `Span.mist`).

**What `GetPrimitiveKeyword` does:** Maps an IType singleton (e.g., `Int32Type.Instance`) back to its keyword string (`"int"`). This already exists implicitly in the codebase through `FormatPrimitiveType` — we either reuse that logic or add a reverse mapping.

### 4. Constructor Registration for Intrinsic Types

**Current state:** `Span.mist` does NOT declare a constructor. The type checker's `if (createdType is SpanType)` branch (lines 13785–13827) hardcodes the `new Span<T>(array)` validation logic.

**Change required in `Span.mist`:** Add an intrinsic constructor declaration:

```mist
intrinsic public Span(Array<T, unknown> source);
```

And for `ReadOnlySpan`:

```mist
intrinsic public ReadOnlySpan(Array<T, unknown> source);
```

**Registration flow:** When `Span.mist` parses fully, the SemanticModelBuilder's member registration pass encounters the constructor declaration and registers it as a `SymbolKind.Constructor` in the struct's member scope. The constructor's parameter type is `Array<T, unknown>` — matching the existing hardcoded validation that requires an array argument.

**Type checker change:** Remove the `if (createdType is SpanType spanCtorType)` block entirely. Constructor resolution falls through to the standard `CollectConstructorOverloads(createdType)` path. This method looks up `memberScope` on the type:

```csharp
var memberScope = lookupType switch
{
    ClassType ct => ct.MemberScope,
    StructType st => st.MemberScope,
    ...
};
```

**Bridge for dedicated types:** `SpanType` is not a `StructType` — it's a dedicated IType. `CollectConstructorOverloads` needs to find constructors when `createdType` is `SpanType`. Two approaches:

1. **Preferred: Cross-reference lookup.** When `createdType` is `SpanType`, look up the corresponding registered `StructType` in the Standard namespace scope by name and arity. Pull constructors from its member scope. This keeps `SpanType` lean and doesn't duplicate member data.

2. **Alternative: Add `MemberScope` to SpanType.** Rejected — SpanType is a lightweight representation focused on element type and length; adding a full scope is architecturally inappropriate.

The cross-reference is implemented as an extension to `CollectConstructorOverloads`:

```csharp
if (lookupType is SpanType spanLookup)
{
    // Find the registered StructType for Span/ReadOnlySpan in Standard scope.
    var registeredStruct = FindRegisteredStructType(
        spanLookup.IsReadOnly ? "ReadOnlySpan" : "Span", "Standard");
    memberScope = registeredStruct?.MemberScope;
}
```

### 5. Foreach Fast-Path Removal (`FindIEnumerableImplementation`)

**Current code:**

```csharp
private static IType? FindIEnumerableImplementation(IType type)
{
    if (type is ArrayType arrayType)
        return arrayType.ElementType;
    if (type is SpanType spanType)
        return spanType.ElementType;
    // ... standard interface lookup ...
}
```

**After removal:** The two fast-path branches are deleted. The method proceeds directly to `GetInterfaceList(type)` and the standard interface search.

**Bridge for dedicated types in `GetInterfaceList`:** `ArrayType` and `SpanType` don't have an `Interfaces` property today. The `GetInterfaceList` helper is extended:

```csharp
private static IReadOnlyList<IType> GetInterfaceList(IType type)
{
    // Bridge: for dedicated intrinsic types, look up the registered declaration's interfaces.
    if (type is ArrayType)
    {
        var registeredClass = FindRegisteredClassType("Array", "Standard");
        if (registeredClass != null) return registeredClass.Interfaces;
    }
    if (type is SpanType spanType)
    {
        var name = spanType.IsReadOnly ? "ReadOnlySpan" : "Span";
        var registeredStruct = FindRegisteredStructType(name, "Standard");
        if (registeredStruct != null) return registeredStruct.Interfaces;
    }
    
    return type switch
    {
        ClassType ct => ct.Interfaces,
        StructType st => st.Interfaces,
        InterfaceType it => it.Interfaces,
        _ => Array.Empty<IType>(),
    };
}
```

**Type argument substitution:** When `FindIEnumerableImplementation` finds `IEnumerable<TValue>` on the registered `ClassType` for Array, it needs to substitute `TValue` with the actual element type from the `ArrayType` instance. The existing substitution logic in the method handles this via `typeParams`/`typeArgs` mapping. For the bridge case, we construct the mapping from the dedicated type's element type:

- `ArrayType`: `typeParams = [TValue, Length]`, `typeArgs = [arrayType.ElementType, unknownLengthPlaceholder]`
- `SpanType`: `typeParams = [T, Length]`, `typeArgs = [spanType.ElementType, unknownLengthPlaceholder]`

### 6. Workaround Removal Strategy

The removal follows a strict ordering to maintain a compilable, test-passing state at each step:

| Step | Action | Prerequisite |
|------|--------|--------------|
| 1 | Grammar change + semantic resolution | None |
| 2 | Parser completeness check | Step 1 (stdlib files now parse) |
| 3 | Verify stdlib registration through parsed declarations | Steps 1–2 |
| 4 | Remove `EnsureSpanAliasesRegistered` | Step 3 (parsed declarations register aliases) |
| 5 | Add constructor declarations to `Span.mist`, remove SpanType constructor special case | Step 3 (constructors registered from parsed AST) |
| 6 | Remove Array/Span foreach fast-path | Step 3 (interfaces registered from parsed AST) |

**What remains unchanged after all removals:**

- `ArrayType` and `SpanType` dedicated IType representations
- `new int[n]` → `ArrayType` construction path
- `stackalloc T[n]` → `SpanType` construction path
- Slice length inference
- Bounds checking via `KnownLength`/`SymbolicLengthId`
- Type substitution through `SubstituteType`

### 7. `FindRegisteredClassType` / `FindRegisteredStructType` Helper

A new helper method is added to the TypeCheckerAnalyzer (or extracted to a shared location) that performs the cross-reference lookup from a dedicated type to its registered declaration:

```csharp
private static ClassType? FindRegisteredClassType(string name, string namespaceName, ISemanticModel model)
{
    // Search the model's registered types for a ClassType with matching name and namespace.
    foreach (var symbol in model.AllSymbols)
    {
        if (symbol.Name == name && symbol.Kind == SymbolKind.Class 
            && symbol.ContainingNamespace == namespaceName
            && symbol.DeclaredType is ClassType ct)
            return ct;
    }
    return null;
}
```

The same pattern applies for `FindRegisteredStructType`. These lookups are cached after first resolution since the stdlib types don't change during a compilation.

## Data Models

### New AST Node Type

```
NamedConstraintDependentTypeParameterTypeParameterNode : TypeParameterNode
├── Name: Token            (IDENTIFIER — the parameter name, e.g., "Length")
└── ConstraintName: Token  (IDENTIFIER — the refinement type name, e.g., "ZeroOrPositiveInt")
```

Generated by the Storm source generator from the new grammar alternative.

### Modified Types

| Type | Change |
|------|--------|
| `DependentTypeParameterType` | Add optional `RefinementConstraint: RefinementType?` property for storing the resolved refinement (used for future constraint propagation) |
| `Mist.Generated.MistParser` | Add `public static int LastPosition => _pos;` property via source generator |

### New Diagnostic

| ID | Severity | Stage | Message Template |
|----|----------|-------|------------------|
| `MIST_PARSE_INCOMPLETE` | Error | Parsing | "Parser did not consume all input. Unparseable content begins at line {line}." |

### Modified Stdlib Files

| File | Change |
|------|--------|
| `Span.mist` | Add `intrinsic public Span(Array<T, unknown> source);` constructor declaration |
| `Span.mist` | Add `intrinsic public ReadOnlySpan(Array<T, unknown> source);` constructor declaration |

### Removed Components

| Component | Location |
|-----------|----------|
| `EnsureSpanAliasesRegistered` method | `SemanticModelBuilder.cs` |
| `RegisterSyntheticSpanAlias` helper | `SemanticModelBuilder.cs` |
| `if (createdType is SpanType)` constructor block | `TypeCheckerAnalyzer.cs` (~line 13785) |
| `if (type is ArrayType)` in `FindIEnumerableImplementation` | `TypeCheckerAnalyzer.cs` (~line 6530) |
| `if (type is SpanType)` in `FindIEnumerableImplementation` | `TypeCheckerAnalyzer.cs` (~line 6533) |

## Error Handling

### Grammar Parse Failures

When the new `NamedConstraintDependentTypeParameter` alternative fails to match (e.g., token after colon is neither a primitive keyword nor an IDENTIFIER), the PEG parser backtracks normally to the `RegularTypeParameter` alternative. If that also fails, the enclosing rule fails and the standard Storm error-recovery mechanism produces a parse diagnostic.

### Incomplete Parse Detection

The `MIST_PARSE_INCOMPLETE` diagnostic is severity Error. If emitted for a stdlib file, the compilation continues (other files are still processed) but the incomplete file's declarations are not registered — any downstream "type not found" errors are a natural consequence and easier to diagnose with the incomplete-parse error visible.

### Semantic Resolution Failures

Three failure modes in identifier constraint resolution, each producing a distinct diagnostic:

1. **Constraint not found:** The identifier doesn't match any type in scope → error diagnostic naming the unresolved identifier.
2. **Not a refinement type:** The identifier resolves but isn't a `RefinementType` → error diagnostic explaining the constraint must be a refinement type.
3. **Disallowed base type:** The refinement's base type isn't in `AllowedDependentTypeAnnotations` → `MIST_DT_0001` (same diagnostic as for disallowed primitive keywords, extended message includes the constraint name).

In all failure cases, `ErrorType.Instance` is used as the value type so downstream type checking degrades gracefully.

### Workaround Removal Ordering

If any step in the removal chain fails (tests break), the ordering ensures the failure is isolated to that step. Each step is independently revertable without affecting subsequent steps.

## Testing Strategy

### Property-Based Tests

1. **TypeParameter round-trip** — Generate random valid type parameter nodes (all three variants), pretty-print, re-parse, assert structural equivalence. Validates grammar correctness without relying on specific examples.

2. **Identifier constraint resolution** — Generate scenarios with refinement types (varying base types from both allowed and disallowed sets), assert correct `DependentTypeParameterType.ValueType` or correct diagnostic emission.

3. **Parser completeness** — Generate source strings that are valid prefixes but have trailing unparseable content, assert `MIST_PARSE_INCOMPLETE` is emitted with correct position.

### Integration Tests

1. **Stdlib compilation** — Compile `Array.mist` and `Span.mist` through the full pipeline, assert zero diagnostics and verify type registration in the semantic model.

2. **Span constructor** — Compile `new Span<int>(arr)` and `new Span<int>(notAnArray)`, verify accept/reject matches previous behavior.

3. **Foreach over Array/Span** — Compile foreach loops over arrays and spans, verify element type resolution succeeds.

### Regression

All existing 4,257 tests must pass. The workaround removal steps are ordered so that at each step, the full test suite continues to pass.

## Correctness Properties

### Property 1: TypeParameter Round-Trip (Grammar)

**For all** valid type parameter forms (`Name:PrimitiveKeyword`, `Name:Identifier`, `Name`), parsing the pretty-printed form produces an equivalent AST node with the same parameter name and constraint.

**Validates: Requirements 1.1, 1.2, 1.3, 1.4, 9.1, 9.2, 9.3**

### Property 2: Identifier Constraint Resolution Correctness

**For all** `NamedConstraintDependentTypeParameter` nodes where the identifier resolves to a `RefinementType` whose `BaseType` is in `AllowedDependentTypeAnnotations`, the resulting `DependentTypeParameterType` has `ValueType` equal to that base type.

**Validates: Requirements 2.1**

### Property 3: Disallowed Base Type Rejection

**For all** `NamedConstraintDependentTypeParameter` nodes where the identifier resolves to a `RefinementType` whose `BaseType` is NOT in `AllowedDependentTypeAnnotations`, the SemanticModelBuilder emits `MIST_DT_0001`.

**Validates: Requirements 2.2**

### Property 4: Unresolved Constraint Rejection

**For all** `NamedConstraintDependentTypeParameter` nodes where the identifier does not resolve to any known type, the SemanticModelBuilder emits an error diagnostic.

**Validates: Requirements 2.3**

### Property 5: Parser Completeness Invariant

**For all** source files where the Storm parser returns a non-null root but does not consume all default-channel tokens, `MIST_PARSE_INCOMPLETE` is emitted with the correct line position of the first unconsumed token.

**Validates: Requirements 3.1, 3.3, 3.4**

### Property 6: Completeness Silence on Full Consumption

**For all** source files where the Storm parser returns a non-null root and consumes all default-channel tokens, no `MIST_PARSE_INCOMPLETE` diagnostic is emitted.

**Validates: Requirements 3.2**

### Property 7: Stdlib Full Parse

After the grammar extension, `Array.mist` and `Span.mist` parse to completion with zero `MIST_PARSE_INCOMPLETE` diagnostics, and all type declarations are registered in the Standard namespace scope.

**Validates: Requirements 4.1, 4.2, 4.3, 4.4**

### Property 8: Span Alias Registration from Parsed Declarations

After `Span.mist` parses fully, the arity-1 `Span<T>` and `ReadOnlySpan<T>` type aliases are registered from the parsed `type Span<T> = Span<T, unknown>;` declarations, not from synthetic code.

**Validates: Requirements 5.1, 5.2, 5.3, 5.4**

### Property 9: Constructor Resolution Equivalence

**For all** `new Span<T>(expr)` constructions, the constructor resolution via member-scope lookup produces the same accept/reject behavior and diagnostic messages as the previous hardcoded branch.

**Validates: Requirements 6.1, 6.2, 6.3, 6.4, 6.5**

### Property 10: Foreach Element-Type Resolution Equivalence

**For all** `foreach` iterations over `ArrayType` or `SpanType` values, the element type resolved through the standard `FindIEnumerableImplementation` interface lookup equals the type's `ElementType` property.

**Validates: Requirements 7.1, 7.2, 7.3, 7.4, 7.5**

### Property 11: Regression-Free

All 4,257 existing tests pass with no changes to expected behavior for primitive-keyword dependent type parameters.

**Validates: Requirements 8.1, 8.2, 8.3**

## Requirements Coverage Map

| Requirement | Design Section(s) |
|-------------|-------------------|
| Req 1: Extend TypeParameter Grammar | §1 Grammar Change |
| Req 2: Validate Refinement Type Constraints | §3 Semantic Resolution |
| Req 3: Parser Completeness Checking | §2 Parser Completeness Check |
| Req 4: Stdlib Files Parse Completely | §1 Grammar, §3 Semantic Resolution, §6 Registration Flow |
| Req 5: Remove Synthetic Span Alias Workaround | §6 Workaround Removal Strategy |
| Req 6: Remove SpanType Constructor Special Case | §4 Constructor Registration |
| Req 7: Remove Foreach Fast-Path | §5 Foreach Fast-Path Removal |
| Req 8: Existing Test Suite Passes | §6 Workaround Removal Strategy (ordering) |
| Req 9: Round-Trip Property | §1 Grammar Change (pretty-printer), Properties §1 |
| Req 10: Sample Application | Not covered in design (implementation task) |
| Req 11: Book Documentation | Not covered in design (documentation task) |
