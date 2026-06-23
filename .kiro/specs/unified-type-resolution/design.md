# Unified Type Resolution Bugfix Design

## Overview

The Mist compiler's `SemanticModelBuilder` has a phase ordering violation: explicit type annotations on fields, properties, method parameters, method return types, indexers, operators, cast operators, and constructors are resolved in `Pass2_ResolveTypes` on a per-file basis, interleaved with expression-level type inference. When File B's expression inference needs the `DeclaredType` of a field/property/method from File A, and File A hasn't been processed yet (or is processed later in the file loop), the type is still `null` — causing inference failures for type arguments, lambda return types, and other dependent constructs.

The fix introduces a dedicated **Pass 1.5** (explicit type resolution phase) that runs globally across ALL compilation units, resolving every explicitly-annotated member type BEFORE any expression-level inference begins. This decouples "what type is this member?" from "what type does this expression evaluate to?", eliminating file-ordering dependencies.

Additionally, the scope-supply mechanism is corrected: type annotations will be resolved using the declaration-site scope (the file scope and namespace scope where the declaration lives) rather than relying on whatever scope the caller happens to pass.

## Glossary

- **Bug_Condition (C)**: A cross-file reference to a field, property, or method signature whose `DeclaredType` is still `null` because the declaring file hasn't been processed by `Pass2_ResolveTypes` yet
- **Property (P)**: All explicitly-annotated member types SHALL have their `DeclaredType` populated before any expression-level inference begins, regardless of file ordering
- **Preservation**: All existing single-file resolution, standard library type resolution, generics, diagnostics, base type resolution, and extension method behavior must remain unchanged
- **SemanticModelBuilder**: The class in `Source/Mist/Semantics/Model/SemanticModelBuilder.cs` that orchestrates all compilation passes from symbol registration through expression type resolution
- **Pass 1**: The existing phase that registers type symbols (classes, structs, interfaces, traits, enums) and their members (fields, properties, methods) with `DeclaredType = null`
- **Pass 1b**: The existing phase that resolves base types, interface lists, constraints, trait parents, and with-blocks
- **Pass 2**: The existing phase that resolves type annotations AND expression types file-by-file (the source of the bug)
- **Pass 1.5 (proposed)**: A new global phase that resolves ONLY explicit type annotations across all files before Pass 2 begins
- **Declaration-site scope**: The file scope (with its namespace parent and using imports) where a member is syntactically declared
- **Caller-supplied scope**: The ad-hoc scope passed by the current `fileScope` parameter in `Pass2_ResolveTypes`, which may differ from the declaration site for cross-file references

## Bug Details

### Bug Condition

The bug manifests when expression-level type inference in one compilation unit depends on an explicitly-annotated type from a different compilation unit that hasn't been processed yet in the file-by-file `Pass2_ResolveTypes` loop. The `DeclaredType` field on the referenced symbol is still `null` (set during Pass 1 registration), causing type argument inference, lambda target-type propagation, and member type lookup to fail.

**Formal Specification:**
```
FUNCTION isBugCondition(input)
  INPUT: input of type (ExpressionNode, ReferencedSymbol, FileProcessingOrder)
  OUTPUT: boolean
  
  RETURN input.ReferencedSymbol.DeclaredType == null
         AND input.ReferencedSymbol.HasExplicitTypeAnnotation == true
         AND input.ReferencedSymbol.DeclaringFile != input.ExpressionFile
         AND indexOf(input.ReferencedSymbol.DeclaringFile, FileProcessingOrder) 
             > indexOf(input.ExpressionFile, FileProcessingOrder)
END FUNCTION
```

A secondary bug condition exists for scope mismatch:
```
FUNCTION isScopeBugCondition(input)
  INPUT: input of type (TypeAnnotation, ResolutionScope)
  OUTPUT: boolean
  
  RETURN input.ResolutionScope != input.TypeAnnotation.DeclarationSiteScope
         AND input.TypeAnnotation.ReferencedType is resolvable 
             in DeclarationSiteScope but NOT in ResolutionScope
END FUNCTION
```

### Examples

- **Cross-file field reference**: Class `Renderer` in `renderer.mist` has field `canvas: Canvas`. Class `App` in `app.mist` calls `renderer.canvas.Draw(...)`. If `app.mist` is processed first, `canvas.DeclaredType` is `null` and `Draw` lookup fails.
- **Cross-file method return type for type inference**: `Repository.FindAll<T>()` returns `List<T>`. A caller in another file does `var items = repo.FindAll<Widget>()` — type argument inference for `items` requires knowing the return type, which is `null` if Repository's file wasn't processed first.
- **Cross-file property for lambda target-type**: Class `EventBus` has property `Handler: Func<Event, bool>`. Another file assigns `bus.Handler = e => e.IsValid` — lambda parameter type inference requires the property type, which may be `null`.
- **Interface member type for implementing class**: Interface `ISerializer` declares `Serialize(data: T) -> string`. A class implementing it in another file relies on the interface method's resolved return type for inference — but it may not be resolved yet.

## Expected Behavior

### Preservation Requirements

**Unchanged Behaviors:**
- All single-file type resolution must continue to produce identical results (Requirements 3.1)
- Standard library and ambient type resolution must continue working (Requirement 3.2)
- Base types, interface lists, and constraint clauses must continue resolving before member types (Requirement 3.3)
- Extension method receiver types and signatures must remain available before expression inference (Requirement 3.4)
- Type parameters, generics, and constructed types must produce identical representations (Requirement 3.5)
- Expression-level inference (lambdas, type arguments, returns) must produce identical results for already-working programs (Requirement 3.6)
- Diagnostics for unresolvable types must remain unchanged (Requirement 3.7)
- Storage-ban checks for `unknown` must continue to emit the same errors (Requirement 3.8)
- Type alias cycle detection must continue to work correctly (Requirement 3.9)

**Scope:**
All inputs that do NOT involve cross-file type references (or that already work due to fortuitous file ordering) should be completely unaffected by this fix. This includes:
- All single-file programs
- Programs where referenced files happen to be processed before referencing files
- All expression-level inference that doesn't depend on cross-file member types

## Hypothesized Root Cause

Based on the architecture analysis, the root causes are:

1. **Single-pass interleaving**: `Pass2_ResolveTypes` resolves both explicit annotations (field types, property types, method signatures) AND expression types (initializers, method bodies) in a single file-by-file loop. Since the loop iterates `asts[0..N]` sequentially, file A's members aren't resolved when file B (processed earlier) needs them.

2. **Per-file scope construction**: Each file constructs its own `fileScope` in `Pass2_ResolveTypes` with using imports and namespace parents. When `PreResolveMethodSignature` runs, it resolves method signatures using THIS file's scope — but this only helps within-class forward references, not cross-file references. Other files that reference these methods before they're processed see `null`.

3. **No global explicit-type pass**: The `PreResolveMethodSignature` call inside `ResolveClassDeclaration` only pre-resolves methods within the same class declaration. There is no equivalent global pre-resolution for fields, properties, indexers, operators, cast operators, or constructors across all files.

4. **Ad-hoc scope supply**: The `fileScope` parameter threaded through resolution calls represents the CURRENT file being processed, not necessarily the file where the type annotation was declared. This means a type annotation `Foo` that's resolvable via a `using` in the declaring file may not resolve when looked up through a different file's scope context.

## Correctness Properties

Property 1: Bug Condition - Cross-File Explicit Types Resolved Before Expression Inference

_For any_ compilation where multiple files reference each other's explicitly-annotated member types, the fixed `SemanticModelBuilder` SHALL guarantee that every field, property, method parameter, method return type, indexer, operator, cast operator, and constructor parameter across ALL compilation units has its `DeclaredType` populated (non-null) before any expression-level inference (`ResolveExpression`) begins in any file.

**Validates: Requirements 2.1, 2.2, 2.3, 2.4**

Property 2: Preservation - Single-File and Existing Behavior Unchanged

_For any_ compilation that already succeeds (no cross-file type ordering issues), the fixed `SemanticModelBuilder` SHALL produce identical `DeclaredType` values, identical expression types, and identical diagnostics as the original implementation, preserving all existing resolution behavior.

**Validates: Requirements 3.1, 3.2, 3.3, 3.4, 3.5, 3.6, 3.7, 3.8, 3.9**

## Fix Implementation

### Changes Required

Assuming our root cause analysis is correct:

**File**: `Source/Mist/Semantics/Model/SemanticModelBuilder.cs`

**Corrected Pipeline Ordering**:

The fix restructures the pipeline into clearly separated phases with strict invariants at each boundary:

| Phase | What it does | Invariant after completion |
|-------|-------------|--------------------------|
| 1. Build file scopes & register types | Build each file's declaration-site scope (namespace + usings + global usings), then walk all files registering every type declaration recursively (including nested types). Type parameter slots are created here as part of the type's identity. | Every type name is known — lookup returns a symbol. Type parameter slots exist. Each file's scope is built and attached. |
| 2. Resolve type forwards | Resolve all type aliases and newtype source types. These form a dependency graph (alias→alias, alias→newtype, newtype→alias, newtype→newtype) that must be walked leaves-up. | Every alias/newtype forwards to its underlying concrete type. |
| 3. Resolve inheritance | Base types, interface lists, constraints, trait parents, with-blocks (must unwrap aliases/newtypes when encountered) | Inheritance hierarchy is wired; type parameters from base types are visible |
| 4. Resolve explicit member types | Fields, properties, method signatures, constructors, indexers, operators, cast operators. Dependent type parameter constraints (e.g., `Size:int`) are resolved here too — the slot exists from phase 1, the type of the slot is resolved like any other explicit type annotation. | Every explicitly-typed member has a non-null DeclaredType |
| 4b. Clone newtype members | After source types have their members fully typed, clone them onto newtypes | Newtypes have complete member sets |
| 5. Resolve bodies/expressions | Method bodies, initializers, expression-level inference | Full program is typed |

**Key ordering rationale:**

- Phase 1 builds the declaration-site scope (file namespace, using statements, global usings) FIRST, then registers types into those scopes. The scope must exist before registration because it determines which namespace the types land in.
- Phase 2 (type forwards) must come before Phase 3 (inheritance) because you can inherit from an alias or newtype — e.g., `type Base = SomeClass; class Derived : Base { }`.
- Phase 2 requires a dependency graph because aliases and newtypes can reference each other: `type A = B; newtype B = C; type C = int;` — must resolve C first, then B, then A. Walk the graph leaves-up. Cycles are detected and reported as errors (existing cycle detection mechanism preserved).
- Phase 3 (inheritance) must come before Phase 4 (explicit member types) because member type annotations may reference type parameters inherited from a base type.
- Phase 4b (clone newtype members) runs after phase 4 because the source type's members need to be fully typed before cloning. Once cloned, newtypes behave like normal types for the rest of the pipeline.
- Dependent type parameters (`<Size:int>`) — the slot `Size` is registered in phase 1 as part of the type's identity/arity. The constraint that Size is an `int` is resolved in phase 4, same as any other explicit type annotation. It's structurally equivalent to a field's type.

**Specific Changes**:

1. **Build file scopes eagerly in Phase 1**: Before registering types, build each file's declaration-site scope (namespace scope lookup, using imports, global usings). This scope is attached to registered types so it can be reused in phases 2, 3, and 4 without reconstruction. Factor out the scope-building logic from `Pass2_ResolveTypes` into a reusable helper `BuildFileScopeForUnit(...)`.

2. **Introduce Phase 2 — Resolve Type Forwards (new pass before inheritance)**: Extract type alias and newtype source-type resolution from Pass 2 into a new pass that runs BEFORE `Pass1_ResolveBaseTypes`. This pass:
   - Builds a dependency graph of all alias/newtype symbols (edges point from each symbol to the symbols it references)
   - Walks the graph in topological order (leaves first)
   - Resolves each alias/newtype's target using its declaration-site scope
   - Detects cycles (existing `_typeAliasResolutionStack` mechanism preserved; extended to cover newtypes)
   - After this pass, every alias symbol has a populated `DeclaredType` (TypeAliasType or RefinementType) and every newtype symbol knows its source type identity (but members are NOT yet cloned)

3. **Update `Pass1_ResolveBaseTypes` to unwrap type forwards**: After aliases/newtypes are resolved, the base type resolution pass must unwrap `TypeAliasType` and `NewtypeType` when it encounters them. Currently the code checks `resolvedSymbol.DeclaredType is ClassType` — it needs to also handle the case where `DeclaredType` is a `TypeAliasType` whose `UnderlyingType` is a `ClassType` (same for `NewtypeType`). Same treatment for interface lists and struct base types.

4. **Introduce Phase 4 — Global Explicit Member Type Resolution**: After inheritance is resolved (existing Pass 1b through 1g), add a new pass that iterates ALL compilation units and resolves ONLY explicit type annotations on members:
   - Field declared types (via `ResolveTypeReference` on field type annotations)
   - Property declared types (block properties and expression-bodied property type annotations)
   - Method return types and parameter types (currently done by `PreResolveMethodSignature`)
   - Constructor parameter types
   - Indexer parameter and return types
   - Operator return types and parameter types
   - Cast operator target types and parameter types
   - Dependent type parameter constraints (e.g., resolving `int` in `<Size:int>`)
   - Extension block receiver types
   
   Each file's annotations are resolved using its declaration-site scope (built in phase 1).

5. **Introduce Phase 4b — Clone Newtype Members**: After all source types have their members fully typed, iterate all newtype symbols and clone members from their source types. This is the existing `CloneAllMembers` logic, just moved to a guaranteed-safe point in the pipeline.

6. **Guard against re-resolution**: Add early-return guards in the existing `ResolveFieldDeclaration`, `ResolveBlockPropertyDeclaration`, `PreResolveMethodSignature`, etc. to skip type resolution if `DeclaredType` is already populated (idempotency check). This ensures Pass 2 (phase 5) doesn't re-resolve types that phase 4 already handled.

7. **Declaration-site scope for type resolution**: In all phases, annotations are resolved using the scope built in phase 1 for their declaring file. This fixes the ad-hoc scope-supply issue — the scope is constructed from the file's own `using` declarations, namespace context, and global usings, not from whatever file happens to be driving resolution.

8. **Wire into the Build method**: 
   - Phase 1: File scope construction + type registration (existing Pass 1, augmented with eager scope building)
   - Phase 2: New type-forwards resolution pass inserted BEFORE `Pass1_ResolveBaseTypes`
   - Phase 3: Existing Pass 1b through 1g (inheritance, constraints, traits, with-blocks) — updated to unwrap aliases/newtypes
   - Phase 4: New explicit member type resolution pass inserted AFTER trait satisfaction / interface cycle passes and BEFORE `Pass2_RegisterExtensionMembers`
   - Phase 4b: New newtype member cloning pass after phase 4
   - Phase 5: Existing Pass 2 (expression resolution), with idempotency guards so it doesn't re-resolve already-typed members

9. **Preserve PreResolveMethodSignature in Pass 2**: Keep the existing `PreResolveMethodSignature` call within `ResolveClassDeclaration` as a no-op safety net (guarded by the `DeclaredType is not null` check). This ensures backward compatibility.

**Known gap — Newtype inheritance**: Currently newtypes cannot be inherited from (they produce `NewtypeType`, not `ClassType`). This should be addressed as a follow-up feature. Tracked in the task list as a TODO.

## Testing Strategy

### Validation Approach

The testing strategy follows a two-phase approach: first, surface counterexamples that demonstrate the bug on unfixed code, then verify the fix works correctly and preserves existing behavior.

### Exploratory Bug Condition Checking

**Goal**: Surface counterexamples that demonstrate the bug BEFORE implementing the fix. Confirm or refute the root cause analysis. If we refute, we will need to re-hypothesize.

**Test Plan**: Write multi-file Mist programs where File A declares explicitly-typed members and File B references them. Compile with files in an order that ensures B is processed before A. Observe that type inference fails with `null` DeclaredType lookups.

**Test Cases**:
1. **Cross-file field type**: File A has `field canvas: Canvas`, File B does `renderer.canvas.Draw()` — type lookup fails (will fail on unfixed code)
2. **Cross-file method return type inference**: File A has `FindAll<T>() -> List<T>`, File B infers from return type — inference fails (will fail on unfixed code)
3. **Cross-file property lambda target-type**: File A has `Handler: Func<Event, bool>`, File B assigns lambda — parameter inference fails (will fail on unfixed code)
4. **Cross-file interface method type**: Interface in File A, implementor in File B uses interface method return type for inference (will fail on unfixed code)

**Expected Counterexamples**:
- `DeclaredType` is `null` when accessed during expression resolution
- Type argument inference produces `ErrorType` or fails silently
- Possible causes: file ordering in the `asts` array, lack of global pre-resolution pass

### Fix Checking

**Goal**: Verify that for all inputs where the bug condition holds, the fixed function produces the expected behavior.

**Pseudocode:**
```
FOR ALL input WHERE isBugCondition(input) DO
  result := SemanticModelBuilder_fixed.Build(input)
  ASSERT allExplicitTypesResolved(result)
  ASSERT expressionInferenceSucceeds(result)
END FOR
```

### Preservation Checking

**Goal**: Verify that for all inputs where the bug condition does NOT hold, the fixed function produces the same result as the original function.

**Pseudocode:**
```
FOR ALL input WHERE NOT isBugCondition(input) DO
  ASSERT SemanticModelBuilder_original.Build(input) 
         = SemanticModelBuilder_fixed.Build(input)
END FOR
```

**Testing Approach**: Property-based testing is recommended for preservation checking because:
- It generates many single-file programs and multi-file programs with "safe" ordering automatically
- It catches edge cases where the new pass might accidentally change resolution behavior
- It provides strong guarantees that the semantic model is identical for non-buggy inputs

**Test Plan**: Compile the existing test corpus (all `.mist` files in `Test/` and sample apps) with both the original and fixed code. Assert identical `DeclaredType` values on all symbols and identical expression type maps.

**Test Cases**:
1. **Single-file preservation**: Compile each existing single-file test and verify identical semantic model output
2. **Standard library type preservation**: Verify stdlib types (Int32, String, List, etc.) resolve identically
3. **Generics preservation**: Verify generic type parameter resolution produces identical types
4. **Diagnostics preservation**: Verify error diagnostics for invalid programs are identical

### Unit Tests

- Test that Pass 1.5 populates `DeclaredType` on field symbols for all explicitly-typed fields
- Test that Pass 1.5 populates `DeclaredType` on property symbols for block and expression-bodied properties
- Test that Pass 1.5 populates method return types and parameter types
- Test that type aliases are resolved before field/property types that reference them
- Test that declaration-site scope is used (type resolvable via declaring file's `using` but not referencing file's `using`)
- Test cross-file ordering independence: same result regardless of file order in `asts` array

### Property-Based Tests

- Generate random multi-file programs with cross-file type references and verify all `DeclaredType` values are non-null before expression inference
- Generate random single-file programs and verify the fixed compiler produces identical semantic models to the original
- Generate random file orderings for the same set of files and verify compilation results are order-independent

### Integration Tests

- Compile the full sample apps (`Samples/`) and verify they produce identical output
- Compile multi-project scenarios with cross-project type references
- Verify that the compiler produces identical C# transpiler output for existing passing programs
