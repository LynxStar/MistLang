# Design Document: Mist Standard Library

## Overview

Mist today has no standard library. Primitive types are compiler singletons in `Source/Mist/Semantics/Model/Types/PrimitiveType.cs` with no members. Operators are hardcoded in `Source/Mist/Analysis/Builtin/TypeCheckerAnalyzer.cs` (a `NumericTypes` HashSet at line 26 plus per-operator `Check*Node` methods at lines ~1411–1580), and the C# emitter passes operator lexemes straight through to .NET. Types like `String`, `Array`, `Console`, `Math`, `Convert` are **ambient types** per-backend (`Source/Mist/Backends/CSharpTranspiler/CSharpAmbientTypeResolver.cs`), not stdlib types.

This feature introduces a real Mist Standard Library under the `Standard` namespace, organized into two tiers:

- **Bootstrap tier** (numeric primitives, `Boolean`, `Char`, `String`, `Array<T>`): declared in Mist with a type-level `intrinsic` modifier. The backend provides storage layout and emission for their operators via a backend-private **Backing_Type_Map** and an **emission-template registry**.
- **Pure-Mist tier**: anything expressible in plain `.mist` source using only bootstrap types. Empty in v1 except for one demo type (see § *Pure-Mist Tier for v1*).

Four cornerstone design decisions shape the rest of the document:

1. **First-class user-definable operators.** `public static T operator +(T a, T b) { ... }` is grammar-accepted on any class or struct, not privileged to stdlib. The type checker's hardcoded `NumericTypes` HashSet is deleted; operator expressions go through `OverloadResolver.cs`.

2. **Primitive keyword aliases bypass scoping.** `int`, `string`, `bool`, etc. are reserved keywords (already in `Source/Mist/Grammar/keywords.storm`) that act as aliases for fully-qualified stdlib type names (`int → Standard.Int32`). Resolution of a primitive keyword is a direct lookup in the `Keyword_Alias_Mapping` and ignores all scoping — `using` directives, user-declared types with matching simple names, nothing can redirect it. Writing the unqualified capitalized form `Int32` remains an ordinary identifier and requires `using Standard;` to resolve.

3. **No implicit prelude.** `using Standard;` is not implicit. Existing programs that use `int`, `string`, `bool` compile unchanged because those keywords alias through the mapping, not through scope.

4. **Intrinsic model, dual-level.** The reserved keyword `intrinsic` is added to `keywords.storm` and appears in a declaration's modifier list:
   - **Type level:** `intrinsic public struct Int32 { ... }` — declares a Backend_Backed_Type whose storage is provided by the backend.
   - **Member level:** `intrinsic public static Int32 operator +(Int32 a, Int32 b);` — body omitted with `;`; backend provides the emission template.
   - Stdlib-only; rejected in user compilation units. Member-level `intrinsic` is only permitted inside a type that also carries type-level `intrinsic`.

Under this model, `Standard.String` is the single canonical string type in the Mist semantic model. The C# emitter's private Backing_Type_Map rewrites `Standard.String → string`, `Standard.Array<T> → T[]`, `Standard.Int32 → int`, etc., at the emission boundary. `StringType`, `ArrayType`, and `PrimitiveType.*` are removed (or reduced to a private implementation detail of the semantic model's migration window) so no target-language type leaks back up into the semantic model.

## Architecture

### High-level pipeline

```
          ┌─────────────────────────────────────────────────────────────┐
          │                      CompilerDriver                         │
          └─────────────────────────────────────────────────────────────┘
                                       │
                                       ▼
        ┌──────────────────┐    ┌──────────────────┐
        │  Stdlib source   │    │   User sources   │   ← options.SourcePaths
        │  Source/Standard │    │                  │
        └────────┬─────────┘    └────────┬─────────┘
                 │                       │
                 └───────────┬───────────┘
                             ▼
                  Lex  →  Parse  →  SemanticModelBuilder
                                         │
                                         │  (IAmbientTypeResolver
                                         │   for Console/Math/Convert;
                                         │   produces Mist-typed signatures)
                                         ▼
                                    ISemanticModel
                                         │
                                         ▼
                                  AnalyzerRunner
                                 (TypeCheckerAnalyzer:
                                  operator overload resolution
                                  via OverloadResolver.Resolve)
                                         │
                                         ▼
                             CSharpTranspilerBackend.Emit
                                         │
                                         ▼
                   CSharpEmitter  ←  Backing_Type_Map
                                  ←  EmissionTemplateRegistry
                                         │
                                         ▼
                                    Emitted .cs files
```

Stdlib source enters the pipeline the same way user source does; § *Stdlib Loading Strategy* picks the strategy.

### Mist types vs backing types — strict one-way wall

```
      Mist semantic model             │           C# backend (private)
                                      │
    Standard.Int32   (StructType)     │  ───►   "int"
    Standard.String  (ClassType)      │  ───►   "string"
    Standard.Array<T>(ClassType)      │  ───►   "{elem}[]"
    Standard.Boolean (StructType)     │  ───►   "bool"
          ...                         │
    user types (ClassType/StructType) │  ───►   user types emitted verbatim
                                      │
       ▲  semantic model, type        │   ▲  Backing_Type_Map consulted only
       │  checker, ambient resolver,  │   │  in CSharpTypeMap / CSharpEmitter;
       │  analyzers all see these     │   │  never exposed back up.
       │                              │
```

No arrow runs right-to-left. The Backing_Type_Map is a private dictionary inside `Source/Mist/Backends/CSharpTranspiler/`; no contract in `Source/Mist/Semantics/Contracts/` or `Source/Mist/Analysis/Contracts/` references it.

### How `intrinsic` members resolve at emission

For any call `a + b` where operator overload resolution selects an intrinsic operator, the emitter takes a different path than for ordinary method calls:

```
   AdditiveNode (typed "a + b")
         │
         │   SemanticModel.GetResolvedOperator(node)
         │       → ISymbol for Standard.Int32.operator+
         │
         ▼
   CSharpEmitter.EmitAdditive
         │
         │   symbol.IsIntrinsic == true
         │
         ▼
   EmissionTemplateRegistry.TryGetBinaryOperator(
       typeId = "Standard.Int32", op = "+")
         │
         │   returns template "{left} + {right}"
         │
         ▼
   "{left} + {right}"  →  emitted C# source
```

When overload resolution selects a non-intrinsic Mist-bodied operator (user-defined or stdlib pure-tier), the emitter falls through to the ordinary static-method-call path, emitting `Vector3.op_Addition(a, b)` or the equivalent. Only intrinsics short-circuit through the template registry.

## Components and Interfaces

### 1. Grammar changes (`Source/Mist/Grammar/Mist.storm`, `keywords.storm`)

**Add `INTRINSIC` keyword token** in `Source/Mist/Grammar/keywords.storm`, adjacent to the existing modifier keywords:

```storm
token INTRINSIC = "intrinsic";
```

Per Requirement 5.14, the keyword is defined in exactly one place. It is declared before `IDENTIFIER` in `Mist.storm` (which already imports `keywords.storm`) so keyword priority wins (same mechanism as `public`, `static`, etc.).

**Extend `Modifier` alternatives** (currently at line 140 of `Mist.storm`) to include `intrinsic`:

```storm
rule Modifier
    = PUBLIC     as PublicModifier
    | PRIVATE    as PrivateModifier
    | STATIC     as StaticModifier
    | INTRINSIC  as IntrinsicModifier
    ;
```

This single rule change lets `intrinsic` appear at both type level (because `ClassDeclaration` and `StructDeclaration` already prefix `modifiers:Modifier*`) and member level (because `MethodDeclaration`, `ConstructorDeclaration`, and the new `OperatorDeclaration` all prefix `modifiers:Modifier*`). No separate grammar rule is needed for type-level vs member-level intrinsic — one alternative in `Modifier` satisfies Requirements 5.1 and 5.11.

**Add `OperatorDeclaration` rule and wire into `MemberDeclaration`.** The operator form mirrors `MethodDeclaration` but substitutes `operator <symbol>` for the method name and allows an optional `;`-terminated body (for intrinsic operators):

```storm
rule MemberDeclaration
    = constructor:ConstructorDeclaration as ConstructorMember
    | method:MethodDeclaration           as MethodMember
    | operator:OperatorDeclaration       as OperatorMember   // NEW
    | property:PropertyDeclaration       as PropertyMember
    | indexer:IndexerDeclaration         as IndexerMember
    | field:FieldDeclaration             as FieldMember
    ;

rule OperatorDeclaration
    = modifiers:Modifier*
      returnType:TypeReference
      OPERATOR
      symbol:OperatorSymbol
      LEFT_PARENTHESIS parameters:ParameterList? RIGHT_PARENTHESIS
      body:OperatorBody
    ;

rule OperatorBody
    = block:Block            as OperatorBlockBody
    | SEMICOLON              as OperatorNoBody
    ;

rule OperatorSymbol
    = PLUS                  as PlusOp
    | MINUS                 as MinusOp
    | STAR                  as StarOp
    | SLASH                 as SlashOp
    | MODULO                as ModuloOp
    | EQUAL_EQUAL           as EqualEqualOp
    | NOT_EQUAL             as NotEqualOp
    | LESS_THAN             as LessThanOp
    | GREATER_THAN          as GreaterThanOp
    | LESS_THAN_OR_EQUAL    as LessThanOrEqualOp
    | GREATER_THAN_OR_EQUAL as GreaterThanOrEqualOp
    | LOGICAL_AND           as LogicalAndOp
    | LOGICAL_OR            as LogicalOrOp
    | EXCLAMATION           as ExclamationOp
    ;
```

A new `OPERATOR` keyword (`"operator"`) is added to `keywords.storm` — contextual would suffice but Mist prefers reserved, matching the `intrinsic` choice and avoiding any ambiguity with identifiers. The 14 operator symbols exactly cover Requirements 4.3, 4.4, and 4.5 (binary arithmetic, binary/unary comparison, unary `!`, unary `-` which reuses `MinusOp` above).

**Body-omission for intrinsic members.** `MethodDeclaration` and `ConstructorDeclaration` currently require `body:Block`. Relax to `body:MethodBody` with the same `Block | SEMICOLON` alternatives as `OperatorBody`. This is the grammar-level machinery behind Requirement 5.2.

```storm
rule MethodBody
    = block:Block  as MethodBlockBody
    | SEMICOLON    as MethodNoBody
    ;
```

Whether `;` is valid on a given declaration is checked in the semantic model (Requirement 5.3, 5.4), not in the grammar — this keeps the grammar local and moves the semantic invariants to where diagnostics are already emitted. Interface methods continue to use their existing `InterfaceMethodDeclaration` rule unchanged.

### 2. Semantic model types (`Source/Mist/Semantics/Model/Types/`)

**Decision: keep `StructType` / `ClassType` as the representation of every Bootstrap_Tier type.** A new subtype (e.g., `IntrinsicType`) is not introduced — stdlib bootstrap types are ordinary user-type instances with additional metadata. Rationale: the semantic model, type checker, and member lookup already work for `StructType` and `ClassType`; grafting a fourth concrete type variant would force every `switch (type)` site to grow. Requirement 2.4 explicitly says bootstrap types are treated "as a normal user-declared type for all purposes" — the representation supports this literally.

**Intrinsic markers on types and symbols.** Add two boolean flags (set by the semantic model builder; immutable after Pass 1):

| Flag                         | Where                               | Meaning                                     |
| ---------------------------- | ----------------------------------- | ------------------------------------------- |
| `IType.IsBackendBacked`      | New readonly on `ClassType`, `StructType` | Type-level `intrinsic` was present. |
| `ISymbol.IsIntrinsic`        | New readonly on `Symbol` | Member-level `intrinsic` was present. |

The two flags encode independent information. The type-level flag tells the emitter the type has a target-language backing and participates in the `Backing_Type_Map`. The member-level flag tells the emitter the member's body comes from the `IntrinsicEmissionTable` rather than from a Mist-bodied static method. Of the four possible `(IsBackendBacked, IsIntrinsic)` combinations, three are valid and one is rejected at semantic-model validation time:

| Type `IsBackendBacked` | Member `IsIntrinsic` | Status | Example |
|---|---|---|---|
| `true` | `true` | Valid | `Standard.Int32.operator+` — backend-provided template |
| `true` | `false` | Valid | A hypothetical pure-Mist helper method on a backed type (e.g., `Int32.IsEven()` composing intrinsic `%` and `==`); none exist in v1, but the combination must be allowed so v1.x can add them |
| `false` | `true` | **Rejected** — Requirement 5.15 pairing rule, diagnostic `MIST_SB_0032 (historical)` | A user class with an `intrinsic` method; forbidden |
| `false` | `false` | Valid | Any ordinary user type and member |

Because a pure-Mist method on a backed type is legal (even though v1 ships none), the two flags are not redundant: an emitter or analyzer cannot derive one from the other in the general case.

For Task 1.1 simplicity, these are concrete fields on `ClassType` and `StructType` (not pushed into the `IType` interface). Call sites that need them are few: the emitter, the Pass 1 diagnostic checks, and the operator overload resolver.

**Fate of `PrimitiveType.cs`, `StringType.cs`, `ArrayType.cs`.**

| File | Disposition |
|---|---|
| `PrimitiveType.cs` | Removed at the end of migration. During the migration window (see § *Migration Plan*) the singletons are retained but mutated so each `PrimitiveType.Int` becomes a reference-equal alias for the `Standard.Int32` `StructType` instance. Requirement 8.3 allows the shim; Requirement 8.2 requires eventual removal. |
| `StringType.cs` | Removed. `StringType.Instance` is replaced across the codebase by the `Standard.String` `ClassType` instance obtained through the semantic model's type-lookup helpers. |
| `ArrayType.cs` | **Kept.** `ArrayType` encodes the structural pairing of element-type and array-ness; it is a *generic constructed type over `Standard.Array<T>`*, not itself a target-language type. Rename the emission intent: `ArrayType(elementType)` conceptually represents `Standard.Array<elementType>`. The C# emitter continues to emit it as `T[]` via the Backing_Type_Map. Alternative of folding into a single `ConstructedType(Standard.Array<T>, [elem])` is possible but defers to a v1.x refactor; keeping `ArrayType` preserves reference-equality semantics for `int[]` expressions across the migration window. |

**Keyword_Alias_Mapping location.** A single new file `Source/Mist/Semantics/Model/KeywordAliases.cs` holds the mapping. Requirement 7.5 mandates a single place; this is the place.

```csharp
// Source/Mist/Semantics/Model/KeywordAliases.cs
namespace Mist.Semantics.Model;

public static class KeywordAliases
{
    // Primitive keyword → fully-qualified stdlib type name.
    // See Requirement 7.1 for the full list.
    public static readonly ImmutableDictionary<string, string> Map =
        new Dictionary<string, string>
        {
            ["int"]     = "Standard.Int32",
            ["uint"]    = "Standard.UInt32",
            ["long"]    = "Standard.Int64",
            ["ulong"]   = "Standard.UInt64",
            ["short"]   = "Standard.Int16",
            ["ushort"]  = "Standard.UInt16",
            ["byte"]    = "Standard.Byte",
            ["sbyte"]   = "Standard.SByte",
            ["float"]   = "Standard.Single",
            ["double"]  = "Standard.Double",
            ["decimal"] = "Standard.Decimal",
            ["bool"]    = "Standard.Boolean",
            ["char"]    = "Standard.Char",
            ["string"]  = "Standard.String",
        }.ToImmutableDictionary();
}
```

### 3. Semantic model builder (`Source/Mist/Semantics/Model/SemanticModelBuilder.cs`)

Three additions to `SemanticModelBuilder.Build`:

**(a) Pass 0 — Stdlib registration.** Before Pass 1 registers user types, stdlib ASTs are registered into the `Standard` namespace scope. Under Approach B (see § *Stdlib Loading Strategy*), stdlib ASTs arrive as part of `asts` with a flag on their `CompilationUnit` saying "this is stdlib source"; Pass 0 simply splits the AST list into stdlib and user segments, registers stdlib first, and lets Pass 1 register user types on top. Because the existing Pass 1 already registers types into the scope they declare (via `QualifiedIdentifier` of a namespace declaration), the stdlib's `namespace Standard;` headers put its types in the right place automatically.

**(b) Primitive keyword resolution.** In `Pass2_ResolveTypes`, when the builder encounters a `PrimitiveTypeRefTypeReferenceNode` (the grammar rule that matches `int`, `string`, etc.), it no longer returns a `PrimitiveType.*` singleton or `StringType.Instance`. Instead:

```csharp
private IType ResolvePrimitiveKeyword(PrimitiveTypeNode keyword)
{
    // Requirement 7.6: bypass all scoping. Look up directly in the global
    // Standard-namespace scope, not through any using-directive or lexical
    // scope chain.
    var keywordText = GetKeywordLexeme(keyword);                 // e.g., "int"
    var qualifiedName = KeywordAliases.Map[keywordText];          // "Standard.Int32"
    return LookupQualifiedStdlibType(qualifiedName);              // StructType for Standard.Int32
}
```

`LookupQualifiedStdlibType` is a new helper that walks directly from the global scope to the `Standard` namespace scope, finds the type symbol by its unqualified name, and returns the `IType`. If the stdlib has not been loaded (misconfigured installation), this throws a fatal `MIST_DRIVER_* (historical scheme)` diagnostic.

**(c) Operator declarations registered as static members.** The new `OperatorDeclaration` AST nodes are handled in the same Pass 1 loop that registers methods. Each operator becomes a `Symbol` with:

- `Kind = SymbolKind.Method` (operators are static methods for overload resolution purposes — Requirement 4.6)
- `Name = "operator+"` / `"operator=="` / etc., using the operator symbol as the name key
- `IsIntrinsic` set from the `intrinsic` modifier
- `DeclaredType` set to the return type
- Parameter signatures extracted exactly like for methods

Naming operators `operator+` (with the `operator` prefix) ensures they don't collide with user-defined methods named `plus` or similar, and lets the type checker look them up by constructing the same key (`"operator" + opLexeme`).

**(d) Intrinsic-marker validation.** After Pass 1 registers symbols, a new `Pass1e_ValidateIntrinsicMarkers` pass walks every type and emits diagnostics for:

| Check | Requirement | Diagnostic ID |
|---|---|---|
| Member `intrinsic` with a body | 5.3 | `MIST_SB_0030 (historical)` (new) |
| Member `intrinsic` in a non-stdlib unit | 5.6 | `MIST_SB_0031 (historical)` (new) |
| Type-level `intrinsic` in a non-stdlib unit | 5.13 | `MIST_SB_0031 (historical)` (same ID — one message covers both placements, text differs) |
| Member `intrinsic` whose containing type is not `intrinsic` | 5.15 | `MIST_SB_0032 (historical)` (new) |

"Non-stdlib unit" is determined by a flag on `CompilationUnit` set when the driver loads stdlib source (see § *Stdlib Loading Strategy*).

### 4. Type checker (`Source/Mist/Analysis/Builtin/TypeCheckerAnalyzer.cs`)

**Remove `NumericTypes` HashSet** (lines 26–34). `IsNumeric` (line ~3186) is removed; no caller remains after the operator rewrites below.

**Rewrite `CheckAdditiveNode`, `CheckMultiplicativeNode`, `CheckRelationalNode`, `CheckEqualityNode`** (lines ~1490, 1529, 1463, 1436) to dispatch through a single new helper, `ResolveBinaryOperator`:

```csharp
private void ResolveBinaryOperator(
    string operatorSymbol,              // "+", "-", "==", etc.
    ExpressionNode leftNode,
    ExpressionNode rightNode,
    SyntaxNode surroundingNode,         // for diagnostic span
    ISemanticModel model,
    DiagnosticBag diagnostics)
{
    var leftType = model.GetTypeOf(leftNode);
    var rightType = model.GetTypeOf(rightNode);
    if (leftType is null or UnknownType || rightType is null or UnknownType)
        return;

    var opName = "operator" + operatorSymbol;

    // Requirement 6.1: collect candidates from BOTH operand types.
    var candidates = new List<ISymbol>();
    candidates.AddRange(GetOperatorMembers(leftType, opName));
    if (!ReferenceEquals(leftType, rightType))
        candidates.AddRange(GetOperatorMembers(rightType, opName));

    if (candidates.Count == 0)
    {
        diagnostics.Add(MakeDiagnostic(
            "MIST_TC_0003",
            $"Operator '{operatorSymbol}' cannot be applied to operands of type '{leftType.DisplayName}' and '{rightType.DisplayName}'",
            surroundingNode, model));
        return;
    }

    // Requirement 6.3: reuse the existing OverloadResolver.
    var result = OverloadResolver.Resolve(
        candidates,
        argumentTypes: new[] { leftType, rightType },
        model);

    switch (result.Status)
    {
        case OverloadResolutionStatus.Success:
            // Cache the selected operator on the AST node for the emitter.
            model.RecordResolvedOperator(surroundingNode, result.BestCandidate!);
            model.SetTypeOf(surroundingNode, result.BestCandidate!.DeclaredType!);
            break;

        case OverloadResolutionStatus.NoApplicableCandidate:
            diagnostics.Add(MakeDiagnostic(
                "MIST_TC_0003",
                $"Operator '{operatorSymbol}' cannot be applied to operands of type '{leftType.DisplayName}' and '{rightType.DisplayName}'",
                surroundingNode, model));
            break;

        case OverloadResolutionStatus.AmbiguousCall:
            diagnostics.Add(MakeDiagnostic(
                "MIST_TC_0032",
                $"Ambiguous operator '{operatorSymbol}' between types '{leftType.DisplayName}' and '{rightType.DisplayName}'",
                surroundingNode, model));
            break;
    }
}
```

The four existing `CheckAdditiveNode` / `CheckMultiplicativeNode` / `CheckRelationalNode` / `CheckEqualityNode` methods collapse to three-line wrappers that call `ResolveBinaryOperator` with the appropriate lexeme. The existing diagnostic IDs `MIST_TC_0003 (historical)`–`MIST_TC_0006 (historical)` (covered by Requirement 6.4) are preserved by keyed lookup based on which `Check*` wrapper called in — `MIST_TC_0003 (historical)` for additive/multiplicative, `MIST_TC_0005 (historical)` for relational, `MIST_TC_0006 (historical)` for equality. The message template stays the same.

**Unary operators.** `CheckUnaryNode`'s cases for `LogicalNotUnaryNode` and `NegateUnaryNode` get analogous rewrites to a `ResolveUnaryOperator` helper: single operand, look up `"operator-"` or `"operator!"` on the operand's type, resolve with a one-element argument list. Unary operators almost never have overloads (only one operator per type on one type), so the `OverloadResolver` path always returns `Success` or `NoApplicableCandidate`.

**`RecordResolvedOperator` / `GetResolvedOperator`.** Two new methods on `ISemanticModel` (and `SemanticModel`) for the emitter to retrieve which operator symbol was selected. Internally a `Dictionary<SyntaxNode, ISymbol>` populated during type checking and read during emission.

**Diagnostics for operator declaration misuse.** Three new checks during Pass 1:

| Check | Requirement | Diagnostic ID |
|---|---|---|
| Operator declared outside a class/struct body (detected at parse level by the grammar, but confirmed here) | 4.7 | `MIST_SB_0033 (historical)` (new) |
| Binary operator without exactly 2 parameters | 4.8 | `MIST_SB_0034 (historical)` (new) |
| Unary operator without exactly 1 parameter | 4.9 | `MIST_SB_0034 (historical)` (same ID, different message) |
| Operator not declared `public static` | 4.2 | `MIST_SB_0035 (historical)` (new) |

### 5. Overload resolver (`Source/Mist/Analysis/Builtin/OverloadResolver.cs`)

**No algorithmic changes.** The existing `Resolve` method (lines 40–150 of `OverloadResolver.cs`) works unchanged — it accepts any `IReadOnlyList<ISymbol>` and any `IReadOnlyList<IType>`. Operator overload resolution differs from method overload resolution only in how candidates are gathered, which is the type checker's job.

**Tiebreaker for "operator on A vs operator on B" (Requirement 15.4 open question).**

**Decision: simple deterministic rule — candidates from the *left* operand's type take precedence over candidates from the *right* operand's type when `MoreSpecific` would otherwise tie.** This is added as an optional last-resort tiebreaker to `OverloadResolver.Resolve` via a new parameter `IReadOnlyList<int>? candidateOrigin` (0 = left, 1 = right, `null` for normal calls) that, when provided, resolves an otherwise-ambiguous set by preferring lower-origin candidates. This is a new code path — the method-call call site passes `null` and keeps existing behavior.

Justification for left-precedence: (a) deterministic (meets Requirement 12.5); (b) avoids C#'s "better conversion" machinery which Mist doesn't have (no implicit conversions in v1); (c) the author of `A` is typically the one defining `A + B` and expecting it to win; (d) can be revisited once implicit conversions land without breaking existing code.

### 6. Backend abstraction (`Source/Mist/Backends/Contracts/`)

**No change to `IBackend`.** The Backing_Type_Map is backend-private (Requirement 9.11); exposing anything about it in the `IBackend` contract would violate that. Each backend implements its mapping internally.

**New backend-private contract (inside `Source/Mist/Backends/CSharpTranspiler/`).** Two types:

```csharp
// Source/Mist/Backends/CSharpTranspiler/Intrinsics/IntrinsicEmission.cs
internal sealed record IntrinsicEmissionTemplate(string Template);
// Template uses "{0}" / "{1}" for positional operand/argument substitution.

internal interface IIntrinsicEmissionRegistry
{
    // Look up by (declaring Mist type's fully-qualified name, operator name or method name).
    IntrinsicEmissionTemplate? TryGetBinaryOperator(string declaringType, string op);
    IntrinsicEmissionTemplate? TryGetUnaryOperator(string declaringType, string op);
    IntrinsicEmissionTemplate? TryGetInstanceMethod(string declaringType, string method, ImmutableArray<string> paramTypes);
    IntrinsicEmissionTemplate? TryGetConstructor(string declaringType, ImmutableArray<string> paramTypes);
}
```

The registry is populated from a table inside the C# backend at startup. One concrete record per intrinsic member from Requirement 3 (e.g., 5 arithmetic + 6 comparison operators per numeric type × 11 numeric types, plus `String.operator+`, `==`, `!=`, `Length`, `Substring`, `Contains`, and `Array<T>.Length`). Templates are trivial C# expression fragments:

```
("Standard.Int32", "+")   → "{0} + {1}"
("Standard.Int32", "<")   → "{0} < {1}"
("Standard.Int32", "-")   → "{0} - {1}"   // binary
("Standard.Int32", "negate") → "-{0}"     // unary (name-mangled to distinguish)
("Standard.String", "+")  → "{0} + {1}"    // C# string concat is already the right semantics
("Standard.String", "Contains") → "{0}.Contains({1})"
("Standard.Array", "Length")   → "{0}.Length"
```

Requirement 5.9 mandates a hard error when a lookup misses; the backend throws `MIST0001` (new) `MissingIntrinsicEmissionTemplate`, fails the compilation, and emits no output per Requirement 5.10.

### 7. C# backend (`Source/Mist/Backends/CSharpTranspiler/`)

**Evolve `CSharpTypeMap.cs` into the Backing_Type_Map.** The existing `CSharpTypeMap.GetTypeName` (lines 42–57) already switches on `PrimitiveType`, `StringType`, `ArrayType` — all of which go away. Replace with a single dictionary keyed on stdlib type identity:

```csharp
internal static class CSharpTypeMap
{
    // Backing_Type_Map (Requirement 9.7). Key: fully-qualified Mist stdlib type name.
    // Value: C# source-code representation.
    private static readonly Dictionary<string, string> BackingTypes = new()
    {
        ["Standard.Int32"]   = "int",
        ["Standard.UInt32"]  = "uint",
        ["Standard.Int64"]   = "long",
        ["Standard.UInt64"]  = "ulong",
        ["Standard.Int16"]   = "short",
        ["Standard.UInt16"]  = "ushort",
        ["Standard.Byte"]    = "byte",
        ["Standard.SByte"]   = "sbyte",
        ["Standard.Single"]  = "float",
        ["Standard.Double"]  = "double",
        ["Standard.Decimal"] = "decimal",
        ["Standard.Boolean"] = "bool",
        ["Standard.Char"]    = "char",
        ["Standard.String"]  = "string",
        // Array is generic — handled specially below.
    };

    public static string GetTypeName(IType type, bool isValueContext = false)
    {
        // Requirement 9.8 — uniform substitution at every type-reference site.
        switch (type)
        {
            case ClassType ct when ct.IsBackendBacked && ct.ContainingNamespace == "Standard":
                if (BackingTypes.TryGetValue($"Standard.{ct.DisplayName}", out var keyword))
                    return keyword;
                break;
            case StructType st when st.IsBackendBacked && st.ContainingNamespace == "Standard":
                if (BackingTypes.TryGetValue($"Standard.{st.DisplayName}", out var kw))
                    return kw;
                break;
            case VoidType: return isValueContext ? "MistVoid" : "void";
            case ArrayType a: return $"{GetTypeName(a.ElementType, isValueContext)}[]";
            case TypeType: return "System.Type";
            case TypeParameterType tp: return tp.Name;
            case ConstructedType ct: return $"{GetTypeName(ct.GenericDefinition, isValueContext)}<{string.Join(", ", ct.TypeArguments.Select(t => GetTypeName(t, isValueContext)))}>";
            case ClassType c: return c.DisplayName;
            case StructType s: return s.DisplayName;
            case InterfaceType i: return i.DisplayName;
        }
        throw new ArgumentException($"Cannot map type to C#: {type.DisplayName}");
    }
}
```

Requirement 9.11 is preserved structurally: this file lives in `Source/Mist/Backends/CSharpTranspiler/`, is `internal`, and has no reference from `Source/Mist/Semantics/`, `Source/Mist/Analysis/`, or any `Contracts/` directory.

**Evolve `CSharpAmbientTypeResolver.cs`.** The file today builds `ClassType` instances with members whose `DeclaredType` is `StringType.Instance`, `PrimitiveType.Int`, etc. (see lines 113, 125, 148 of the current source). After this feature, those singletons don't exist — the resolver needs the stdlib types. Two changes:

1. The resolver is constructed **with an `ISemanticModel`-like handle** passed by the driver, from which it can look up `Standard.String`, `Standard.Int32`, etc. This handle is an `IStdlibTypeProvider` (a new, read-only interface with a single `IType LookupStandardType(string simpleName)` method), passed in at construction. The new `CompilerDriver` flow is: build stdlib-only partial model first → construct ambient resolver with that model as the provider → run full build with the resolver.

2. Remove the `BuildString` and `BuildArray` factories (lines 140–160, 185–200) entirely — per Requirement 9.1, 9.2, `String` and `Array<T>` are stdlib types now; the ambient resolver does not expose them. `BuildConsole`, `BuildMath`, `BuildConvert` remain but their signatures switch to reference stdlib types:

```csharp
// Before: members.Add(BuildMethodSymbol("WriteLine", VoidType.Instance, ..., ...)));
//         members.Add(BuildMethodSymbol("Abs", PrimitiveType.Int, ..., ...)));
// After (requires holding a reference to Standard.Int32 from the provider):
members.Add(BuildMethodSymbol("Abs", stdlib.LookupStandardType("Int32"), ..., ...));
```

Requirement 9.9, 9.10 are thus satisfied at the API boundary: everything the resolver returns is a Mist type.

**Evolve `CSharpEmitter.cs`.** Specifically `EmitAdditive` / `EmitMultiplicative` / `EmitRelational` / `EmitEquality` at lines ~1663–1693. Currently:

```csharp
// From Source/Mist/Backends/CSharpTranspiler/CSharpEmitter.cs lines 1663-1672:
private string EmitAdditive(AdditiveNode additive)
{
    var left = EmitMultiplicative(additive.Left);
    if (additive.Right is not null && !ReferenceEquals(additive.Right, additive.Left))
    {
        var op = additive.Operator.Lexeme;   // passes Mist's "+" straight through
        var right = EmitMultiplicative(additive.Right);
        return $"{left} {op} {right}";
    }
    return left;
}
```

After this feature:

```csharp
private string EmitAdditive(AdditiveNode additive)
{
    var left = EmitMultiplicative(additive.Left);
    if (additive.Right is null || ReferenceEquals(additive.Right, additive.Left))
        return left;

    var right = EmitMultiplicative(additive.Right);
    var opSymbol = additive.Operator.Lexeme;    // "+", "-"

    // Look up the operator resolved during type checking.
    var resolved = _semanticModel.GetResolvedOperator(additive);
    if (resolved is not null && resolved.IsIntrinsic)
    {
        // Backend-backed intrinsic: use emission template.
        var declaringType = FullyQualifiedName(resolved.ContainingScope);
        var template = _intrinsicRegistry.TryGetBinaryOperator(declaringType, opSymbol)
            ?? throw FailMissingIntrinsic(declaringType, opSymbol, additive);
        return string.Format(template.Template, left, right);
    }

    if (resolved is not null)
    {
        // User-defined or pure-Mist operator: emit as static method call.
        var declaringType = FullyQualifiedName(resolved.ContainingScope);
        var methodName = CSharpOperatorName(opSymbol);      // "+" → "op_Addition"
        return $"{declaringType}.{methodName}({left}, {right})";
    }

    // Unreachable if type checker did its job; defensive fallback:
    return $"{left} {opSymbol} {right}";
}
```

The same shape applies to `EmitMultiplicative`, `EmitRelational`, `EmitEquality`, and the `LogicalNotUnaryNode` / `NegateUnaryNode` cases of `EmitUnary`.

For the common case (intrinsic operators on `Standard.Int32` etc.), the emitted C# is byte-identical to today — the intrinsic template is `{0} + {1}` — which satisfies Requirement 11.5.

### 8. Compiler driver (`Source/Mist/Driver/CompilerDriver.cs`)

**One new step between the current Step 1 (load sources) and Step 2 (lex).** After `options.SourcePaths` is loaded into `CompilationUnit` records, the driver loads stdlib sources and prepends them to `compilation.Units`:

```csharp
// After: var compilation = new MistCompilation(units.ToImmutable());

// --- NEW: Inject stdlib sources (Requirement 10.1, Approach B). ---
var stdlibUnits = StdlibLoader.LoadFor(backend, allDiagnostics);
var allUnits = stdlibUnits.AddRange(compilation.Units);
compilation = new MistCompilation(allUnits) { DisplayName = compilation.DisplayName };
```

`StdlibLoader` is a new class in `Source/Mist/Driver/` that:
- Locates stdlib source via `AppContext.BaseDirectory + "/Standard/"` (or a per-compiler-install well-known path, see § *Stdlib Loading Strategy* for details on packaging).
- Reads every `.mist` under that directory.
- Constructs `CompilationUnit` records with a new `IsStdlib` flag (a boolean on `CompilationUnit`).
- Returns an `ImmutableArray<CompilationUnit>`.

The `IsStdlib` flag is consumed by the semantic model builder's `Pass1e_ValidateIntrinsicMarkers` (§ 3(d)) to distinguish permitted stdlib `intrinsic` usage from prohibited user usage.

**One compilation, one semantic model.** The stdlib is compiled into the *same* `MistCompilation` and *same* `ISemanticModel` as user source. This is the right answer because:

- Stdlib types must appear in the root scope (alongside user types) so name resolution Just Works.
- The ambient resolver needs to reference stdlib types as it builds `Console.WriteLine(Standard.String)` signatures — a separate stdlib-only model would force two-pass plumbing.
- Overload resolution for `user_vec + user_vec` doesn't care which tier the operator came from.

No separate "stdlib model" is built and merged; stdlib source simply leads the AST list.

## Data Models

### `Keyword_Alias_Mapping`

`ImmutableDictionary<string, string>` — primitive keyword lexeme to fully-qualified stdlib type name. See the concrete value in § *Components and Interfaces → 2. Semantic model types*. Single copy in `Source/Mist/Semantics/Model/KeywordAliases.cs`. Read by `SemanticModelBuilder.ResolvePrimitiveKeyword` only.

### `Backing_Type_Map`

Internal to the C# backend. Shape: `Dictionary<string, string>` keyed on fully-qualified stdlib Mist type name (e.g., `"Standard.Int32"`), valued as a C# source-code string (e.g., `"int"`). `Array<T>` is handled structurally (not via the dict) because the element type is generic. See `CSharpTypeMap.cs` revision in § 7.

### Emission template registry

Internal to the C# backend. Shape: four `Dictionary<(string, string), IntrinsicEmissionTemplate>` (one per member kind) plus one for constructors. Populated at backend startup from a static initializer table. Read only by the emitter.

```csharp
internal static class IntrinsicEmissionTable
{
    public static readonly Dictionary<(string Type, string Op), IntrinsicEmissionTemplate> BinaryOps = new()
    {
        // Numeric arithmetic — same template shape for every type. Generated programmatically
        // in the static constructor to avoid 11 * 5 = 55 copy-pasted entries.
        [("Standard.Int32", "+")] = new("{0} + {1}"),
        [("Standard.Int32", "-")] = new("{0} - {1}"),
        // ... (auto-generated in the static constructor)
        [("Standard.String", "+")] = new("{0} + {1}"),
    };

    public static readonly Dictionary<(string Type, string Op), IntrinsicEmissionTemplate> UnaryOps = new()
    {
        [("Standard.Int32", "negate")] = new("-{0}"),
        [("Standard.Boolean", "not")] = new("!{0}"),
        // ...
    };

    public static readonly Dictionary<(string Type, string Method, string ParamTypeSignature), IntrinsicEmissionTemplate> InstanceMethods = new()
    {
        [("Standard.String", "Length", "")] = new("{0}.Length"),
        [("Standard.String", "Substring", "Standard.Int32")] = new("{0}.Substring({1})"),
        [("Standard.String", "Contains", "Standard.String")] = new("{0}.Contains({1})"),
        [("Standard.Array`1", "Length", "")] = new("{0}.Length"),
    };
}
```

### `IntrinsicMarker` in the AST

Not a distinct AST node. Represented as an `IntrinsicModifierModifierNode` in the generated `Modifier` sum type (same mechanism as `PublicModifierModifierNode`, `StaticModifierModifierNode`). The `SemanticModelBuilder` sets a `IsIntrinsic` bool on the resulting `Symbol` / `IType` and the AST node itself is discarded after Pass 1.

## Stdlib Loading Strategy

**Recommendation: Approach B — source prelude.** Locked in for v1.

### Evaluation

| Criterion | A: Precompiled | B: Source prelude | C: Decl-only + backend |
|---|---|---|---|
| Lines of new compiler code | ~800 (serializer + deserializer + format spec) | ~80 (`StdlibLoader` + driver integration) | ~400 (two separate loading paths because pure-tier still needs A or B) |
| Per-compilation cost | Milliseconds (deserialize once) | ~50–100 ms (re-parse + re-analyze ~20 small .mist files) | Near-zero for bootstrap; same as A or B for pure tier |
| Bootstrap time risk | High — serialization format changes break installed compilers | Low | Medium — hybrid loading model has more moving parts |
| Version drift risk | Artifact can mismatch compiler binary if not rebuilt in sync | None — source ships with compiler | Medium |
| Supports pure-Mist tier | Yes | Yes | No (pure-tier requires A or B) |
| Caching story | Already free | Deferred to future (compilation-cache layer) | Free for bootstrap |
| End-to-end proof of stdlib-as-Mist-source | Weakens it (stdlib becomes binary blob) | Strong — every stdlib file goes through lex → parse → analyze | Weakens bootstrap (bootstrap never parsed) |

### Justification for Approach B

1. **Simplest implementation.** The only new code is `StdlibLoader` (~40 lines) and a two-line addition to `CompilerDriver.Compile` to prepend stdlib units. The semantic model, analyzers, and emitter do not need any "stdlib vs user" distinction beyond the `IsStdlib` flag on `CompilationUnit` for diagnostic purposes (Requirement 5.6). Approach A would require a serialization format spec, a serializer, a deserializer, and a compiler-build step — all of which become version-correctness liabilities.

2. **Stdlib is small.** The bootstrap tier is ~14 types × ~5 operators/methods each = ~70 declarations. Each file is O(50–150) lines. Parsing and Pass-1/Pass-2 cost for 20 files of this size is tens of milliseconds on developer hardware — negligible alongside the existing sub-second `dotnet publish` invocation in `CSharpCompilationDriver`.

3. **Guarantees version consistency.** With Approach B, stdlib source literally ships next to the compiler binary in the `dotnet publish` output. There is no possibility of a stdlib/compiler version mismatch.

4. **Proves the end-to-end pipeline.** Every stdlib type goes through the same lex → parse → analyze → emit path user types go through. This exercises grammar and semantic-model changes on real inputs and surfaces bugs early. Approach A would hide stdlib source from the pipeline, leaving grammar bugs undetected until someone writes an equivalent user-defined operator.

5. **Caching can come later without changing the surface.** If stdlib parse time ever becomes a bottleneck, the driver can cache ASTs keyed on stdlib file mtime without touching the rest of the pipeline — Approach B's parse → analyze → emit boundary is still where Approach A would plug in.

### Approach B implementation details

**Well-known path (Requirement 10.9).** Stdlib source is located at `<compiler-install>/Standard/*.mist`, where `<compiler-install>` is `AppContext.BaseDirectory`. For local development, `AppContext.BaseDirectory` is `Source/Mist.CLI/bin/<config>/net10.0/` and the stdlib is either copied there by a `<ItemGroup>` in `Mist.csproj` or symlinked. For shipped builds, `dotnet publish` on `Mist.CLI` writes the compiler's output to a publish directory; a `<Content Include="..\..\Source\Standard\**\*.mist" CopyToPublishDirectory="PreserveNewest" TargetPath="Standard\%(RecursiveDir)%(Filename)%(Extension)" />` item in `Mist.CLI.csproj` ensures the stdlib is colocated with the binary. Requirement 15.5 is resolved: `dotnet publish` packages the stdlib into `publish/Standard/`.

**Load order.** Stdlib units are loaded first. If stdlib files fail to parse, compilation fails with a `MIST0031` error and the user's own sources are not attempted (because nothing will resolve without stdlib types).

**What about tests?** Unit tests for the semantic model can bypass `StdlibLoader` by calling `SemanticModelBuilder.Build` directly with a test-constructed stdlib AST, avoiding filesystem coupling.

## Pure-Mist Tier for v1

**Recommendation: ship one minimal pure-Mist demo — `Standard.Optional<T>`.** Rationale:

1. Requirement 13.6 explicitly defers non-trivial pure-tier content to v1.x, but says nothing about zero content. An empty pure tier means no end-to-end proof that pure-Mist types interoperate with bootstrap types.
2. `Optional<T>` is small (~30 lines), useful (nullability modeling in current Mist is ad-hoc), and exercises: generics over a bootstrap type argument, a constructor, an instance method, a field of type `T`, and a method returning `bool` (a bootstrap operator result). Every machinery of the semantic model + emitter gets touched.
3. Ship location: `Source/Standard/Optional.mist` (directly in the pure-tier root, not under `Bootstrap/`, so Requirement 1.3 ordering is visible).
4. A regression test compiles a user program that uses `Optional<Int32>`, exercising the boundary between user code and pure-Mist tier.

Example shape:

```mist
namespace Standard;

public class Optional<T>
{
    private T value;
    private bool hasValue;

    public Optional()  { hasValue = false; }
    public Optional(T value) { this.value = value; this.hasValue = true; }

    public bool HasValue => hasValue;
    public T Value => value;
}
```

The type contains zero `intrinsic` members — it's pure Mist. It compiles like any user type.

## Correctness Properties

*A property is a characteristic or behavior that should hold true across all valid executions of a system — essentially, a formal statement about what the system should do. Properties serve as the bridge between human-readable specifications and machine-verifiable correctness guarantees.*

The requirements for this feature include several universal statements (Requirement 12 in particular). The following properties formalize them for property-based testing.

### Property 1: Primitive keyword resolution maps to stdlib types with reference identity

*For any* valid Mist program containing one or more occurrences of a primitive keyword `k` (one of `int`, `uint`, `long`, `ulong`, `short`, `ushort`, `byte`, `sbyte`, `float`, `double`, `decimal`, `bool`, `char`, `string`), every occurrence's resolved `IType` is reference-equal to the `Standard` namespace type named by `KeywordAliases.Map[k]`, and all occurrences of the same keyword within a single compilation resolve to the same `IType` instance.

**Validates: Requirements 7.1, 7.2, 8.5**

### Property 2: Primitive keyword resolution bypasses all scoping

*For any* user-authored Mist Compilation_Unit `U` and any primitive keyword `k` whose mapped stdlib type's simple name is `S`, even if `U` declares or imports a type named `S` in any namespace and regardless of whether `using Standard;` is present, every occurrence of `k` in `U` resolves to the stdlib type `KeywordAliases.Map[k]` — never to any user-declared or imported type named `S`. Conversely, an occurrence of the unqualified simple name `S` in `U` resolves via ordinary name-resolution scoping rules and may resolve to a user-declared `S` when such a type shadows.

**Validates: Requirements 7.6, 7.7, 7.8, 12.9**

### Property 3: Every operator expression is typed via OverloadResolver

*For any* well-typed binary or unary operator expression `e` in a user or stdlib Compilation_Unit, the symbol selected by the type checker for `e` equals the symbol that `OverloadResolver.Resolve` would select given (a) the set of operator-member candidates collected from the operand types and (b) the operand expression types as `argumentTypes`. Equivalently, the type checker contains no special-case operator logic that bypasses `OverloadResolver`.

**Validates: Requirements 6.1, 6.2, 6.3, 6.7, 6.8, 6.9**

### Property 4: Operator overload resolution is deterministic

*For any* well-typed operator expression `e` and any ordering of the candidate operator-member list provided to the type checker, repeated resolutions of `e` (including across runs, HashSet iteration orders, and parallel executions) select the same operator symbol every time.

**Validates: Requirement 12.5**

### Property 5: Bootstrap numeric operators match C# semantics

*For any* bootstrap numeric type `N` (one of `Int32`, `UInt32`, `Int64`, `UInt64`, `Int16`, `UInt16`, `Byte`, `SByte`, `Single`, `Double`, `Decimal`), any operator `op` in `{+, -, *, /, %, <, >, <=, >=, ==, !=}` legal on `N`, and any operand pair `(a, b)` of type `N` with `b ≠ 0` for `/` and `%`, the value produced by compiling and executing a Mist program that computes `a op b` equals the value produced by evaluating the equivalent C# expression `(N_cs)a op (N_cs)b` where `N_cs` is `N`'s backing C# type.

**Validates: Requirements 12.1, 12.2, 12.3**

### Property 6: Bootstrap operator round-trip inverse

*For any* bootstrap numeric type `N` on which both `+` and `-` are declared, and any pair `(a, b)` of type `N` for which `a + b` does not overflow `N`'s representable range, the Mist expression `(a + b) - b` evaluates to `a`.

**Validates: Requirement 12.7**

### Property 7: No target-language backing type appears in the semantic model

*For any* Mist Compilation_Unit that compiles successfully, *every* `IType` instance reachable from the resulting `ISemanticModel` — including parameter types, return types, local variable types, field types, expression types, generic type arguments, and the types of all members produced by every `IAmbientTypeResolver` — is either a user-declared Mist type, a `Standard.*` type (bootstrap or pure-Mist tier), or an ambient synthesized type whose own members' signature types recursively satisfy this property. No reachable `IType` corresponds to a target-language backing type such as `System.String`, `System.Int32`, or `System.Array`.

**Validates: Requirements 9.6, 9.9, 9.10, 12.10**

## Error Handling and Diagnostics

### Diagnostic IDs introduced by this feature

All new IDs are `Error` severity. Existing IDs `MIST_TC_0003 (historical)`, `MIST_TC_0005 (historical)`, `MIST_TC_0006 (historical)`, `MIST_TC_0032 (historical)` are reused per Requirement 14.1–14.2. The last `MIST_SB_ (historical scheme)` ID used today is `MIST_SM_0022 (historical)` (see `SemanticModelBuilder.cs` line 22); new IDs claim the next `MIST_SB_* (historical scheme)` block.

| ID | Requirement | Message template | Trigger example |
|---|---|---|---|
| `MIST_SB_0030 (historical)` | 5.3 | `Intrinsic member '{type}.{member}' must not have a body; use ';' to mark it as backend-provided.` | `intrinsic public static Int32 operator +(Int32 a, Int32 b) { return 0; }` |
| `MIST_SB_0031 (historical)` | 5.6, 5.13 | `The 'intrinsic' modifier is reserved for the Standard Library and cannot be used in user code.` (same ID for both type-level and member-level misuse; message identical) | `intrinsic public struct MyThing { }` in user code |
| `MIST_SB_0032 (historical)` | 5.15 | `Member-level 'intrinsic' is only permitted inside a type that is itself declared 'intrinsic'. Type '{type}' is not intrinsic.` | A non-intrinsic class in stdlib contains `intrinsic public void Foo();` |
| `MIST_SB_0033 (historical)` | 4.7 | `Operators must be declared as 'public static' members of a class or struct.` | An operator declaration appearing at namespace scope (caught in grammar/semantic builder) |
| `MIST_SB_0034 (historical)` | 4.8, 4.9 | `Binary operator '{op}' must declare exactly 2 parameters; found {n}.` / `Unary operator '{op}' must declare exactly 1 parameter; found {n}.` | `public static Int32 operator +(Int32 a) { ... }` |
| `MIST_SB_0035 (historical)` | 4.2 | `Operator '{op}' must be declared 'public static'.` | `private static Int32 operator +(...) { ... }` |
| `MIST0001` | 5.9, 5.10 | `No emission template found for intrinsic '{type}.{member}' in backend '{backend}'.` | An intrinsic declared in stdlib but not registered in `IntrinsicEmissionTable`. Fatal; no output. |
| `MIST0031` | 10.1 | `Failed to load Standard Library source: {path}: {underlying message}` | Stdlib file missing or syntactically invalid at `AppContext.BaseDirectory/Standard/*.mist`. |

Reused existing IDs:

| ID | Use |
|---|---|
| `MIST_TC_0003 (historical)` | Binary additive/multiplicative operator no-applicable (today: type check; after: overload-resolution `NoApplicableCandidate`) |
| `MIST_TC_0005 (historical)` | Relational operator no-applicable |
| `MIST_TC_0006 (historical)` | Equality operator no-applicable |
| `MIST_TC_0032 (historical)` | Ambiguous operator (operator overload resolution `AmbiguousCall`) |

## Testing Strategy

The feature includes both pure-logic components (operator overload resolution, primitive-keyword resolution, Backing_Type_Map lookup) and integration points (compile-a-sample regression guard). Property-based testing is appropriate for the former; example-based / integration tests cover the latter.

### Unit tests

**Grammar changes.** Test the parser on small snippets:

- Parsing `public static Int32 operator +(Int32 a, Int32 b) { return a; }` produces an `OperatorDeclaration` AST node with `PlusOp`, two parameters, a block body.
- Parsing `intrinsic public static Int32 operator +(Int32 a, Int32 b);` produces an operator with `OperatorNoBody`.
- Parsing `intrinsic public struct Int32 { }` produces a struct with the `IntrinsicModifier` in its modifier list.
- Parsing `operator` as an identifier produces an error (reserved keyword).
- Parsing a method without a body (`public void Foo();`) inside a non-interface class is accepted by the parser but rejected by the semantic model unless the method also has `intrinsic`.

**Type checker changes.**

- `int + int` resolves through operator overload resolution to `Standard.Int32.operator+`; the resulting expression type is `Standard.Int32`.
- `user_Vec3 + user_Vec3` resolves to the user's declared operator; the AST is annotated with the selected symbol.
- `int + string` produces `MIST_TC_0003 (historical)`.
- A user type that declares two overloads of `+` with argument types `(Vec3, Vec3)` and `(Vec3, float)`: `vec + 1.0` resolves to the latter; `vec + vec` resolves to the former; ambiguous call produces `MIST_TC_0032 (historical)`.

### Property-based tests (FsCheck or equivalent)

Each property test runs ≥100 iterations per Requirement 12.8. Each is tagged with a comment referencing the design property.

**Property 1: Keyword-alias reference identity.** For any primitive keyword appearing in any valid Mist program, the `IType` the semantic model assigns to every occurrence of that keyword is reference-equal to every other occurrence.

**Property 2: Bootstrap numeric correctness.** For all `int`-range pairs `(a, b)` and each operator `op ∈ {+, -, *, /, %, <, >, <=, >=, ==, !=}`, compiling a Mist program that prints `a op b` and executing it produces the same value as `(int)a op (int)b` in C#. Repeats Requirement 12.1–12.3 structurally. Narrow operand space for `/` and `%` to avoid divide-by-zero (inputs filtered), consistent with Requirement 12.8's "pure function" guidance.

**Property 3: Operator overload-resolution determinism.** For any well-typed binary operator expression built from a fixed bag of operator declarations, repeated invocations of `TypeCheckerAnalyzer` + `OverloadResolver` select the same operator symbol every time. Shuffles candidate input order between runs; verifies identical output. Covers Requirement 12.5.

**Property 4: Round-trip inverse.** For all pairs `(a, b)` of `Standard.Int32` with `a + b` not overflowing, `(a + b) - b == a`. Applied to `Int32`, `Int64`, `UInt32`, `UInt64`, `Double`, `Decimal`. Covers Requirement 12.7.

**Property 5: Keyword vs simple-name resolution.** Given an adversarial user Compilation_Unit that declares `class Int32 { ... }` in some user namespace `N`, the semantic model's resolution of the keyword `int` in a member of `N` yields the `Standard.Int32` `IType` (not the user's `N.Int32`). The property enumerates every user namespace shape × every simple-name collision × presence/absence of `using Standard;`. Covers Requirement 12.9.

**Property 6: No backing-type leak.** For any valid Mist compilation that invokes `Console.WriteLine("hi")`, `Math.Abs(-1)`, `array.Length`, `x.ToString()`, every `IType` reachable from the built `ISemanticModel` (parameter types, return types, expression types, generic arguments, field types, ambient members) is either a user type, a `Standard.*` type, or an ambient type whose members recursively satisfy the same property. No `System.String`, `System.Int32`, or any CLR type appears. Covers Requirement 12.10.

### Integration tests

**Sample regression guard.** A test harness that walks every `.mist` file under `samples/`, compiles it using the real `CompilerDriver`, runs the resulting program, captures stdout, and diffs against a baseline captured before this feature lands. Fails on any byte difference. Covers Requirements 11.1–11.6 and 12.6.

**Two-tier compilation smoke.** A test that invokes `CompilerDriver.Compile` on a user program using `Optional<Int32>` — ensures stdlib source (including pure-tier) parses, analyzes, and co-compiles with user source without issue.

### Testing across tiers

Stdlib source itself needs a test pass: every `.mist` in `Source/Standard/` must parse and analyze without diagnostics at build time. Implement this as a `StdlibCompilesTest` in the unit test project that constructs the same stdlib unit list the driver does, runs it through `SemanticModelBuilder.Build`, and fails on any diagnostic of severity `Error`. This is a smoke test (single execution), not a property.

## Migration Plan

The migration from `PrimitiveType`/`StringType`/`ArrayType` singletons to stdlib types is staged to keep the compiler buildable at every step. Each step compiles and passes tests independently.

### Stage 1: Land the grammar and intrinsic plumbing

- Add `INTRINSIC`, `OPERATOR` tokens to `keywords.storm`.
- Extend `Modifier` and `MemberDeclaration` rules in `Mist.storm`.
- Regenerate lexer/parser.
- Add `IsIntrinsic` / `IsBackendBacked` fields to `Symbol` / `ClassType` / `StructType`.
- Add `KeywordAliases.cs`.

No behavior change yet — grammar accepts new forms but no stdlib exists to use them. Existing tests pass.

### Stage 2: Land operator declarations and overload resolution

- Implement `Pass1e_ValidateIntrinsicMarkers` and the new operator-declaration registration path.
- Implement `ResolveBinaryOperator` / `ResolveUnaryOperator` helpers in `TypeCheckerAnalyzer`.
- Introduce a **temporary flag** `TypeCheckerAnalyzer.UseOperatorOverloadResolution = false`. When `false`, the analyzer uses the old `NumericTypes` path. When `true`, it uses the new helpers.
- Set the flag `false` in production; `true` in a single new unit test that exercises user-defined `Vector3.operator+`.

Existing samples continue to compile (flag is off). The new user-defined-operator test demonstrates overload resolution works end-to-end.

### Stage 3: Stand up the stdlib files (without removing old singletons)

- Create `Source/Standard/Bootstrap/Int32.mist`, `Int64.mist`, ..., `String.mist`, `Array.mist`.
- Create `Source/Standard/Optional.mist`.
- Add `StdlibLoader` and wire it into `CompilerDriver`.
- Add the `IsStdlib` flag on `CompilationUnit`.
- Add `MistCompilesTest` that asserts stdlib source compiles standalone (no user program).

At this stage, stdlib types coexist with old singletons. The semantic model contains both `PrimitiveType.Int` and a separate `Standard.Int32` type. This is temporarily OK because nothing yet routes primitive keywords to stdlib.

### Stage 4: Flip primitive keyword resolution to stdlib

- Modify `SemanticModelBuilder.Pass2_ResolveTypes` to resolve `PrimitiveTypeRefTypeReferenceNode` via `KeywordAliases.Map` instead of returning `PrimitiveType.*`.
- Modify `StringType.Instance` references to go through `LookupStandardType("String")`.
- Add compat shim: `PrimitiveType.Int` becomes a static readonly property whose getter returns the stdlib `Standard.Int32` type looked up from a static semantic-model handle. Preserves reference-equality (Requirement 8.5, 15.3) during the transition. Any remaining direct reference to `PrimitiveType.Int` still works.
- Flip `TypeCheckerAnalyzer.UseOperatorOverloadResolution` to `true` and delete the old `NumericTypes`-based `Check*Node` paths.

At this point, the pipeline end-to-end uses stdlib types. Sample regression guard should pass. If any sample breaks, it is a real bug that gets fixed before proceeding.

### Stage 5: Migrate the C# backend

- Rewrite `CSharpTypeMap` into the Backing_Type_Map form.
- Add `IntrinsicEmissionTable` / `IIntrinsicEmissionRegistry`.
- Rewrite `EmitAdditive` / `EmitMultiplicative` / `EmitRelational` / `EmitEquality` / unary ops in `CSharpEmitter` to dispatch through the resolved-operator symbol.
- Rewrite `CSharpAmbientTypeResolver` to use the stdlib type provider and drop `BuildString` / `BuildArray`.

Samples run identically to baseline. Property-based tests 2–4 pass.

### Stage 6: Remove the shim and the ghost singletons

- Delete `PrimitiveType.cs`.
- Delete `StringType.cs`.
- Delete (or refactor) `ArrayType.cs` — this one may stay as a structural marker; see § 2.
- Delete any remaining references across the codebase (search: `PrimitiveType\.`, `StringType\.Instance`).

Build fails loudly on any missed reference. Fix the reference, not the singleton. Requirement 8.4 is now enforced by the compiler itself.

### Shim removal trigger

The shim in Stage 4 is removed in Stage 6, a single PR. It exists for ≤2 feature PRs. If Stage 5 or later discovers a pervasive remaining reference to `PrimitiveType.Int`, it is treated as a bug in the migration, not a reason to keep the shim.

## Open Questions (Requirement 15 follow-ups)

| # | Description | Resolution |
|---|---|---|
| 15.1 | Intrinsic marker syntax | **Resolved in requirements** — reserved `intrinsic` keyword, modifier-list placement. |
| 15.2 | Loading strategy A/B/C | **Resolved in this design — Approach B, source prelude.** See § *Stdlib Loading Strategy*. |
| 15.3 | Nominal identity during migration | **Resolved** — Stage 4 compat shim retains reference equality by routing `PrimitiveType.Int` to the stdlib type instance; shim removed in Stage 6. |
| 15.4 | Operator resolution across type boundaries | **Resolved** — left-operand precedence on ties, deterministic. See § 5. |
| 15.5 | Stdlib location at install time | **Resolved** — `AppContext.BaseDirectory/Standard/` via `<Content Include ... CopyToPublishDirectory />` in `Mist.CLI.csproj`. See § *Stdlib Loading Strategy*. |
| 15.6 | Pure-Mist tier contents for v1 | **Resolved** — ship one minimal demo, `Standard.Optional<T>`. See § *Pure-Mist Tier for v1*. |

### New open questions surfaced by this design (deferred to implementation)

- **Generic-over-T intrinsic emission.** `Standard.Array<T>.Length` has an intrinsic template `{0}.Length`. What about future members that depend on `T`'s backing type (none in v1 scope, but e.g., `Array<T>.Clear()` would want `Array.Clear({0})` without a T dependency)? Deferred — v1 intrinsics on `Array<T>` only need `Length`.
- **Diagnostic locality for `MIST0001`.** When an intrinsic is missing an emission template, the diagnostic is raised in the backend. Ideally it should include the source span of the call site, not just the declaration. The emitter has the call site's AST node; the existing `MakeDiagnostic` helper accepts a node. Deferred to implementation.
- **Pure-tier test surface.** `Optional<T>` in isolation doesn't exercise pure-tier-calls-bootstrap-intrinsics (the tricky direction). A small regression test that instantiates `Optional<Int32>`, stores a value, and reads it back would exercise the bootstrap-to-pure-to-bootstrap round trip. Deferred to the tasks phase.
