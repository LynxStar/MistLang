> **Reconciliation authority (2026-10-03):** [issues.yaml](issues.yaml) owns
> outstanding work. This document retains contracts and historical evidence; its old
> tasks/statuses are not another current queue. See the [dated reconciliation](inventory-2026-10-03.md).
> Reconciled baseline: `da1aaa8fd416bd3bdf93e496281e7b2cbf52594c`.

# Refactor: Store AST on CompilationUnit

## Problem

The pipeline passes two parallel arrays — `ImmutableArray<SyntaxNode> asts` and `compilation.Units` — that are correlated by index. Every consumer does indexed `for` loops to pair `asts[i]` with `compilation.Units[i]`. This is fragile, unclear, and prevents simple `foreach` iteration.

## Desired State

`CompilationUnit` should own its parsed AST:

```csharp
public sealed record CompilationUnit(Common.Source.SourceText Source)
{
    public CompilationUnitNode? Ast { get; init; }
    // ...existing properties...
}
```

Consumers iterate `compilation.Units` directly:

```csharp
foreach (var unit in compilation.Units)
{
    var ast = unit.Ast!;
    var sourceText = unit.Source;
    // ...
}
```

The separate `asts` array and `PipelineInput.Asts` are removed.

## Steps

1. Add `CompilationUnitNode? Ast { get; init; }` to `CompilationUnit` record.
2. In the driver (parsing stage), set `Ast` on each unit after parsing.
3. Remove `ImmutableArray<Storm.Runtime.SyntaxNode> Asts` from `PipelineInput`.
4. Update `SemanticModelBuilder.Build` signature to not take a separate `asts` param (get it from `compilation.Units`).
5. Update all ~20 indexed loops in `SemanticModelBuilder` to iterate units.
6. Update all analyzers that receive `asts` (~8 files in `Analysis/Builtin/`).
7. Update `ExperimentalPipeline` and `ClassicPipeline`.
8. Update tests that construct `PipelineInput` or call the builder directly.

## Scope

~30+ files touched. Mechanically straightforward but wide. Should be its own PR with full test verification.


---

# Refactor: Always Set NamespaceRoot/ProjectFolder on CompilationUnit

## Problem

`CompilationUnit.NamespaceRoot` and `CompilationUnit.ProjectFolder` are nullable. The user project's own units leave them null, relying on a fallback to `CompilerOptions.NamespaceRoot` / `CompilerOptions.ProjectFolder`. This forces every consumer to do `unit?.NamespaceRoot ?? options.NamespaceRoot`.

Only stdlib and referenced-project units set these explicitly today.

## Desired State

The driver should always populate `NamespaceRoot` and `ProjectFolder` on every unit — including the user project's own files. The properties become non-nullable:

```csharp
public required string NamespaceRoot { get; init; }
public required string ProjectFolder { get; init; }
```

Consumers then just use `unit.NamespaceRoot` directly with no fallback logic.

## Steps

1. In the driver (where user source `CompilationUnit` instances are created), set `NamespaceRoot` and `ProjectFolder` from `CompilerOptions`.
2. Change the properties from `string?` to `required string`.
3. Remove all `unit?.NamespaceRoot ?? options.NamespaceRoot` patterns in consumers.
4. Update tests that construct `CompilationUnit` without these values.
