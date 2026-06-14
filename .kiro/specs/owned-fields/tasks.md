# Implementation Plan: Owned Fields

## Overview

Owned Fields is a delta on ownership-v2 that reintroduces the `owned` modifier on individual fields with new semantics: per-field state tracking, this-tier inference, ownership inference on locals, and a field-type restriction for `owned type`. The implementation is sequenced to keep the build coherent: additive foundations first (grammar `with {tier}` clause, type model `IsOwnedField`, `ThisTier` enum, `InferredBinding` record, and the standalone `FieldTypeRestrictionVerifier`), then the single breaking commit that removes the three deleted passes (`RejectOwnedFieldModifier`, `MethodTierVisibilityFilter`, `CooperationContractVerifier`) and their diagnostics, then additive new passes (`ThisTierInferencePass`, `OwnershipInferencePass`, per-field state in `MethodFlowState`, shared-vs-owned parent enforcement), then orchestrator wiring, diagnostics, property tests, unit tests, and finally integration (sample update, TextMate grammar, book chapter).

All work is in **C#** (compiler at `Source/Mist/`), **Mist** (samples at `samples/`), and **Markdown** (book at `book/src/`). Tests live in `Test/Mist.Tests/`. The 9 correctness properties map to FsCheck property tests that use **in-process `CompilerDriver.Compile()`** (analysis-only) — no per-iteration process spawning (see Notes).

## Tasks

- [x] 1. Grammar: `with {tier}` clause on method declarations
  - [x] 1.1 Add `with {tier}` clause to Storm grammar
    - In `Source/Mist/Grammar/Mist.storm`, extend the `MethodDeclaration` rule to accept an optional `WithClause` after the parameter list and before the body: `WithClause: 'with' OwnershipTierKeyword` where `OwnershipTierKeyword` is `'owned' | 'borrowed' | 'frozen' | 'readonly' 'ref'`
    - Regenerate grammar artifacts after the `.storm` file changes
    - _Requirements: 17.8, 17.9_

  - [x] 1.2 Parse and expose `WithClause` on method symbol
    - In `Source/Mist/Semantics/`, update the `SemanticModelBuilder` to read the parsed `WithClause` and store the declared tier on the method symbol as an optional `ThisTier?` property (null when not declared)
    - Verify that methods without `with` parse as before (no regression)
    - _Requirements: 17.8, 17.9, 17.10_

  - [x] 1.3 Write unit tests for `with {tier}` parsing
    - Assert `void Foo() with borrowed { }` parses successfully and produces the correct symbol annotation
    - Assert `string Describe() with frozen { }` and `void Consume() with owned { }` parse correctly
    - Assert `void Bar() with readonly ref { }` (two-token tier) parses correctly
    - Assert methods without `with` retain null/None annotation
    - _Requirements: 17.8, 17.9_

- [x] 2. Type model: `IsOwnedField` property on field symbols
  - [x] 2.1 Add `IsOwnedField` to field symbol
    - In `Source/Mist/Semantics/Model/`, add `public bool IsOwnedField { get; internal set; }` to the field symbol type, set during semantic model building from the field declaration's ownership specifier
    - For `owned type` instances, add logic in the semantic model builder (or a post-pass) that sets `IsOwnedField = true` on ALL instance fields regardless of explicit annotation (Req 3 AC 1)
    - _Requirements: 1.1, 1.2, 3.1, 3.2_

  - [x] 2.2 Reject `owned` on static fields
    - In the type checker or semantic model builder, detect `owned` on a static field and emit `MIST_OWN_STATIC_FIELD_OWNED`
    - _Requirements: 1.5, 11.4_

  - [x] 2.3 Write unit tests for `IsOwnedField` resolution
    - Assert explicit `owned Widget data;` → `IsOwnedField == true`
    - Assert non-owned field → `IsOwnedField == false`
    - Assert `owned type` implicit marking: all instance fields report `IsOwnedField == true`
    - Assert redundant `owned` on a field within an `owned type` is accepted without error (Req 3 AC 2)
    - Assert `owned` on static field → error
    - _Requirements: 1.1, 1.2, 1.5, 3.1, 3.2_

- [x] 3. `ThisTier` enum and `InferredBinding` record types
  - [x] 3.1 Define the `ThisTier` enum
    - Add `Source/Mist/Analysis/Builtin/ThisTier.cs` defining the enum: `None = 0`, `ReadonlyRef = 1`, `Frozen = 2`, `Borrowed = 3`, `Owned = 4`
    - Include ordering comparison helper: `static bool Satisfies(ThisTier callerTier, ThisTier requiredTier)` → `callerTier >= requiredTier`
    - Include the satisfaction table mapping `OwnershipTier` (from bindings) to `ThisTier`
    - _Requirements: 17.15_

  - [x] 3.2 Define the `InferredBinding` record and `InferenceReason` enum
    - Add `Source/Mist/Analysis/Builtin/InferredBinding.cs` defining `public readonly record struct InferredBinding(ISymbol Symbol, OwnershipTier InferredTier, InferenceReason Reason)` and the `InferenceReason` enum (`OwnedInit`, `ThisTierRequired`, `OwnedParamConsumption`, `SharedFallback`, `Downgraded`)
    - _Requirements: 18.1, 18.2_

- [x] 4. `FieldTypeRestrictionVerifier` — standalone, testable independently
  - [x] 4.1 Implement `FieldTypeRestrictionVerifier`
    - Add `Source/Mist/Analysis/Builtin/FieldTypeRestrictionVerifier.cs` with `static void Verify(IType ownedType, ISemanticModel model, DiagnosticBag diagnostics)`
    - For each instance field of an owned type, verify the field's type is: (1) another owned type, (2) implements `IClonable<T>`, or (3) is a value type. If none, emit `MIST_OWN_FIELD_TYPE_VIOLATION`
    - Handle generic type parameters: check constraints for owned/clonable (Req 4 AC 6)
    - Runs after IClonable derivation so auto-derived implementations are considered (Req 4 AC 5)
    - _Requirements: 4.1, 4.2, 4.3, 4.4, 4.5, 4.6, 5.1, 5.2, 5.3_

  - [x] 4.2 Write unit tests for `FieldTypeRestrictionVerifier`
    - Owned type with all-owned-type fields → passes
    - Owned type with all-IClonable fields → passes
    - Owned type with value-type fields → passes
    - Owned type with mixed valid fields → passes
    - Owned type with non-owned reference type field (not IClonable) → `MIST_OWN_FIELD_TYPE_VIOLATION`
    - Owned type with unconstrained generic type parameter field → error
    - Multiple violations in same type → each reported independently (Req 5.3, 11.6)
    - _Requirements: 4.1, 4.2, 4.3, 4.4, 4.6, 5.2, 5.3, 11.2_

- [x] 5. Checkpoint — Wave 0 foundations build and tests pass
  - Ensure the grammar change regenerated successfully, type model additions compile, `ThisTier`/`InferredBinding` types exist, and `FieldTypeRestrictionVerifier` unit tests pass. Ask the user if questions arise.

- [x] 6. Delete `RejectOwnedFieldModifier` pass + `MIST_OWN_FIELD_MODIFIER_REMOVED` diagnostic
  - [x] 6.1 Remove `RejectOwnedFieldModifier` and its diagnostic
    - In `Source/Mist/Analysis/Builtin/OwnershipAnalyzer.cs`, delete the `RejectOwnedFieldModifier` method and its orchestrator call site
    - Delete the `MIST_OWN_FIELD_MODIFIER_REMOVED` diagnostic ID definition, message builder (`FieldModifierRemovedMessage`), and all emission sites
    - Delete/update unit tests in `Test/Mist.Tests/` that assert `MIST_OWN_FIELD_MODIFIER_REMOVED` is emitted — replace with tests that assert `owned` field declarations are now accepted
    - _Requirements: 7.1, 7.2, 7.3, 7.4_

- [x] 7. Delete `MethodTierVisibilityFilter` + `MIST_OWN_MUTABLE_HIDDEN` diagnostic
  - [x] 7.1 Remove `MethodTierVisibilityFilter` and its diagnostic
    - Delete `Source/Mist/Analysis/Builtin/MethodTierVisibilityFilter.cs` entirely
    - Delete the `MIST_OWN_MUTABLE_HIDDEN` diagnostic ID definition and message builder
    - Remove all call sites in `OwnershipAnalyzer.cs` that reference the filter
    - Delete unit tests for `MethodTierVisibilityFilter` in `Test/Mist.Tests/`
    - _Requirements: 6.1, 6.2, 6.3, 6.4_

- [x] 8. Delete `CooperationContractVerifier` + `MIST_OWN_CONTRACT_VIOLATION` diagnostic
  - [x] 8.1 Remove `CooperationContractVerifier` and its diagnostic
    - Delete `Source/Mist/Analysis/Builtin/CooperationContractVerifier.cs` entirely
    - Delete the `MIST_OWN_CONTRACT_VIOLATION` diagnostic ID definition and message builder
    - Remove the cooperation contract loop call site in `OwnershipAnalyzer.cs`
    - Delete unit tests for `CooperationContractVerifier` in `Test/Mist.Tests/`
    - _Requirements: 5.4, 8.1, 8.2_

- [x] 9. Checkpoint — Wave 1 removals build coherently
  - Ensure the build compiles after all three deletions. The owned-field-modifier error no longer fires, mutable method hiding is gone, and the behavioral cooperation contract is removed. Tests that asserted those old behaviors are deleted. Ask the user if questions arise.

- [ ] 10. `ThisTierInferencePass` — infer this-tier per method
  - [x] 10.1 Implement `ThisTierInferencePass`
    - Add `Source/Mist/Analysis/Builtin/ThisTierInferencePass.cs` with `void Analyze(ISemanticModel model, DiagnosticBag diagnostics)` and `static ThisTier GetThisTier(ISymbol methodSymbol)`
    - Implement the inference algorithm: for each method, scan owned-field accesses in the body; move/write → `Borrowed`, freeze/frozenRef → `Frozen`, read → `ReadonlyRef`, no access → `None`; compose transitively through helper calls on `this` (Req 17 AC 7)
    - For methods with explicit `with {tier}` annotation: use the declared tier but validate that `declaredTier >= requiredTier`; if under-declared, emit `MIST_OWN_THIS_TIER_INSUFFICIENT`; over-declared is accepted (Req 17 AC 11–12)
    - Record the inferred/declared `ThisTier` on each method symbol for consumption by the flow-sensitive pass
    - _Requirements: 17.1, 17.2, 17.3, 17.4, 17.5, 17.6, 17.7, 17.10, 17.11, 17.12, 17.13, 17.14, 17.17_

  - [ ] 10.2 Write unit tests for `ThisTierInferencePass`
    - Method that only reads owned fields → inferred `ReadonlyRef`
    - Method that freezes an owned field → inferred `Frozen`
    - Method that writes to an owned field → inferred `Borrowed`
    - Method that moves out of an owned field → inferred `Borrowed`
    - Method with no owned-field access → inferred `None`
    - Composition: reads field A, writes field B → `Borrowed` (most restrictive wins)
    - Transitive: method A calls method B on `this` where B requires `Borrowed` → A requires `Borrowed`
    - Explicit `with borrowed` matching body → accepted
    - Explicit `with frozen` but body writes → `MIST_OWN_THIS_TIER_INSUFFICIENT`
    - Explicit `with owned` but body only reads → accepted (over-constraining is valid)
    - _Requirements: 17.1, 17.2, 17.3, 17.4, 17.5, 17.6, 17.7, 17.11, 17.12_

- [ ] 11. `OwnershipInferencePass` — infer binding tiers for var declarations
  - [x] 11.1 Implement `OwnershipInferencePass`
    - Add `Source/Mist/Analysis/Builtin/OwnershipInferencePass.cs` with `IReadOnlyDictionary<ISymbol, InferredBinding> InferBindings(SyntaxNode functionBody, ISemanticModel model, DiagnosticBag diagnostics)`
    - Implement the fixed-point algorithm: collect `var` declarations, initialize candidates optimistically (`Owned` for constructor/owned-returning inits), iterate downgrading on conflicts (`Owned → Borrowed → Frozen → ReadonlyRef → Shared`), `Shared` as ultimate fallback (never conflicts)
    - If no consistent assignment after downgrade → emit `MIST_OWN_INFERENCE_FAILURE`
    - Does not apply to variables with explicit specifiers — those are validated directly
    - _Requirements: 18.1, 18.2, 18.3, 18.4, 18.5, 18.6, 18.7, 18.8, 18.11, 18.12_

  - [ ] 11.2 Write unit tests for `OwnershipInferencePass`
    - `var x = new T()` where x used for ownership ops → inferred `Owned`
    - `var x = new T()` where x only used for reads → inferred `Shared` (downgraded)
    - `var y = x` where x used later → y cannot be `Owned` (would move x), downgrades
    - Fixed-point with mutual dependencies between two vars
    - `var x` with no ownership usage → inferred `Shared`
    - Explicit `owned var` → no inference, validated directly
    - Conflicting usage that cannot be resolved → `MIST_OWN_INFERENCE_FAILURE`
    - _Requirements: 18.1, 18.2, 18.3, 18.4, 18.5, 18.6, 18.7, 18.12_

- [ ] 12. Per-field state tracking in `MethodFlowState`
  - [x] 12.1 Extend `MethodFlowState` with per-field state
    - In `Source/Mist/Analysis/Builtin/MethodFlowState.cs`, add `Dictionary<ISymbol, Dictionary<ISymbol, VariableOwnershipState>> FieldStates` and `Dictionary<(ISymbol Parent, ISymbol Field), SourceSpan> FieldMoveLocations`
    - Initialize: when an owned binding is registered for a type with owned fields, all owned fields start at `Live`
    - Implement state transitions: field moved out → `Moved`; field borrowed → `LentRef`; field frozen → `LentFrozen`; field readonly-ref'd → `LentReadonly`; new owned value written to Moved field → `Live` (restoration); borrow scope ends → `Live`
    - _Requirements: 16.1, 16.2, 16.3, 16.4, 16.5, 16.6, 16.7, 16.8_

  - [ ] 12.2 Implement composite state computation
    - Add `CompositeState(ISymbol parentSymbol)` method: returns the most restrictive state among all owned fields using ordering `Live < LentReadonly < LentFrozen < LentRef = Moved`
    - The parent's actual state does NOT transition due to child moves/borrows (Req 16 AC 15) — composite state is a restriction on whole-object operations only
    - Field restoration permitted on a parent with composite Moved (parent is actually Live, Req 16 AC 16)
    - _Requirements: 16.9, 16.10, 16.11, 16.12, 16.13, 16.14, 16.15, 16.16, 16.17, 16.18_

  - [ ] 12.3 Extend `Fork()`/`Merge()` for per-field state
    - In `Fork()`: deep-clone `FieldStates` and `FieldMoveLocations`
    - In `Merge()`: for each parent, for each field, take `MoreRestrictiveState`
    - _Requirements: 16.1_

  - [ ] 12.4 Write unit tests for per-field state tracking
    - Move one field → field is Moved, sibling is Live, parent is actually Live
    - Composite state after one field moved → `Moved`
    - Composite state after one field frozen → `LentFrozen`
    - Restore moved field → field returns to Live, composite returns to Live
    - Sibling independence: field A borrowed does NOT block ops on field B (Req 16 AC 22)
    - Parent-to-child restriction: parent externally frozen → child write blocked (Req 16 AC 19–21)
    - Fork/merge preserves per-field state correctly
    - _Requirements: 16.1, 16.2, 16.3, 16.4, 16.5, 16.6, 16.9, 16.11, 16.15, 16.16, 16.19, 16.20, 16.21, 16.22_

- [ ] 13. Shared vs owned parent access enforcement
  - [ ] 13.1 Implement shared parent extraction blocking
    - In the flow-sensitive analysis, when an owned field is accessed through a shared (non-owned) parent binding: permit write with provenance (Req 2 AC 2), permit clone-read if IClonable (Req 2 AC 3), reject move/borrow/freeze/readonly-ref with `MIST_OWN_SHARED_EXTRACT` (Req 2 AC 4–5)
    - When reading through shared parent and type is NOT IClonable → emit `MIST_OWN_SHARED_READ_NOT_CLONABLE` (Req 2 AC 6)
    - _Requirements: 2.1, 2.2, 2.3, 2.4, 2.5, 2.6, 11.1, 11.7_

  - [ ] 13.2 Implement owned parent field access with per-field state
    - When an owned field is accessed through an owned parent: all operations permitted (move, borrow, freeze, readonly ref, write with provenance) subject to per-field state tracking
    - Connect to `MoveBoundaryResolver` for provenance on writes
    - After resolver approves a move-out, update per-field state to `Moved`
    - _Requirements: 2.1, 8.1, 8.2, 8.3, 8.4, 16.2_

  - [ ] 13.3 Implement method calls with partial field states
    - When a method call is attempted on an instance with non-Live owned fields, use `ThisTierInferencePass.GetThisTier()` + field-read analysis to determine which fields the method accesses; reject if any accessed field's state does not permit the required action
    - _Requirements: 16.23, 16.24_

  - [ ] 13.4 Write unit tests for shared/owned parent access rules
    - Shared parent → write with owned source succeeds
    - Shared parent → clone-read of IClonable field succeeds
    - Shared parent → move out of owned field → `MIST_OWN_SHARED_EXTRACT`
    - Shared parent → borrow owned field → `MIST_OWN_SHARED_EXTRACT`
    - Shared parent → read non-IClonable field → `MIST_OWN_SHARED_READ_NOT_CLONABLE`
    - Owned parent → move out succeeds, field enters Moved
    - Owned parent → write with non-owned non-clonable source → provenance error
    - Method call on instance with partial field state (accessed field is Moved) → rejected
    - _Requirements: 2.1, 2.2, 2.3, 2.4, 2.5, 2.6, 8.1, 8.4, 11.1, 11.7, 16.23_

- [ ] 14. Checkpoint — Wave 2 new passes build and unit tests pass
  - Ensure `ThisTierInferencePass`, `OwnershipInferencePass`, per-field state tracking, and shared/owned enforcement compile and all unit tests pass. Ask the user if questions arise.

- [ ] 15. Rewrite `OwnershipAnalyzer` orchestrator to wire new passes
  - [ ] 15.1 Rewrite the `OwnershipAnalyzer` orchestrator
    - Rewrite `Source/Mist/Analysis/Builtin/OwnershipAnalyzer.cs` `Analyze()` method:
      - Pass A: `RejectOwnedOnInterfacesAndTraits` (unchanged)
      - Pass B: **DELETED** (already removed in Wave 1)
      - Pass B2: `RejectOwnedBindingOnSubstructuralTypes` (unchanged)
      - Pass C: `FieldTypeRestrictionVerifier.Verify()` for each owned type (replaces cooperation contract loop)
      - Pass D-pre: `ThisTierInferencePass.Analyze()` — NEW
      - Pass D: `AnalyzeFlowSensitive()` extended with `OwnershipInferencePass`, per-field state, this-tier call-site checking
    - _Requirements: 5.4, 7.2, 6.4, 17.15, 17.16, 18.1_

  - [ ] 15.2 Extend `AnalyzeFlowSensitive` for per-field state + this-tier call-site checking
    - Initialize per-field state for owned bindings of types with owned fields
    - At each field access site: determine parent tier, apply shared/owned rules (task 13)
    - At each method call site: query `ThisTierInferencePass.GetThisTier(method)` and verify caller's binding tier satisfies it; if insufficient, emit `MIST_OWN_THIS_TIER_MISMATCH`
    - Integrate `OwnershipInferencePass.InferBindings()` to determine var binding tiers before flow analysis
    - _Requirements: 2.1, 2.4, 16.1, 16.9, 17.15, 17.16, 18.1_

  - [ ] 15.3 Write unit tests for orchestrator integration
    - End-to-end: owned type with valid fields compiles cleanly
    - End-to-end: owned type with violating field → `MIST_OWN_FIELD_TYPE_VIOLATION`
    - End-to-end: method call through shared binding where method requires `borrowed` → `MIST_OWN_THIS_TIER_MISMATCH`
    - End-to-end: owned binding, move field out, access sibling → succeeds
    - End-to-end: `var x = new T(); x.MutateTier();` → inference gives `Owned`, call succeeds
    - _Requirements: 4.4, 5.1, 6.3, 16.2, 17.16, 18.1_

- [ ] 16. Checkpoint — Wave 3 orchestrator wired, build green
  - Ensure the rewritten orchestrator correctly delegates to all new passes and the build is green with all existing + new tests passing. Ask the user if questions arise.

- [ ] 17. Define new diagnostics
  - [x] 17.1 Define diagnostic IDs and message builders
    - Add diagnostic ID definitions and message builders for:
      - `MIST_OWN_SHARED_EXTRACT` — move/borrow/freeze/readonly-ref of owned field through shared parent (Req 2 AC 5, Req 11 AC 1)
      - `MIST_OWN_FIELD_TYPE_VIOLATION` — owned type field type not owned/IClonable/value (Req 4 AC 4, Req 11 AC 2)
      - `MIST_OWN_THIS_TIER_INSUFFICIENT` — explicit `with` tier under-declared (Req 17 AC 11)
      - `MIST_OWN_THIS_TIER_MISMATCH` — call-site receiver does not satisfy method's this-tier (Req 17 AC 16)
      - `MIST_OWN_SHARED_READ_NOT_CLONABLE` — read of owned field through shared parent, type not IClonable (Req 2 AC 6, Req 11 AC 7)
      - `MIST_OWN_INFERENCE_FAILURE` — ownership inference cannot find consistent tier (Req 18 AC 5)
      - `MIST_OWN_STATIC_FIELD_OWNED` — `owned` on a static field (Req 1 AC 5, Req 11 AC 4)
    - Confirm removed diagnostics (`MIST_OWN_FIELD_MODIFIER_REMOVED`, `MIST_OWN_MUTABLE_HIDDEN`, `MIST_OWN_CONTRACT_VIOLATION`) are no longer defined
    - _Requirements: 11.1, 11.2, 11.3, 11.4, 11.5, 11.6, 11.7_

  - [ ] 17.2 Write unit tests for each new diagnostic's trigger and message
    - One test per diagnostic exercising the error scenario and asserting the ID and key message tokens
    - Assert removed diagnostics never fire for any input
    - Assert multiple violations in same compilation unit are all reported independently (Req 11 AC 6)
    - _Requirements: 11.1, 11.2, 11.3, 11.4, 11.5, 11.6, 11.7_

- [ ] 18. Property tests — 9 properties from the design
  - [ ] 18.1 Property 1: This-tier inference soundness (Req 17 AC 1–6)
    - For all methods m with owned-field accesses, the inferred this-tier is the minimum tier that satisfies all field operations in the body: `inferred == MaxTierFromFieldOps(m.Body)`
    - In-process `CompilerDriver.Compile()`, inspect semantic model
    - **Validates: Requirements 17.1, 17.2, 17.3, 17.4, 17.5, 17.6**

  - [ ] 18.2 Property 2: Ownership inference soundness (Req 18 AC 1–5)
    - For all `var` bindings b in a function, the inferred tier satisfies all uses of b. If downgraded one step, at least one use would produce an error: `AllUsesSatisfied(b, tier) ∧ (tier == Shared ∨ ¬AllUsesSatisfied(b, Downgrade(tier)))`
    - In-process `CompilerDriver.Compile()`, inspect diagnostics
    - **Validates: Requirements 18.1, 18.2, 18.3, 18.4, 18.5**

  - [ ] 18.3 Property 3: Composite state is max(child states) (Req 16 AC 9–14)
    - For all owned bindings with per-field state, `CompositeState(p) == Max(State(f₁), ..., State(fₙ))`
    - In-process semantic-model inspection
    - **Validates: Requirements 16.9, 16.10, 16.11, 16.12, 16.13, 16.14**

  - [ ] 18.4 Property 4: Shared parent blocks extraction (Req 2 AC 4–5)
    - For all shared parent bindings and owned fields: extract → `MIST_OWN_SHARED_EXTRACT`, write with provenance → succeeds, clone-read → succeeds iff IClonable
    - In-process `CompilerDriver.Compile()`, inspect diagnostics
    - **Validates: Requirements 2.2, 2.3, 2.4, 2.5, 2.6**

  - [ ] 18.5 Property 5: Siblings are independent (Req 16 AC 22)
    - For all owned bindings with multiple owned fields where field A is in a non-Live state, operations on sibling field B are unaffected
    - In-process `CompilerDriver.Compile()`, inspect diagnostics
    - **Validates: Requirements 16.22**

  - [ ] 18.6 Property 6: Field-type restriction correctness (Req 4)
    - For all owned types T: `Compiles(T) ⟺ ∀ field f: IsOwnedType(typeof(f)) ∨ ImplementsIClonable(typeof(f)) ∨ IsValueType(typeof(f))`
    - In-process `CompilerDriver.Compile()`, inspect diagnostics
    - **Validates: Requirements 4.1, 4.2, 4.3, 4.4**

  - [ ] 18.7 Property 7: Provenance on owned fields (Req 8)
    - For all owned-field assignments, accepted ⟺ `MoveBoundaryResolver.Resolve(source, OwnedFieldAssignment) ∉ {Error}`
    - In-process `CompilerDriver.Compile()`, inspect diagnostics
    - **Validates: Requirements 8.1, 8.2, 8.3, 8.4**

  - [ ] 18.8 Property 8: With-annotation validation (Req 17 AC 10–12)
    - For all methods with explicit `with declared`: `Compiles(m) ⟺ declared >= InferredMinimum(m.Body)`
    - In-process `CompilerDriver.Compile()`, inspect diagnostics
    - **Validates: Requirements 17.10, 17.11, 17.12**

  - [ ] 18.9 Property 9: Existing truth table preserved (ownership-v2 Req 7)
    - For all states s and actions a applied to a parent binding's composite state, permissions equal the v2 truth table
    - In-process semantic-model inspection
    - **Validates: Requirements 16.9, 16.10, 16.11, 16.12, 16.13, 16.14**

- [ ] 19. Unit tests for each component
  - [ ] 19.1 Write comprehensive diagnostic removal tests
    - Assert `MIST_OWN_FIELD_MODIFIER_REMOVED` is never emitted for any `owned` field declaration
    - Assert `MIST_OWN_MUTABLE_HIDDEN` is never emitted for any method call through owned binding
    - Assert `MIST_OWN_CONTRACT_VIOLATION` is never emitted for any owned-type method
    - Test programs that previously triggered these diagnostics now compile cleanly
    - _Requirements: 6.2, 7.1, 7.3, 7.4_

  - [ ] 19.2 Write owned field provenance tests
    - Constructor assigns owned field from owned parameter → succeeds
    - Constructor assigns owned field from non-owned non-clonable param → error
    - Constructor assigns owned field from IClonable source → `.Clone()` inserted, succeeds
    - Method assigns owned field from `default` → succeeds
    - Method assigns owned field from value-type expression → succeeds (value copy exempt)
    - _Requirements: 8.1, 8.2, 8.3, 8.4, 12.1, 12.2, 12.3, 12.4_

  - [ ] 19.3 Write this-tier call-site checking tests
    - Owned binding calls method requiring `Borrowed` → succeeds
    - Borrowed binding calls method requiring `Borrowed` → succeeds
    - Frozen binding calls method requiring `Borrowed` → `MIST_OWN_THIS_TIER_MISMATCH`
    - Shared binding calls method requiring `ReadonlyRef` → `MIST_OWN_THIS_TIER_MISMATCH`
    - Shared binding calls method with no tier requirement → succeeds
    - `with owned` method consumes receiver, subsequent access → `MIST_OWN_USE_AFTER_MOVE`
    - _Requirements: 17.13, 17.14, 17.15, 17.16_

- [ ] 20. Checkpoint — Wave 4+5 diagnostics and tests pass
  - Ensure all new diagnostics are defined, all 9 property tests pass, all unit tests pass. Ask the user if questions arise.

- [ ] 21. Update `samples/Ownership/Program.mist` with owned-field proof code
  - [ ] 21.1 Add owned-field demonstrations to the sample
    - Update `samples/Ownership/Program.mist` with the verbatim proof code from Requirement 14 (immutable per sample-proof-code policy): `OwnedFieldDemo`, `OwnedTypeFieldRestrictionDemo`, `PartialMoveDemo`, `FieldLendingDemo`, `ThisTierDemo`, `OwnershipInferenceDemo`
    - Add `RunDemo()` calls for each new demo class in the `Program` orchestrator
    - Preserve ALL existing demonstrations from ownership-v2 (`NonOwnedBindingDemo`, `OwnedTypeBindingDemo`, `ProvenanceDemo`, `ClonableDemo`, `CloneFromLentDemo`, `LendingDemo`, `AliasingDemo`, `ValueTypeTiersDemo`, `BorrowedRecipientDemo`, `TruthTableDemo`, `MoveDemo`, `DefaultDemo`)
    - _Requirements: 14.1, 14.2, 14.3, 14.4, 14.5_

  - [ ] 21.2 Compile and run the Ownership sample, verifying exact output
    - Build and run `samples/Ownership` and assert stdout matches the expected full-program output byte-for-byte
    - If the proof code does not compile or output differs, fix the compiler (never the proof code) or stop and report the gap per the sample-proof-code policy
    - _Requirements: 14.5_

- [ ] 22. TextMate grammar verification
  - [ ] 22.1 Verify/extend TextMate grammar for `owned` fields and `with` keyword
    - In `Editor/mist-syntax-highlighting/syntaxes/mist.tmLanguage.json`, ensure `owned` before a field type annotation scopes as `storage.modifier.mist` at word boundaries
    - Add `with` keyword highlighting in method declarations: scope as `keyword.other.mist` when followed by a tier keyword (`owned`, `borrowed`, `frozen`, `readonly ref`)
    - Verify existing `storage.modifier.mist` pattern covers `owned` in the field-modifier position (Req 13 AC 3)
    - Add test cases: `owned int count` in a class body → `owned` gets `storage.modifier.mist`; `void Foo() with borrowed` → `with` highlighted
    - _Requirements: 13.1, 13.2, 13.3, 13.4_

- [ ] 23. Book chapter update
  - [ ] 23.1 Update ownership chapter with owned-fields section
    - In `book/src/advanced/ownership.md`, add a section on owned fields placed after "Field Assignment Provenance" covering: (a) owned field declaration syntax, (b) this-tier system (inference + `with`), (c) jagged ownership patterns, (d) `owned type` field-type restriction, (e) static-aliasing and non-owned-constructor problems solved, (f) per-field state tracking and partial moves, (g) ownership inference on local bindings
    - _Requirements: 19.1, 19.2_

  - [ ] 23.2 Add compiled snippets for owned-fields section
    - Create 5–12 snippet pairs (`.mist` + `.cs`) in `book/snippets/advanced/ownership/` demonstrating each sub-topic from 23.1, generated by the SnippetEmitter tool following the Book Snippet Policy
    - _Requirements: 19.3, 19.5_

- [ ] 24. Final checkpoint — full suite and all samples
  - Run the full `Test/Mist.Tests` suite and the full sample run (AllFeatures plus `samples/Ownership`), verify zero warnings (zero-warnings policy), and confirm the Ownership sample output matches expected byte-for-byte. Ensure all tests pass, ask the user if questions arise.
  - _Requirements: 14.5, 19.4_

## Notes

- Each task references specific requirement sub-clauses for traceability.
- The 9 correctness properties from the design map to property-based tests (Properties 1–9 are analysis-only and use in-process `CompilerDriver.Compile()`). **No property test spawns `dotnet` per iteration.** If any property is later found to require per-iteration process execution, it must be raised for approval per the spec-process-spawning-approval policy before implementation.
- The proof code in `samples/Ownership/Program.mist` is immutable (sample-proof-code policy). If it cannot be made to compile and produce the exact expected output, stop and report the gap rather than weakening it.
- The breaking removals (tasks 6–8, Wave 1) are grouped so the build breaks and fixes in one coherent commit; tests asserting the removed diagnostics are deleted in the same commit.
- `MIST_DROP_FIELD_NOT_OWNED` in `DropAnalyzer.cs` is OUT OF SCOPE and must not be removed.
- The `with {tier}` grammar change requires Storm grammar regeneration (`Source/Mist/Grammar/Mist.storm`).
- Requirements 9 and 10 are marked "Removed" in the requirements document (subsumed by Requirement 8) — no tasks map to them.
- The `samples/Ownership/Program.mist` update (task 21) preserves all existing ownership-v2 demos; no change to AllFeatures wiring is needed since the sample name and entry point are unchanged.
- Diagnostic definitions (task 17) may be implemented earlier and referenced from tasks 10–13; they are placed in Wave 4 for sequencing clarity but can be done incrementally as each emitting pass is written.

## Task Dependency Graph

```json
{
  "waves": [
    { "id": 0, "tasks": ["1.1", "1.2", "2.1", "2.2", "3.1", "3.2"] },
    { "id": 1, "tasks": ["1.3", "2.3", "4.1"] },
    { "id": 2, "tasks": ["4.2", "6.1", "7.1", "8.1"] },
    { "id": 3, "tasks": ["10.1", "11.1", "12.1", "17.1"] },
    { "id": 4, "tasks": ["10.2", "11.2", "12.2", "12.3", "12.4", "13.1", "13.2", "13.3"] },
    { "id": 5, "tasks": ["13.4", "15.1"] },
    { "id": 6, "tasks": ["15.2", "15.3"] },
    { "id": 7, "tasks": ["17.2", "18.1", "18.2", "18.3", "18.4", "18.5", "18.6", "18.7", "18.8", "18.9"] },
    { "id": 8, "tasks": ["19.1", "19.2", "19.3"] },
    { "id": 9, "tasks": ["21.1", "22.1", "23.1"] },
    { "id": 10, "tasks": ["21.2", "23.2"] }
  ]
}
```
