# Design Document: Tuple Positional Switch Patterns

## Overview

This feature is a parser grammar reordering that enables tuple positional patterns (`(pattern, pattern, ...)`) in switch expression arms. Currently the parser's `PrimaryPattern` rule tries `BareTypePattern` before `TuplePositionalPattern`, causing `(EnumName.Variant, EnumName.Variant)` to be consumed as a tuple type reference. The fix moves `TuplePositionalPattern` between `TypePattern` (which requires a trailing binding identifier) and `BareTypePattern`, so parenthesized comma-separated patterns without a binding name are correctly parsed as positional patterns. All downstream compiler components already handle the `TuplePositionalPatternPrimaryPatternNode` — no semantic model, emitter, or type checker changes are needed.

## Architecture

This feature is a parser grammar reordering. The `PrimaryPattern` rule in `Mist.storm` uses PEG ordered choice. Currently, `TypePattern` and `BareTypePattern` appear before `TuplePositionalPattern`. Because `TypeReference` includes `TupleSingleTypeRef` (which matches `(TypeRef, TypeRef, ...)`), any parenthesized comma-separated list of qualified identifiers like `(WorkItemState.Pending, WorkItemState.Searching)` is consumed by `BareTypePattern` before `TuplePositionalPattern` is ever tried.

The fix: move `TuplePositionalPattern` and `ParenthesizedPattern` between `TypePattern` and `BareTypePattern`. The disambiguation relies on `TypePattern` requiring a trailing identifier — `(int, string) pair` succeeds at `TypePattern` (type + binding), while `(WorkItemState.Pending, WorkItemState.Searching)` fails `TypePattern` (no trailing identifier) and falls through to `TuplePositionalPattern`.

All downstream components (semantic model builder, type checker, exhaustiveness checker, emitter, pretty printer) already handle `TuplePositionalPatternPrimaryPatternNode`. No code changes are needed beyond the grammar reorder and sample additions.

### Current PrimaryPattern Rule (Broken)

```storm
rule PrimaryPattern
    = type:TypeReference name:(IDENTIFIER | DISCARD)        as TypePattern
    | type:TypeReference                                    as BareTypePattern
    | VAR name:(IDENTIFIER | DISCARD)                       as VarPattern
    | DISCARD                                               as DiscardPattern
    | ...relational, property, list patterns...
    | LEFT_PARENTHESIS first:Pattern COMMA rest:Pattern (COMMA additional:Pattern)* RIGHT_PARENTHESIS  as TuplePositionalPattern
    | LEFT_PARENTHESIS inner:Pattern RIGHT_PARENTHESIS      as ParenthesizedPattern
    | literal:ConstantLiteral                               as ConstantPattern
    ;
```

### Fixed PrimaryPattern Rule

```storm
rule PrimaryPattern
    = type:TypeReference name:(IDENTIFIER | DISCARD)        as TypePattern
    | LEFT_PARENTHESIS first:Pattern COMMA rest:Pattern (COMMA additional:Pattern)* RIGHT_PARENTHESIS  as TuplePositionalPattern
    | LEFT_PARENTHESIS inner:Pattern RIGHT_PARENTHESIS      as ParenthesizedPattern
    | type:TypeReference                                    as BareTypePattern
    | VAR name:(IDENTIFIER | DISCARD)                       as VarPattern
    | DISCARD                                               as DiscardPattern
    | LESS_THAN constant:VoidCoalescing                     as LessThanRelationalPattern
    | GREATER_THAN constant:VoidCoalescing                  as GreaterThanRelationalPattern
    | LESS_THAN_OR_EQUAL constant:VoidCoalescing            as LessThanOrEqualRelationalPattern
    | GREATER_THAN_OR_EQUAL constant:VoidCoalescing         as GreaterThanOrEqualRelationalPattern
    | typeName:QualifiedIdentifier? LEFT_BRACE subpatterns:PropertySubpatternList? RIGHT_BRACE name:(IDENTIFIER | DISCARD)?  as PropertyPattern
    | LEFT_BRACKET elements:ListPatternElementList? RIGHT_BRACKET  as ListPattern
    | literal:ConstantLiteral                               as ConstantPattern
    ;
```

### Disambiguation Table

| Input | TypePattern | TuplePositionalPattern | BareTypePattern | Final Match |
|-------|-------------|----------------------|-----------------|-------------|
| `(WorkItemState.Pending, WorkItemState.Searching)` | TypeRef ✓, no IDENTIFIER → fail | `(Pattern, Pattern)` ✓ | — | TuplePositionalPattern |
| `(int, string) pair` | TypeRef ✓, `pair` ✓ → success | — | — | TypePattern |
| `(> 0, < 5)` | TypeRef fails → fail | `(Pattern, Pattern)` ✓ | — | TuplePositionalPattern |
| `int x` | TypeRef ✓, `x` ✓ → success | — | — | TypePattern |
| `int` | TypeRef ✓, no IDENTIFIER → fail | No `(` → fail | TypeRef ✓ → success | BareTypePattern |
| `(> 0)` | TypeRef fails → fail | No COMMA → fail | — | ParenthesizedPattern |
| `(int)` | TypeRef ✓ (ParenTypeRef), no IDENTIFIER → fail | No COMMA → fail | — | ParenthesizedPattern |

## Components and Interfaces

### Modified Component: Storm Grammar (`Source/Mist/Grammar/Mist.storm`)

The `PrimaryPattern` rule is reordered. No new rules, tokens, or AST node types are introduced.

### Unchanged Components (Already Handle TuplePositionalPatternPrimaryPatternNode)

| Component | File | Handling |
|-----------|------|----------|
| Semantic Model Builder | `SemanticModelBuilder.cs` | `CollectFromPrimaryPattern` + `ResolvePrimaryPattern` recurse into element subpatterns |
| Type Checker | `TypeCheckerAnalyzer.cs` | Validates element type compatibility with scrutinee tuple type |
| Exhaustiveness Checker | `TypeCheckerAnalyzer.cs` | Treats tuple of all-discard elements as catch-all |
| C# Emitter | `CSharpEmitter.cs` | Emits `(pattern1, pattern2, ...)` — context-independent |
| Pretty Printer | `MistPrettyPrinter.cs` | Formats `(pattern1, pattern2, ...)` with comma-space separators |

### Enum Member Resolution in Tuple Elements

Each element of a `TuplePositionalPatternPrimaryPatternNode` is a full `PatternNode` that recurses through the pattern rule chain. When an element is a qualified identifier like `WorkItemState.Pending`, it reaches `BareTypePattern` within its own `PrimaryPattern` resolution. The semantic model builder's existing enum-member-pattern detection (using `ResolveNamedType` with suppressed diagnostics) handles this — the same path used for `is WorkItemState.Pending` in standalone expressions.

## Data Models

### AST Node: TuplePositionalPatternPrimaryPatternNode (Existing — No Changes)

```
TuplePositionalPatternPrimaryPatternNode
├── First: PatternNode         — first element subpattern
├── Rest: PatternNode          — second element subpattern
└── Patterns: PatternNode[]    — additional element subpatterns (3+ elements)
```

### AST Node: BareTypePatternPrimaryPatternNode (Existing — Reduced Scope)

After the fix, this node is only produced for non-parenthesized type references (e.g., `int`, `MyClass`). It is never produced for parenthesized comma-separated lists — those always become `TuplePositionalPatternPrimaryPatternNode`.

### No New AST Nodes Required

The grammar reorder does not introduce new node types. It changes which existing node type is produced for parenthesized patterns.

## Correctness Properties

### Property 1: Parse Determinism

For any valid pattern input, exactly one `PrimaryPattern` alternative succeeds. The PEG ordered choice guarantees this.

### Property 2: Backward Compatibility

All existing patterns that currently parse correctly continue to parse to the same AST node type, EXCEPT `(Type, Type)` without a trailing binding name — this now becomes `TuplePositionalPatternPrimaryPatternNode` instead of `BareTypePatternPrimaryPatternNode`. This is semantically equivalent (both represent "check if each element matches the given type").

### Property 3: Round-Trip Fidelity

The pretty printer emits identical text for both node types when the content is a tuple of type references. Parse → print → parse produces a stable AST.

### Property 4: Memoization Correctness

`TypeReference` is `@memo`-annotated. The first alternative (`TypePattern`) memoizes its `TypeReference` result. When `BareTypePattern` (position 4) is tried later, it reuses the memoized result — no double-parsing.

## Error Handling

### Parser Errors

No new parse errors are introduced. If a parenthesized pattern has malformed elements, the parser already reports errors at the `Pattern` level within the recursive descent.

### Semantic Errors (Existing — No Changes)

- `MIST_UNDEFINED_TYPE (historical)`: Fired if an element resolved as a type reference cannot be found. Already handled by `ResolveTypeReference` fallback in `BareTypePattern` within element resolution.
- Enum member not found: Fired by the existing `UndefinedEnumMemberAccess` diagnostic when `EnumName.InvalidMember` is used.
- Tuple arity mismatch: Fired by the type checker when a `TuplePositionalPattern` has a different number of elements than the scrutinee's `TupleType`.

## Testing Strategy

### Regression Tests (Run Existing Suite)

1. Full test suite (`dotnet test`) — confirms no parser regressions.
2. `samples/AllFeatures` via `mist run` — confirms all existing features including `TuplePatternMatching` sample.
3. `SamplesCompileAndMatchBaselineTest` — confirms baseline output unchanged.

### New Verification

1. **Pendragon.Tests**: The `StateMachinePropertyTests.mist` with `(WorkItemState.Pending, WorkItemState.Searching) => true` compiles and tests pass via `mist test`.
2. **Proof code in TuplePatternMatching sample**: Add proof code from requirements (enum tuple switch, string-backed enums, 3-element tuples, mixed patterns, cross-type tuples). Verify output matches expected.
3. **Edge case: `(int, string) pair`**: Verify this still parses as `TypePattern` (tuple type + binding name) by checking the existing TuplePatternMatching sample which uses `(int, string)` type patterns in `is` expressions.

### What Does NOT Need New Tests

- Semantic model resolution (existing property tests cover `TuplePositionalPatternPrimaryPatternNode`)
- Exhaustiveness checking (existing property tests)
- Pretty-printer round-trip (existing property tests)
- C# emission in `is` context (existing sample proves it)
