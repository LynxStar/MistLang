# Implementation Plan: Ownership v2

## Overview

Ownership v2 reworks the Mist ownership system so ownership is a property of a *binding* rather than a *type*. The implementation is sequenced to keep the build coherent: additive foundations first (the `IClonable<T>` stdlib bridge, the IClonable derivation pass, and the three new analyzer collaborators with their own tests), then the single breaking change — the `OwnershipAnalyzer` rewrite that deletes the v1 passes (`MIST_OWN_FIELD_NOT_OWNED`, structural `MIST_OWN_TYPE_REQUIRES_OWNED`, owned-field-modifier validation, struct ownership inference) and simultaneously wires in the new components. Sample fallout (deleting `samples/DeepOwnershipTypes/` and `samples/OwnershipSystem/`, creating `samples/Ownership/`) is fixed immediately after the rewrite so AllFeatures stays runnable. Emitter `.Clone()` insertion, the substructural restriction, grammar/diagnostic cleanup, TextMate grammar, and the book chapter follow.

All work is in **C#** (compiler at `Source/Mist/`), **Mist** (stdlib at `Source/Standard/`, samples at `samples/`), and **Markdown** (book at `book/src/`). Tests live in `Test/Mist.Tests/`. The 13 correctness properties map to FsCheck property tests that use **in-process `CompilerDriver.Compile()`** (analysis-only) or a **single batched compile-and-run** for stdout observation — no per-iteration process spawning (see Notes).

## Tasks

- [x] 1. Standard library: `IClonable<T>` interface and bootstrap base cases
  - [x] 1.1 Create the `IClonable<T>` interface
    - Add `Source/Standard/IClonable.mist` declaring `interface IClonable<T> { owned T Clone(); }` in `namespace Standard` (sibling to `IEnumerable.mist`)
    - The `owned T` return specifier is the move-boundary contract: `Clone()` hands out a fresh owned value
    - _Requirements: 8.1_

  - [x] 1.2 Add `IClonable<Self>` to bootstrap value types and String
    - Update `Source/Standard/Bootstrap/` declarations for `Int32`, `Int64`, `Int16`, `UInt32`, `UInt64`, `UInt16`, `Byte`, `SByte`, `Single`, `Double`, `Decimal`, `Boolean`, `Char`, and `String` to declare `: IClonable<Self>` with an `intrinsic public owned T Clone();`
    - These are the recursion base cases for auto-derivation; value-type `Clone()` is an identity copy, `String.Clone()` returns the immutable reference (observationally independent)
    - _Requirements: 8.6_

  - [x] 1.3 Write unit tests for bootstrap IClonable declarations
    - Assert each bootstrap type's semantic model reports `ImplementsIClonable == true`
    - _Requirements: 8.6_

- [x] 2. Type model: `ImplementsIClonable` and `IsOwnedType` repurpose
  - [x] 2.1 Add `ImplementsIClonable` to `ClassType` and `StructType`
    - Add `public bool ImplementsIClonable { get; internal set; }` to `Source/Mist/Semantics/Model/Types/ClassType.cs` and `StructType.cs`
    - Update the `IsOwnedType` doc comment on both to describe the cooperation contract ("type author guarantees field assignments come from owned sources"), removing the v1 "fields must themselves be owned" wording
    - _Requirements: 8.6, 8.7, 5.3, 10.1_

- [x] 3. IClonable derivation pass
  - [x] 3.1 Implement the IClonable derivation pass
    - New pass (Phase 2.5) that computes `ImplementsIClonable` for each user and bootstrap type: true if the type explicitly declares `IClonable<T>` OR every field's type implements `IClonable<T>` (recursive over the type graph, structs and classes alike)
    - Bootstrap value types and `String` are base cases (always true); a type with any non-clonable field is not derived
    - Cache the result on the type model for `MoveBoundaryResolver` to query
    - Wire the pass into the pipeline before ownership analysis
    - _Requirements: 8.6, 8.7_

  - [x] 3.2 Write property test for IClonable auto-derivation
    - **Property 6: IClonable auto-derivation is exactly "all fields clonable"**
    - For all types T: `ImplementsIClonable(T)` ⟺ (T declares `IClonable<T>`) ∨ (∀ field f: `ImplementsIClonable(typeof(f))`); bootstrap types always true
    - In-process `CompilerDriver.Compile()`, inspect semantic model only
    - **Validates: Requirements 8.6, 8.7**

  - [x] 3.3 Write unit tests for derivation edge cases
    - All-clonable fields (derives), one non-clonable field (does not derive), recursive struct-of-structs, explicit declaration overrides
    - _Requirements: 8.6, 8.7_

- [x] 4. `MoveBoundaryResolver` — the single enforcement point
  - [x] 4.1 Implement `MoveBoundaryResolver` and its result types
    - Add `Source/Mist/Analysis/Builtin/MoveBoundaryResolver.cs` with `Resolve(source, destinationType, site, flowState, model, diagnostics)`, the `OwnedMoveSite` enum (`OwnedBindingInit`, `OwnedFieldAssignment`, `OwnedParameterPass`), the `MoveResolution` record, and the `MoveOutcome` enum (`MovedFromOwned`, `ValueCopy`, `ImplicitClone`, `DefaultGraph`, `Error`)
    - Implement the resolution algorithm in order: (1) `default(T)`/bare `default` → `DefaultGraph`; (2) value/primitive type → `ValueCopy`; (3) already-owned source (constructor call, owned-returning call, live owned binding) → `MovedFromOwned`, guarded by the truth table (reject move from LentRef/LentFrozen/LentReadonly/Moved); (4) non-owned source implementing `IClonable<T>` → `ImplicitClone` (cloning from a lent tier does NOT transition the lender's state); (5) otherwise → `Error` emitting `MIST_OWN_REQUIRES_OWNED` or `MIST_OWN_NOT_CLONABLE`
    - Absorb v1's `YieldsUniqueOwnership` / `IsConstructorOrOwnedCall` provenance logic here; record a clone annotation for `ImplicitClone` outcomes; mark consumed bindings `Moved` for `MovedFromOwned`
    - _Requirements: 6.9, 6.1, 6.2, 6.3, 6.8, 6.10, 4.1, 4.2, 4.3, 4.4, 8.2, 8.3, 8.4, 8.5_

  - [x] 4.2 Write property test: move boundary is the sole gate
    - **Property 1: Move boundary is the sole gate**
    - For all sites S and sources e, the accept/reject and clone decision equals `MoveBoundaryResolver.Resolve(e, ...)`
    - In-process `CompilerDriver.Compile()`, compare per-site decisions to a direct resolver call
    - **Validates: Requirements 6.9, 4.1, 4.2**

  - [x] 4.3 Write property test: owned source implies no clone
    - **Property 2: Owned source implies no clone**
    - For all already-owned sources, `Resolve(e)` ∈ {MovedFromOwned, ValueCopy, DefaultGraph}, never `ImplicitClone`
    - **Validates: Requirements 6.1, 6.2, 6.3**

  - [x] 4.4 Write property test: clonable source implies exactly one clone
    - **Property 3: Non-owned clonable source implies exactly one clone**
    - For all non-owned reference sources implementing `IClonable<T>`, `Resolve(e) == ImplicitClone`
    - **Validates: Requirements 6.9, 8.2, 8.3**

  - [x] 4.5 Write property test: non-clonable source implies error
    - **Property 4: Non-owned non-clonable source implies error**
    - For all non-owned, non-clonable reference sources, `Resolve(e) == Error` and `MIST_OWN_NOT_CLONABLE` is emitted
    - **Validates: Requirements 8.4, 13.7**

  - [x] 4.6 Write property test: clone from lent tier does not consume lender
    - **Property 5: Clone from lent tier does not consume the lender**
    - For all bindings lent as borrowed/frozen/readonly-ref then cloned into an owned destination, the lender's post-clone state equals its pre-clone state
    - **Validates: Requirements 8.5**

  - [x] 4.7 Write property test: default yields ownership
    - **Property 10: `default` yields ownership**
    - For all owned destinations of type T, both `default(T)` and bare `default` (typed LHS) resolve as `DefaultGraph` and are accepted
    - **Validates: Requirements 6.10**

  - [x] 4.8 Write unit tests for the resolver decision matrix
    - Table-driven `[Theory]`/`[InlineData]` over (source kind × site × clonability) asserting the `MoveOutcome`
    - _Requirements: 6.9, 4.3, 4.4_

- [x] 5. `MethodTierVisibilityFilter`
  - [x] 5.1 Implement `MethodTierVisibilityFilter`
    - Add `Source/Mist/Analysis/Builtin/MethodTierVisibilityFilter.cs` with `Resolve(methodSymbol, receiverType, receiverIsOwnedBinding, model)` returning `MethodVisibility` (`Allowed` / `HiddenMutable`)
    - Decision table: owned type → all methods Allowed; non-owned type → readonly Allowed, mutable HiddenMutable; value-type receiver → Allowed regardless of method kind; non-owned-binding receiver → Allowed
    - Reuse `ReadonlyInferenceEngine` / `Symbol.ReadonlyStatus` to classify readonly vs mutable (no new readonly machinery)
    - _Requirements: 2.1, 2.2, 2.4, 3.1, 3.3_

  - [x] 5.2 Write property test for method tier visibility
    - **Property 7: Method tier visibility**
    - For all owned bindings of type T and methods m: m callable ⟺ (T owned) ∨ (m readonly) ∨ (T value type)
    - In-process `CompilerDriver.Compile()`, inspect diagnostics
    - **Validates: Requirements 2.1, 2.2, 3.1**

  - [x] 5.3 Write unit tests for the visibility decision table
    - Explicit `[Theory]`/`[InlineData]` for each row of the decision table
    - _Requirements: 2.1, 2.2, 2.4, 3.1_

- [x] 6. `CooperationContractVerifier`
  - [x] 6.1 Implement `CooperationContractVerifier`
    - Add `Source/Mist/Analysis/Builtin/CooperationContractVerifier.cs` with `Verify(ownedType, resolver, model, diagnostics)`
    - For each method/constructor of an `owned` type, find assignments `this.Field = <source>` and route each source through `MoveBoundaryResolver.Resolve(..., OwnedFieldAssignment)`; on `Error`, emit `MIST_OWN_CONTRACT_VIOLATION` naming the method, field, and non-owned source
    - Reject static/global reads as sources (they fall through the resolver to `Error`) per design LD-2
    - _Requirements: 5.3, 5.4, 13.4, 13.5_

  - [x] 6.2 Write property test for cooperation contract soundness
    - **Property 8: Cooperation contract soundness**
    - For all owned types T, T compiles ⟺ every field assignment in T's methods/ctors resolves as a non-error owned move
    - In-process `CompilerDriver.Compile()`, inspect diagnostics
    - **Validates: Requirements 5.3, 5.4**

  - [x] 6.3 Write unit tests for contract verification
    - Compliant bodies (owned params, constructor calls, clone), violating bodies (non-owned param, static read)
    - _Requirements: 5.4, 13.4, 13.5_

- [x] 7. Checkpoint - new components build and unit/property tests pass
  - Ensure all tests pass, ask the user if questions arise.

- [x] 8. Rewrite `OwnershipAnalyzer` orchestrator and remove v1 passes
  - [x] 8.1 Rewrite the `OwnershipAnalyzer` orchestrator
    - Rewrite `Source/Mist/Analysis/Builtin/OwnershipAnalyzer.cs` to keep `AnalyzerId => "OWNERSHIP"` and orchestrate the collaborators in passes: Pass A `RejectOwnedOnInterfacesAndTraits` (Req 5.6), Pass B `RejectOwnedFieldModifier` (Req 11), Pass B2 `RejectOwnedBindingOnSubstructuralTypes` (handled in task 9), Pass C cooperation contract over owned types, Pass D flow-sensitive analysis delegating move sites to `MoveBoundaryResolver` and method calls to `MethodTierVisibilityFilter`
    - **Delete** v1 logic in the same change: `ValidateOwnedBindingTypes` "type must be owned" check, `ValidateOwnedFieldModifiers` (Pass 2c), `ValidateNonStorability` owned-type-field application, `SatisfiesOwnedTypeFieldRestriction`, and the field-restriction loop in `ValidateOwnedTypeDeclarations` (keep the interface/trait rejection, relocated to `RejectOwnedOnInterfacesAndTraits`)
    - Retain the truth-table machinery verbatim (`TruthTable`, `MethodFlowState`, `VariableOwnershipState`, `AccessKind`)
    - _Requirements: 1.3, 2.3, 3.2, 5.6, 6.1, 6.2, 6.3, 6.4, 6.5, 6.6, 6.7, 7.1, 7.2, 7.3, 7.4, 7.5, 9.1, 9.2, 9.3, 9.4, 9.5, 9.6, 9.7, 9.8, 10.1, 10.3, 10.4, 12.1, 12.2, 12.3, 12.4_
    - Standard-pipeline consumed and reassigned lifetimes (2026-10-03): the move rule is unchanged. A move into another binding (6.2) and a reassignment after a move, in a loop or out (6.6), are admitted, and a use after a move is still refused. What changed is the cleanup those states owe. For a linear local, consumption and a later initialization are tracked separately, so the binding owes the release of each fresh value it is given, and the destination of a move owes the release instead of the source. A lease owes nothing. See [`backlog/analysis-v2-capability-progression.yaml` at da1aaa8f](https://github.com/LynxStar/MistLang/blob/da1aaa8fd416bd3bdf93e496281e7b2cbf52594c/backlog/analysis-v2-capability-progression.yaml), `ownership-borrow-checking` (order 11), and [`backlog/emitter-v2-capability-progression.yaml` at da1aaa8f](https://github.com/LynxStar/MistLang/blob/da1aaa8fd416bd3bdf93e496281e7b2cbf52594c/backlog/emitter-v2-capability-progression.yaml), order 161. _Requirements: 6.2, 6.6._

  - [x] 8.2 Remove the `MIST_OWN_FIELD_NOT_OWNED` and structural `MIST_OWN_TYPE_REQUIRES_OWNED` diagnostics
    - Delete the `MIST_OWN_FIELD_NOT_OWNED` diagnostic id, message definition, and all references (do NOT touch `MIST_DROP_FIELD_NOT_OWNED` in `DropAnalyzer.cs` — out of scope)
    - Delete the `MIST_OWN_TYPE_REQUIRES_OWNED` structural-eligibility diagnostic id, message, and references
    - Remove any struct ownership inference engine artifacts (Req 12.1)
    - _Requirements: 10.2, 10.3, 11.3, 12.1_

  - [x] 8.3 Write property test: truth table preserved
    - **Property 9: Truth table preserved**
    - For all states s and actions a, v2's permitted(s, a) == the v1 truth table value
    - In-process semantic-model inspection
    - **Validates: Requirements 7.1, 7.2, 7.3, 7.4, 7.5**

  - [x] 8.4 Write unit tests asserting removed diagnostics never fire
    - Assert `MIST_OWN_FIELD_NOT_OWNED` and structural `MIST_OWN_TYPE_REQUIRES_OWNED` are never emitted for owned types with non-owned fields or owned bindings of non-owned types
    - _Requirements: 10.2, 10.3, 10.4_

  - [x] 8.5 Write unit tests for lending tier integration
    - Owned binding lent as borrowed/frozen/readonly ref transitions correctly; unrestricted binding to borrowed/frozen rejected; double-lend rejected; frozen-while-frozen allowed, borrow/move rejected
    - _Requirements: 9.1, 9.2, 9.3, 9.4, 9.5, 9.6, 9.7, 9.8_

- [x] 9. Substructural restriction on owned bindings
  - [x] 9.1 Implement `RejectOwnedBindingOnSubstructuralTypes`
    - In the `OwnershipAnalyzer` Pass B2, reject the `owned` binding specifier applied to a `linear`, `affine`, or `relevant` type, emitting `MIST_OWN_SUBSTRUCTURAL_BINDING` naming the binding and substructural type
    - Accept owned bindings of non-substructural types (unrestricted or `owned`), subject to provenance
    - _Requirements: 1.4, 13.8_

  - [x] 9.2 Write property test for substructural rejection
    - **Property 13: Owned binding rejects substructural types**
    - For all `linear`/`affine`/`relevant` types T, `owned` binding of T → `MIST_OWN_SUBSTRUCTURAL_BINDING`; for all non-substructural types, permitted
    - In-process `CompilerDriver.Compile()`, inspect diagnostics
    - **Validates: Requirements 1.3, 1.4**

- [x] 10. Grammar and owned-field-modifier hard error
  - [x] 10.1 Remove/hard-error the `owned` field modifier
    - In `Source/Mist/Grammar/Mist.storm`, evaluate dropping the owned alternative from `FieldDeclaration` (preserve `frozen`/`borrowed`/`readonly ref` field tiers); if clean grammar removal is not feasible without regeneration impact, keep parsing it syntactically and reject in `RejectOwnedFieldModifier`
    - Implement `RejectOwnedFieldModifier` to emit the hard error `MIST_OWN_FIELD_MODIFIER_REMOVED` (no warning, no silent acceptance) when an `owned` specifier appears on a field declaration
    - Regenerate grammar artifacts if `Mist.storm` changed
    - _Requirements: 11.1, 11.2, 13.6, 19.1, 19.2_

  - [x] 10.2 Write unit tests for the owned-field-modifier hard error
    - Assert `owned MyClass data;` as a field produces `MIST_OWN_FIELD_MODIFIER_REMOVED` (error, not warning)
    - _Requirements: 11.2, 13.6, 19.2_

- [x] 11. New diagnostic definitions
  - [x] 11.1 Define the new ownership diagnostics
    - Add diagnostic id + message definitions for `MIST_OWN_MUTABLE_HIDDEN`, `MIST_OWN_REQUIRES_OWNED` (repurposed), `MIST_OWN_NOT_CLONABLE`, `MIST_OWN_CONTRACT_VIOLATION`, `MIST_OWN_FIELD_MODIFIER_REMOVED`, and `MIST_OWN_SUBSTRUCTURAL_BINDING`, each with the message content described in the Error Handling table (method/type/field/source names and remediation hints)
    - Confirm kept diagnostics (`MIST_OWN_USE_AFTER_MOVE`, `MIST_OWN_INTERFACE_FORBIDDEN`, `MIST_OWN_INCOMPATIBLE_LEND`, `MIST_OWN_MUTATION_BLOCKED`, `MIST_OWN_EXCLUSIVE_VIOLATION`) remain
    - _Requirements: 13.1, 13.2, 13.3, 13.4, 13.5, 13.6, 13.7, 13.8_

  - [x] 11.2 Write unit tests for each new diagnostic's trigger and message
    - One test per diagnostic exercising the error scenario and asserting the id and key message tokens
    - _Requirements: 13.1, 13.2, 13.4, 13.7, 13.8_

- [x] 12. C# emitter: `.Clone()` insertion and ownership erasure
  - [x] 12.1 Emit `.Clone()` at resolver-marked clone sites and confirm erasure
    - In `Source/Mist/Backends/CSharpTranspiler/CSharpEmitter.cs`, read the `ImplicitClone` annotations recorded by `MoveBoundaryResolver` and emit an explicit `.Clone()` call at the assignment/argument site; apply value-type optimization (elide `.Clone()` for value types whose copy semantics already produce independence)
    - Confirm `owned` keyword erasure, no runtime tracking, no move-nulling; `IClonable<T>` emits as a standard C# interface; `__CreateDefault` reuse for `default`-as-owned-source is unchanged
    - _Requirements: 14.1, 14.2, 14.3, 14.4, 14.5, 14.6, 14.7, 8.8_

  - [x] 12.2 Write property test: zero runtime ownership artifacts
    - **Property 12: Zero runtime ownership artifacts**
    - For all compiled programs, emitted C# contains no `owned` keyword, no reference counting, no state fields, no move-nulling — only `.Clone()` at resolved clone sites
    - In-process compile, inspect emitted C# text
    - **Validates: Requirements 14.1, 14.3, 14.4**

  - [x] 12.3 Write unit tests for clone emission and value-type elision
    - Assert `.Clone()` emitted for clonable class sources; assert elided for value types
    - _Requirements: 14.7, 8.8_

- [x] 13. Round-trip parsing support
  - [x] 13.1 Write property test for ownership round-trip parsing
    - **Property 11: Round-trip parsing**
    - For all valid owned type decls, owned bindings, and `IClonable` impls, parse → pretty-print → parse yields an equivalent tree; parse errors on reprint count as a violation
    - In-process parse/pretty-print only (no process spawn)
    - **Validates: Requirements 17.1, 17.2, 17.3**

  - [x] 13.2 Fix any pretty-printer gaps surfaced by round-trip
    - If the round-trip property reveals `MistPrettyPrinter` does not faithfully reproduce `owned` type modifiers, `owned` bindings, or `IClonable` impls, fix the printer to round-trip them
    - _Requirements: 17.1, 17.2, 17.3, 17.4_

- [x] 14. Checkpoint - full compiler analysis path green
  - Ensure all tests pass, ask the user if questions arise.

- [x] 15. Remove v1 ownership samples and create the v2 sample
  - [x] 15.1 Delete old ownership samples and unwire from AllFeatures
    - Delete `samples/DeepOwnershipTypes/` and `samples/OwnershipSystem/` entirely
    - Remove their `RunDemo()` calls from `samples/AllFeatures/Program.mist` and their `[[references]]` entries from `samples/AllFeatures/mist.toml`
    - _Requirements: 16.1_

  - [x] 15.2 Create `samples/Ownership/` with the verbatim proof code
    - Create `samples/Ownership/mist.toml` with `name = "Ownership"` and `samples/Ownership/Program.mist` containing the EXACT proof code from Requirement 16 (immutable per the sample-proof-code policy — do not simplify or alter)
    - Include all demos: `NonOwnedBindingDemo`, `OwnedTypeBindingDemo`, `ProvenanceDemo`, `ClonableDemo`, `CloneFromLentDemo`, `LendingDemo`, `AliasingDemo`, `ValueTypeTiersDemo`, `BorrowedRecipientDemo`, `TruthTableDemo`, `MoveDemo`, `DefaultDemo`, and the `Program` orchestrator with `public static void RunDemo()`
    - _Requirements: 16.2, 16.3, 16.4, 16.5_

  - [x] 15.3 Wire the Ownership sample into AllFeatures
    - Add `Ownership.Program.RunDemo()` to `samples/AllFeatures/Program.mist` and a `[[references]]` entry with `path = "../Ownership"` to `samples/AllFeatures/mist.toml`
    - _Requirements: 16.6_

  - [x] 15.4 Compile and run the Ownership sample, verifying exact output
    - Build and run `samples/Ownership` and assert stdout matches the expected full-program output in Requirement 16 byte-for-byte; if the proof code does not compile or output differs, fix the compiler (never the proof code) or stop and report the gap per the sample-proof-code policy
    - _Requirements: 16.7_

- [x] 16. TextMate grammar update
  - [x] 16.1 Confirm/extend `owned` highlighting in the TextMate grammar
    - In `Editor/mist-syntax-highlighting/syntaxes/mist.tmLanguage.json`, ensure `owned` before `class`/`struct` scopes as `storage.modifier.mist` and highlights the following identifier as `entity.name.type.class.mist` / `entity.name.type.struct.mist`; ensure `owned` before `var`/a type scopes as `storage.modifier.mist`; match `owned` only at word boundaries (no IClonable-specific changes)
    - _Requirements: 15.1, 15.2, 15.3, 15.4, 15.5_

- [x] 17. Book documentation
  - [x] 17.1 Write the ownership chapter
    - Add `book/src/advanced/ownership.md` as the single authoritative ownership chapter (no version references / migration notes), covering binding-level ownership, the owned-type cooperation contract, method visibility for non-owned vs owned types in owned bindings, field assignment provenance, move semantics, and `IClonable` bridging (including implicit cloning)
    - _Requirements: 18.1, 18.2_

  - [x] 17.2 Add compiled snippets and SUMMARY.md entry
    - Author chapter code examples as `.mist` snippets in `book/snippets/` with `.cs` files generated by the SnippetEmitter tool (Book Snippet Policy); add the `advanced/ownership.md` reference to `book/src/SUMMARY.md`
    - _Requirements: 18.3, 18.4_

- [x] 18. Final checkpoint - full suite and all samples
  - Run the full `Test/Mist.Tests` suite and the full sample run (AllFeatures plus the `samples/Ownership` sample), verify zero warnings (zero-warnings policy), and confirm the Ownership sample output matches expected byte-for-byte. Ensure all tests pass, ask the user if questions arise.

## Notes

- Tasks marked with `*` are optional test sub-tasks and can be skipped for a faster MVP; core implementation tasks are never optional.
- Each task references specific requirement sub-clauses for traceability.
- The 13 correctness properties from the design map to property-based tests (Properties 1–10, 13 are analysis-only and use in-process `CompilerDriver.Compile()`; Property 11 uses in-process parse/pretty-print; Property 12 inspects emitted C# in-process). **No property test spawns `dotnet` per iteration.** If any property is later found to require per-iteration process execution, it must be raised for approval per the spec-process-spawning-approval policy before implementation.
- The proof code in `samples/Ownership/Program.mist` is immutable (sample-proof-code policy). If it cannot be made to compile and produce the exact expected output, stop and report the gap rather than weakening it.
- The breaking removals (task 8) are grouped with the orchestrator rewrite so the build is fixed in one coherent change; sample fallout is repaired immediately after in task 15.
- `MIST_DROP_FIELD_NOT_OWNED` in `DropAnalyzer.cs` is out of scope and must not be removed.

## Task Dependency Graph

```json
{
  "waves": [
    { "id": 0, "tasks": ["1.1", "1.2", "2.1", "16.1", "17.1"] },
    { "id": 1, "tasks": ["1.3", "3.1", "11.1", "17.2"] },
    { "id": 2, "tasks": ["3.2", "3.3", "4.1", "5.1", "11.2"] },
    { "id": 3, "tasks": ["4.2", "4.3", "4.4", "4.5", "4.6", "4.7", "4.8", "5.2", "5.3", "6.1"] },
    { "id": 4, "tasks": ["6.2", "6.3", "8.1"] },
    { "id": 5, "tasks": ["8.2", "8.3", "8.4", "8.5", "9.1", "13.1"] },
    { "id": 6, "tasks": ["9.2", "10.1", "12.1", "13.2"] },
    { "id": 7, "tasks": ["10.2", "12.2", "12.3", "15.1"] },
    { "id": 8, "tasks": ["15.2"] },
    { "id": 9, "tasks": ["15.3"] },
    { "id": 10, "tasks": ["15.4"] }
  ]
}
```
