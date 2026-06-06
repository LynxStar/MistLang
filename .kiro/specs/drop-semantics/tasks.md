# Implementation Plan: Drop Semantics

## Overview

This plan implements substructural type modifiers (`affine`, `linear`, `relevant`) and a unified `Drop()` method for deterministic cleanup, replacing the existing `LiveTaskInfo`-based live-task tracking. Work is ordered foundation-first: keywords → grammar → type model → discipline resolution → synthetic drop inference → `DropAnalyzer` → emitter. The `LiveTaskInfo` → Drop migration is isolated into its own late, carefully-ordered block (Epic 10) so it does not entangle the simpler foundational and emitter work. `ValueTask<T>` is introduced as a new intrinsic linear type, and `Task<T>` becomes relevant.

All code is C# (the compiler) and Mist (standard library + samples), matching the existing codebase — no implementation-language selection is required.

**Testing process policy (approved with the user):**
- All property-based tests run fully in-process via a shared `CompilerDriver.Compile()` and inspect either diagnostics or the emitted C# text. They spawn **0 external processes**.
- The three runtime integration tests use the batched approach: compile once in-process, then spawn `dotnet` **exactly once** to run the compiled output DLL and compare all output lines (mirrors the existing `StdlibEmitterIntegrationTests` pattern).
- No per-iteration process spawning anywhere.

## Tasks

- [x] 1. Keywords and grammar for substructural modifiers
  - [x] 1.1 Add `affine`, `linear`, `relevant` keyword tokens
    - In `Source/Mist/Grammar/keywords.storm`, add `token AFFINE = "affine";`, `token LINEAR = "linear";`, `token RELEVANT = "relevant";` alongside the existing ownership keywords (`OWNED`, `FROZEN`, `REF`, `BORROWED`).
    - _Requirements: 1.1, 1.2_

  - [x] 1.2 Add the new modifiers to the grammar `Modifier` rule
    - In `Source/Mist/Grammar/Mist.storm`, extend the `Modifier` rule (currently `PUBLIC | PRIVATE | STATIC | INTRINSIC | READONLY | OWNED | ASYNC | REF`) with `AFFINE as AffineModifier`, `LINEAR as LinearModifier`, and `RELEVANT as RelevantModifier` so they are accepted in the same position as `OwnedModifier` on `ClassDeclaration` and `StructDeclaration`.
    - _Requirements: 1.1, 1.2_

  - [x] 1.3 Regenerate the parser and verify the build
    - Rebuild the solution so the Storm source generator regenerates the parser/AST node types (`AffineModifierModifierNode`, `LinearModifierModifierNode`, `RelevantModifierModifierNode`).
    - Confirm zero build warnings (zero-warnings policy) and that a `public linear class`/`public relevant class`/`public affine class` declaration parses.
    - _Requirements: 1.1, 1.2_

  - [x] 1.4 Write property test for modifier parsing round-trip
    - **Property 21: Parsing round-trip for substructural modifiers**
    - **Validates: Requirements 1.1, 1.2**
    - Tag: `Feature: drop-semantics, Property 21: Parsing round-trip`
    - Generate valid class/struct declarations with each modifier (and combined with `public`/`static`), parse then print the AST, assert the modifier is preserved. In-process lex/parse only; **spawns 0 external processes**.

- [x] 2. Substructural type model
  - [x] 2.1 Add the `SubstructuralDiscipline` enum
    - Create the `SubstructuralDiscipline` enum (`None`, `Affine`, `Linear`, `Relevant`) in namespace `Mist.Semantics.Model`, with doc comments matching the design.
    - _Requirements: 1.3, 1.4, 1.5_

  - [x] 2.2 Add discipline/drop members to `ClassType` and `StructType`
    - In namespace `Mist.Semantics.Model.Types`, add `Discipline { get; internal set; } = SubstructuralDiscipline.None`, `HasDrop { get; internal set; }`, and `DropReturnType { get; internal set; }` to both `ClassType` and `StructType`.
    - Leave `IsOwnedType` as the existing settable backing field — do NOT convert it to a computed property (multiple producers, including `OwnershipInferenceEngine`, assign to it).
    - _Requirements: 1.3, 1.4, 1.5, 2.2, 7.1, 8.1_

- [x] 3. Discipline resolution and Drop() recognition in the semantic model builder
  - [x] 3.1 Resolve discipline from modifiers and recognize `Drop()`
    - In `Source/Mist/Semantics/Model/SemanticModelBuilder.cs`, during type construction map modifiers to `Discipline`: `affine`/`linear` → resolve and set `IsOwnedType = true` (write-through); `relevant` → leave `IsOwnedType` governed by existing logic; conflicting combinations resolve to `SubstructuralDiscipline.None` (the diagnostic is reported by `DropAnalyzer`, task 5.1).
    - Recognize a method named `Drop` with zero parameters as the type's drop method: set `HasDrop = true` and `DropReturnType` (null when void). The void aliases `Task`/`ValueTask` inherit discipline/`Drop()` from their generic forms.
    - _Requirements: 1.3, 1.4, 1.5, 2.1, 2.2, 2.4, 3.4_

  - [x] 3.2 Write property test for modifier-implies-ownership
    - **Property 1: Substructural modifier implies correct ownership**
    - **Validates: Requirements 1.3, 1.4, 1.5**
    - Tag: `Feature: drop-semantics, Property 1: Substructural modifier implies correct ownership`
    - Generate type declarations with each discipline, build the model via the shared `CompilerDriver`, assert `IsOwnedType` is `true` for affine/linear and `false` for relevant. In-process; **spawns 0 external processes**.

- [x] 4. Synthetic drop inference engine (Stage 4d)
  - [x] 4.1 Implement `SyntheticDropInferenceEngine.RunInference(semanticModel)`
    - New engine mirroring the static `RunInference` shape of `OwnershipInferenceEngine`. For each type with droppable fields and no explicit `Drop()`, synthesize a void `Drop()` that calls each droppable field's `Drop()` in declaration order; set `HasDrop = true`, `DropReturnType = null`. Process in dependency order so the obligation cascades transitively (Req 10.5). Explicit `Drop()` takes precedence (no synthesis).
    - _Requirements: 10.1, 10.2, 10.3, 10.4, 10.5_

  - [x] 4.2 Wire the engine into `CompilerDriver.Compile` as Stage 4d
    - In `Source/Mist/Driver/CompilerDriver.cs`, invoke `SyntheticDropInferenceEngine.RunInference(semanticModel)` as a new hard-coded stage immediately AFTER Stage 4c (`OwnershipInferenceEngine.RunInference`), since it reads finalized `IsOwnedType`/`Discipline` state. No pass-registry — it is a direct sequential call like 4b/4c.
    - _Requirements: 10.1, 10.4_

  - [x] 4.3 Write property test for synthetic drop inference
    - **Property 12: Synthetic drop inference for containing types**
    - **Validates: Requirements 10.1, 10.2, 10.3, 10.4, 10.5**
    - Tag: `Feature: drop-semantics, Property 12: Synthetic drop inference`
    - Generate types with varied field compositions (droppable/non-droppable, nested), assert synthetic `Drop()` is generated exactly when expected, returns void, and the emitted body calls fields in declaration order. In-process; **spawns 0 external processes**.

- [x] 5. DropAnalyzer and diagnostics
  - [x] 5.1 Create `DropAnalyzer` skeleton, diagnostics, and declaration validations
    - New `DropAnalyzer : IAnalyzer` in `Source/Mist/Analysis/Builtin/`, following the `OwnershipAnalyzer` structure (a `MakeDiagnostic` helper and analyzer-local descriptive IDs — NOT central numeric codes).
    - Define IDs: `MIST_DROP_USE_AFTER_MOVE`, `MIST_DROP_USE_AFTER_DROP`, `MIST_DROP_FIELD_NOT_OWNED`, `MIST_DROP_FIELD_OUTSIDE_CONTAINER`, `MIST_DROP_CONFLICTING_MODIFIERS`, `MIST_DROP_PARAMETERS`.
    - Implement declaration-level validations: conflicting modifiers (`MIST_DROP_CONFLICTING_MODIFIERS`), `Drop()` with parameters (`MIST_DROP_PARAMETERS`), and non-owned container with affine/linear field (`MIST_DROP_FIELD_NOT_OWNED`).
    - Register `new DropAnalyzer()` in the analyzer array in `Source/Mist.CLI/Program.cs`, ordered AFTER `OwnershipAnalyzer`.
    - _Requirements: 1.6, 1.7, 2.3, 11.1, 11.2, 11.3, 11.4, 15.3, 15.5_

  - [x] 5.2 Implement flow-sensitive drop-state tracking
    - Add the `VariableDropState` enum (`Live`, `Dropped`, `Moved`, `Returned`) and walk method bodies flow-sensitively, reusing the existing `MethodFlowState` Fork/Merge infrastructure used by `OwnershipAnalyzer`.
    - Report use-after-drop/move on linear (`MIST_DROP_USE_AFTER_MOVE`) and use-after-drop on affine (`MIST_DROP_USE_AFTER_DROP`); `Drop()` on relevant types does NOT consume; field `Drop()` outside the container's `Drop()` reports `MIST_DROP_FIELD_OUTSIDE_CONTAINER` (affine/linear fields only, relevant exempt).
    - _Requirements: 3.1, 3.2, 3.3, 6.2, 6.3, 12.1, 12.2, 12.3, 15.1, 15.2, 15.4_

  - [x] 5.3 Write test for conflicting-modifier rejection
    - **Property 2: Conflicting modifier rejection**
    - **Validates: Requirements 1.6, 1.7**
    - Tag: `Feature: drop-semantics, Property 2: Conflicting modifier rejection`
    - Example-based `[Theory]`/`[InlineData]` over the finite invalid set (`affine linear`, `affine relevant`, `linear relevant`, `owned affine`, `owned linear`, `owned relevant`); assert `MIST_DROP_CONFLICTING_MODIFIERS`. In-process; **spawns 0 external processes**.

  - [x] 5.4 Write property test for field-containment rule
    - **Property 13: Field containment rule — owned fields require owned containers**
    - **Validates: Requirements 11.1, 11.2, 11.3, 11.4**
    - Tag: `Feature: drop-semantics, Property 13: Field containment rule`
    - Generate container/field discipline combinations; assert error iff a non-owned container has an affine/linear field, no error for owned containers or relevant/unrestricted fields. In-process; **spawns 0 external processes**.

  - [x] 5.5 Write property test for drop-as-consume
    - **Property 3: Drop-as-consume for owned types**
    - **Validates: Requirements 3.1, 3.2**
    - Tag: `Feature: drop-semantics, Property 3: Drop-as-consume for owned types`
    - Generate programs that call `Drop()` on an affine/linear variable then access it; assert a use-after-drop error in all cases. In-process; **spawns 0 external processes**.

  - [x] 5.6 Write test for relevant types not consumed by Drop
    - **Property 4: Drop does not consume relevant types**
    - **Validates: Requirements 3.3**
    - Tag: `Feature: drop-semantics, Property 4: Drop does not consume relevant types`
    - Example/generated programs that call `Drop()` on a relevant variable then access it; assert no error. In-process; **spawns 0 external processes**.

  - [x] 5.7 Write property test for field-drop restriction
    - **Property 14: Field drop restricted to container's Drop method**
    - **Validates: Requirements 12.1, 12.2, 12.3**
    - Tag: `Feature: drop-semantics, Property 14: Field drop restriction`
    - Generate field `Drop()` calls at varied sites (inside the container's `Drop()`, in other methods, on another object's field); assert error iff outside the container's `Drop()` for affine/linear fields; relevant fields exempt. In-process; **spawns 0 external processes**.

- [x] 6. Checkpoint - foundational analysis complete
  - Ensure all tests pass, ask the user if questions arise.

- [x] 7. C# emitter auto-drop insertion
  - [x] 7.1 Emit auto-drop at scope exit for linear/relevant variables
    - In `Source/Mist/Backends/CSharpTranspiler/CSharpEmitter.cs`, for scopes containing linear/relevant locals emit `Drop()` calls at scope exit in reverse declaration order, wrapped in `try/finally` so drops fire on exception. Ensure the new `affine`/`linear`/`relevant` modifier nodes are erased (never emitted) in C#. Relevant types always drop (idempotent); linear drops exactly once when `Live`.
    - _Requirements: 4.1, 4.4, 4.5, 4.7, 13.1, 13.2, 14.1, 14.2, 14.3, 14.7_

  - [x] 7.2 Implement drop elision and conditional-move branch sensitivity
    - Skip drop insertion when a linear variable was moved (to a variable or function parameter) or returned; for a variable moved in one conditional branch but not another, insert `Drop()` only in the live branches. Loop-body variables drop at end of each iteration.
    - _Requirements: 4.2, 4.3, 4.6, 13.4, 13.5_

  - [x] 7.3 Emit `await` for Task/ValueTask Drop and support explicit Drop return capture
    - Emit `await expr` (not a `.Drop()` call) when the dropped value's type is `Task<T>`/`ValueTask<T>`; emit a Mist-source `await expr` as C# `await`; on explicit `Drop()` of an owned non-void type allow the caller to capture the return value (`T r = v.Drop();`); discard the return value on auto-insert and in `with` contexts.
    - _Requirements: 2.5, 3.4, 3.5, 7.4, 7.10, 8.8, 14.5, 14.6_

  - [x] 7.4 Complete manual `Drop()` bodies with remaining field drops
    - When a type provides a manual `Drop()` and has droppable fields, track which fields were explicitly dropped in the body and emit `Drop()` for the remaining droppable fields (declaration order) at the end of the method body.
    - _Requirements: 10.7, 10.8_

  - [x] 7.5 Write property test for linear auto-drop and elision
    - **Property 5: Linear auto-drop at scope exit** and **Property 6: Linear drop elision on move or return**
    - **Validates: Requirements 2.8, 4.1, 4.2, 4.3**
    - Tag: `Feature: drop-semantics, Property 5: Linear auto-drop at scope exit`
    - Generate linear locals with random control flow (some moved/returned, some live at exit), compile once in-process, inspect emitted C# to assert `Drop()`/`await` appears exactly where expected and is elided otherwise. **Spawns 0 external processes**.

  - [x] 7.6 Write property test for reverse-declaration drop order
    - **Property 10: Reverse declaration order for multiple drops**
    - **Validates: Requirements 4.5, 13.1**
    - Tag: `Feature: drop-semantics, Property 10: Reverse declaration order`
    - Generate scopes with N droppable variables (N from 2 to 10), assert emitted drop order is reverse declaration order. In-process; **spawns 0 external processes**.

  - [x] 7.7 Write property test for modifier erasure
    - **Property 17: Substructural modifiers erased in emitted C#**
    - **Validates: Requirements 14.7**
    - Tag: `Feature: drop-semantics, Property 17: Modifier erasure`
    - Generate programs with substructural types, compile in-process, assert emitted C# contains none of `affine`/`linear`/`relevant`. **Spawns 0 external processes**.

  - [x] 7.8 Write test for try/finally wrapping
    - **Property 18: Try/finally wrapping for linear/relevant scopes**
    - **Validates: Requirements 14.3**
    - Tag: `Feature: drop-semantics, Property 18: Try/finally wrapping`
    - Assert emitted C# wraps linear/relevant scopes in `try/finally` with drops in the `finally`. In-process; **spawns 0 external processes**.

  - [x] 7.9 Write test for conditional-move branch sensitivity
    - **Property 19: Conditional move branch-sensitive drop**
    - **Validates: Requirements 4.6, 13.5**
    - Tag: `Feature: drop-semantics, Property 19: Conditional move branch-sensitive drop`
    - Assert a variable moved in one branch is dropped only in the other branch. In-process; **spawns 0 external processes**.

  - [x] 7.10 Write test for relevant drop firing for every reference
    - **Property 7: Relevant auto-drop fires for every reference**
    - **Validates: Requirements 2.9, 4.4, 6.3**
    - Tag: `Feature: drop-semantics, Property 7: Relevant auto-drop fires for every reference`
    - Assert auto-drop is emitted for a relevant local even when it was aliased/passed elsewhere. In-process; **spawns 0 external processes**.

  - [x] 7.11 Write test for no auto-drop on unrestricted/affine at scope exit
    - **Property 8: No auto-drop for unrestricted and affine at scope exit**
    - **Validates: Requirements 2.6, 2.7, 4.8, 5.3**
    - Tag: `Feature: drop-semantics, Property 8: No auto-drop for unrestricted and affine`
    - Assert no scope-exit `Drop()` is emitted for unrestricted/affine locals outside a `with` form. In-process; **spawns 0 external processes**.

  - [x] 7.12 Write test for drop return-value capture
    - **Property 20: Drop return value capture**
    - **Validates: Requirements 3.4, 7.4**
    - Tag: `Feature: drop-semantics, Property 20: Drop return value capture`
    - Assert `T r = v.Drop();` on an owned non-void Drop compiles and captures the value. In-process; **spawns 0 external processes**.

  - [x] 7.13 Write test for C# emission using await on Task/ValueTask drop
    - **Property 16: C# emission uses await for Task/ValueTask drop**
    - **Validates: Requirements 7.10, 8.8, 14.5, 14.6**
    - Tag: `Feature: drop-semantics, Property 16: C# emission uses await for Task/ValueTask drop`
    - Assert auto-drop/explicit drop on `Task<T>`/`ValueTask<T>` emits `await`, not a `.Drop()` call. In-process; **spawns 0 external processes**.

  - [x] 7.14 Write test for await-desugars-to-drop equivalence
    - **Property 15: Await desugars to Drop**
    - **Validates: Requirements 7.5, 8.5**
    - Tag: `Feature: drop-semantics, Property 15: Await desugars to Drop`
    - Assert `await e` and `e.Drop()` resolve to the same method and result type `T` for Task/ValueTask at the semantic-model level. In-process; **spawns 0 external processes**.

  - [x] 7.15 Write test for lending retaining drop responsibility
    - **Property 11: Lending does not transfer drop responsibility**
    - **Validates: Requirements 9.1, 9.2, 9.3**
    - Tag: `Feature: drop-semantics, Property 11: Lending does not transfer drop responsibility`
    - Assert auto-drop still fires on the owner after lending as `readonly ref`/`frozen`/`borrowed`. In-process; **spawns 0 external processes**.

- [x] 8. With-block drop composition (Option A)
  - [x] 8.1 Extend existing with-block emission to compose Drop into the finally
    - Extend `CSharpEmitter.EmitWithBlockStatement` (~line 7186) and `EmitContextDeclarationWithWrapping` so the EXISTING `finally` emits `MistContext.Pop<T>()` first, then the `Drop()` call (or `await` for Task/ValueTask) — pop before drop, in the same single `finally`. Do NOT add new scope-exit machinery. For linear values bound by `with`, the with-exit satisfies the obligation (no double drop at enclosing scope). Affine `Drop()` fires only via the `with` family. Discard non-void `Drop()` return in `with` context.
    - _Requirements: 5.1, 5.2, 5.3, 5.4, 5.5, 5.6, 5.7, 13.3_

  - [x] 8.2 Write property test for with-block drop composition
    - **Property 9: With-block composes drop with context pop at block exit**
    - **Validates: Requirements 5.1, 5.2, 5.3, 5.4, 5.5, 5.6, 5.7**
    - Tag: `Feature: drop-semantics, Property 9: With-block composes drop with context pop`
    - Generate each `with`-form binding over a droppable type; assert emitted `finally` contains `Pop<T>()` before the `Drop()`/`await`. In-process; **spawns 0 external processes**.

- [x] 9. ValueTask<T> as intrinsic linear type
  - [x] 9.1 Declare disciplines and Drop() in the standard library
    - In `Source/Standard/Bootstrap/Task.mist`, add `relevant` to the intrinsic `Task<TResult>` and `intrinsic public TResult Drop();`; add a new `intrinsic public linear class ValueTask<TResult>` with `intrinsic public TResult Drop();`; add `type ValueTask = ValueTask<void>;`.
    - _Requirements: 7.1, 7.2, 7.7, 8.1, 8.2, 8.7_

  - [x] 9.2 Project inbound CLR ValueTask types to the Mist ValueTask type
    - Expand `Source/Mist/Backends/CSharpTranspiler/CSharpAmbientTypeResolver.cs` and the assembly type provider to recognize `System.Threading.Tasks.ValueTask` and `System.Threading.Tasks.ValueTask<T>` (param/return/field types) and project them to the Mist `ValueTask`/`ValueTask<TResult>`, mirroring the existing `Task`/`Task<T>` intake.
    - _Requirements: 8.9_

  - [x] 9.3 Emit ValueTask via CSharpTypeMap and reach EmitAsyncReturnType for explicit returns
    - Make `CSharpTypeMap` emit `System.Threading.Tasks.ValueTask<T>` for the generic Mist `ValueTask<TResult>` and `System.Threading.Tasks.ValueTask` for the void alias. Make `EmitAsyncReturnType` emit ValueTask when the method's return type is the explicit user-written `ValueTask<TResult>` (this path is currently unreachable).
    - _Requirements: 8.10, 7.10, 14.5_

  - [x] 9.4 Reconcile explicit ValueTask with the UseValueTask optimization
    - Constrain `ValueTaskUsageAnalysisPass`/`Symbol.UseValueTask` to apply only to async methods whose return type was inferred; an explicitly written `ValueTask<TResult>` return type takes precedence and is driven by the intrinsic type (task 9.3), never by `UseValueTask`. The two notions must not compete for the same method.
    - _Requirements: 8.11, 8.12_

  - [x] 9.5 Update/supersede ValueTaskEmissionPropertyTests
    - Update `Test/Mist.Tests/Backend/ValueTaskEmissionPropertyTests.cs`: remove the blanket "emitted C# never contains `ValueTask<`" assertion; keep asserting inferred-return methods follow the conservative pass behavior; add assertions that an explicitly written `ValueTask<TResult>` return emits `System.Threading.Tasks.ValueTask<T>` (and `ValueTask` for the void alias) and that inbound CLR ValueTask projects to the Mist type.
    - **Validates: Requirements 8.9, 8.10, 8.11, 8.12**
    - In-process; **spawns 0 external processes**.

- [x] 10. Migrate LiveTaskInfo live-task tracking to the Drop-based model
  - [x] 10.1 Replace the thread-static live-task machinery in SemanticModelBuilder
    - In `Source/Mist/Semantics/Model/SemanticModelBuilder.cs`, replace the per-method thread-static `_liveTaskScopes`/`_liveTaskCounter`/`_liveTaskResult` `LiveTaskInfo` tracking with the Drop-based obligation recording (relevant `Task<T>`/linear `ValueTask<T>` locals are scope-exit drop obligations). Preserve recorded obligations so task awaiting at scope exit still occurs (Req 7.9.2). Note: `project/TODO.md` records that scope-exit await is currently not emitted in some paths — the Drop-based model must close this gap, not inherit it.
    - _Requirements: 7.9, 7.9.2_

  - [x] 10.2 Supersede the ISemanticModel.GetLiveTasks contract
    - Update `Source/Mist/Semantics/Contracts/ISemanticModel.cs` and `SemanticModel.cs` so `GetLiveTasks(...)` is correctly superseded by the Drop-based obligation query (either reimplemented over drop obligations or replaced with the new query and all callers updated). Retire `LiveTaskInfo` where no longer needed. Ensure no semantic-model consumer regresses (Req 7.9.1).
    - _Requirements: 7.9.1_

  - [x] 10.3 Migrate EmitScopeExitAwait to drop-based await emission
    - In `CSharpEmitter.cs`, route `EmitScopeExitAwait` and `_currentBlockDiscardedTasks` handling through the unified drop emission from Epic 7 so auto-dropped `Task<T>`/`ValueTask<T>` are awaited at scope exit. Remove the special-case `LiveTaskInfo` emission path once tasks flow through the generic drop mechanism.
    - _Requirements: 7.9.3, 7.3, 7.10, 14.5_

  - [x] 10.4 Migrate existing structured-concurrency tests for no regression
    - Update the structured-concurrency tests that query `GetLiveTasks` / assert scope-exit await (`Test/Mist.Tests/Semantics/ScopeExitAwaitPropertyTests.cs` and related) to the Drop-based model so behavior is preserved or correctly superseded.
    - **Validates: Requirements 7.9.4**
    - In-process; **spawns 0 external processes**.

- [x] 11. Checkpoint - tasks unified under Drop
  - Ensure all tests pass, ask the user if questions arise.

- [x] 12. TextMate grammar update
  - [x] 12.1 Add substructural modifiers and Drop to the grammar
    - In `Editor/mist-syntax-highlighting/syntaxes/mist.tmLanguage.json`, add `affine`/`linear`/`relevant` to `storage.modifier.mist` (same position as `owned`), recognize `Drop` as `entity.name.function.destructor.mist` in method-declaration position, and preserve existing `owned class`/`owned struct` highlighting.
    - _Requirements: 16.1, 16.2, 16.3, 16.4_

  - [x] 12.2 Write scope-assignment test for the grammar update
    - Verify the new keywords map to `storage.modifier.mist` and `Drop` maps to the destructor scope, using the existing TextMate scope-assertion test approach.
    - _Requirements: 16.1, 16.2, 16.3, 16.4_
    - In-process; **spawns 0 external processes**.

- [x] 13. Sample application
  - [x] 13.1 Create the DropSemantics sample with the proof code verbatim
    - Create `samples/DropSemantics/Program.mist` containing the Requirement 17 Proof Code VERBATIM (immutable — if it does not compile, fix the compiler, not the proof code) and a `mist.toml`. Use Allman braces. The proof code already includes `Program.Main`, `RunDemo()`, and `ReadFd`.
    - _Requirements: 17.1, 17.2, 17.3, 17.5_

  - [x] 13.2 Wire the sample into the AllFeatures orchestrator
    - Add `DropSemantics.Program.RunDemo();` to `samples/AllFeatures/Program.mist` `RunDemo()`.
    - _Requirements: 17.4_

  - [x] 13.3 Write integration test for the sample's exact output
    - Compile `samples/DropSemantics/Program.mist` once in-process via the shared `CompilerDriver`, then run the compiled output DLL and assert stdout matches the Requirement 17 Expected output exactly (line by line).
    - **Spawns at most 1 external process per test run** (one `dotnet` invocation of the output DLL; batched compile in-process).
    - _Requirements: 17.1, 17.2, 17.3, 17.5_

  - [x] 13.4 Write integration test for Task await via auto-drop
    - Build one Mist program exercising `Task<T>` auto-drop/await producing labeled results, compile once in-process, run the output DLL once, compare output lines.
    - **Validates: Requirements 7.2, 7.3, 7.5, 7.6**
    - **Spawns at most 1 external process per test run.**

  - [x] 13.5 Write integration test for exception propagation through Drop()
    - Build one Mist program where a faulted `Task<T>` is dropped/awaited, compile once in-process, run the output DLL once, assert the exception is propagated.
    - **Validates: Requirements 7.8**
    - **Spawns at most 1 external process per test run.**

- [x] 14. Final checkpoint - full suite green
  - Ensure all tests pass, ask the user if questions arise.

## Notes

- Tasks marked with `*` are optional test sub-tasks and can be skipped for a faster MVP; core implementation tasks are never optional.
- Each task references specific requirement sub-clauses for traceability; each property test references its design Property number via the tag-comment format from the design's Testing Strategy.
- The `LiveTaskInfo` → Drop migration (Epic 10) is intentionally sequenced after the foundational modifier/parsing/type-model/emitter work so the most invasive change (crossing the `ISemanticModel.GetLiveTasks` contract, the thread-static machinery in `SemanticModelBuilder`, and `EmitScopeExitAwait`) does not entangle simpler work. `project/TODO.md` notes the current scope-exit await wiring is partially incomplete; the migration closes that gap rather than preserving it.
- Process-spawning policy: all property tests run fully in-process (0 spawns); the three runtime integration tests (13.3, 13.4, 13.5) each spawn `dotnet` at most once to run the compiled DLL, matching the approved batched pattern. No per-iteration spawning exists anywhere in this plan.
- Zero-warnings policy applies to every build step (1.3 and beyond).

## Task Dependency Graph

```json
{
  "waves": [
    { "id": 0, "tasks": ["1.1", "1.2", "2.1", "12.1"] },
    { "id": 1, "tasks": ["1.3", "2.2", "12.2"] },
    { "id": 2, "tasks": ["3.1", "1.4"] },
    { "id": 3, "tasks": ["4.1", "5.1", "9.1", "3.2"] },
    { "id": 4, "tasks": ["4.2", "5.2", "9.2", "5.3", "5.4"] },
    { "id": 5, "tasks": ["7.1", "4.3", "5.5", "5.6", "5.7"] },
    { "id": 6, "tasks": ["7.2", "7.6", "7.7", "7.8", "7.10", "7.11"] },
    { "id": 7, "tasks": ["7.3", "7.5", "7.9", "7.15"] },
    { "id": 8, "tasks": ["7.4", "7.12", "7.13", "7.14"] },
    { "id": 9, "tasks": ["8.1"] },
    { "id": 10, "tasks": ["9.3", "8.2"] },
    { "id": 11, "tasks": ["9.4", "10.1"] },
    { "id": 12, "tasks": ["10.2", "9.5"] },
    { "id": 13, "tasks": ["10.3"] },
    { "id": 14, "tasks": ["10.4", "13.1", "13.4", "13.5"] },
    { "id": 15, "tasks": ["13.2", "13.3"] }
  ]
}
```
