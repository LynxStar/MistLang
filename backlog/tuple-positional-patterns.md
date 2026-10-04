> **Reconciliation authority (2026-10-03):** [issues.yaml](issues.yaml) owns
> outstanding work. This document retains contracts and historical evidence; its old
> tasks/statuses are not another current queue. See the [dated reconciliation](inventory-2026-10-03.md).
> Reconciled baseline: `da1aaa8fd416bd3bdf93e496281e7b2cbf52594c`.

# Tuple Positional Patterns in Switch Expressions

## Problem

The following valid C# pattern matching syntax does not work in Mist:

```mist
return (current, target) switch
{
    (WorkItemState.Pending, WorkItemState.Searching) => true,
    (WorkItemState.Searching, WorkItemState.Downloading) => true,
    _ => false
};
```

The parser treats `(WorkItemState.Pending, WorkItemState.Searching)` as a **tuple type reference** (producing a `BareTypePatternPrimaryPatternNode` with a `TupleSingleTypeRefSingleTypeRefNode` inside), not as a **positional tuple pattern** (`TuplePositionalPatternPrimaryPatternNode`).

This means the semantic model tries to resolve `WorkItemState.Pending` as a type name rather than recognizing it as an enum member constant in a positional pattern.

## Root Cause

The Storm grammar's pattern rules don't have a production that recognizes `(pattern, pattern, ...)` as a positional pattern when the elements are constant expressions (like enum member accesses). The grammar instead falls through to the type reference production, parsing it as a tuple type.

A `TuplePositionalPatternPrimaryPatternNode` AST node exists and is handled by both `CollectFromPrimaryPattern` and `ResolvePrimaryPattern` — it just never gets produced by the parser for this syntax in switch arm context.

## Current Workaround

Use property patterns with `Item1`/`Item2` to match tuple elements:

```mist
return (current, target) switch
{
    { Item1: WorkItemState.Pending, Item2: WorkItemState.Searching } => true,
    _ => false
};
```

Or use chained `if`/`is` statements (which work since the enum pattern fix in v0.317.0):

```mist
if (current is WorkItemState.Pending && target is WorkItemState.Searching) { return true; }
```

## What Needs to Change

### Option A: Grammar/Parser Change (Preferred)

Add a grammar production so that in pattern position, `(pattern, pattern, ...)` is parsed as a `TuplePositionalPatternPrimaryPatternNode` rather than a tuple type reference. The parser needs to disambiguate:

- `(int, string)` in type position → tuple type reference
- `(WorkItemState.Pending, WorkItemState.Searching)` in pattern position → positional pattern

The disambiguation rule: in pattern context (switch arm patterns, `is` patterns), a parenthesized comma-separated list where elements are patterns (constants, type patterns, discard, relational, etc.) produces a `TuplePositionalPatternPrimaryPatternNode`.

### Option B: Semantic Model Workaround

In `BareTypePatternPrimaryPatternNode` handling, detect when the type reference is a tuple type whose elements are dotted names that resolve to enum members. Reinterpret as a positional match and generate equality checks for each element. This avoids grammar changes but adds complexity to the semantic model.

### Additional Work Required

1. **Emitter** — The `CSharpSourceWriter` needs to emit `TuplePositionalPatternPrimaryPatternNode` in switch arms as C# positional tuple patterns: `(pattern1, pattern2) => result`.

2. **Enum constant patterns inside positional patterns** — Each element of the positional pattern can be an enum member reference (`EnumName.Variant`), which requires the namespace-aware enum resolution already implemented in v0.317.0 for `is` expressions.

3. **Nested patterns** — C# supports arbitrary nesting: `(> 0, WorkItemState.Pending)` mixing relational and constant patterns. The grammar production needs to allow any pattern as a tuple element.

4. **Sample/tests** — The `SwitchExpressions` sample should be extended with positional tuple pattern demonstrations.

## Scope

This is a parser-level feature addition. The semantic model and emitter infrastructure for `TuplePositionalPatternPrimaryPatternNode` already exists (it's used in `is` expression context). The main work is making the grammar produce this node type in switch arm pattern position.

## Related

- Switch expressions spec: `.kiro/specs/switch-expressions/requirements.md` (Requirement 15 mentions tuple scrutinee but only demonstrates property patterns)
- Enum pattern fix (v0.317.0): namespace-aware enum member resolution in `BareTypePatternPrimaryPatternNode`
