---
inclusion: fileMatch
fileMatchPattern: '**/ProgramModel/**'
---

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
    Model/              — Concrete implementations (future)
    Builder/            — Builds IProgramModel from ASTs (future)
```

## Dual-Path Strategy

During the transition period:
- The old `SemanticModelBuilder` continues to run and produce `ISemanticModel`
- A new `ProgramModelBuilder` (future) will run alongside it, producing `IProgramModel`
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
