# Implementation Plan

## Overview

This plan restructures the Mist compiler's type resolution pipeline in `SemanticModelBuilder.cs` to fix cross-file type ordering bugs. The pipeline is separated into distinct phases with strict invariants at each boundary: register types → resolve type forwards → resolve inheritance → resolve explicit member types → clone newtype members → resolve bodies/expressions.

## Tasks

- [x] 1. Extract file scope construction into a reusable helper. Factor out the file-scope-building logic from `Pass2_ResolveTypes` into a new static method `BuildFileScopeForUnit(...)` that returns a `Scope`. It must handle namespace scope lookup (file-scoped and block namespaces), using imports, global usings, and inferred namespace computation. Update `Pass2_ResolveTypes` to call the helper instead of duplicating scope construction inline. Verify build succeeds with zero warnings and all existing tests pass.
- [x] 2. Build file scopes eagerly during Phase 1. Move the `BuildFileScopeForUnit` call to run during or immediately after the Phase 1 type registration loop. Store each file's scope in a dictionary indexed by AST position so subsequent phases can reuse them. Verify all existing tests pass — this is a pure refactor with no behavioral change.
- [x] 3. Introduce Phase 2 — Resolve type forwards (aliases and newtype source types). Create `Pass1a_ResolveTypeForwards(...)` that runs BEFORE `Pass1_ResolveBaseTypes`. Build a dependency graph of all alias/newtype symbols, walk it in topological order (leaves first), resolve each target using declaration-site scopes. Extend cycle detection to cover newtypes. For newtypes, resolve source type identity only (do NOT clone members). Wire into `Build()` before `Pass1_ResolveBaseTypes`. Verify existing tests pass and alias cycle detection still works.
- [x] 4. Update inheritance resolution to unwrap type forwards. In `Pass1_ResolveBaseTypes`, `Pass1_ResolveStructBaseTypes`, and `Pass1_ResolveInterfaceLists`: unwrap `TypeAliasType` (via `.UnderlyingType`) and `NewtypeType` (via `.SourceType`) before checking for ClassType/StructType/InterfaceType. Verify a program with `type Base = SomeClass; class Derived : Base { }` compiles correctly and existing inheritance tests pass.
- [x] 5. Introduce Phase 4 — Global explicit member type resolution. Create `Pass1_5_ResolveExplicitMemberTypes(...)` that iterates ALL compilation units and resolves explicit type annotations on all members (fields, properties, methods, constructors, indexers, operators, cast operators, dependent type parameter constraints, extension block receiver types) using declaration-site scopes. Wire into `Build()` after trait satisfaction passes and before `Pass2_RegisterExtensionMembers`. Verify build succeeds with zero warnings.
- [x] 6. Add idempotency guards to existing resolution methods. Add early-return `if (symbol.DeclaredType is not null) return;` guards to `ResolveFieldDeclaration`, `ResolveBlockPropertyDeclaration`, constructor/indexer/operator/cast-operator resolution methods. Confirm `PreResolveMethodSignature` and `ResolveTypeAliasDeclaration` already have guards. Verify Pass 2 doesn't re-resolve types and all existing tests pass unchanged.
- [x] 7. Introduce Phase 4b — Clone newtype members. Create `Pass1_5b_CloneNewtypeMembers(...)` that runs after Phase 4. Iterate all newtype symbols whose source type is resolved but members not yet cloned. Call existing `CloneAllMembers` logic. Wire into `Build()` after Phase 4 and before `Pass2_RegisterExtensionMembers`. Verify newtypes have complete member sets and existing newtype tests pass.
- [x] 8. Remove redundant alias/newtype resolution from Pass 2. Guard or remove the inline calls to `ResolveTypeAliasDeclaration` and `ResolveNewtypeDeclaration` within `Pass2_ResolveTypes` — they are no-ops after phases 2 and 4b. Clean up dead code paths for clarity. Verify all existing tests pass.
- [x] 9. Regression verification — full test suite. Run `dotnet test` for the full test suite. Build all sample apps under `samples/` confirming zero errors, zero warnings, zero diagnostics. Build `samples/AllFeatures` confirming identical output. Manually test with reversed file ordering to confirm cross-file programs compile correctly regardless of order.
- [x] 10. Add cross-file type resolution integration tests. Add tests for: cross-file field type reference, cross-file method return type inference, cross-file property lambda target-type, cross-file interface member type, type alias in inheritance position, dependent type parameter constraint across files, lambdas with type inference on both input and output parameters (cross-file), chaining at least 3 generic methods deep where each takes a lambda using the type arguments (e.g., `items.Select(x => x.Name).Where(n => n.Length > 3).Select(n => n[0])`), and type inference on the return type of a lambda whose target type comes from a separate file. Each test compiles files in BOTH orderings and asserts identical results.
- [x] 11. Track newtype inheritance gap. Add entry to project TODO/backlog: newtypes cannot currently be inherited from (they produce NewtypeType not ClassType). Support for `class Derived : MyNewtype { }` should unwrap the newtype to its source class type in base type resolution. This is a follow-up feature, not part of this bugfix.

## Task Dependency Graph

```json
{
  "waves": [
    { "id": 0, "tasks": ["1"] },
    { "id": 1, "tasks": ["2"] },
    { "id": 2, "tasks": ["3"] },
    { "id": 3, "tasks": ["4"] },
    { "id": 4, "tasks": ["5"] },
    { "id": 5, "tasks": ["6"] },
    { "id": 6, "tasks": ["7"] },
    { "id": 7, "tasks": ["8"] },
    { "id": 8, "tasks": ["9"] },
    { "id": 9, "tasks": ["10", "11"] }
  ]
}
```

Tasks 1–8 are sequential — each builds on the previous refactoring step. Task 9 is the regression gate. Task 10 adds new test coverage. Task 11 is independent (backlog tracking only).

## Notes

- All tasks modify `Source/Mist/Semantics/Model/SemanticModelBuilder.cs` as the primary file.
- The zero-warnings policy applies to all tasks — no warnings may be introduced.
- Process spawning policy: integration tests in Task 10 that compile and run Mist programs should use the shared `CompilerDriver` for in-process compilation, spawning `dotnet` only once to run the output DLL (batched if multiple scenarios).
- The sample app integrity policy applies: if any change breaks an existing sample, the fix must be in the compiler, not the sample.
