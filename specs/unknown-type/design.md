# Design Document

## Overview

This design introduces the user-facing `unknown` type (UnknownType) as a first-class concept in Mist. The new type is a true top type for **assignment into** but is deliberately restricted so that it cannot appear as the declared type of writable **storage** (fields, properties with backing fields, indexer elements, `new` expressions, generic-construction type arguments, constructor-constructed objects). Pattern matching (`is T t`) is the designated mechanism for narrowing from `unknown` back to a concrete type.

The design has four distinct concerns, each of which corresponds to a specific source subsystem:

1. **Rename the internal error sentinel.** The existing `UnknownType` class in the semantic model is an error sentinel (display name `"?"`, no `TypeKind`). It must be renamed to `ErrorType` so the name `UnknownType` can be reused for the user-facing top type.
2. **Model the user-facing UnknownType.** A new singleton `IType` with display name `"unknown"` and a new `TypeKind` value. Lowered to C# `object`.
3. **Write-reachability analysis on generic types.** A new semantic-model pass computes, for every member of every generic type, whether that member can transitively write into storage whose type is one of the enclosing type's type parameters. This classification drives member filtering on `Closed_Generic_With_Unknown` receivers.
4. **Covariant unknown widening.** A single new implicit conversion rule: `Foo<A₁, …, Aₙ> → Foo<B₁, …, Bₙ>` is allowed when for every position `i`, either `Aᵢ = Bᵢ` or `Bᵢ = unknown`.

Every other concern in the requirements document (storage prohibition, pattern narrowing, trait satisfaction, cast-operator integration, C# emission, diagnostics) is a localized follow-on to these four core pieces.

The key non-obvious design decision is the renaming of the existing `UnknownType` sentinel. Its current name collides with the user-facing `UnknownType` from the requirements, and the two have incompatible semantics (one has no `TypeKind` and throws on access; the other has a real `TypeKind` and is the top type). The rename is performed as the **first** phase of the implementation, before any new code is added, so that both concepts never co-exist under the same name.

## Architectural Context

The Mist compiler pipeline, from the perspective of this feature, consists of these components:

| Component                  | Location                                             | Role                                                                                |
| -------------------------- | ---------------------------------------------------- | ----------------------------------------------------------------------------------- |
| Grammar                    | `Source/Mist/Grammar/Mist.storm`, `keywords.storm`   | Adds the `UNKNOWN` keyword and extends the primitive-type references.               |
| Pretty_Printer             | `Source/Mist/Parsing/MistPrettyPrinter.cs`           | Prints `unknown` in all legal positions.                                            |
| Semantic_Model_Builder     | `Source/Mist/Semantics/Model/SemanticModelBuilder.cs`| Resolves `unknown` references to the UnknownType singleton; enforces Req 5/6/7.     |
| Reachability_Analyzer      | **new**, `Source/Mist/Semantics/Model/ReachabilityAnalyzer.cs` | Classifies every member of every generic type w.r.t. each of its type parameters. |
| Type_Checker               | `Source/Mist/Analysis/Builtin/TypeCheckerAnalyzer.cs`| Assignability, member filtering, pattern narrowing, cast integration.               |
| Emitter                    | `Source/Mist/Backends/CSharpTranspiler/CSharpEmitter.cs`, `CSharpTypeMap.cs` | Lowers `unknown` → `object`; emits covariant widening casts.                       |
| Syntax Highlighting        | `Editor/mist-syntax-highlighting/syntaxes/mist.tmLanguage.json` | Adds `unknown` to the primitive-type keyword pattern.                              |

The existing two-pass semantic model (Pass 1 registers types, Pass 2 resolves references) is extended with a new **Post-Pass 2** step — the Reachability Analyzer — that requires fully resolved member signatures and method bodies. Reachability runs after type resolution but before the Type Checker executes.

## Phase Map (guides task ordering)

The implementation is naturally ordered into five phases, each of which builds on the previous:

| Phase | Name                                        | Dependencies                     |
| ----- | ------------------------------------------- | -------------------------------- |
| A     | Rename `UnknownType` (sentinel) → `ErrorType` | None                             |
| B     | Introduce the user-facing UnknownType       | Phase A                          |
| C     | Reachability Analyzer + member filtering    | Phase B                          |
| D     | Covariant unknown widening + cast/trait integration | Phases B, C                |
| E     | Emission, pretty-printing, syntax, samples, docs | All of the above             |

Each phase is designed to leave the compiler in a fully-building state with the existing test suite passing.

---

## Phase A: Rename the Internal `UnknownType` Sentinel to `ErrorType`

### Why first

The rename is mechanical but cross-cutting. Doing it before any new code is added means that when Phase B introduces the **real** `UnknownType`, there is no risk of accidentally conflating the two, and reviewers can read Phase B in isolation without having to remember that "UnknownType" used to mean something different.

### What changes

| Item                                        | Before                                            | After                                          |
| ------------------------------------------- | ------------------------------------------------- | ---------------------------------------------- |
| File                                        | `Source/Mist/Semantics/Model/Types/UnknownType.cs` | `Source/Mist/Semantics/Model/Types/ErrorType.cs` |
| Class                                       | `UnknownType`                                     | `ErrorType`                                    |
| Singleton accessor                          | `UnknownType.Instance`                            | `ErrorType.Instance`                           |
| Display name                                | `"?"`                                             | `"?"` (unchanged)                              |
| `TypeKind`                                  | throws (no kind)                                  | throws (no kind)                               |

Semantics, display name, and usage are all preserved — only the symbol name changes.

### Scope of the rename

Based on a grep of the repository, `UnknownType` appears in three categories of locations:

1. `Source/Mist/Semantics/Model/SemanticModelBuilder.cs` — the vast majority. Used as the "bail-out" return value when type resolution fails, and as the "no expression type known yet" sentinel. This is the intended `ErrorType` semantics.
2. `Source/Mist/Analysis/Builtin/TypeCheckerAnalyzer.cs` and `OverloadResolver.cs` — `IsUnknown(t)` and `t is UnknownType` checks that short-circuit type-checking when an earlier stage already emitted a diagnostic.
3. Test code — the test suite inspects the sentinel directly.

All three are mechanically renamed. The public contract of `IType` does not change.

### Tool selection

Use the `semanticRename` tool to rename the class, then `smartRelocate` to move the file. Follow up with a full-solution `brazil-build` / `dotnet build` and full test run to confirm zero warnings and zero broken tests (Requirements 2.4 and 2.5).

### Migration of `IsUnknown` helpers

The existing `IsUnknown(IType)` helper in `TypeCheckerAnalyzer` that tests for the **error sentinel** must remain functionally identical, but it is renamed to `IsErrorType` (or kept private-named `IsUnknown` with a clarifying comment) so that Phase B can introduce a separate `IsUnknownType` for the **user-facing** UnknownType without naming conflict. Design choice: rename to `IsErrorType` for clarity. Every call site currently reading "is this an error we should ignore?" becomes clearer under the new name.

### Acceptance for Phase A

Phase A is complete when:

- `Source/Mist/Semantics/Model/Types/UnknownType.cs` no longer exists.
- `Source/Mist/Semantics/Model/Types/ErrorType.cs` exists with identical semantics to the original sentinel.
- `brazil-build release` produces zero warnings.
- The existing test suite passes with no behavioral changes.

---

## Phase B: Introduce the User-Facing UnknownType

### New type representation

Add a new singleton type class at `Source/Mist/Semantics/Model/Types/UnknownType.cs` (the newly-freed filename):

```csharp
public sealed class UnknownType : IType
{
    public static readonly UnknownType Instance = new();
    public string DisplayName => "unknown";
    public TypeKind Kind => TypeKind.Unknown;
    private UnknownType() { }
}
```

### New `TypeKind` value

The `TypeKind` enum in `Source/Mist/Semantics/Contracts/IType.cs` gains a new variant:

```csharp
public enum TypeKind
{
    Class,
    Struct,
    Interface,
    Trait,
    Unknown,  // new — the user-facing `unknown` top type
}
```

`TypeKind.Unknown` is a sentinel; switch statements that currently handle `{Class, Struct, Interface, Trait}` must be audited and extended. The audit list:

- `ConstructedType.MemberScope` — throws for non-{Class,Struct,Interface}; the throw remains correct because `UnknownType` is never the generic definition of a constructed type.
- Any other `switch` on `TypeKind` that defaults to throwing — each must explicitly handle `Unknown` (usually by returning an empty member list, or producing a filtering diagnostic).

### Singleton access pattern

Phase A and Phase B produce two distinct singletons:

| Singleton             | Class        | Meaning                             | Access pattern                            |
| --------------------- | ------------ | ----------------------------------- | ----------------------------------------- |
| Error sentinel        | `ErrorType`  | Type resolution failed or cascaded  | `ErrorType.Instance`                      |
| User-facing top type  | `UnknownType`| Spelled `unknown` in source         | `UnknownType.Instance`                    |

Code that needs to ask "should I cascade-suppress here?" uses `IsErrorType(t)`. Code that needs to ask "is this the Mist `unknown` top type?" uses `t is UnknownType` or a dedicated `IsUnknownType(t)` helper.

### Grammar changes

In `keywords.storm`:

```
UNKNOWN: "unknown"
```

In `Mist.storm`, the `TypeReference` production gains a new alternative for the primitive-like spelling, producing a new AST node kind (e.g., `UnknownTypeReferenceNode`) analogous to `IntTypeReferenceNode`. The AST node carries only a source span.

Reserved-word behavior is handled by the lexer: because `unknown` is now a token, the parser naturally rejects it in identifier positions. This satisfies Req 1.4 without additional validation logic.

### Pretty-printer changes

In `MistPrettyPrinter`, extend the type-reference printer to emit `unknown` when the AST node is the new `UnknownTypeReferenceNode`. Round-trip is handled by construction: the token is a single keyword and there is no whitespace sensitivity.

### Semantic model resolution

In `SemanticModelBuilder.ResolveTypeReference`, add a case for `UnknownTypeReferenceNode` that returns `UnknownType.Instance`. This is the single integration point for Req 3.6.

### Assignability rule (Req 4)

In `TypeCheckerAnalyzer.IsAssignable`, add an early clause, **after** the existing `IsErrorType` cascade-suppression check and **before** all other rules:

```
if target is UnknownType:
    if source is VoidType: return false          // Req 4.2
    return true                                  // Req 4.1, 4.3, 4.5
```

And symmetrically, a rule that rejects assigning from `unknown` to a non-`unknown` non-`object` target:

```
if source is UnknownType and target is not UnknownType:
    return false                                 // Req 4.4
```

The final clause is subtle: existing "implicit cast operator fallback" must not rescue it, because UnknownType has no member scope. This is naturally satisfied — UnknownType has no members, so no cast operators are visible on it — but the invariant should be reinforced by an early-exit.

### Storage prohibition (Req 5) — the storage rule cascade

The storage prohibition is conceptually simple ("`unknown` may not appear at a storage position") but the recursive formulation matters.

Define `IsStorageBanned(t: IType): bool`:

```
IsStorageBanned(t) =
    match t with
    | UnknownType              → true
    | ArrayType(element)       → IsStorageBanned(element)
    | ConstructedType(def, args) →
          // For each position i in args, substitute into def and recurse.
          exists i in 0..args.Length-1:
              IsStorageBanned(SubstituteAtPosition(def, args, i))
    | otherwise                → false
```

Where `SubstituteAtPosition(def, args, i)` returns the effective type of position `i` in the type arguments **after** applying the type parameter substitution at that position. This handles the recursive case `List<Optional<unknown>>`: the outer `List<T>` has `T = Optional<unknown>`, which is itself a `ConstructedType(Optional, [unknown])`, whose first position is directly `unknown`, triggering the ban.

Every field/property/indexer declaration in Pass 1 is checked via `IsStorageBanned(declaredType)`, and the appropriate diagnostic is emitted.

`new` expressions are checked in Pass 2 / the Type Checker using the same predicate applied to the constructed type.

### Inheritance prohibition (Req 6)

When resolving base clauses in Pass 1b, check whether any base or interface reference resolves to `UnknownType`. If so, emit the appropriate diagnostic and drop the reference from the type's base/interface list.

### Closed_Generic_With_Unknown (Req 7)

A `ConstructedType` with any `unknown` argument is a valid type expression in non-storage positions. The storage-ban rule above is what rejects it when it would land at a storage position. No new representation is needed; the existing `ConstructedType` already carries its type arguments and is produced uniformly by `ResolveTypeReference`.

### Phase B acceptance

- Source like `unknown x = 42;` parses, resolves, type-checks, and emits `object x = 42;`.
- Source like `class Foo { unknown f; }` emits a diagnostic.
- Source like `List<unknown>` can be declared as a local/parameter/return type but rejected as a field type.

---

## Phase C: Reachability Analyzer

The Reachability Analyzer is the heart of the feature. It decides, for every member of every generic type, whether calling that member could write into storage of one of the enclosing type's type parameters.

### IR: operates on the semantic model, not the AST

The analyzer runs on the **semantic model** rather than the raw AST. Rationale:

- Method body expressions and their types are available only after Pass 2. Reachability needs to know the type of every assignment target.
- Interprocedural analysis requires stable symbol identity for callee lookup; symbols are the Pass-1-registered objects.
- Generic substitution (e.g., transitively knowing that a call into `this.BaseMethod<int>()` does or does not write T) requires the resolved types.

The analyzer walks the member scope of each generic type definition, and for each method body, walks statements and expressions via the semantic model's `expressionTypes` map.

### Pass ordering

The new analyzer is invoked from `SemanticModelBuilder.Build` as a **post-Pass-2** step, after `ComputeDefaultability` and `Pass1_CheckMemberRedeclarations`. It requires:

- Complete expression-type map.
- Complete member signatures.
- Complete base type and interface resolution.

```
... existing Post-Pass 2 sequence ...
Pass1_CheckMemberRedeclarations(...);
Pass1_CheckDuplicateSignatures(...);
// NEW:
reachabilityMap = new ReachabilityAnalyzer().Analyze(symbolTable, expressionTypes);
// then stored on the SemanticModel for later lookup by the TypeChecker.
```

### Classification output shape

For each generic type parameter `T` of each generic type `G`, the analyzer produces:

```
ReachabilityMap : (memberSymbol, typeParameter) → MemberClassification
MemberClassification = { ReadWrites, WriteWrites, BothWrite }
```

Where:

- **Field**: tracked as `(readClassification, writeClassification)`. Reading a field never writes; writing a field of type T (or transitively T) writes T.
- **Property / Indexer**: also tracked as a `(read, write)` pair (Req 9.11). A property's read-classification considers only the getter body; its write-classification considers the setter body (or, for an auto-property with a T-typed backing, is automatically `WritesT`).
- **Method**: tracked as a single write-classification per type parameter — methods do not have distinct read/write sides. (A method read-classification would be `NeverWrites` by definition.)
- **Constructor**: always `WritesT` with respect to every type parameter that appears as a parameter type or in the type of any field initialized in the body (Req 9.7). For the purposes of Req 10.5 this is moot — constructor invocations on `Closed_Generic_With_Unknown` are rejected outright by the Req 5.7 storage ban — but the classification is still produced for completeness and for uniform invocation through traits.

### Fixed-point algorithm

Mutually recursive methods force an iterative algorithm. Pseudocode:

```
Algorithm: ClassifyAll(G: Generic_Type_Declaration)

1. Initialize: for each member m of G, for each type parameter T of G:
       classification[m, T] = NeverWrites

2. Build call graph: for each method body b of G, extract the set of
   method-call targets. For each call, record the target member symbol
   (or "unknown callee" if dynamic dispatch precludes static resolution).

3. Build write-site set: for each method body b, for each assignment
   expression whose target is a field/element/property whose declared
   type transitively contains T, record (b, "writes T").

4. Iterate to fixed point:
       changed = true
       while changed:
           changed = false
           for each member m of G:
               for each type parameter T of G:
                   newClass = NeverWrites
                   if any write-site in m writes T: newClass = WritesT
                   for each call (m → callee) in call graph:
                       if classification[callee, T] == WritesT:
                           newClass = WritesT
                           break
                       if callee is "unknown" and argument-type transitively contains T:
                           newClass = WritesT
                           break
                   if newClass != classification[m, T]:
                       classification[m, T] = newClass
                       changed = true

5. Return classification.
```

This is the standard ascending-lattice fixed-point algorithm. Because the lattice has height 2 (`NeverWrites < WritesT`), termination is guaranteed in a bounded number of iterations.

Read-classifications for properties are computed by the same algorithm but with the write-site set restricted to the getter body only.

### Transitive type containment

The "T appears at a Storage_Location position in type X" test is a recursive structural check on `IType`:

```
ContainsTAtStoragePosition(t, T) =
    match t with
    | T                          → true
    | ArrayType(element)         → ContainsTAtStoragePosition(element, T)
    | ConstructedType(def, args) → exists i: ContainsTAtStoragePosition(args[i], T)
    | otherwise                  → false
```

This is used in step 3 (is this assignment writing T?) and in step 4 (for unknown callees, does the argument type contain T?).

Note the symmetry with `IsStorageBanned` from Phase B: `IsStorageBanned(t)` is exactly `ContainsTAtStoragePosition(t, UnknownType)` viewed as structural containment of the UnknownType singleton. In the implementation, both share a generic `ContainsType(haystack, needle)` helper.

### Integration with the existing Pass 1 / Pass 2 model

The analyzer runs as a new Post-Pass-2 step (see "Pass ordering" above). It does not modify the semantic model in place; it produces a `ReachabilityMap` that the `TypeCheckerAnalyzer` consults during member-access type checking.

The map is stored on the `SemanticModel` as a new property:

```csharp
public sealed class SemanticModel {
    ...
    public IReachabilityMap ReachabilityMap { get; }
}
```

And threaded to `TypeCheckerAnalyzer` through the existing `ISemanticModel` parameter on `IsAssignable` and the analyzer context.

### When are classifications computed for closed generics?

Classifications are computed **per generic definition**, not per closed generic. When the Type Checker needs to know whether `List<unknown>.Add` is filtered, it:

1. Looks up the classification of `List.Add` with respect to `List`'s type parameter `T`.
2. Finds `WritesT`.
3. Observes that in the receiver `List<unknown>`, the type argument at `T`'s position is `UnknownType`.
4. Filters the member.

This means the reachability map is computed **once** for each generic definition, not re-computed for each closed variant. Computational cost is bounded by the size of the generic definition, not by the number of closures.

### External (C#-defined) callees

Calls to methods defined in external C# types are handled per Req 9.9: treated as `WritesT` if any argument's type transitively contains T, else `DoesNotWriteT`. This is a conservative over-approximation that requires no inspection of the external method body.

### Virtual / trait dispatch

Per Req 9.8, dynamic dispatch is handled conservatively: if the callee cannot be fully determined statically, the call is treated as `WritesT` when any argument's type transitively contains T. This is the same rule as for external callees.

### Diagnostics from Phase C

The Reachability Analyzer itself emits no diagnostics — it only produces the classification map. All user-facing diagnostics from Req 10 are emitted by the Type Checker during member-access type checking, using the map as a lookup.

### Phase C acceptance

- For `List<T>`, the analyzer classifies `Add(T)` as `WritesT` and `Count` (read) as `DoesNotWriteT`.
- For `Map<K, V>`, `Set(K, V)` is classified as `WritesV` (and also `WritesK` if keys are stored) and `Get(K)` as `DoesNotWriteV`.
- Mutually recursive methods that eventually write T are all classified `WritesT`.

---

## Phase D: Covariant Unknown Widening, Cast Operators, Traits, Pattern Narrowing

### Covariant Unknown Widening (Req 8) — restricted to class generics

**Scope restriction**: Per Req 8.1 and 8.6, covariant unknown widening is only allowed when the generic definition has `TypeKind.Class`. Widening a struct generic like `Optional<int>` to `Optional<unknown>` is rejected at compile time with a dedicated diagnostic (see Diagnostic ID Allocation). This restriction is what allows the emission strategy described in Phase E (a reference cast through `object`) to be sound — it would be unsound for value-type layouts.

The rule is added to `TypeCheckerAnalyzer.IsAssignable` as a new clause in the `ConstructedType → ConstructedType` branch:

```
if source is ConstructedType(defS, argsS) and target is ConstructedType(defT, argsT):
    if defS == defT and argsS.Length == argsT.Length:
        // Existing identity rule
        if all i: argsS[i] == argsT[i]: return true
        // NEW: covariant unknown widening (Req 8.1, 8.6)
        if defS.Kind == TypeKind.Class
           and all i: argsS[i] == argsT[i] OR argsT[i] is UnknownType:
               return true
        // If any position widens to UnknownType but defS is a struct,
        // emit MIST_TC_0058 (Req 8.6) at the widening site rather than
        // silently rejecting via IsAssignable.
    // ... fall through to inheritance, interface, trait satisfaction ...
```

The struct-generic rejection diagnostic (`MIST_TC_0058 (historical)`) is produced by the Type Checker at the **widening site** (assignment, argument pass, return) rather than inside `IsAssignable`, because `IsAssignable` returns a bool and the user needs a specific explanation. The Type Checker inspects the source/target pair and, if the only thing preventing assignment is the struct-generic restriction, emits `MIST_TC_0058 (historical)` instead of the generic type-mismatch diagnostic.

Transitivity (Req 8.2) is already provided by the existing loop that traverses base types and interfaces — each recursive call to `IsAssignable` picks up the covariant rule on its own.

Nested positions (Req 8.3) are handled by making the rule **recursive** on type arguments. Nested widening only needs reference-covariance at the nested position, so nested widening is available even when the **outer** generic is a struct — what is prohibited is widening the struct's own type arguments:

```
// Position-wise check that allows recursion into nested generics.
// Enforces the class-only rule at each generic-definition boundary
// that is actually being widened.
AssignableWithUnknownWidening(defCtx, argsS, argsT) =
    all i:
        argsS[i] == argsT[i]
        OR (argsT[i] is UnknownType and defCtx.Kind == TypeKind.Class)
        OR (argsS[i] is ConstructedType(dS, aS)
            and argsT[i] is ConstructedType(dT, aT)
            and dS == dT
            and AssignableWithUnknownWidening(dS, aS, aT))
```

The `defCtx` parameter carries the generic definition whose type arguments are being compared at this level, so that the class-only rule is enforced only where a widening is actually happening.

Example: `Optional<List<int>>` → `Optional<List<unknown>>` is allowed even though `Optional` is a struct, because the widening is at the inner `List<T>` position and `List` is a class. The outer struct's arguments are checked with identity (`List<int>` vs `List<unknown>`), and inside that comparison the inner `List` widening fires under the class rule.

### Reverse direction not introduced (Req 8.5)

The rule is one-directional by construction: we only allow `argsT[i] is UnknownType`, never `argsS[i] is UnknownType`. No additional guard is needed.

### Interaction with `IsDirectlyAssignable` (Req 8.4, Req 13.5)

The existing `IsDirectlyAssignable` helper in `TypeCheckerAnalyzer` is used by `CheckCastExpression` to determine "trivially-valid" casts. Two unknown-type rules are added:

1. **Covariant Unknown Widening** (Req 8.4) — a cast expression from `List<int>` to `List<unknown>` is trivially valid without requiring a cast operator.
2. **Top-type direct assignability** (Req 13.5, Task 26.2) — a cast `(unknown) expr` for any non-`void` `expr` is trivially valid. Before Task 26, this rule lived only in `IsAssignable`, which caused the cast-expression path to search for a `(unknown) from (T)` operator and emit `MIST_TC_0042 (historical)` / `MIST_TC_0067 (historical)` when none was found. The Task 26 addition mirrors the `IsAssignable` top-type clause in `IsDirectlyAssignable` so both paths agree.

### Cast Operator Integration (Req 13.4, Req 15.4) — from `unknown` to `T`

Casts **from `unknown` to `T`** are handled by extending the cast-operator resolution logic (specified in the cast-operators feature, implemented in `TypeCheckerAnalyzer.CheckCastExpression`) with a dedicated `CheckCastFromUnknown` branch that fires when the operand's static type is `UnknownType` and the target is not.

The critical design decision: where are `(unknown → T)` cast operators **declared**?

**Design**: Per the cast-operators feature's Requirement 6.2, a cast operator declared in class `C` must have either its source parameter or its target type equal to `C`. A cast from `unknown` to `T` therefore requires a cast operator declared in type `T` with source parameter `unknown`. Example:

```
class MyInt {
    implicit operator MyInt(unknown u) { ... }
}
```

Because `UnknownType` has no member scope (Req 3.4), `CheckCastFromUnknown` searches only `T`'s member scope for `operator_implicit_{T}` / `operator_explicit_{T}` symbols. The unknown-source branch and the normal concrete→concrete branch differ in one respect: when no candidate is found, `CheckCastFromUnknown` emits the targeted `MIST_TC_0067 (historical)` diagnostic ("no cast operator from `unknown` to T") rather than the generic `MIST_TC_0042 (historical)`. The targeted diagnostic names `T` explicitly and suggests either declaring the operator or using an `is` pattern to narrow first, which matches the user-facing guidance in Req 16.4.

### Casts **into `unknown`** (Req 13.5)

A cast `(unknown) expr` is always valid because UnknownType is the top type (Req 4.1). The Type Checker accepts it without searching for a cast operator. The Emitter lowers it as a direct C# cast to `object` (Req 15.4).

### Cast Expression Emission

The Emitter's cast lowering branches on the resolved cast operator:

| Source       | Target       | Resolution                          | Emission                                  |
| ------------ | ------------ | ----------------------------------- | ----------------------------------------- |
| Any          | UnknownType  | No operator lookup                  | `(object)(expr)`                          |
| UnknownType  | Concrete T   | Must resolve to user-defined operator | `T.op_Explicit(expr)` or `T.op_Implicit(expr)` |
| UnknownType  | T, but no op | Diagnostic `MIST_TC_0067 (historical)` emitted   | No emission (error)                       |

### Trait Satisfaction with UnknownType (Req 12)

Trait matching is performed by the existing `TraitSatisfactionMap` builder in Pass 1g. It compares each candidate concrete-type member's signature to each trait member's signature for a structural match.

The new rules (Req 12.1, 12.2) modify the **return-position** compatibility check so that:

- A trait return type of `UnknownType` matches any non-`void` concrete return type.
- A trait return type of `ConstructedType(def, argsT)` matches a concrete return type `ConstructedType(def, argsS)` when `AssignableWithUnknownWidening(argsS, argsT)`.

Parameter positions remain exact-match (Req 12.3) — no unknown widening at parameter positions. Implementation: in the trait-match predicate, the return-type branch is relaxed via the new assignability rule; the parameter-type branch is unchanged.

Req 12.5 requires that the Reachability Analyzer's classifications also apply to trait receivers. Implementation: when the Type Checker sees a member access on a trait-typed receiver, and the trait's signature contains `UnknownType` at one or more type-parameter-like positions, the Type Checker filters the trait's members using the same reachability logic applied to a generic type. For v1, traits do not carry type parameters, so this reduces to filtering any trait member whose return or parameter types mention UnknownType — specifically, filtering trait members whose classification is `WritesT` where `T` is a position occupied by UnknownType.

### Pattern Matching Narrowing (Req 11)

Pattern matching infrastructure already exists. The only additions are compile-time rejections:

1. `x is unknown u` — the Type Checker emits `MIST_TC_0063 (historical)` because the pattern is tautologically true and the binding has no more specific type than the scrutinee (Req 11.3). (Reallocated from the originally-proposed `MIST_TC_0051 (historical)`; see the Diagnostic ID Allocation note on numbering.)
2. `x is Foo<unknown> fu` — the Type Checker emits `MIST_TC_0064 (historical)` because the pattern is tautologically true under Covariant_Unknown_Widening and the binding loses information (Req 11.4). (Reallocated from the originally-proposed `MIST_TC_0052 (historical)`; see the same note.)

Implementation: both checks happen in the existing `CheckPrimaryPattern` method of `TypeCheckerAnalyzer`, alongside the `PatternUndefinedType` (`MIST_TC_0050 (historical)`) check. The order is:

```
if patternType is null or IsErrorType(patternType):
    existing undefined-type diagnostic (MIST_TC_0050)
else if patternType is UnknownType:
    diagnostic MIST_TC_0063 (narrow-to-unknown)
else if patternType is ConstructedType and any arg transitively contains UnknownType:
    diagnostic MIST_TC_0064 (narrow-to-closed-with-unknown)
else:
    existing narrowing logic (binding with the concrete type)
```

The "transitively contains UnknownType" check recurses through nested `ConstructedType` and `ArrayType` arguments, so `Optional<List<unknown>>` is also rejected even though the outer `Optional` has no direct `unknown` argument.

Note: `x is Foo<unknown> fu` is syntactically legal in a pattern; the diagnostic is the semantic rejection. The AST carries `unknown` naturally via the grammar change in Phase B.

### Bare-unknown operator rejection (Req 13.1, 13.2)

Member access and operator application on a bare-`unknown`-typed operand are rejected in Pass 2 / the Type Checker:

- **Member access**: When resolving a member access `x.M`, if `TypeOf(x) is UnknownType`, emit the diagnostic and return `ErrorType.Instance`.
- **Operators**: When resolving a binary or unary operator, if any operand is `UnknownType`, emit the diagnostic.

### Phase D acceptance

- `List<int>` assigns to `List<unknown>` without diagnostic.
- `List<unknown>` does **not** assign to `List<int>`.
- `x.M` where `x: unknown` emits MIST_TC_* (historical scheme) (suggests `is` pattern).
- `(int) x` where `x: unknown` requires an `implicit operator int(unknown)` declared somewhere; otherwise emits `MIST_TC_0067 (historical)`.
- `x is unknown u` emits narrow-to-unknown diagnostic.
- `x is List<unknown> lu` emits narrow-to-closed-with-unknown diagnostic.

---

## Phase E: Emission, Pretty-Printing, Syntax Highlighting, Samples, Docs

### C# emission — the `unknown` → `object` mapping

Every Mist reference to UnknownType is emitted as C# `object`. This is implemented in `CSharpTypeMap`:

```csharp
public string EmitType(IType t) =>
    t switch {
        ...
        UnknownType => "object",
        ...
    };
```

All Mist-level type references that reduce to UnknownType (directly or after substitution) emit `object`.

### The delicate emission concern: Covariant Unknown Widening

The requirements note: "WHEN emitting a covariant widening conversion, rely on C# reference-type covariance and explicit casts."

The key observation: **C# `List<T>` is not covariant in `T`**. In C#, `List<int>` is NOT assignable to `List<object>`, even with a cast. Only interface types marked `out T` (e.g., `IEnumerable<out T>`) are covariant.

Three possible strategies:

**Option 1: Do nothing and hope.** Emit `List<object> xs = intList;` directly. The C# compiler will reject this. ❌ Incorrect.

**Option 2: Explicit C# cast.** Emit `List<object> xs = (List<object>)(object)intList;` — the intermediate `(object)` cast via a reference upcast. C# accepts this, but the cast is unchecked at runtime and would fail if any subsequent operation tried to treat it as a `List<object>` in a way that required actual covariance. Since the Reachability Analyzer ensures that all writing operations on the closed-with-unknown receiver are **filtered out at compile time**, the C# runtime will never observe an incompatible write. Read operations (`Count`, indexer getter, `GetEnumerator`) are covariant-safe for `List<T>` at runtime because they don't access the variance-sensitive slots in a way that matters. ⚠️ Potentially correct but fragile and relies on runtime details of specific collection types.

**Option 3: Wrapper/adapter emission.** Emit a runtime conversion helper that produces a read-only view, e.g., `List<object> xs = intList.ConvertAll(x => (object)x);` or a custom `ReadOnlyUnknownView<T>` wrapper. ⚠️ Adds runtime cost; changes reference identity.

**Option 4: Change the C# type.** Emit `Closed_Generic_With_Unknown` values using C# `IEnumerable<object>` or `IReadOnlyCollection<object>` instead of `List<object>`. This uses C#'s native covariance at interface level. But it loses access to all non-`IEnumerable` members even when those members are reachability-safe. ❌ Loses too much access.

**Chosen approach (revised November 2026): Option C — lower Closed_Generic_With_Unknown to C# `dynamic`.**

**Original choice (Option 2) and its failure mode.** The design originally emitted the double cast `(List<object>)(object)intList`. Empirical testing showed this throws `InvalidCastException` at runtime for user-defined generics because the CLR enforces strict generic type identity — no IL-level coercion exists from `List<int>` to `List<object>` for non-interface, non-delegate, non-array types. The original rationale that "the C# runtime tolerates the reference cast because the underlying object remains a `List<int>`" was factually incorrect: the cast operation itself fails, regardless of what the underlying object is.

**Option C rationale.** Lowering Closed_Generic_With_Unknown to C# `dynamic`:

1. Removes the invalid cast entirely. Assigning any value to a `dynamic` target is always accepted at compile time and succeeds at runtime with no coercion — `dynamic` is `object` plus DLR metadata.
2. Routes member access on the widened receiver through the DLR, which dispatches against the receiver's actual runtime generic type. A call like `xs.Size()` on `dynamic xs` (underlying `List<int>`) dispatches to `List<int>.Size()` directly.
3. Preserves reference identity (the underlying object never changes).
4. The Reachability Analyzer's compile-time guarantee still holds — the DLR never sees a write-path member call on a Closed_Generic_With_Unknown receiver because the Mist type checker rejects those calls with `MIST_TC_0062 (historical)`.

Concretely, the Emitter uses:

```csharp
// Mist:  List<unknown> xs = intList;
// C#:    dynamic xs = intList;

// Mist:  Map<string, unknown> m = stringIntMap;
// C#:    dynamic m = stringIntMap;

// Mist:  unknown x = 42;              (bare unknown; not a closed generic)
// C#:    object x = 42;
```

Note the distinction: bare `unknown` still lowers to `object` (not `dynamic`) because the type checker rejects member access and operator dispatch on a bare-`unknown` receiver at compile time (MIST_TC_0065 (historical) / MIST_TC_0066 (historical)). The DLR never sees such a receiver, so `object` is sufficient. `dynamic` is reserved for Closed_Generic_With_Unknown receivers, where it is strictly necessary because read-path member access is permitted by the type checker (e.g., `xs.Size()` on `xs : List<unknown>`).

**Requirement 15.2 restated under Option C.** The emitter no longer "inserts an explicit C# cast" at widening sites. Instead, it lowers the target's declared type to `dynamic` at every widening site involving a Closed_Generic_With_Unknown, and emits the operand unchanged. The implicit conversion from any reference/value type to `dynamic` is handled by the C# compiler.

**Costs and trade-offs of Option C:**

- **Performance**: dynamic call sites add DLR overhead (on the order of nanoseconds to microseconds per call after the DLR binding tree is warmed). Suitable for most application code; avoid Closed_Generic_With_Unknown receivers in tight hot loops.
- **C# type checking at widened receivers**: reduced. `dynamic` defers member resolution to runtime; the C# compiler no longer catches typos like `xs.Sze()` at compile time. Mist's own type checker does catch them (it runs against the full closed generic definition).
- **RuntimeBinderException risk**: a malformed emitted call that passes the Mist type checker but fails DLR lookup produces a `RuntimeBinderException` at runtime. In practice, the Mist type checker's member resolution is faithful to the closed generic's public API, so this should not occur; defensive integration tests catch any regression.
- **Dependencies**: the generated C# requires `Microsoft.CSharp.dll`, `System.Linq.Expressions.dll`, and `System.Dynamic.Runtime.dll`. The Mist CLI's in-process Roslyn driver resolves these to the SDK's reference-assembly pack at compile time. The emitted `.csproj` declares a `PackageReference` to `Microsoft.CSharp` for users who build the emitted C# directly.

**Design check / discovery**: The Emitter distinguishes bare `UnknownType` (lowers to `object`) from Closed_Generic_With_Unknown (lowers to `dynamic`) in a single place: `CSharpTypeMap.GetTypeName` inspects the type-argument list of any `ConstructedType` and returns `"dynamic"` when unknown is transitively present.

**Historical alternatives considered and rejected:**

- **Option 1**: Emit `List<object> xs = intList;` directly. Rejected: C# compiler rejects this (no implicit conversion).
- **Option 2 (original)**: Emit `(List<object>)(object)intList`. Rejected after implementation: runtime `InvalidCastException`. See failure discussion above.
- **Option 3**: Runtime conversion helper (element-wise copy). Rejected: changes reference identity; requires knowledge of the generic's constructor shape; adds per-widening-site O(n) runtime cost.
- **Option 4**: Change the C# type (e.g., `IEnumerable<object>`). Rejected: loses access to non-`IEnumerable` members even when those members are reachability-safe.

### Surfaced Discovery (open concern)

The Option 2 approach **depends on** the Reachability Analyzer correctly filtering every write path. If the analyzer is conservative in the *wrong direction* (classifying a true write as `DoesNotWriteT`), the emitted cast would allow a genuine variance violation at runtime, producing a C# `ArrayTypeMismatchException` or similar.

**Soundness claim**: The analyzer is conservative in the **safe** direction — when uncertain (dynamic dispatch, external callees), it classifies `WritesT`, thereby *over-filtering* members. Under-filtering can only occur through an analyzer bug, not through the analyzer's design. Mitigation: every write-classification rule is implemented as a fail-open default (assume `WritesT` in ambiguous cases).

### Other emission concerns

- **`is` patterns**: Mist `foo is Optional<int> oi` emits C# `foo is Optional<int> oi` directly. C# preserves full generic type metadata at runtime, so the pattern match tests the closed constructed type, not the erased form.
- **Cast expressions**: See Phase D's table above.
- **Storage declarations**: By the time the Emitter runs, all storage-ban violations have been reported and the declarations still exist in the AST. Design: the Emitter **skips** emission of any field/property whose declared type contains UnknownType at a storage position. This produces compilable C# even for programs that contain diagnostics — the Mist compiler's existing policy is to emit "best-effort" output when diagnostics are present. This avoids secondary C# compile errors.

### Pretty-Printer

Straightforward. `MistPrettyPrinter` already prints primitive-type keywords; add `unknown` to the keyword-formatter.

### Syntax Highlighting

`Editor/mist-syntax-highlighting/syntaxes/mist.tmLanguage.json` — add `unknown` to the primitive-type keyword pattern (`\\b(int|bool|string|...)\\b`). Scoped as `keyword.type.primitive.mist`.

### Sample Applications

Two samples:

1. `Samples/UnknownType/` — standalone sample covering all feature areas.
2. `Samples/AllFeatures/UnknownType.mist` — new file in the existing comprehensive sample.

Both compile and run as part of the standard sample test suite.

### CSHARP_FEATURES.md

A new `unknown` entry in the Reference Types section with a short prose description:

- Top-type assignability.
- Storage prohibition.
- Reachability-based member filtering on closed generics.
- Pattern-matching narrowing as the designated escape.

Summary-table counts are updated.

---

## Diagnostic ID Allocation

Existing ranges in use:
- `MIST_SB_0030 (historical)–0043` — Semantic Model Builder (intrinsics, operators, cast operators)
- `MIST_TC_0031 (historical)–0044` — Type Checker (overloads, cast operators)

New range allocated for UnknownType feature:

| ID              | Requirement | Description                                                     | Emitted by              |
| --------------- | ----------- | --------------------------------------------------------------- | ----------------------- |
| `MIST_SB_0050 (historical)`  | Req 5.1     | `unknown` used as field type                                    | SemanticModelBuilder    |
| `MIST_SB_0051 (historical)`  | Req 5.2     | `unknown` used as property type                                 | SemanticModelBuilder    |
| `MIST_SB_0052 (historical)`  | Req 5.3     | `unknown` used as indexer element type                          | SemanticModelBuilder    |
| `MIST_SB_0053 (historical)`  | Req 5.7     | Generic construction with `unknown` argument                    | SemanticModelBuilder    |
| `MIST_SB_0054 (historical)`  | Req 6.1–4   | `unknown` used as base or implemented type                      | SemanticModelBuilder    |
| `MIST_SB_0055 (historical)`  | Req 5.5     | `new unknown[n]` — `unknown` as array element in new expression | SemanticModelBuilder    |
| `MIST_SB_0056 (historical)`  | Req 5.4     | `new unknown(...)` — attempting to instantiate `unknown`        | SemanticModelBuilder    |
| `MIST_SB_0057 (historical)`  | Req 5.6     | `default(unknown)` — `unknown` has no default                   | SemanticModelBuilder    |
| `MIST_TC_0062 (historical)`  | Req 10.1–4, 7 | Member filtered on Closed_Generic_With_Unknown receiver       | TypeCheckerAnalyzer     |
| `MIST_TC_0063 (historical)`  | Req 11.3    | Narrowing to `unknown`                                          | TypeCheckerAnalyzer     |
| `MIST_TC_0064 (historical)`  | Req 11.4    | Narrowing to Closed_Generic_With_Unknown                        | TypeCheckerAnalyzer     |
| `MIST_TC_0065 (historical)`  | Req 13.1    | Member access on bare `unknown`                                 | TypeCheckerAnalyzer     |
| `MIST_TC_0066 (historical)`  | Req 13.2    | Operator applied to `unknown` operand                           | TypeCheckerAnalyzer     |
| `MIST_TC_0067 (historical)`  | Req 13.4    | No cast operator from `unknown` to T                            | TypeCheckerAnalyzer     |
| `MIST_TC_0060 (historical)`  | Req 4.4     | `unknown` value assigned to non-`unknown` target                | TypeCheckerAnalyzer     |
| `MIST_TC_0061 (historical)`  | Req 4.2     | `void` expression assigned to `unknown`                         | TypeCheckerAnalyzer     |
| `MIST_TC_0058 (historical)`  | Req 8.6     | Covariant `unknown` widening attempted on a struct generic      | TypeCheckerAnalyzer     |

> **Note on numbering:** The initial design proposed `MIST_TC_0056 (historical)` and
> `MIST_TC_0057 (historical)` for the Req 4.4 and Req 4.2 assignability rejections, but
> those codes were already allocated to the pattern-matching feature
> (`PatternOrInconsistentVariables`, `PatternNotCannotBindVariable`). During
> Task 9.2 the unknown-type assignability diagnostics were reallocated to
> `MIST_TC_0060 (historical)` / `MIST_TC_0061 (historical)` to avoid overloading a diagnostic ID with
> two distinct meanings. The struct-generic-widening code (`MIST_TC_0058 (historical)`)
> was still free at the time of allocation and is retained as proposed.
> During Task 21 the member-filtering code originally proposed as
> `MIST_TC_0050 (historical)` was similarly reallocated to `MIST_TC_0062 (historical)` — the code
> was already in use by the pattern-matching feature's
> `PatternUndefinedType` diagnostic. During Task 24 the pattern-narrowing
> rejection codes originally proposed as `MIST_TC_0051 (historical)` and `MIST_TC_0052 (historical)`
> were reallocated to `MIST_TC_0063 (historical)` and `MIST_TC_0064 (historical)` for the same
> reason — those codes were already owned by the pattern-matching feature's
> `PatternConstantTypeMismatch` and `PatternRelationalNonComparable`
> diagnostics. During Task 25 the bare-unknown rejection codes originally
> proposed as `MIST_TC_0053 (historical)` and `MIST_TC_0054 (historical)` were reallocated to
> `MIST_TC_0065 (historical)` and `MIST_TC_0066 (historical)` for the same reason — those codes
> were already owned by the pattern-matching feature's
> `PatternUndefinedProperty` and `PatternListRequiresArray` diagnostics.
> During Task 26 the cast-from-unknown code originally proposed as
> `MIST_TC_0055 (historical)` was reallocated to `MIST_TC_0067 (historical)` for the same reason —
> that code was already owned by the pattern-matching feature's
> `PatternListTooManyElements` diagnostic (a warning). Merging an error
> into the same ID as a warning would make filter rules and severity
> overrides ambiguous, so the reallocation preserved both meanings.
> The reallocation preserves the contiguous unknown-type
> numbering (0058, 0060, 0061, 0062, 0063, 0064, 0065, 0066, 0067).

Diagnostic messages follow the existing convention: name the member/type involved, state the rule, and (where useful) suggest a remedy (e.g., "use `is` to narrow before accessing members").

---

## Testing Strategy

### Unit tests (new)

- `UnknownTypeAssignabilityTests` — in-memory IsAssignable() tests for Req 4 and Req 8.
- `ReachabilityAnalyzerTests` — in-memory analyzer tests, one test per classification rule (field, property, indexer, method, constructor, fixed-point iteration).
- `UnknownStorageProhibitionTests` — compiler-driver tests that each storage-ban variant emits the right diagnostic.
- `UnknownPatternNarrowingTests` — diagnostics for narrow-to-unknown and narrow-to-closed-with-unknown.
- `UnknownCastTests` — cast-from-unknown resolution and emission.

All new unit tests run fully in-process using `SharedDriver.Compile()`; no `dotnet` process spawns (per the test-process-spawning policy).

### Integration tests (existing pattern)

The existing sample-app test harness runs each `Samples/*/Program.mist` through the full pipeline and verifies console output matches the expected string. New `Samples/UnknownType/` and the new `UnknownType.mist` in `Samples/AllFeatures/` are picked up automatically.

### Property-based tests (new)

Candidate properties (to be finalized in tasks.md):

- **Round-trip property**: for any Mist AST containing `unknown` at any legal position, `parse(prettyPrint(ast)) == ast`.
- **Covariant widening preservation**: for any concrete Mist type `Foo<A₁, …, Aₙ>` with `n ≥ 1`, for any subset `S ⊆ {1..n}` of positions, widening `argsₛ[i] := unknown for i ∈ S` produces a type to which the original is assignable.
- **Reachability fixed-point monotonicity**: given a generic type `G`, the reachability map produced on any iteration is a pointwise under-approximation of the final fixed-point map (safety property of the iterative algorithm).

All PBTs use in-process compilation via `SharedDriver` with batched scenarios (a single compile + a single run per property, N scenarios compared line-by-line in output).

---

## Risk Register and Open Discoveries

1. **Reachability analyzer completeness (High)** — Any missed write path in the analyzer leads to runtime failures via DLR dispatch after the `dynamic` lowering. Mitigation: fail-open default in ambiguous cases; exhaustive unit tests covering every member shape; integration tests that actually run emitted C# code.

2. **C# covariance for non-interface generics (Resolved — Option C)** — The original Option 2 emission (`(List<object>)(object)intList`) was empirically shown to throw `InvalidCastException` at runtime for user-defined generics because the CLR enforces strict generic type identity — there is no IL-level coercion from `List<int>` to `List<object>` for non-interface, non-delegate, non-array types. The design's rationale ("the C# runtime tolerates the reference cast") was factually incorrect for user-defined generics. **Resolution (November 2026, applied)**: replaced Option 2 with **Option C** — `Closed_Generic_With_Unknown` lowers to C# `dynamic` rather than `Foo<object>`. Member access on the widened receiver routes through the DLR, which dispatches against the receiver's actual runtime generic type. The Reachability Analyzer (Phase C) still prevents write-path member calls on such a receiver at compile time, so the DLR only sees reachability-safe calls at runtime. Trade-off: dynamic dispatch overhead (nanoseconds–microseconds per call site) and reduced C#-layer type-checking at widened-receiver call sites. Mist-level type safety is fully preserved. Requires `Microsoft.CSharp.dll` + `System.Linq.Expressions.dll` + `System.Dynamic.Runtime.dll` references in the generated C#; the Mist CLI's in-process Roslyn driver resolves these to the SDK's reference-assembly pack. Integration-test coverage added: the `samples/UnknownType` baseline test compiles and runs the widened-receiver sample end-to-end, catching any regression that re-introduces the Option 2 runtime failure.

3. **Cast operators declared on `unknown` (Medium)** — Because `unknown` is a singleton with no member scope, casts from `unknown` to `T` must be declared on `T`, not on `unknown`. This is a natural restriction but differs from most cast-operator patterns where the operator is declared on the source type. Mitigation: documentation and sample show the pattern clearly.

4. **Reachability + inheritance (Medium)** — When a derived class inherits a method from a generic base, does the derived type re-classify or inherit the base's classification? Design: inherits, because the method body is the base's and unchanged. Verified via unit test.

5. **External C# callee conservatism (Low)** — Every argument passed to a C# method whose type contains `T` is treated as a write. This is conservative and may over-filter. Acceptable for v1.

6. **Parameter-list source-text shortcut drops later parameters (Medium, pre-existing, worked around)** — `CSharpEmitter.EmitParameterList` uses a source-text fast-path that copies the full parameter list verbatim when the first parameter's type needs no rewriting. When any parameter (including later ones) contains `unknown` or `void` as a keyword, the fast-path is bypassed and the emitter falls back to a single-parameter emission that drops later parameters. This is a pre-existing Storm-parser limitation (the AST only exposes the first parameter structurally). Impact on the unknown-type feature: methods with `unknown` in a non-first parameter position are affected. Mitigation: a textual word-boundary check on the parameter-list source text now catches `unknown`/`void` in later positions and routes to the fallback, producing a compilable — though potentially signature-truncated — output. The `samples/UnknownType` sample sidesteps this by keeping `unknown`-typed parameters at the first position. A full fix requires extending the Storm parser's `ParameterListNode` to expose all parameters, which is out of scope for this feature.

### Requirement Refinement — resolved and applied

**Discovery from design:** Option 4's risk analysis (item 2 above) shows that `Closed_Generic_With_Unknown` widening is only safe for **reference-type** generic definitions. For a value-type generic like `Optional<T>` (a struct), the C# emitted code `Optional<object> o = (Optional<object>)(object)optInt;` is *invalid C#* — it would require unboxing through `object` and C# rejects the pattern.

**Resolution (user-approved):** Req 8 is restricted to class-generic definitions. Specifically:

- Req 8.1 now requires the generic definition to have `TypeKind.Class` for widening to fire.
- Req 8.2 and 8.3 inherit the restriction.
- A new Req 8.6 requires a dedicated diagnostic when widening is attempted on a struct generic, so the failure is explanatory rather than a generic type-mismatch.

Struct generics with `unknown` type arguments remain legal as type expressions in non-storage positions (Req 7 is unchanged), but values of those types cannot flow in from widened concrete struct generics. This does not affect the core feature because Mist's reference-type collections (`List`, `Map`, `Set`) remain fully supported.

---

## Design Completeness Checklist

| Requirement                  | Covered by section                                          |
| ---------------------------- | ----------------------------------------------------------- |
| Req 1 (Grammar)              | Phase B → Grammar changes                                   |
| Req 2 (Rename)               | Phase A                                                     |
| Req 3 (Semantic model)       | Phase B → New type representation, Semantic model resolution |
| Req 4 (Assignability)        | Phase B → Assignability rule                                |
| Req 5 (Storage prohibition)  | Phase B → Storage rule cascade                              |
| Req 6 (Inheritance prohibition) | Phase B → Inheritance prohibition                        |
| Req 7 (Type argument)        | Phase B → Closed_Generic_With_Unknown, Storage rule cascade |
| Req 8 (Covariant widening)   | Phase D → Covariant Unknown Widening                        |
| Req 9 (Reachability)         | Phase C (entire)                                            |
| Req 10 (Member filtering)    | Phase C + Phase D (Type Checker integration)                |
| Req 11 (Pattern narrowing)   | Phase D → Pattern Matching Narrowing                        |
| Req 12 (Traits)              | Phase D → Trait Satisfaction                                |
| Req 13 (Bare-unknown)        | Phase D → Bare-unknown rejection + Cast Operator Integration|
| Req 14 (Pretty-printer)      | Phase E → Pretty-Printer                                    |
| Req 15 (Emission)            | Phase E (entire Emission section)                           |
| Req 16 (Diagnostic quality)  | Diagnostic ID Allocation + each phase's diagnostics         |
| Req 17 (Syntax highlighting) | Phase E → Syntax Highlighting                               |
| Req 18 (CSHARP_FEATURES.md)  | Phase E → CSHARP_FEATURES.md                                |
| Req 19 (Sample app)          | Phase E → Sample Applications                               |
| Req 20 (AllFeatures sample)  | Phase E → Sample Applications                               |

All 20 requirements have a design home.
