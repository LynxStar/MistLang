
# Program Model (Frontend 2)

## Purpose

`Mist.ProgramModel` is the compiler's frontend. It represents the resolved semantic structure of a Mist program — types, members, namespaces, relationships and bound bodies. Semantic relationships are navigable directly; declarations, expressions and patterns retain syntax nodes for source-anchored reporting and lowering.

## Isolation Rule

`Mist.ProgramModel` is the only semantic model in the tree. The system it replaced, classic's `Mist.Semantics` (`ISemanticModel`, `ISymbol`, `IType`, `IScope`, `SymbolTable` and the rest), exists only in git history, and there is no adapter to it.

Code under `Source/Mist/ProgramModel/` MUST NOT reintroduce those types or be derived from that source — see the clean-room rule in the root `CLAUDE.md`.

## Design Principles

1. **Self-describing graph**: The program model is navigable through semantic relationships (type → members → types). No syntax tree required to understand the program.

2. **Contracts first**: All public API is defined through interfaces in `ProgramModel/Contracts/`. Implementations are internal details.

3. **Own types and source anchors**: The program model defines its own type contracts (`IMistType` and related interfaces) and enums (`ByRefDirection`, `MistAssemblyKind`, etc.). Source anchors are the retained declaration or bound-node syntax nodes; there is no `SourceOrigin` type.

4. **Immutable after construction**: Once built, the program model is read-only. No `Record*` or `Set*` mutation methods on the model itself. Analysis results from later passes live in separate annotation stores.

5. **No syntax nodes as keys**: The program model is indexed by its own semantic entities, never by `SyntaxNode` as the primary lookup key.

Binding owns name/member lookup, overload selection, generic argument inference, operator
resolution and conversion classification. Later analyzers validate the resolved graph; the
bound model must carry the selections and targets those checks need rather than require them
to resolve again.

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

## Planned Rename

`ProgramModelBuilder` is the only model builder, and every consumer (analyzers, emitter) reads `IProgramModel`. Renaming the `Mist.ProgramModel` namespace to `Mist.Semantics` is a separate, future change; the name is free, since classic's `Mist.Semantics` is no longer in the tree.

## What Belongs Here

- Type definitions and their members (methods, properties, fields, constructors)
- Type references (named, generic, array, union, tuple, etc.)
- Namespace hierarchy
- Extension definitions
- Conformance declarations (with-blocks)
- Type parameter constraints
- Declaration and bound-node syntax anchors for diagnostics and lowering

## What Does NOT Belong Here

- Syntax-driven semantic lookup in place of the model's resolved relationships
- Expression type inference (that's the builder's job, not the model's)
- Mutable annotation stores for cross-pass communication
- Cross-pass findings stored on syntax nodes instead of separate analysis stores

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

ProgramModel is tested like every other part of the compiler, by the testing philosophy, `.claude/skills/testing-philosophy/SKILL.md` (`/testing-philosophy`), strictly. There is no exemption: ProgramModel is not "exercised indirectly through end-to-end tests". One binder method is Tier 1; a binder's dispatch chain is Tier 2; `ProgramModelBuilder.Build` over a minimal source with a stub ambient provider is Tier 3; the provider that loads the reference assemblies is Tier 5 infrastructure outside its own area. A program compiled end to end is not a binder test, and a sample does not verify the binder.

After a batch, build, then run the tests that cover what you changed (`--filter`).

Every new or migrated test declares its tier with `[Trait("Tier", "N")]` following
`Test/CLAUDE.md`, "Test tier classification". Existing unclassified tests may remain unmarked;
classify the implementation and its helpers before assigning a tag.

- Run the full test suite (`tools/test-ledger/run.sh`, see `Test/CLAUDE.md`) only when:
  - Changes touch files outside ProgramModel (grammar, emitter, CLI, etc.)
  - You are at a natural checkpoint before presenting results to the user
  - The user explicitly asks
