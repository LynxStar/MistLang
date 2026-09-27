
# Program Model (Frontend 2)

## Purpose

`Mist.ProgramModel` is the foundation for a new compiler frontend that will eventually replace the existing `Mist.Semantics` system. It represents the fully resolved semantic structure of a Mist program — types, members, namespaces, and relationships — navigable without reference to syntax trees.

## Isolation Rule

Code under `Source/Mist/ProgramModel/` MUST NOT reference any type from the old semantic system. This means:

- No `using Mist.Semantics.*`
- No `using Mist.Semantics.Contracts.*`
- No `using Mist.Semantics.Model.*`
- No `using Mist.Semantics.Model.Types.*`
- No references to `ISemanticModel`, `ISymbol`, `IType`, `IScope`, `SymbolTable`, `SemanticModel`, `ClassType`, `StructType`, `AbstractType`, `ExtensionRegistry`, `ConformanceMap`, or any other type from the legacy system

The old and new systems are intentionally separate. The only bridge between them is an adapter layer that lives OUTSIDE of `ProgramModel/` and translates old representations into new ones.

## Design Principles

1. **Self-describing graph**: The program model is navigable through semantic relationships (type → members → types). No syntax tree required to understand the program.

2. **Contracts first**: All public API is defined through interfaces in `ProgramModel/Contracts/`. Implementations are internal details.

3. **Own types**: The program model defines its own type reference system (`ITypeReference`), its own enums (`OwnershipKind`, `ParameterPassingKind`, etc.), and its own source location type (`SourceOrigin`). It does not reuse old-system types even when they look similar.

4. **Immutable after construction**: Once built, the program model is read-only. No `Record*` or `Set*` mutation methods on the model itself. Analysis results from later passes live in separate annotation stores.

5. **No syntax nodes as keys**: Unlike the old `ISemanticModel` which uses `SyntaxNode` as the primary lookup key, the program model is indexed by its own semantic entities.

## Structure

```
Source/Mist/ProgramModel/
    Contracts/          — Interfaces and enums defining the public API
    Model/              — Concrete implementations
    Construction/       — Builds IProgramModel from ASTs
```

## Formatting

When working on ProgramModel code, scope `dotnet format` to this folder to save time:

```bash
dotnet format Mist.slnx --include Source/Mist/ProgramModel/
dotnet format Mist.slnx --verify-no-changes --include Source/Mist/ProgramModel/
```

## Dual-Path Strategy

During the transition period:
- The old `SemanticModelBuilder` continues to run and produce `ISemanticModel`
- `ProgramModelBuilder` runs alongside it, producing `IProgramModel`, selected with
  `--pipeline experimental`
- Consumers (analyzers, emitter) are ported one at a time from old → new
- When all consumers use `IProgramModel`, the old system is removed
- The `Mist.ProgramModel` namespace is then renamed to `Mist.Semantics`

## What Belongs Here

- Type definitions and their members (methods, properties, fields, constructors)
- Type references (named, generic, array, union, tuple, etc.)
- Namespace hierarchy
- Extension definitions
- Conformance declarations (with-blocks)
- Type parameter constraints
- Source origin metadata for diagnostics

## What Does NOT Belong Here

- Syntax trees or AST nodes
- Expression type inference (that's the builder's job, not the model's)
- Mutable annotation stores for cross-pass communication
- Anything that requires `Storm.Runtime.SyntaxNode`

# Program Model Verification Policy

## Rule: Batch Verification, Not Per-Change

When making a series of small, related changes to `Source/Mist/ProgramModel/` (refactoring, renaming, moving code between files, adjusting signatures), do NOT build or run tests after every individual change.

Instead:

1. Make all the related changes in a batch.
2. Build once at the end of the batch.
3. Fix any compilation errors.
4. Run tests once after the build succeeds.

## When To Build Mid-Batch

Only build between changes if:

- You are unsure whether the approach compiles (novel pattern, unfamiliar API)
- The user explicitly asks you to verify
- You are switching to a fundamentally different phase of work

## What Counts as "Small Changes"

- Renaming a field, parameter, or method
- Changing a method signature and updating its callers
- Extracting or inlining a method
- Adding/removing a using directive
- Moving logic between files without changing behavior
- Updating XML doc comments

These do NOT need individual build+test cycles.

## Tests

ProgramModel is tested like every other part of v2, by the testing philosophy, `.claude/skills/testing-philosophy/SKILL.md` (`/testing-philosophy`), strictly. There is no exemption: ProgramModel is not "exercised indirectly through end-to-end tests". One binder method is Tier 1; a binder's dispatch chain is Tier 2; `ProgramModelBuilder.Build` over a minimal source with the real ambient provider is Tier 3. A program compiled end to end is not a binder test, and a sample does not verify the binder.

After a batch, build, then run the tests that cover what you changed (`--filter`).

- Run the full test suite (`tools/test-ledger/run.sh`, see `Test/CLAUDE.md`) only when:
  - Changes touch files outside ProgramModel (grammar, emitter, CLI, etc.)
  - You are at a natural checkpoint before presenting results to the user
  - The user explicitly asks
