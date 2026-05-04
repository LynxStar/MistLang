# Implementation Plan

This plan implements the `unknown` type feature in five phases (A–E) that mirror the design document's phase map. Each phase is designed to leave the compiler in a fully-building state with the existing test suite passing, so the phase boundaries are also natural checkpoints. Tasks within a phase are listed in strict dependency order: no task references a construct that has not been introduced by an earlier task.

Testing is interleaved, not deferred: every implementation task is followed by unit and/or property-based tests that validate its specific rule. All tests use in-process compilation via a shared `CompilerDriver` (per the workspace `test-process-spawning.md` policy). No test spawns `dotnet` per iteration; property-based tests are batched (1 compile, 1 run, N scenarios compared line-by-line) when execution is required at all, and most PBTs run entirely in-process against `IsAssignable`, the semantic model, or the reachability map.

All implementation tasks must build with zero warnings (per `zero-warnings-policy.md`). Phase A includes a dedicated gating task — rebuild plus full test suite must pass before Phase B begins.

## Tasks

- [x] 1. Read the spec files and existing code
  - Read `.kiro/specs/unknown-type/requirements.md` in full
  - Read `.kiro/specs/unknown-type/design.md` in full
  - Read the existing `Source/Mist/Semantics/Model/Types/UnknownType.cs` (the sentinel to be renamed)
  - Skim `Source/Mist/Semantics/Model/SemanticModelBuilder.cs`, `Source/Mist/Analysis/Builtin/TypeCheckerAnalyzer.cs`, `Source/Mist/Analysis/Builtin/OverloadResolver.cs`, and `Source/Mist/Backends/CSharpTranspiler/CSharpTypeMap.cs` to locate every reference to `UnknownType`
  - Skim `Source/Mist/Grammar/Mist.storm`, `Source/Mist/Grammar/keywords.storm`, and `Source/Mist/Parsing/MistPrettyPrinter.cs` to locate primitive-type keyword and `TypeReference` machinery
  - _Requirements: (context only)_

---

### Phase A — Rename internal `UnknownType` sentinel to `ErrorType`

Phase A is purely mechanical. It runs first so Phase B can reuse the name `UnknownType` for the user-facing top type without conflict. Phase A ends with a gating task: rebuild + full test suite must pass before Phase B begins.

- [x] 2. Rename the `UnknownType` class to `ErrorType`
  - [x] 2.1 Rename the class using `semanticRename` at the declaration site in `Source/Mist/Semantics/Model/Types/UnknownType.cs`
    - Old name: `UnknownType`, new name: `ErrorType`
    - Verify that `UnknownType.Instance` accessor call sites are auto-updated to `ErrorType.Instance` across the solution
    - _Requirements: 2.1, 2.2_

  - [x] 2.2 Move the file using `smartRelocate`
    - Source path: `Source/Mist/Semantics/Model/Types/UnknownType.cs`
    - Destination path: `Source/Mist/Semantics/Model/Types/ErrorType.cs`
    - Verify no import/reference is broken by the move
    - _Requirements: 2.1_

  - [x] 2.3 Rename the `IsUnknown(IType)` helper in `TypeCheckerAnalyzer` to `IsErrorType(IType)`
    - Use `semanticRename` at the helper's declaration site
    - Audit every call site in `TypeCheckerAnalyzer.cs` and `OverloadResolver.cs` to confirm the intent is "is this the error sentinel" (cascade suppression), not "is this the user-facing unknown"
    - _Requirements: 2.2_

  - [x] 2.4 Audit any remaining textual references to `UnknownType` in test code and comments
    - Search for `UnknownType` across `Tests/` and non-generated source; each hit in this phase must refer to the sentinel and be updated to `ErrorType`
    - Leave a comment marker `// Phase B will reintroduce UnknownType as the user-facing top type` near places that previously asserted on the sentinel, to aid reviewers
    - _Requirements: 2.1, 2.2_

- [x] 3. Phase A gate — rebuild with zero warnings and run the full existing test suite
  - Run dotnet build -c Release on the full solution; confirm zero warnings and zero errors
  - Run the full existing test suite; confirm it passes with no behavioral changes
  - **Do not proceed to Phase B until this gate passes.** Ensure all tests pass, ask the user if questions arise.
  - _Requirements: 2.3, 2.4, 2.5_

---

### Phase B — Introduce the user-facing UnknownType and grammar

Phase B adds the new `TypeKind.Unknown` variant, the new `UnknownType` singleton at the freed filename, the grammar keyword `unknown`, the new `UnknownTypeReferenceNode`, the pretty-printer case, semantic resolution, the assignability rule, the storage-ban cascade, the inheritance prohibition, and the permission for `unknown` as a type argument.

- [x] 4. Add `TypeKind.Unknown` variant and audit switch statements
  - [x] 4.1 Add the `Unknown` variant to the `TypeKind` enum in `Source/Mist/Semantics/Contracts/IType.cs`
    - Place it after the existing `Trait` variant
    - _Requirements: 3.1_

  - [x] 4.2 Audit every `switch` on `TypeKind` across the solution
    - Grep for `TypeKind.` usage; each exhaustive switch must either explicitly handle `Unknown` or have a reasoned default (e.g., `throw` for definition-sites where `UnknownType` is never the generic definition)
    - Document each decision with a one-line comment
    - _Requirements: 3.1_

  - [x] 4.3 Write unit tests that verify `TypeKind.Unknown` is present and that `ConstructedType.MemberScope` throws (unchanged) when invoked with `UnknownType` as the generic definition (which should never happen in practice)
    - _Requirements: 3.1_

- [x] 5. Create the user-facing `UnknownType` singleton
  - [x] 5.1 Add `Source/Mist/Semantics/Model/Types/UnknownType.cs` at the filename freed by Phase A
    - Class: `public sealed class UnknownType : IType`
    - Singleton: `public static readonly UnknownType Instance = new();`
    - `DisplayName => "unknown"`, `Kind => TypeKind.Unknown`, private constructor
    - Report as the user-facing `unknown` type for diagnostics/pretty-printing
    - No declared members (no fields, methods, properties, constructors, or operators)
    - _Requirements: 3.1, 3.2, 3.3, 3.4, 3.5_

  - [x] 5.2 Write unit tests for the singleton
    - Assert `UnknownType.Instance` equals itself (reference equality)
    - Assert `DisplayName == "unknown"`, `Kind == TypeKind.Unknown`
    - Assert the singleton has no member scope or empty member scope
    - _Requirements: 3.2, 3.4_

- [x] 6. Grammar — add `UNKNOWN` keyword and `UnknownTypeReferenceNode`
  - [x] 6.1 Add `UNKNOWN: "unknown"` to `Source/Mist/Grammar/keywords.storm`
    - Place it alongside the other primitive-type keywords
    - _Requirements: 1.1_

  - [x] 6.2 Extend the `TypeReference` production in `Source/Mist/Grammar/Mist.storm` with an `UNKNOWN` alternative producing `UnknownTypeReferenceNode`
    - Node carries only a source span, analogous to `IntTypeReferenceNode`
    - Distinguishable from user-defined named type references and other primitive references
    - _Requirements: 1.2, 1.3_

  - [x] 6.3 Rebuild the source generator and regenerate AST extensions
    - Run `brazil-build release` (or `dotnet build -c Release`) on the solution; the Storm source generator emits updated AST code into `Source/Mist/Generated/CompilationUnitNodeExtensions.cs`
    - Confirm `UnknownTypeReferenceNode` is present in the generated output and that the build succeeds with zero warnings
    - _Requirements: 1.1, 1.2, 1.3_

  - [x] 6.4 Write parser unit tests
    - Positive: `unknown x = 1;` parses to a local declaration whose type reference is `UnknownTypeReferenceNode`
    - Positive: `List<unknown>`, `Map<string, unknown>`, `Optional<List<unknown>>` parse correctly with `UnknownTypeReferenceNode` at the expected positions
    - Negative: `class unknown { ... }`, `fn unknown()`, `let unknown = 1;`, `namespace unknown { ... }`, and a generic parameter named `unknown` all fail to parse because the lexer produces the `UNKNOWN` token rather than an identifier
    - _Requirements: 1.3, 1.4_

- [x] 7. Pretty-printer — emit `unknown`
  - [x] 7.1 Extend `MistPrettyPrinter` to emit `unknown` when the AST node is `UnknownTypeReferenceNode`
    - Handle bare positions (local, parameter, return)
    - Handle type-argument positions (inside `<...>`)
    - _Requirements: 14.1, 14.2, 14.3_

  - [x] 7.2 Write pretty-printer round-trip property test
    - **Property 1: Round-trip parity for `unknown` references** — generate ASTs containing `UnknownTypeReferenceNode` at every legal position; parse(prettyPrint(ast)) must be structurally equivalent to ast
    - **Validates: Requirement 14.4**
    - Test runs fully in-process (AST → string → AST), no process spawns
    - _Requirements: 14.4_

- [x] 8. Semantic model — resolve `UnknownTypeReferenceNode` to `UnknownType.Instance`
  - [x] 8.1 In `SemanticModelBuilder.ResolveTypeReference`, add a case for `UnknownTypeReferenceNode` that returns `UnknownType.Instance`
    - This is the single integration point for Req 3.6
    - _Requirements: 3.6, 7.1, 7.2, 7.3_

  - [x] 8.2 Write unit tests
    - `unknown` local resolves to `UnknownType.Instance`
    - `List<unknown>`, `Map<string, unknown>`, `Optional<List<unknown>>` resolve to constructed types whose type arguments at the correct positions are `UnknownType.Instance`
    - _Requirements: 3.6, 7.1, 7.2, 7.3_

- [x] 9. Type checker — assignability into `unknown`
  - [x] 9.1 Add the top-type assignability rule to `TypeCheckerAnalyzer.IsAssignable`
    - Early clause after `IsErrorType` cascade-suppression, before all other rules
    - If target is `UnknownType` and source is `VoidType`: return false (and let the caller emit `MIST_TC_0061`, reallocated from the originally-proposed `MIST_TC_0057`)
    - If target is `UnknownType` and source is any other type: return true
    - If source is `UnknownType` and target is not `UnknownType`: return false (caller emits `MIST_TC_0060`, reallocated from the originally-proposed `MIST_TC_0056`)
    - `UnknownType → UnknownType`: accepted
    - _Requirements: 4.1, 4.3, 4.5_

  - [x] 9.2 Wire diagnostics at the assignability check sites (assignment, argument pass, return, variable initializer)
    - `MIST_TC_0061` when assigning a `void` expression to an `unknown` target (was `MIST_TC_0057` in the original design; reallocated to avoid collision with the pattern-matching feature's `PatternNotCannotBindVariable`)
    - `MIST_TC_0060` when assigning an `unknown` value to a non-`unknown` target (was `MIST_TC_0056` in the original design; reallocated to avoid collision with `PatternOrInconsistentVariables`) — unless mediated by pattern narrowing (see Task 24)
    - _Requirements: 4.2, 4.4_

  - [x] 9.3 Write property-based test for assignability into `unknown`
    - **Property 2: Every non-`void` type is assignable to `UnknownType`** — generate arbitrary `IType` values from the semantic model (primitives, class types, struct types, constructed types, arrays); assert `IsAssignable(t, UnknownType.Instance)` for every non-`void` `t`, and `false` for `VoidType`
    - **Validates: Requirements 4.1, 4.2**
    - Runs entirely in-process against `IsAssignable`; no compilation or process spawning
    - _Requirements: 4.1, 4.2_

  - [x] 9.4 Write unit tests for the reverse direction and identity
    - `IsAssignable(UnknownType, IntType)` == false
    - `IsAssignable(UnknownType, ClassType)` == false
    - `IsAssignable(UnknownType, UnknownType)` == true
    - _Requirements: 4.4, 4.5_

- [x] 10. Storage-ban cascade — `IsStorageBanned(IType)`
  - [x] 10.1 Add a private helper `IsStorageBanned(IType)` in `SemanticModelBuilder` (or a shared utility near the type model) implementing the recursive rule from the design
    - `UnknownType` → true
    - `ArrayType(element)` → `IsStorageBanned(element)`
    - `ConstructedType(def, args)` → any position `i` where the effective type after substitution is storage-banned
    - otherwise → false
    - Design note: this helper is the specialization of `ContainsType(haystack, needle=UnknownType)`. Extract a shared `ContainsType(haystack, needle)` helper so Phase C's `ContainsTAtStoragePosition` can reuse it
    - _Requirements: 5.1, 5.2, 5.3, 7.5, 7.6_

  - [x] 10.2 Write property-based test for the storage-ban predicate
    - **Property 3: `IsStorageBanned` is monotonic under `unknown` substitution** — for any `IType` t that does not contain `UnknownType`, `IsStorageBanned(t)` is false; replacing any type argument with `UnknownType` at any depth causes `IsStorageBanned` to become true
    - **Validates: Requirements 5.1, 7.5, 7.6**
    - Runs entirely in-process on `IType` values; no compilation or process spawning
    - _Requirements: 5.1, 7.5, 7.6_

- [x] 11. Enforce storage ban at declaration sites
  - [x] 11.1 In Pass 1 of `SemanticModelBuilder`, check every field declaration with `IsStorageBanned(declaredType)` and emit `MIST_SB_0050` if true
    - Diagnostic names the declaring type and the field name
    - _Requirements: 5.1, 16.1_

  - [x] 11.2 Check every property declaration with `IsStorageBanned(declaredType)` and emit `MIST_SB_0051` if true
    - Diagnostic names the declaring type and the property name
    - _Requirements: 5.2, 16.1_

  - [x] 11.3 Check every indexer declaration's element type with `IsStorageBanned(elementType)` and emit `MIST_SB_0052` if true
    - Diagnostic names the declaring type and the indexer
    - _Requirements: 5.3, 16.1_

  - [x] 11.4 Write unit tests
    - `class Foo { unknown f; }` emits `MIST_SB_0050`
    - `class Foo { unknown P { get; set; } }` emits `MIST_SB_0051`
    - `class Foo { unknown this[int i] { get; set; } }` emits `MIST_SB_0052`
    - `class Foo { List<unknown> xs; }` emits `MIST_SB_0050` (storage-ban cascade)
    - `class Foo { Map<string, unknown> m; }` emits `MIST_SB_0050` (storage-ban cascade)
    - `class Foo { List<Optional<unknown>> nested; }` emits `MIST_SB_0050` (recursive cascade, Req 7.6)
    - All tests use `SharedDriver.Compile()` and inspect the diagnostic list in-process
    - _Requirements: 5.1, 5.2, 5.3, 7.5, 7.6, 16.1_

- [x] 12. Enforce storage ban at `new`, `new[]`, and `default` expressions
  - [x] 12.1 In the Type Checker, when checking a `new T(...)` expression, emit `MIST_SB_0056` if the constructed type equals `UnknownType`
    - Diagnostic: `unknown` cannot be instantiated
    - _Requirements: 5.4_

  - [x] 12.2 When checking `new T(...)` where T is a constructed generic, apply `IsStorageBanned(T)` and emit `MIST_SB_0053` if true
    - Diagnostic names the generic type and the position(s) where `unknown` appears
    - _Requirements: 5.7, 7.5, 16.2_

  - [x] 12.3 When checking `new T[n]`, emit `MIST_SB_0055` if the element type equals `UnknownType`
    - _Requirements: 5.5_

  - [x] 12.4 When checking `default(T)` or default-inferred initializers, emit `MIST_SB_0057` if T equals `UnknownType`
    - _Requirements: 5.6_

  - [x] 12.5 Write unit tests for each new/default case
    - `new unknown()` → `MIST_SB_0056`
    - `new unknown[5]` → `MIST_SB_0055`
    - `new List<unknown>()` → `MIST_SB_0053`
    - `new Map<unknown, int>()` → `MIST_SB_0053`
    - `default(unknown)` → `MIST_SB_0057`
    - _Requirements: 5.4, 5.5, 5.6, 5.7, 7.5, 16.2_

- [x] 13. Enforce inheritance prohibition
  - [x] 13.1 In Pass 1b (base clause resolution) of `SemanticModelBuilder`, check whether any base or interface reference resolves to `UnknownType`; emit `MIST_SB_0054` and drop the reference from the base/interface list
    - Applies to classes, structs (interface list), interfaces (base interface list), and traits (base trait position)
    - Diagnostic names the declaring type and states that `unknown` cannot be a base or implemented type
    - _Requirements: 6.1, 6.2, 6.3, 6.4, 16.6_

  - [x] 13.2 Write unit tests
    - `class Foo : unknown { }` → `MIST_SB_0054`
    - `struct S : unknown { }` → `MIST_SB_0054`
    - `interface I : unknown { }` → `MIST_SB_0054`
    - `trait T : unknown { }` → `MIST_SB_0054`
    - _Requirements: 6.1, 6.2, 6.3, 6.4_

- [x] 14. Permit `unknown` as a type argument in non-storage positions
  - [x] 14.1 Confirm (no code change needed) that `ConstructedType` with `UnknownType` arguments is produced by `ResolveTypeReference` without special casing
    - Add a regression test to lock in this behavior
    - _Requirements: 7.1, 7.2, 7.3, 7.4_

  - [x] 14.2 Write unit tests for `unknown` in type-argument positions
    - `Optional<unknown> o;` (local) parses, resolves, and is not flagged
    - `fn f(List<unknown> xs) { }` (parameter) parses and resolves
    - `fn f() : Map<string, unknown> { ... }` (return) parses and resolves
    - Nested occurrences (`Optional<List<unknown>>`, `Map<unknown, List<unknown>>`) resolve correctly
    - _Requirements: 7.1, 7.2, 7.3, 7.4_

- [x] 15. Phase B checkpoint
  - Rebuild with zero warnings; run the full test suite
  - Ensure all tests pass, ask the user if questions arise.
  - _Requirements: 2.3, (phase gate)_

---

### Phase C — Reachability Analyzer and member filtering

Phase C adds `ReachabilityAnalyzer` as a post-Pass-2 step, exposes the `ReachabilityMap` on `SemanticModel`, and integrates the map into the Type Checker's member-access resolution.

- [x] 16. Define analyzer contracts and supporting types
  - [x] 16.1 Create `Source/Mist/Semantics/Contracts/IReachabilityMap.cs`
    - `IReachabilityMap` interface with lookups for member + type-parameter → classification
    - `MemberClassification` enum/record: `DoesNotWriteT`, `WritesT`; for properties/indexers, track `(read, write)` pair
    - _Requirements: 9.1, 9.11_

  - [x] 16.2 Add the shared `ContainsTAtStoragePosition(IType haystack, TypeParameterType needle): bool` helper
    - Recursive structural check: needle itself → true; array element → recurse; constructed type args → exists recurse
    - Share implementation with Phase B's `IsStorageBanned` via a common `ContainsType(haystack, needle)` helper
    - _Requirements: 9.2, 9.8, 9.9_

  - [x] 16.3 Write unit tests for `ContainsTAtStoragePosition`
    - Positive: `T`, `T[]`, `List<T>`, `Map<string, T>`, `List<Optional<T>>`
    - Negative: `int`, `string`, `List<int>`, `Map<T, int>` when checking "does value slot contain T?" using appropriate position-targeted variant if the design uses one, otherwise simple containment
    - _Requirements: 9.2_

- [x] 17. Implement `ReachabilityAnalyzer` field, property, and indexer classification
  - [x] 17.1 Create `Source/Mist/Semantics/Model/ReachabilityAnalyzer.cs`
    - Public API: `IReachabilityMap Analyze(ISymbolTable symbols, IReadOnlyDictionary<IExpressionNode, IType> expressionTypes)`
    - Skeleton: iterate every generic-type declaration; classify every member for every type parameter
    - _Requirements: 9.1_

  - [x] 17.2 Implement field classification
    - Field read → `DoesNotWriteT` w.r.t. every T
    - Field write (assignment to field) → `WritesT` if the field's declared type satisfies `ContainsTAtStoragePosition(fieldType, T)`
    - _Requirements: 9.2_

  - [x] 17.3 Implement property classification (read, write pair)
    - Property read → `WritesT` only if the getter body performs a `Write_To_T` (detected by the method-body walk added in Task 18)
    - Property write → `WritesT` if auto-implemented with T-typed backing, backed by a T-typed field, or the setter body performs a `Write_To_T`
    - _Requirements: 9.3, 9.11_

  - [x] 17.4 Implement indexer classification analogously to property (read, write pair)
    - _Requirements: 9.4, 9.11_

  - [x] 17.5 Write unit tests for field, property, indexer classification
    - `class List<T> { T[] items; }` — `items` field read = `DoesNotWriteT`; field write = `WritesT`
    - Auto-property `T Value { get; set; }` — read = `DoesNotWriteT`, write = `WritesT`
    - Property with setter-body write to a non-T field — write = `DoesNotWriteT`
    - Indexer that stores into `T[]` — write = `WritesT`; read = `DoesNotWriteT`
    - _Requirements: 9.2, 9.3, 9.4, 9.11_

- [x] 18. Implement `ReachabilityAnalyzer` method and constructor classification
  - [x] 18.1 Implement the method-body write-site extraction
    - Walk every statement/expression of the method body using the semantic model's expressionTypes map
    - For every assignment/compound-assignment/element-assignment whose target is a `T_Typed_Storage_Location`, record a write-site
    - _Requirements: 9.5_

  - [x] 18.2 Build the per-member call graph for the generic type
    - For each method body, extract the set of call targets (resolved member symbols) or `"unknown callee"` markers for dynamic-dispatch sites
    - _Requirements: 9.6_

  - [x] 18.3 Implement constructor classification
    - `WritesT` if any constructor parameter type satisfies `ContainsTAtStoragePosition(paramType, T)`
    - `WritesT` if the constructor body performs a `Write_To_T` to a T-typed field
    - `WritesT` if the constructor invokes another `WritesT` constructor via `this(...)` or `base(...)`
    - _Requirements: 9.7_

  - [x] 18.4 Implement conservative handling of virtual dispatch, trait satisfaction, and generic-method invocation
    - Dynamic-dispatch call: treat as `WritesT` if any argument's type satisfies `ContainsTAtStoragePosition(argType, T)`
    - External (C#-defined) callee: same rule — `WritesT` if any argument's type contains T at a storage position; else `DoesNotWriteT`
    - _Requirements: 9.8, 9.9_

  - [x] 18.5 Write unit tests for method and constructor classification
    - Method that assigns to a `T[]` element → `WritesT`
    - Method that only reads → `DoesNotWriteT`
    - Constructor taking a `T` parameter stored into a field → `WritesT`
    - Constructor taking no T parameters and not writing → `DoesNotWriteT`
    - Virtual method call with a T argument → `WritesT` (conservative)
    - External `Console.WriteLine(T)` invocation → `WritesT` if T contains the analyzed parameter at a storage position, else `DoesNotWriteT`
    - _Requirements: 9.5, 9.6, 9.7, 9.8, 9.9_

- [x] 19. Implement fixed-point iteration
  - [x] 19.1 Implement the fixed-point loop described in the design
    - Initialize every `(member, T)` classification to `DoesNotWriteT`
    - Iterate: for each member, if any write-site or any callee's current classification is `WritesT`, promote to `WritesT`
    - Repeat until no change in a full pass
    - Lattice height is 2; termination is bounded
    - _Requirements: 9.6, 9.10_

  - [x] 19.2 Write a property-based test for fixed-point monotonicity
    - **Property 4: Fixed-point iteration is monotone** — for any iteration k, the classification map is a pointwise under-approximation of the final map (no entry ever changes from `WritesT` back to `DoesNotWriteT`)
    - **Validates: Requirement 9.10**
    - Generate synthetic call graphs in-process; run the analyzer step-by-step; assert monotonicity at each step
    - Runs entirely in-process; no compilation or process spawning
    - _Requirements: 9.10_

  - [x] 19.3 Write a unit test for mutual recursion
    - `class G<T> { void A() { B(); this.field = ...; }  void B() { A(); } }` — after fixed-point, both A and B are `WritesT`
    - _Requirements: 9.6, 9.10_

- [x] 20. Wire the analyzer into `SemanticModelBuilder.Build`
  - [x] 20.1 Invoke `ReachabilityAnalyzer.Analyze(...)` as a post-Pass-2 step, after `ComputeDefaultability` and the Pass-1 member-redeclaration and duplicate-signature checks
    - Store the result as a new property `ReachabilityMap` on `SemanticModel`
    - _Requirements: 9.1_

  - [x] 20.2 Thread the reachability map through the analyzer context so `TypeCheckerAnalyzer` can consult it
    - _Requirements: 9.1, 10.1_

  - [x] 20.3 Write an integration test that compiles a small generic type and inspects the resulting `ReachabilityMap`
    - `class List<T> { T[] items; void Add(T item); int Count { get; } }` — `Add` = `WritesT`; `Count` read = `DoesNotWriteT`; `items` write = `WritesT`
    - _Requirements: 9.1_

- [x] 21. Type checker — filter members on `Closed_Generic_With_Unknown` receivers
  - [x] 21.1 In `TypeCheckerAnalyzer`'s member-access resolution, when the receiver's static type is a `Closed_Generic_With_Unknown`, consult the reachability map
    - For each type parameter T whose corresponding type argument is `UnknownType`, filter out members classified `WritesT`
    - Filter methods, property writes, and indexer writes; do not filter property reads / indexer reads whose `read` classification is `DoesNotWriteT`
    - When a read passes, substitute `UnknownType` for the closed parameter(s) in the result type
    - Do not apply filtering inside the declaring body of the generic type itself (receiver is fully open)
    - _Requirements: 10.1, 10.2, 10.3, 10.4, 10.6_

  - [x] 21.2 Emit `MIST_TC_0062` when a member is filtered per 21.1 (reallocated from the design-document-proposed `MIST_TC_0050`, which was already in use by the pattern-matching feature's `PatternUndefinedType` diagnostic — see the "Note on numbering" in `design.md`)
    - Diagnostic names the receiver's static type, names the member, and states that the operation would write to storage of a type parameter bound to `unknown`
    - Use the same "member not found" shape as the existing missing-member diagnostic, per Req 10.1 and Req 10.7
    - _Requirements: 10.1, 10.3, 10.4, 10.7, 16.3_

  - [x] 21.3 Do not apply member filtering to constructor invocations on `Closed_Generic_With_Unknown`
    - Constructor invocations on such a receiver are already rejected by Task 12.2 (`MIST_SB_0053`)
    - _Requirements: 10.5_

  - [x] 21.4 Write unit tests for member filtering
    - `var xs: List<unknown> = ...; xs.Add(x)` → `MIST_TC_0062` (reallocated from the design-document-proposed `MIST_TC_0050`)
    - `var xs: List<unknown> = ...; var n = xs.Count` → accepted (read)
    - `var xs: List<unknown> = ...; var e = xs[0]` → accepted (read, result type is `UnknownType`)
    - `var xs: List<unknown> = ...; xs[0] = x` → `MIST_TC_0062` (indexer write)
    - `class List<T> { void Add(T x) { ... this.Add(x); } }` — inside the declaring body, no filtering: the recursive `this.Add(x)` call is accepted
    - _Requirements: 10.1, 10.2, 10.3, 10.4, 10.6, 10.7_

- [-] 22. Phase C checkpoint
  - Rebuild with zero warnings; run the full test suite
  - Ensure all tests pass, ask the user if questions arise.
  - _Requirements: (phase gate)_

---

### Phase D — Widening, casts, traits, pattern narrowing

Phase D adds the assignability helper for covariant unknown widening, the class-generic-only restriction diagnostic, the bare-unknown rejection rules, cast-operator integration, pattern-narrowing rejections, and trait satisfaction updates.

- [ ] 23. Covariant unknown widening in `IsAssignable` / `IsDirectlyAssignable`
  - [ ] 23.1 Add `AssignableWithUnknownWidening(defCtx, argsS, argsT): bool` helper in `TypeCheckerAnalyzer`
    - For each position i: accept if `argsS[i] == argsT[i]` OR (`argsT[i]` is `UnknownType` AND `defCtx.Kind == TypeKind.Class`) OR both sides are constructed and their nested widening succeeds
    - `defCtx` carries the generic definition whose arguments are being compared at the current level, enforcing the class-only rule only where a widening actually happens at that level
    - _Requirements: 8.1, 8.2, 8.3_

  - [ ] 23.2 Integrate the helper into `IsAssignable`
    - New clause in the `ConstructedType → ConstructedType` branch, after the identity check
    - Transitive/inheritance widening is picked up naturally by the existing loop over base types and interfaces
    - _Requirements: 8.1, 8.2, 8.3, 8.4_

  - [ ] 23.3 Integrate the helper into `IsDirectlyAssignable` so trivial casts `(List<unknown>) intList` work without requiring a cast operator
    - _Requirements: 8.4_

  - [ ] 23.4 At the widening site (assignment, argument pass, return), detect when the only reason assignability fails is the struct-generic restriction, and emit `MIST_TC_0058`
    - Diagnostic: covariant `unknown` widening is only available for class (reference-type) generics
    - _Requirements: 8.6_

  - [ ] 23.5 Confirm the reverse direction is naturally rejected by the helper (never accept `argsS[i]` being `UnknownType` widening to a concrete `argsT[i]`)
    - _Requirements: 8.5_

  - [ ] 23.6 Write property-based test for covariant widening
    - **Property 5: Covariant unknown widening is position-wise and class-only** — for any class-generic definition `Foo` and any type arguments tuple, replacing any subset of positions with `UnknownType` produces a target assignable from the original; struct-generic widening never succeeds
    - **Validates: Requirements 8.1, 8.2, 8.3, 8.5, 8.6**
    - Runs in-process against `IsAssignable`; generates synthetic constructed types from a small pool of class and struct generic definitions
    - _Requirements: 8.1, 8.2, 8.3, 8.5, 8.6_

  - [ ] 23.7 Write unit tests for nested widening and the defCtx rule
    - `Optional<List<int>>` → `Optional<List<unknown>>` is accepted when `Optional` is a struct and `List` is a class (nested widening on the class boundary)
    - `Optional<int>` → `Optional<unknown>` is rejected with `MIST_TC_0058` when `Optional` is a struct
    - `List<int>` → `List<unknown>` is accepted
    - `List<unknown>` → `List<int>` is rejected
    - _Requirements: 8.1, 8.3, 8.5, 8.6_

- [ ] 24. Pattern narrowing — reject narrow to `unknown` and narrow to `Closed_Generic_With_Unknown`
  - [ ] 24.1 In `TypeCheckerAnalyzer.CheckIsExpression` / `CheckTypePattern`, detect when the pattern type is `UnknownType` and emit `MIST_TC_0051`
    - Diagnostic: narrowing to `unknown` is not meaningful; pattern is tautologically true and introduces no new type information
    - _Requirements: 11.3_

  - [ ] 24.2 Detect when the pattern type is a `ConstructedType` with any argument equal to `UnknownType` and emit `MIST_TC_0052`
    - Diagnostic: narrowing to a Closed_Generic_With_Unknown is not meaningful; pattern is tautologically true under Covariant_Unknown_Widening and the binding loses information
    - _Requirements: 11.4_

  - [ ] 24.3 Preserve the existing narrowing behavior for concrete constructed pattern types
    - `foo is Optional<int> oi` continues to introduce `oi : Optional<int>` and permits full member access with no filtering
    - _Requirements: 11.1, 11.2_

  - [ ] 24.4 Write unit tests for narrowing rejection and acceptance
    - `x is unknown u` → `MIST_TC_0051`
    - `x is List<unknown> lu` → `MIST_TC_0052`
    - `x is List<int> li` → accepted; `li.Add(...)` works inside the pattern branch
    - _Requirements: 11.1, 11.2, 11.3, 11.4_

- [ ] 25. Bare-unknown member access and operator rejection
  - [ ] 25.1 In member-access resolution, when the receiver's static type is exactly `UnknownType`, emit `MIST_TC_0053` and return `ErrorType.Instance`
    - Diagnostic names the receiver's static type and suggests using an `is` pattern to narrow
    - _Requirements: 13.1, 16.4_

  - [ ] 25.2 In binary/unary operator resolution, when any operand's static type is `UnknownType`, emit `MIST_TC_0054`
    - Applies to `==`, `!=`, `<`, `>`, `<=`, `>=`, `+`, `-`, `*`, `/`, `%`, `&&`, `||`, `!`, and user-defined operators
    - _Requirements: 13.2, 16.4_

  - [ ] 25.3 Leave `is`-expression handling untouched on bare `unknown` — narrowing is the designated mechanism (Req 13.3)
    - Confirmed by Task 24's tests
    - _Requirements: 13.3_

  - [ ] 25.4 Write unit tests
    - `fn f(x: unknown) { x.M() }` → `MIST_TC_0053`
    - `fn f(x: unknown, y: unknown) { x + y }` → `MIST_TC_0054` (and for every operator above)
    - `fn f(x: unknown) { x is int i; i + 1 }` → accepted
    - _Requirements: 13.1, 13.2, 13.3_

- [ ] 26. Cast-operator integration for `unknown`
  - [ ] 26.1 Extend `TypeCheckerAnalyzer.CheckCastExpression` so a cast `(T) expr` where `expr` is typed `unknown` resolves via the cast-operators feature
    - Search only `T`'s member scope (since `UnknownType` has empty member scope)
    - Accept implicit and explicit operators whose source type is `UnknownType` and target is `T`
    - If no such operator exists, emit `MIST_TC_0055`
    - _Requirements: 13.4, 13.6_

  - [ ] 26.2 Accept `(unknown) expr` for any non-`void` `expr` without searching for a cast operator
    - The top-type assignability rule (Task 9) already covers this; confirm the cast expression path agrees
    - _Requirements: 13.5_

  - [ ] 26.3 Write unit tests for cast resolution
    - `class MyInt { implicit operator MyInt(unknown u) { ... } }` — `(MyInt) someUnknown` resolves and emits the static method call
    - `(int) someUnknown` with no operator declared → `MIST_TC_0055`
    - `(unknown) 42` → accepted; emission checked in Task 28
    - _Requirements: 13.4, 13.5, 13.6_

- [ ] 27. Trait satisfaction with `unknown`
  - [ ] 27.1 Relax the trait-match return-type compatibility check
    - Trait return type `UnknownType` matches any non-`void` concrete return type (Req 12.1)
    - Trait return type `ConstructedType(def, argsT)` matches concrete return type `ConstructedType(def, argsS)` via `AssignableWithUnknownWidening(def, argsS, argsT)` (Req 12.2)
    - _Requirements: 12.1, 12.2_

  - [ ] 27.2 Preserve exact parameter-type matching for trait members (no widening at parameter positions)
    - `unknown` in a trait parameter requires exactly `unknown` in the implementer (and vice versa)
    - _Requirements: 12.3_

  - [ ] 27.3 Apply reachability-based member filtering to trait-typed receivers
    - For v1, traits do not carry type parameters; reduce to filtering trait members whose classification is `WritesT` where `T` is a position occupied by `UnknownType` in the trait's signature
    - _Requirements: 12.5_

  - [ ] 27.4 Confirm that trait field declarations with `unknown` are already rejected by Task 11.1's field check (`MIST_SB_0050`)
    - Add a test to lock in the coverage
    - _Requirements: 12.4_

  - [ ] 27.5 Write unit tests for trait satisfaction
    - Trait `trait T { fn get() : unknown; }` is satisfied by `class C { fn get() : int { ... } }`
    - Trait `trait T { fn get() : List<unknown>; }` is satisfied by `class C { fn get() : List<int> { ... } }` (with class generic widening)
    - Trait `trait T { fn get() : Optional<unknown>; }` is NOT satisfied by `class C { fn get() : Optional<int> { ... } }` when `Optional` is a struct (struct generic restriction)
    - Trait `trait T { fn put(x: unknown); }` is NOT satisfied by `class C { fn put(x: int) { ... } }` (exact parameter match)
    - Trait with `unknown` return, invoked on a trait-typed receiver, filters `WritesT` members correctly
    - Trait with an `unknown` field declaration → `MIST_SB_0050`
    - _Requirements: 12.1, 12.2, 12.3, 12.4, 12.5_

- [ ] 28. Phase D checkpoint
  - Rebuild with zero warnings; run the full test suite
  - Ensure all tests pass, ask the user if questions arise.
  - _Requirements: (phase gate)_

---

### Phase E — Emission, pretty-printer polish, syntax highlighting, samples, docs

Phase E completes the feature: C# emission for UnknownType and covariant widening, pretty-printer round-trip parity, syntax highlighting, two sample apps, and the CSHARP_FEATURES.md update.

- [ ] 29. C# type map — emit `UnknownType` as `object`
  - [ ] 29.1 In `Source/Mist/Backends/CSharpTranspiler/CSharpTypeMap.cs`, add the `UnknownType => "object"` case
    - Every Mist reference to UnknownType (direct or after substitution) emits C# `object`
    - _Requirements: 15.1_

  - [ ] 29.2 Write unit tests for type-map emission
    - `unknown` → `"object"`
    - `List<unknown>` → `"List<object>"`
    - `Map<string, unknown>` → `"Map<string, object>"`
    - `Optional<List<unknown>>` → `"Optional<List<object>>"`
    - _Requirements: 15.1_

- [ ] 30. C# emission — cast expressions involving `unknown`
  - [ ] 30.1 Emit `(unknown) expr` as `(object)(<emitted expr>)` — no operator dispatch
    - _Requirements: 13.5, 15.4_

  - [ ] 30.2 Emit `(T) expr` where `expr` is typed `unknown` as a static method call on the declaring type `T`, per the cast-operators feature's emission rules
    - Depends on Task 26.1's resolution
    - _Requirements: 13.4, 15.4_

  - [ ] 30.3 Write unit tests for cast emission
    - `(unknown) 42` → C# `(object)(42)`
    - `(MyInt) someUnknown` with a resolved implicit operator → C# `MyInt.op_Implicit(someUnknown)` (or the equivalent explicit form)
    - _Requirements: 13.4, 13.5, 15.4_

- [ ] 31. C# emission — covariant unknown widening
  - [ ] 31.1 At each widening site (assignment, argument pass, return), detect whether the source and target differ only by `UnknownType` substitutions on a class-generic definition
    - If so, emit the double cast: `(Foo<object>)(object)<emitted source>`
    - Otherwise, emit normally
    - _Requirements: 15.2_

  - [ ] 31.2 Do not emit the double cast when target is directly `UnknownType` (single `(object)` cast suffices)
    - _Requirements: 15.1, 15.2_

  - [ ] 31.3 Write unit tests for widening emission
    - Mist `List<unknown> xs = intList;` → C# `List<object> xs = (List<object>)(object)intList;`
    - Mist `unknown x = 42;` → C# `object x = 42;` (single implicit conversion; no double cast)
    - Mist `Map<string, unknown> m = stringIntMap;` → C# `Map<string, object> m = (Map<string, object>)(object)stringIntMap;`
    - _Requirements: 15.1, 15.2_

- [ ] 32. C# emission — `is` patterns and best-effort storage skipping
  - [ ] 32.1 Confirm that `is Pattern` emission is unchanged — Mist `foo is Optional<int> oi` emits C# `foo is Optional<int> oi` directly
    - Add a regression test to lock in this behavior
    - _Requirements: 15.3_

  - [ ] 32.2 Emitter skips emission of any field/property/indexer declaration whose declared type satisfies `IsStorageBanned(type)`
    - Best-effort output so that programs with storage-ban diagnostics still produce compilable C# for the unaffected portions
    - _Requirements: 15.5_

  - [ ] 32.3 Write a unit test
    - `class Foo { unknown f; int g; }` → diagnostic `MIST_SB_0050` is emitted; emitted C# contains field `g` but not `f`
    - _Requirements: 15.5_

- [ ] 33. Syntax highlighting
  - [ ] 33.1 Add `unknown` to the primitive-type keyword pattern in `Editor/mist-syntax-highlighting/syntaxes/mist.tmLanguage.json`
    - Scope: `keyword.type.primitive.mist` (the scope used by existing primitive-type keywords)
    - _Requirements: 17.1, 17.2_

  - [ ] 33.2 Add a line containing `unknown` in each relevant position to `Editor/mist-syntax-highlighting/tests/keywords.mist` and `types.mist`
    - Keeps the highlighting tests current
    - _Requirements: 17.1, 17.2_

- [ ] 34. Sample application — `Samples/UnknownType/Program.mist`
  - [ ] 34.1 Create `Samples/UnknownType/Program.mist`
    - `static void Main` entry point
    - Declare an `unknown`-typed local initialized from values of multiple concrete types
    - Declare an `unknown`-typed method parameter and pass values of multiple concrete types to it
    - Pattern-match narrow from `unknown` to a concrete type via `is`; call a method on the narrowed binding
    - Covariant_Unknown_Widening: assign a `List<int>` to a `List<unknown>`; call members that remain accessible (length, iterate, read element)
    - Pattern-narrow a `Closed_Generic_With_Unknown` to a concrete constructed type (e.g., `is List<int> li`) and perform full operations
    - Include at least one inline `//` comment describing an operation that would be rejected (e.g., `// foo.Add(x) would not compile here because List<unknown> hides Add`)
    - Produce observable output via `Console.WriteLine`
    - _Requirements: 19.1, 19.2, 19.3, 19.4, 19.5, 19.6, 19.7, 19.8, 19.9_

  - [ ] 34.2 Wire the sample into the existing sample-app test harness
    - The existing harness discovers `Samples/*/Program.mist` automatically; verify no additional wiring is needed
    - Ensure expected-output file (if the harness uses one) is present
    - _Requirements: 19.8_

- [ ] 35. AllFeatures sample update — `Samples/AllFeatures/UnknownType.mist`
  - [ ] 35.1 Create a new file `Samples/AllFeatures/UnknownType.mist`
    - At least one `unknown`-typed local
    - At least one Covariant_Unknown_Widening assignment
    - At least one pattern-match narrowing
    - File is additive, not a modification of existing files, to simplify merging
    - _Requirements: 20.1, 20.2_

  - [ ] 35.2 Confirm the AllFeatures sample still compiles and runs as part of the existing sample test suite
    - _Requirements: 20.3_

- [ ] 36. CSHARP_FEATURES.md update
  - [ ] 36.1 Add an entry for `unknown` in the `Reference Types` section
    - Marked as implemented
    - Note that this is a Mist-specific feature distinct from C# `object`, and explicitly distinct from C# `dynamic` (which remains skipped)
    - _Requirements: 18.1_

  - [ ] 36.2 Update the `Summary` table at the bottom of CSHARP_FEATURES.md to reflect the new implementation count
    - _Requirements: 18.2_

  - [ ] 36.3 Add a short prose subsection describing the semantics
    - Top-type assignability
    - Storage prohibition
    - Reachability-based member filtering on closed generics
    - Pattern-matching narrowing as the designated escape
    - _Requirements: 18.3_

- [ ] 37. Final checkpoint — rebuild, full tests, samples
  - Rebuild with zero warnings; run the full test suite including the two new samples
  - Ensure all tests pass, ask the user if questions arise.
  - _Requirements: 2.3, (phase gate)_

---

## Notes

- **Optional tasks (`*`)** are unit tests, property-based tests, and integration tests. Core implementation tasks are not optional. Test tasks can be skipped for a faster MVP but are strongly recommended — the Reachability Analyzer especially depends on exhaustive test coverage because the C# emission strategy relies on its correctness (see Risk Register, design item 1).
- **Zero warnings policy** applies to every implementation task. Each phase ends with a rebuild-and-test gate.
- **In-process tests.** All PBT and unit tasks use `SharedDriver.Compile()` in-process (per `test-process-spawning.md`). No test task in this plan spawns `dotnet` per iteration. The sample apps in Phase E are compiled by the existing sample-test harness, which spawns `dotnet` at most once per sample per test run (documented pattern, not per-iteration).
- **Phase gates (tasks 3, 15, 22, 28, 37)** enforce that the compiler remains in a fully-building state at each phase boundary, matching the design's phase-map contract.
- **Requirements coverage.** Every acceptance criterion in the 20 requirements maps to at least one task above. Cross-cutting requirements (Req 16 Diagnostic Quality) are satisfied by the diagnostic emission sub-tasks throughout Phases B and D.

## Workflow Completion

This is a planning artifact. The Feature Requirements-First workflow is complete when `tasks.md` is written. Open `tasks.md` and click "Start task" next to an item to begin implementation.
