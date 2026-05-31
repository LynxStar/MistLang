# Parser Type Argument Backtracking Bugfix Design

## Overview

The Mist parser exhibits exponential backtracking when parsing `TypeArgumentList` nodes because the `TypeReference` rule tries `UnionTypeRef` as its first alternative. For every non-union type reference (the vast majority), the parser speculatively parses a full `SingleTypeRef` including nested `TypeArgumentList`s, fails to find a `PIPE` token, discards the entire parse, then re-parses the same content via later alternatives. This doubling compounds exponentially (~2.3x–3.5x per additional type argument).

The fix restructures the `TypeReference` grammar rule from a discriminated union with `UnionTypeRef` first to a single production that parses one `SingleTypeRef` then optionally consumes `PIPE`-separated continuations. This eliminates all speculative backtracking. The downstream AST node changes are mechanical — every consumer that pattern-matched on `TypeReferenceNode` variants must be updated to access the new structure.

### Update: Two Instances of the Same Defect (confirmed during implementation)

The original analysis correctly identified the `TypeReference` defect, but it was **incomplete**. The same "union-first ordered choice, then single" defect pattern exists in a **second** grammar rule, `RefinableTypeExpression`, which is the right-hand side of every type *alias* declaration. There are therefore **TWO** instances of the identical defect, and both require the same fix:

- **(a) `TypeReference`** (already fixed) — affects type references at usage sites (field types, return types, parameters, object creation, generic constraints, and the `TypeArgumentList`s nested within them).
- **(b) `RefinableTypeExpression`** (the type-alias RHS) — affects type alias declarations (`type Action<T1, T2> = Func<T1, T2, void>;`). A type alias RHS does NOT parse through `TypeReference`; it parses through `RefinableTypeExpression` via `TypeAliasDeclaration = TYPE name:IDENTIFIER typeParams:TypeParameterList? EQUAL type:RefinableTypeExpression ...`.

Crucially, the Func/Action arity-overload performance scenario is **dominated by (b)**, because that scenario is built from 16 type aliases (`type Action<...> = Func<...>;`). Each alias RHS is parsed by `RefinableTypeExpression`, which tries `RefinableUnionTypeExpr` first — fully parsing a `RefinableSingleTypeExpr → SingleTypeRef → the entire nested TypeArgumentList` (which recurses back through `TypeReference`), then failing to find a `PIPE` and backtracking to re-parse the same content as `RefinableSingleType`. This is a SECOND doubling factor that compounds with the recursion through `SingleTypeRef`'s `TypeArgument`.

Measurement during execution confirmed that fix (a) alone is insufficient: it removed one doubling factor (per-argument growth dropped from ~3.5x to a consistent ~2.2x) but the parse remained exponential (~2.2^N), not linear. Fix (b) is required to reach linear time. See the Hypothesized Root Cause section for the measured evidence.

**Union detection after the fix**: Storm generates the `first:X (PIPE rest:X)*` shape as three fields — `First` (the first constituent), `SingleTypeRefs` (an `ImmutableArray` of the additional PIPE-separated constituents), and a non-nullable `Rest` artifact field that holds the *last* matched continuation. The correct way to detect a union is therefore `SingleTypeRefs.Length > 0` (equivalently `!SingleTypeRefs.IsEmpty`). The `Rest` field is a non-nullable `default!` codegen artifact and MUST NOT be used as the union predicate. The existing `SemanticModelBuilder.ResolveUnionTypeRef`, `CSharpEmitter`, and `MistPrettyPrinter` already demonstrate the correct pattern: collect `First`, then iterate `SingleTypeRefs`; none of them read `.Rest`.

## Glossary

- **Bug_Condition (C)**: A `TypeReference` containing nested `TypeArgumentList`s where the parser speculatively parses `UnionTypeRef` first, fails to find `PIPE`, and backtracks — causing exponential time growth
- **Property (P)**: Parsing completes in linear time relative to type argument count, with identical semantic results
- **Preservation**: All existing type reference parsing (unions, primitives, named types, tuples, void, unknown, parenthesized) continues to produce correct AST nodes and semantic resolution
- **TypeReference**: The grammar rule in `Mist.storm` that matches type annotations in source code
- **UnionTypeRef**: The current first alternative in `TypeReference` that requires `SingleTypeRef (PIPE SingleTypeRef)+` — causes speculative parsing and backtracking on non-union types
- **SingleTypeRef**: The grammar rule matching a single non-union type. Its alternatives are primitive, void, unknown, named, parenthesized (`( TypeReference )` with optional array rank), and tuple. Note that `ParenthesizedTypeRef` is an alternative of `SingleTypeRef`, NOT of `TypeReference`.
- **TypeArgumentList**: `< TypeArgument (, TypeArgument)* >` — recursive since `TypeArgument` can contain `TypeReference`
- **RefinableTypeExpression**: The grammar rule in `Mist.storm` that matches the right-hand side of a type *alias* declaration (`type X = <RefinableTypeExpression>;`). It has the SAME defect as `TypeReference`: it tries `RefinableUnionTypeExpr` first, then falls back to `RefinableSingleType`. This is the SECOND instance of the defect and the dominant cost in the Func/Action arity-overload scenario.
- **RefinableUnionTypeExpr**: The current first alternative of `RefinableTypeExpression`, defined as `first:RefinableSingleTypeExpr (PIPE rest:RefinableSingleTypeExpr)+`. Like `UnionTypeRef`, it forces a speculative full parse (through `SingleTypeRef → TypeArgumentList → TypeReference`) before checking for `PIPE`, causing backtracking on every non-union alias RHS.
- **RefinableSingleTypeExpr**: `type:SingleTypeRef refinement:RefinementPredicate?` — a single (optionally refined) type. Because it embeds `SingleTypeRef`, it recurses through `TypeReference` for any nested `TypeArgumentList`, which is why the `RefinableTypeExpression` defect compounds.
- **Storm source generator**: The tool that reads `Mist.storm` and generates C# AST node classes

## Bug Details

### Bug Condition

The bug manifests when parsing any `TypeReference` that is NOT a union type (i.e., has no `PIPE` token). The `TypeReference` rule tries `UnionTypeRef` first, which requires parsing a full `SingleTypeRef` (including any nested `TypeArgumentList`s). When no `PIPE` follows, the entire speculative parse is discarded and the same content is re-parsed by subsequent alternatives. Since `TypeArgument` recursively contains `TypeReference`, this doubling compounds exponentially with nesting depth.

**Key distinction**: Type *parameter* declarations (`TypeParameterList` = `<T1, T2, ...>`) are NOT affected because they use `IDENTIFIER` tokens, not `TypeReference`. The exponential growth occurs only in type *argument* usage sites (`TypeArgumentList` = `<int, string, Foo, ...>`) where each element is parsed as `TypeArgument → TypeReference`. This means the bug affects ANY location where a generic type is closed with concrete type arguments:
- Type alias RHS: `type Action<T1, T2> = Func<T1, T2, void>;`
- Field types: `Func<int, string, bool> field;`
- Method return types: `Func<int, string, bool> GetHandler() { ... }`
- Method parameters: `void Process(Func<int, string, bool> handler) { ... }`
- Object creation: `new Func<int, string, bool>(...)`
- Generic constraints: `where T : IComparable<Foo<int, string, bool>>`

The type alias scenario makes the bug most visible because defining 16 arity-overloaded aliases in sequence accumulates the exponential cost across all of them in a single parse.

**Current grammar structure** (the source of the redundancy): `TypeReference` has six alternatives — `UnionTypeRef` first, followed by `PrimitiveTypeRef`, `VoidTypeRef`, `UnknownTypeRef`, `NamedTypeRef`, and `TupleTypeRef`. Those five non-union alternatives are DUPLICATES of the corresponding `SingleTypeRef` alternatives (`PrimitiveSingleTypeRef`, `VoidSingleTypeRef`, `UnknownSingleTypeRef`, `NamedSingleTypeRef`, `TupleSingleTypeRef`). `SingleTypeRef` additionally contains a `ParenthesizedTypeRef` alternative (`( TypeReference )` with optional array rank) that `TypeReference` does NOT have directly. Because `UnionTypeRef = first:SingleTypeRef (PIPE rest:SingleTypeRef)+` is tried first, every non-union reference is fully parsed as a `SingleTypeRef`, fails the mandatory `PIPE`, and is then re-parsed by the duplicated alternatives. The fix collapses `TypeReference` to delegate entirely to `SingleTypeRef`, removing both the duplication and the speculative backtracking.

**Formal Specification:**
```
FUNCTION isBugCondition(input)
  INPUT: input of type MistSourceFile
  OUTPUT: boolean
  
  RETURN input contains TypeReference T
         AND T is NOT a union type (no PIPE token follows the first SingleTypeRef)
         AND T contains nested TypeArgumentList with N type arguments
         AND N >= 5 (exponential growth becomes measurable)
END FUNCTION
```

### Examples

- `Func<T1, T2, T3, T4, T5, TResult>` — parser takes ~0.5s at N=5, ~33s at N=11, estimated ~5 hours at N=16
- `type Action<T1, T2, T3> = Func<T1, T2, T3, void>;` — each type alias compounds the backtracking
- `List<int>` — even simple generic types pay the cost of one failed `UnionTypeRef` attempt, though it's only noticeable with nesting
- `int | string` — NOT a bug condition (union types parse correctly on the first attempt)

## Expected Behavior

### Preservation Requirements

**Unchanged Behaviors:**
- Union type references (`int | string`, `Foo | Bar | Baz`) must continue to produce correct AST nodes with all constituents in source order
- Simple named type references (`List<int>`, `Map<string, int>`) must continue to resolve correctly with type arguments
- Primitive type references (`int`, `string[]`, `bool?`) must continue to produce correct nodes with array rank and optional suffixes
- Void and unknown type references must continue to produce correct nodes
- Tuple type references (`(int, string)`, `(int x, string y)`) must continue to produce correct nodes
- Parenthesized type references (`(int | string)[]`) must continue to produce correct nodes with inner type and array rank
- The semantic model builder must continue to resolve all type references to the same `IType` instances
- The C# emitter must continue to produce identical C# output for all type references
- The pretty printer must continue to produce identical Mist source output

**Scope:**
All inputs that do NOT involve the grammar rule structure change should be completely unaffected by this fix. The fix changes HOW the parser arrives at the AST, not WHAT AST it produces (semantically). The AST node class names change, but the information content is preserved.

## Hypothesized Root Cause

> **Status: CONFIRMED during implementation.** The hypothesis below is confirmed by grammar inspection and performance measurement. (Section header retained as "Hypothesized Root Cause" for spec-format compatibility.)

Based on the grammar structure and performance measurements, the root cause is confirmed. There are **two instances of the same defect** (see Overview); the analysis below applies identically to both `TypeReference` and `RefinableTypeExpression`.

1. **Ordered choice with expensive first alternative**: The `TypeReference` rule uses `UnionTypeRef` as its first alternative. Storm's PEG-style ordered choice means the parser must fully attempt `UnionTypeRef` before trying other alternatives. The `RefinableTypeExpression` rule has the same shape, trying `RefinableUnionTypeExpr` first.

2. **UnionTypeRef requires full SingleTypeRef parse**: `UnionTypeRef = first:SingleTypeRef (PIPE rest:SingleTypeRef)+` — the parser must parse a complete `SingleTypeRef` (including nested `TypeArgumentList`s with recursive `TypeReference` contents) before it can check for `PIPE`. Likewise `RefinableUnionTypeExpr = first:RefinableSingleTypeExpr (PIPE rest:RefinableSingleTypeExpr)+` parses a complete `RefinableSingleTypeExpr → SingleTypeRef → TypeArgumentList` before checking for `PIPE`.

3. **Exponential compounding through recursion**: `TypeArgument` contains `TypeReference`, so each type argument in a `TypeArgumentList` triggers its own `UnionTypeRef` attempt. With N type arguments, the parser does ~2^N redundant work because each level doubles the parse effort. The `RefinableTypeExpression` defect compounds on top of this because `RefinableSingleTypeExpr` embeds `SingleTypeRef`, whose `TypeArgument`s recurse back through `TypeReference`.

4. **No memoization in Storm's generated parser**: The Storm parser generator does not implement packrat parsing (memoization), so failed speculative parses cannot be reused.

### Second Instance: RefinableTypeExpression (the type-alias RHS)

The original analysis found instance (a) — `TypeReference` — but **missed instance (b)** — `RefinableTypeExpression` — and the Func/Action performance scenario is dominated by (b). A type alias right-hand side does NOT parse through `TypeReference`. It parses through `RefinableTypeExpression`:

```
rule TypeAliasDeclaration = TYPE name:IDENTIFIER typeParams:TypeParameterList? EQUAL type:RefinableTypeExpression
    constraints:WhereClause*
    SEMICOLON;

rule RefinableTypeExpression
    = unionType:RefinableUnionTypeExpr   as RefinableUnionType    // tried FIRST — speculative
    | singleType:RefinableSingleTypeExpr as RefinableSingleType
    ;
rule RefinableUnionTypeExpr = first:RefinableSingleTypeExpr (PIPE rest:RefinableSingleTypeExpr)+;
rule RefinableSingleTypeExpr = type:SingleTypeRef refinement:RefinementPredicate?;
```

For every alias RHS, the parser tries `RefinableUnionTypeExpr` first, which fully parses a `RefinableSingleTypeExpr → SingleTypeRef → the entire nested TypeArgumentList` (which recurses back through `TypeReference`), then fails to find a `PIPE` and backtracks to re-parse the same content as `RefinableSingleType`. This is the SECOND doubling factor, and because `SingleTypeRef`'s `TypeArgument` recurses through `TypeReference`, it compounds — producing the measured ~2.2x growth that remained after fix (a).

A grep of the grammar confirmed that `RefinableTypeExpression` is the **ONLY** other instance of this "union-first ordered choice then single" defect pattern; there are no others.

### Measured Evidence (confirms (a) alone is insufficient; (b) is required)

| Phase | Bail-out point | Per-type-argument growth ratio |
|-------|----------------|--------------------------------|
| Before any fix | bailed at N=10–11 | ~3.0x–4.0x |
| After the `TypeReference` fix (a) only | bails at N=15 | consistent ~2.2x (still exponential) |

Per-N measurements after fix (a) only (still exponential): N=3 → 0.85ms, N=10 → 251ms, N=13 → 3225ms, N=14 → 7424ms, N=15 → 16083ms — consecutive ratios all ~2.1x–2.6x.

Interpretation: the `TypeReference` fix removed ONE doubling factor (3.5x → 2.2x) but the parse is STILL exponential (~2.2^N), not linear. The remaining exponential cost is the `RefinableTypeExpression` defect (b), which dominates the Func/Action scenario because that scenario is built from 16 type aliases. Linear time is reached only after BOTH grammar fixes are applied.

## Correctness Properties

Property 1: Bug Condition - Linear Parsing Time

_For any_ source file containing type references with N type arguments (where N ranges from 0 to 20), the parser SHALL complete parsing in time proportional to N (linear), not exponential in N. Specifically, parsing N=0 through N=16 arity-overloaded Func/Action declarations SHALL complete in under 10 seconds total.

> **Note (confirmed during implementation):** This property requires BOTH grammar fixes — fix (a) on `TypeReference` AND fix (b) on `RefinableTypeExpression`. Fix (a) alone reduces per-argument growth from ~3.5x to ~2.2x but leaves the parse exponential, so the oracle test below still fails. The same performance test (`FuncActionArityOverloads_ShouldNotGrowExponentially`) is the oracle and reaches linear / under-10s only after BOTH grammar fixes are applied. The property itself is unchanged.

**Validates: Requirements 2.1, 2.2, 2.3**

Property 2: Preservation - Semantic Equivalence

_For any_ valid Mist source file that parses successfully before the fix, the fixed parser SHALL produce an AST that resolves to semantically identical types, emits identical C# output, and passes all existing tests (accounting for mechanical node type name changes in test assertions).

**Validates: Requirements 3.1, 3.2, 3.3, 3.4, 3.5, 3.6, 3.7, 3.8, 3.9, 3.10**

## Fix Implementation

### Changes Required

Assuming our root cause analysis is correct (confirmed by the skipped performance test):

**File**: `Source/Mist/Grammar/Mist.storm`

**Rule**: `TypeReference`

**Specific Changes**:

> **Implementation note (do this FIRST):** Immediately after changing the grammar rule, rebuild the project and read the regenerated `Mist.Ast.g.cs` to confirm the EXACT generated node names and field shape before touching any consumer. Prior attempts failed by assuming the generated shape. Storm may produce one of two shapes for `first:SingleTypeRef (PIPE rest:SingleTypeRef)*`:
> - a flat `TypeReferenceNode` exposing `First` / `SingleTypeRefs` directly, OR
> - a wrapper that keeps the current `UnionTypeRefTypeReferenceNode` around a `UnionTypeRefNode`.
>
> Confirm which one is generated, and confirm the field names, before adapting consumers. In all cases the union predicate is `SingleTypeRefs.Length > 0` (equivalently `!SingleTypeRefs.IsEmpty`) — never the non-nullable `Rest` artifact field.

1. **Replace discriminated TypeReference rule with single production**:
   - Old: 6-alternative discriminated union with `UnionTypeRef` first
   - New: `rule TypeReference = first:SingleTypeRef (PIPE rest:SingleTypeRef)*;`
   - This parses one `SingleTypeRef`, then optionally consumes zero or more `PIPE`-separated continuations — no backtracking. It collapses `TypeReference` to delegate entirely to `SingleTypeRef`, eliminating the five duplicated non-union alternatives.

2. **Remove the UnionTypeRef rule entirely**:
   - Old: `rule UnionTypeRef = first:SingleTypeRef (PIPE rest:SingleTypeRef)+;`
   - New: deleted (union detection is now implicit in `TypeReference` when `SingleTypeRefs` is non-empty)

3. **ParenthesizedTypeRef stays in SingleTypeRef**:
   - `ParenthesizedTypeRef` is and remains an alternative of `SingleTypeRef` (`( TypeReference )` with optional array rank), not of `TypeReference`. Its `inner:TypeReference` field continues to work because `TypeReference` still exists as a rule — it just delegates to `SingleTypeRef` now.

**File**: Multiple downstream C# files (7 source files total, excluding generated `obj/` files)

**Pattern**: Every place that pattern-matches on `TypeReferenceNode` variants

4. **Generated AST node structure change**:
   - Old `TypeReferenceNode`: discriminated union with variants (`PrimitiveTypeRefTypeReferenceNode`, `VoidTypeRefTypeReferenceNode`, `UnknownTypeRefTypeReferenceNode`, `NamedTypeRefTypeReferenceNode`, `TupleTypeRefTypeReferenceNode`, `UnionTypeRefTypeReferenceNode`)
   - New `TypeReferenceNode` (the `first:X (PIPE rest:X)*` shape) exposes:
     - `First: SingleTypeRefNode` — the first (and, for non-unions, only) constituent
     - `SingleTypeRefs: ImmutableArray<SingleTypeRefNode>` — the additional PIPE-separated constituents; this is the actual list and its non-emptiness is the union signal
     - `Rest: SingleTypeRefNode` — a NON-nullable `default!` codegen artifact holding the last matched continuation. It is NOT a nullable second constituent and MUST NOT be used to detect a union.
   - **Union predicate:** `typeRef.SingleTypeRefs.Length > 0` (equivalently `!typeRef.SingleTypeRefs.IsEmpty`). The existing `ResolveUnionTypeRef`, `CSharpEmitter`, and `MistPrettyPrinter` already follow this pattern (collect `First`, iterate `SingleTypeRefs`, never read `.Rest`). Confirm the exact generated names per the implementation note above before wiring this up.

5. **SemanticModelBuilder.ResolveTypeReference** (`Source/Mist/Semantics/Model/SemanticModelBuilder.cs` — 68 references, largest consumer):
   - Replace the large `switch (typeRef)` with:
     ```csharp
     if (typeRef.SingleTypeRefs.Length > 0)
         return ResolveUnionTypeRef(typeRef, ...);
     return ResolveSingleTypeRef(typeRef.First, ...);
     ```
   - The existing `ResolveSingleTypeRef` method already handles all `SingleTypeRefNode` variants correctly

6. **SemanticModelBuilder.ResolveUnionTypeRef**:
   - Change parameter from `UnionTypeRefTypeReferenceNode unionRef` to `TypeReferenceNode typeRef` (or keep the wrapper type if the generated shape retains it — confirm via the regenerated AST)
   - Collect constituents from `typeRef.First` then iterate `typeRef.SingleTypeRefs` (do not read `.Rest`). This mirrors the existing implementation, which already collects `First` and iterates `SingleTypeRefs`.

7. **CSharpSourceWriter.EmitTypeReference** (`Source/Mist/Backends/CSharpTranspiler/CSharpEmitter.cs` — 50 references):
   - Replace the `switch (typeRef)` with a union check (`typeRef.SingleTypeRefs.Length > 0`), then delegate to `EmitSingleTypeRef`
   - Union case: when `SingleTypeRefs` is non-empty, emit the union helper type or nullable

8. **CSharpSourceWriter.EmitTypeReferenceWithSubstitution**:
   - Update pattern matching from `typeRef is NamedTypeRefTypeReferenceNode` to `typeRef.First is NamedSingleTypeRefSingleTypeRefNode`

9. **CSharpSourceWriter.GetExtensionReceiverTypeName** and **GetTypeArgName**:
   - Change from matching `TypeReferenceNode` variants to matching `typeRef.First` variants

10. **TypeCheckerAnalyzer** (`Source/Mist/Analysis/Builtin/TypeCheckerAnalyzer.cs` — 17 references, third-largest consumer):
    - This file uses both `switch` statements and `switch` expressions on `TypeReference` variants, plus `is`-pattern checks (e.g. `methodDecl.ReturnType is not VoidTypeRefTypeReferenceNode`, `typeRef is PrimitiveTypeRefTypeReferenceNode`).
    - Migrate `is`/`switch` checks against `TypeReferenceNode` variants to operate on `typeRef.First` (the `SingleTypeRefNode` variants), e.g. `methodDecl.ReturnType.First is not VoidSingleTypeRefSingleTypeRefNode` and `typeRef.First is PrimitiveSingleTypeRefSingleTypeRefNode`.
    - Where a check distinguished a union, use `typeRef.SingleTypeRefs.Length > 0`.

11. **EntryPointAnalyzer.IsStringArrayTypeRef** (`Source/Mist/Analysis/Builtin/EntryPointAnalyzer.cs` — 3 references):
    - Change from `typeRef is PrimitiveTypeRefTypeReferenceNode primitiveRef` to `typeRef.First is PrimitiveSingleTypeRefSingleTypeRefNode primitiveRef`

12. **ExhaustivenessChecker** (`Source/Mist/Analysis/Builtin/ExhaustivenessChecker.cs` — 2 references):
    - Uses `switch` expressions and `is` checks on Primitive/Void/Named variants. Migrate these to match against `typeRef.First` (the corresponding `SingleTypeRefNode` variants).

13. **CSharpTranspilerBackend** (`Source/Mist/Backends/CSharpTranspiler/CSharpTranspilerBackend.cs` — 2 references):
    - Uses `switch` expressions and `is` checks on Primitive/Void/Named variants. Migrate these to match against `typeRef.First`.

14. **MistPrettyPrinter.EmitTypeReference** (`Source/Mist/Parsing/MistPrettyPrinter.cs` — 6 references):
    - Replace the `switch (typeRef)` with a union check (`typeRef.SingleTypeRefs.Length > 0`), then delegate to `EmitSingleTypeRef`

15. **Test files (181 files)**:
    - This is a purely mechanical node-name substitution across 181 test files. Perform it as a SCRIPTED substitution (regex / find-replace) batched by directory, with a build-verify checkpoint after EACH batch. See the Testing Strategy section for the batched-migration procedure.
    - Mechanical migration of AST node construction:
      - `new PrimitiveTypeRefTypeReferenceNode { ... }` → `new TypeReferenceNode { First = new PrimitiveSingleTypeRefSingleTypeRefNode { ... } }`
      - `new VoidTypeRefTypeReferenceNode()` → `new TypeReferenceNode { First = new VoidSingleTypeRefSingleTypeRefNode() }`
      - `new NamedTypeRefTypeReferenceNode { Name = ..., TypeArgs = ... }` → `new TypeReferenceNode { First = new NamedSingleTypeRefSingleTypeRefNode { Name = ..., TypeArgs = ... } }`
    - Mechanical migration of assertions:
      - `Assert.IsType<UnionTypeRefTypeReferenceNode>(typeRef)` → `Assert.NotEmpty(typeRef.SingleTypeRefs)`
      - `Assert.IsType<PrimitiveTypeRefTypeReferenceNode>(typeRef)` → `Assert.IsType<PrimitiveSingleTypeRefSingleTypeRefNode>(typeRef.First)`

16. **Un-skip the performance test**:
    - Remove `Skip = "Known exponential parsing issue — documents the problem for future fix"` from `FuncActionArityOverloads_ShouldNotGrowExponentially`

### Second Fix: RefinableTypeExpression (the type-alias RHS) — REQUIRED for linear time

This fix mirrors the `TypeReference` fix exactly and is the dominant cost in the Func/Action arity-overload scenario (16 type aliases). It MUST be applied in addition to the `TypeReference` fix; fix (a) alone leaves the parse exponential (see Hypothesized Root Cause → Measured Evidence).

**File**: `Source/Mist/Grammar/Mist.storm`

**Rule**: `RefinableTypeExpression`

> **Implementation note (do this FIRST, same lesson as task 3.2):** Immediately after changing the grammar rule, rebuild the project and read the regenerated AST to confirm the EXACT generated node name and field shape before touching any consumer. This change regenerates `RefinableTypeExpressionNode` as a FLAT node (exactly as `TypeReferenceNode` became a flat node). Do NOT assume the generated field names — confirm them from the regenerated AST. In all cases the union predicate is the continuation collection's `.Length > 0` (equivalently `!IsEmpty`) — NEVER the non-nullable `Rest` codegen artifact.

17. **Replace discriminated RefinableTypeExpression rule with single production**:
    - Old:
      ```
      rule RefinableTypeExpression
          = unionType:RefinableUnionTypeExpr   as RefinableUnionType
          | singleType:RefinableSingleTypeExpr as RefinableSingleType
          ;
      ```
    - New: `rule RefinableTypeExpression = first:RefinableSingleTypeExpr (PIPE rest:RefinableSingleTypeExpr)*;`
    - This parses one `RefinableSingleTypeExpr`, then optionally consumes zero or more `PIPE`-separated continuations — no backtracking. Union detection becomes implicit: a union when the PIPE-separated continuation collection is non-empty.

18. **Remove the RefinableUnionTypeExpr rule entirely**:
    - Old: `rule RefinableUnionTypeExpr = first:RefinableSingleTypeExpr (PIPE rest:RefinableSingleTypeExpr)+;`
    - New: deleted (union detection is now implicit in `RefinableTypeExpression` when the continuation collection is non-empty)
    - `RefinableSingleTypeExpr = type:SingleTypeRef refinement:RefinementPredicate?;` stays unchanged.

19. **Generated AST node structure change (flat node)**:
    - Old `RefinableTypeExpressionNode`: discriminated union with variants `RefinableUnionTypeRefinableTypeExpressionNode` (exposing `UnionType.First` + `UnionType.RefinableSingleTypeExprs`) and `RefinableSingleTypeRefinableTypeExpressionNode` (exposing `SingleType`).
    - New `RefinableTypeExpressionNode` (the `first:X (PIPE rest:X)*` shape) becomes a FLAT node exposing:
      - `First: RefinableSingleTypeExprNode` — the first (and, for non-unions, only) constituent
      - a continuation collection of the additional PIPE-separated `RefinableSingleTypeExprNode`s (an `ImmutableArray`); its non-emptiness is the union signal
      - a non-nullable `Rest` `default!` codegen artifact that MUST NOT be used to detect a union
    - **Union predicate:** the continuation collection's `.Length > 0` (equivalently `!IsEmpty`). **Confirm the exact generated field name for the continuation collection from the regenerated AST** before wiring this up — same lesson as task 3.2; do not assume it matches `TypeReferenceNode`'s field name.

20. **SemanticModelBuilder** (`Source/Mist/Semantics/Model/SemanticModelBuilder.cs` — 13 references):
    - Migrate the three methods that switch on `RefinableTypeExpressionNode` variants — `ResolveRefinableTypeExpression`, `HasRefinementPredicate`, and `ExtractRefinableTypeExpressionName` — plus the `ResolveTypeAlias` call site.
    - Replace each `switch (refinableExpr)` over `RefinableSingleTypeRefinableTypeExpressionNode` / `RefinableUnionTypeRefinableTypeExpressionNode` with a union check on the flat node's continuation-collection-non-empty predicate.
    - Read constituents via `First` then iterate the continuation collection (mirrors the old `union.UnionType.First` + `union.UnionType.RefinableSingleTypeExprs` pattern). For the non-union case, operate on `First` directly (equivalent to the old `single.SingleType`).
    - `HasRefinementPredicate`: check `First.Refinement` then iterate the continuation collection checking each `.Refinement`.
    - `ExtractRefinableTypeExpressionName`: read the name from `First.Type`.

21. **MistPrettyPrinter** (`Source/Mist/Parsing/MistPrettyPrinter.cs` — 5 references):
    - Replace the `node switch` in `EmitRefinableTypeExpression` with a union check (continuation collection non-empty), then delegate to the single-type emission.
    - Fold `EmitRefinableUnion` logic into the flat-node path: emit `First`, then iterate the continuation collection. The existing `EmitRefinableSingle(RefinableSingleTypeExprNode)` helper is reused unchanged.

22. **Test migration** (`Test/Mist.Tests/Semantics/TypeAliasRegistrationPropertyTests.cs` — 3 references):
    - Migrate the `MakeRefinableTypeExpr` helper that constructs `new RefinableSingleTypeRefinableTypeExpressionNode { SingleType = new RefinableSingleTypeExprNode { ... } }` to construct the flat `new RefinableTypeExpressionNode { First = new RefinableSingleTypeExprNode { ... } }` shape (confirm the exact field name from the regenerated AST).

**Consumer migration scope for the second fix** (verified by grep, EXCLUDING generated `obj/`): 2 source files + 1 test file — `SemanticModelBuilder.cs` (13 references), `MistPrettyPrinter.cs` (5 references), and `TypeAliasRegistrationPropertyTests.cs` (3 references).

## Testing Strategy

### Validation Approach

The testing strategy follows a two-phase approach: first, confirm the exponential behavior exists on unfixed code (already documented by the skipped test), then verify the fix eliminates it while preserving all existing behavior.

### Exploratory Bug Condition Checking

**Goal**: Surface counterexamples that demonstrate the bug BEFORE implementing the fix. Confirm the root cause analysis.

**Test Plan**: The existing skipped test `FuncActionArityOverloads_ShouldNotGrowExponentially` already demonstrates the bug. Un-skipping it on unfixed code would show exponential growth and timeout. Note (confirmed during execution): this test remains the oracle for BOTH defect instances — after fixing only `TypeReference` (a) it still fails with consistent ~2.2x growth (bails at N=15). It reaches linear / under-10s only after the `RefinableTypeExpression` fix (b) is ALSO applied, because the Func/Action scenario is built from 16 type aliases whose RHS parse through `RefinableTypeExpression`.

**Test Cases**:
1. **Arity Overload Test**: Parse Func<T1..Tn, TResult> + Action<T1..Tn> for N=0..16 (will timeout on unfixed code at ~N=11)
2. **Nested TypeArgumentList Test**: Parse `Foo<Bar<Baz<int>>>` with increasing nesting depth (will show exponential growth)
3. **Simple Generic Test**: Parse `List<int>` — baseline showing even simple cases pay the speculative cost (fast but measurable)

**Expected Counterexamples**:
- Parser exceeds 10 second timeout at N=11 for Func/Action arity overloads
- Growth ratio between consecutive N values is ~2.3x–3.5x (exponential)

### Fix Checking

**Goal**: Verify that for all inputs where the bug condition holds, the fixed parser completes in linear time.

> **Confirmed during execution:** The oracle test passes (linear / under-10s) only after BOTH grammar fixes — (a) `TypeReference` AND (b) `RefinableTypeExpression`. With fix (a) alone the test still fails: measured per-N growth stays at a consistent ~2.2x (N=13 → 3225ms, N=14 → 7424ms, N=15 → 16083ms) and the run bails at N=15. The `growthRatio < 2.0` assertion below therefore only holds once fix (b) removes the second doubling factor.

**Pseudocode:**
```
FOR ALL N in [0..20] DO
  source := GenerateFuncActionSource(N)
  time := MeasureParseTime(source)
  ASSERT time < 10_000ms
  ASSERT growthRatio(time, previousTime) < 2.0  // linear, not exponential
END FOR
```

### Preservation Checking

**Goal**: Verify that for all inputs where the bug condition does NOT hold (and all inputs in general), the fixed parser produces semantically equivalent results.

**Pseudocode:**
```
FOR ALL source IN existingTestSuite DO
  ASSERT parse(source) produces correct AST
  ASSERT semanticModel(source) resolves same types
  ASSERT emit(source) produces same C# output
END FOR
```

**Testing Approach**: The full test suite (2979 tests across 181 test files that reference these node types) serves as the preservation oracle. Each test constructs AST nodes or parses source and verifies specific behaviors. After the mechanical migration of node type names, all tests must pass with identical semantics. Note that "2979" is the TOTAL number of tests in the suite — it is the preservation oracle, NOT the count of files being migrated. The migration touches 7 source files and 181 test files.

**Test Plan**: Run the full test suite after the fix. Additionally, compile and run the AllFeatures sample to verify end-to-end correctness.

#### Scripted, Batched Test Migration (chosen approach)

The 181-file test migration MUST be performed as a SCRIPTED substitution (regex / find-replace), batched by directory, with a build-verify checkpoint after EACH batch. This is purely mechanical node-name substitution.

- Process batches by directory area: Parsing, Semantics, Backends, Analysis, and any remaining areas.
- After each batch, the build MUST be green before moving to the next batch. Do not advance on a red build.
- Run the relevant subset of tests at each checkpoint where practical, and the full suite at the end.

**Why batched with checkpoints:** Prior ad-hoc attempts failed because the migration was treated as a single monolithic pass with no intermediate checkpoints — a single mistake cascaded across all 181 files and made the build impossible to bisect. Batching by directory with a green-build checkpoint after each batch keeps every failure localized and recoverable.

**Test Cases**:
1. **Union Type Parsing**: Verify `int | string` still produces correct union AST with 2 constituents (`UnionTypeRefParsingTests`)
2. **Named Type Parsing**: Verify `List<int>` still produces correct named type with type arguments
3. **Primitive Type Parsing**: Verify `int[]`, `bool?` still produce correct nodes with suffixes
4. **Tuple Type Parsing**: Verify `(int, string)` still produces correct tuple nodes
5. **Parenthesized Type Parsing**: Verify `(int | string)[]` still produces correct parenthesized union with array rank
6. **Optional Suffix Parsing**: Verify `int?`, `Foo?` still produce correct optional types (`OptionalTypeSuffixPropertyTests`)
7. **Semantic Resolution**: Verify all type references resolve to same `IType` instances
8. **C# Emission**: Verify emitted C# is byte-for-byte identical for all samples
9. **AllFeatures Sample**: Compile and run to verify end-to-end correctness
10. **Baseline Output**: Verify baseline output matches expected

### Unit Tests

- Un-skip `FuncActionArityOverloads_ShouldNotGrowExponentially` — must pass after fix
- `TypeParameterParsing_ShouldNotGrowExponentially` — must continue to pass
- All `UnionTypeRefParsingTests` — migrated to new node structure
- All `OptionalTypeSuffixPropertyTests` — migrated to new node structure
- All semantic model builder tests — migrated to new node construction

### Property-Based Tests

- Existing property-based tests (`InterfaceRoundTripPropertyTests`, `NameResolutionPropertyTests`, `ParameterScopingPropertyTests`, etc.) serve as preservation tests after mechanical migration
- The performance test itself is a form of property test — it asserts a property (linear time) holds for all N in [0..20]

### Integration Tests

- AllFeatures sample compilation and execution
- Baseline output comparison
- Full test suite pass (2979 tests — the preservation oracle, across the 181 affected test files)

## Confirmed Root Cause (Measured) and Fix-Direction Analysis

> **Addendum (recorded diagnosis).** This section records findings from a measurement spike that instrumented the Storm-generated parser with per-`(rule, position)` entry counters and captured the call chain at the redundant re-entry. It is additive: all prior sections, requirements mappings, and Correctness Properties remain unchanged. The two grammar fixes documented above (`TypeReference` and `RefinableTypeExpression` flattening) are committed; this section explains why they were insufficient and where the actual fix must live.

### 1. The defect is in the Storm-generated parser's backtracking, not in the Mist grammar's union rules

The two grammar fixes already applied removed constant-factor work but did NOT make parsing linear. After both fixes, the Func/Action arity perf test still fails: parsing remains exponential at ~2.2x per type argument (bails at N=16). The root cause is that the generated recursive-descent parser re-parses the same `(rule, position)` repeatedly with no memoization. The grammar union-first ordered choices were a contributing constant factor, not the exponential driver.

### 2. Measured proof of the exponential

Per-`(rule, position)` entry counters showed, for the WIDTH case `Func<T1..Tn, void>` (flat arity), that a single `(rule, position)` pair — e.g. `TypeArgument@<pos of void>` — is entered exactly `2^(N-1)` times:

| N | Entries of the `void`-position `TypeArgument` |
|---|-----------------------------------------------|
| 4 | 16 |
| 5 | 32 |
| 6 | 64 |
| 7 | 128 |
| 8 | 256 |

Each earlier argument position is entered half as often. The `TypeArgument` invocation count follows `T(k) = 1 + 2·T(k-1)` (the `2^k - 1` sequence): the tail of the argument list is re-parsed twice per element, compounding exponentially through recursion. By contrast:

- **NESTING** (`Func<Func<...int...>>`) is LINEAR in depth.
- **Primitive args** (`Func<int..int, void>`) are FLAT (`TypeArgumentList` constant at 6 entries) because primitives match `TypeArgument`'s early literal alternatives without descending into `QualifiedIdentifier` / `SingleTypeRef` / `TypeArgumentList` recursion.

So the exponential is specific to the WIDTH of a comma-separated `TypeArgumentList` whose elements are NAMED type references.

### 3. The exact construct (captured call chain)

A stack trace captured at the redundant re-entry showed this repeating cycle:

```
_ParseTypeArgument  (TypeReferenceTypeArgument alternative)
 → _ParseTypeReference
   → _ParseSingleTypeRef  (NamedSingleTypeRef alt, optional TypeArgumentList?)
     → _ParseTypeArgumentList  ((COMMA TypeArgument)* star loop)
       → _ParseTypeArgument   ← re-enters the SAME position; cycle repeats
```

The responsible emitter construct is the interaction of:

- the `ZeroOrMoreIR` star-loop emission (the `(COMMA TypeArgument)*` `while (!_hasError)` loop in `ParserEmitter`),
- the optional `TypeArgumentList?` in `NamedSingleTypeRef`, and
- the ordered-choice `_pos = saved` restores in `_ParseSingleTypeRef`.

Each comma-separated element is re-descended from scratch when the enclosing speculative context retries; no position result is cached.

### 4. Two fix paths and their tradeoffs

**Path A — Packrat memoization** (cache `(rule, position) → result`):

- Mechanism-agnostic: fixes this and any other backtracking pathology in any Storm grammar.
- Collapses the `2^N` re-entries to one parse + O(1) cache hits → linear.
- Costs: a permanent per-rule-invocation constant cost (NOT per-token) and memory O(distinct `(rule, position)` visited).
- Correctness subtlety: the generated parser mutates shared `[ThreadStatic]` state (`_pos`, `_hasError`, `_diagnostics`); memoization MUST capture/replay it and MUST NOT double-emit diagnostics on a cache hit.

**Path B — Fix the `(X)*` / ordered-choice emission so tails aren't re-parsed**:

- No memory overhead, genuinely linear work, smaller surface in principle.
- BUT the captured call chain shows the re-entry is driven by BOTH the star loop AND the ordered-choice `_pos = saved` restores in `_ParseSingleTypeRef`, so Path B is not a one-line star-loop tweak — it likely requires changing how committed sub-parses survive ordered-choice retries, raising its complexity toward Path A's.
- Risk: changing speculative-parse emission can change which alternative wins or which diagnostics are produced for ambiguous input (the 2989-test suite is the preservation oracle).

### 5. Cost measurement (realistic files) and the selective-memoization heuristic

Measured on realistic samples (AllFeatures, Traits, RefinementTypes), rule-method entries per token `M ≈ 11.6–13.7` (much higher than a naive estimate), so UNIVERSAL packrat would impose a real constant cost. Therefore memoization should be SELECTIVE.

Key empirical finding: in NORMAL code the re-entry `maxCount` is mostly 2–8 (bounded / non-exponential on realistic input — the `2^N` only appears in the pathological arity case). Which rules are re-entered at the same position:

- **Dominant** — the EXPRESSION PRECEDENCE LADDER: `Expression`, `Assignment`, `VoidCoalescing`, `LogicalOr`, `LogicalAnd`, `Equality`, `Relational`, `Additive`, `Multiplicative`, `Unary`, `Postfix`, `Primary`, `PostfixSuffix` (structural to precedence-climbing PEG).
- **Secondary** — the TYPE-REFERENCE cluster: `TypeReference`, `SingleTypeRef`, `TypeArgumentList`, `QualifiedIdentifier`, `PrimitiveType`. This cluster is the source of the EXPONENTIAL pathology even though it is only secondary in normal code.
- **Minor** — `Modifier` (high `maxCount` ~14 at a few positions, cheap win).
- **Low value / skip** — declaration rules (`ClassDeclaration`, `StructDeclaration`, `TypeDeclaration`, etc.) re-enter exactly 2x via their ordered choice — little benefit.

**Recommended heuristic for the emitter:** memoize a rule when it is reachable through a backtracking edge (a non-first ordered-choice alternative, an optional, or a repetition) AND it participates in a re-entrant cycle / has a non-trivial subtree — concretely the precedence-ladder rules + the type-reference cluster + `Modifier` (~15–20 of ~150 rules). Memoizing the TOP of each backtracking cycle caps the whole subtree below it (e.g. memoizing `TypeArgument` makes its `TypeReference` / `SingleTypeRef` / `PrimitiveType` descendants single-parse).

**Caveat to record:** a PURELY static "reachable via backtracking edge" heuristic over-includes (e.g. the 2x-only declaration rules). The heuristic should use a threshold or be validated against measurement rather than be purely structural.

### 6. Scope note (open decision)

The actual fix is a change to the Storm SOURCE GENERATOR (`ParserEmitter`), NOT the Mist grammar. Whether it lives in this spec or a new spec is UNDECIDED pending this writeup. This is recorded explicitly as an open decision.
