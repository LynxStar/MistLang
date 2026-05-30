# Implementation Plan

## Overview

This plan fixes exponential backtracking in the Mist parser's `TypeArgumentList` parsing by restructuring the `TypeReference` grammar rule. The fix follows the exploratory bugfix workflow: first confirm the bug exists via a failing performance test, establish the preservation baseline, change the grammar, then — critically — rebuild and CONFIRM the exact generated AST shape before touching any consumer. The downstream work updates 7 source files and migrates 181 test files via a scripted, directory-batched substitution with a green-build checkpoint after each batch. Finally, verify both the fix and preservation.

**Scope note:** The migration touches **7 source files** (excluding generated `obj/` files) and **181 test files**. The number **2979** is the TOTAL test count in the suite — it is the preservation oracle, NOT a count of files to edit.

## Tasks

- [ ] 1. Write bug condition exploration test
  - **Property 1: Bug Condition** - Exponential Backtracking in TypeArgumentList Parsing
  - **CRITICAL**: This test MUST FAIL on unfixed code - failure confirms the bug exists
  - **DO NOT attempt to fix the test or the code when it fails**
  - **NOTE**: This test encodes the expected behavior - it will validate the fix when it passes after implementation
  - **GOAL**: Surface counterexamples that demonstrate exponential parsing time growth
  - **Scoped PBT Approach**: Use deterministic arity levels N=5..16 to measure parse time growth ratios
  - **Process spawning note**: This test parses IN-PROCESS only (`MistLexer` + `MistParser`) — no external process spawning (per test-process-spawning policy)
  - Un-skip the existing `FuncActionArityOverloads_ShouldNotGrowExponentially` test in `Test/Mist.Tests/Parsing/TypeArgumentParsingPerformanceTest.cs` (remove the `Skip = "..."` argument)
  - The test generates `Func<T1, ..., Tn, TResult>` and `Action<T1, ..., Tn>` declarations for increasing N and parses them in-process
  - Assert that parsing N=0 through N=16 completes in under 10 seconds total (from Bug Condition in design)
  - Assert that growth ratio between consecutive N values is < 2.0 (linear, not exponential)
  - Run test on UNFIXED code
  - **EXPECTED OUTCOME**: Test FAILS (timeout or growth ratio exceeds 2.0 — this confirms the bug exists)
  - Document counterexamples found (e.g., "At N=11, parsing takes ~33s; growth ratio ~2.3x–3.5x per additional type argument")
  - Mark task complete when test is written, run, and failure is documented
  - _Bug_Condition: isBugCondition(input) where input contains a non-union TypeReference with nested TypeArgumentList and N >= 5 type arguments_
  - _Requirements: 1.1, 1.2, 1.3_

- [ ] 2. Write preservation property tests (BEFORE implementing fix)
  - **Property 2: Preservation** - Type Reference Parsing Semantic Equivalence
  - **IMPORTANT**: Follow observation-first methodology
  - **Process spawning note**: This baseline uses in-process compilation via `CompilerDriver` — no per-iteration external process spawning (per test-process-spawning policy)
  - Observe: Parse `int | string` on unfixed code → produces a union type AST with 2 constituents
  - Observe: Parse `List<int>` on unfixed code → produces a named type with 1 type argument
  - Observe: Parse `int[]` on unfixed code → produces a primitive type with array rank 1
  - Observe: Parse `(int, string)` on unfixed code → produces a tuple type with 2 elements
  - Observe: Parse `void` on unfixed code → produces a void type ref node
  - Observe: Parse `unknown` on unfixed code → produces an unknown type ref node
  - Observe: Parse `(int | string)[]` on unfixed code → produces a parenthesized type with inner union and array rank
  - Write property-based test: for all valid type reference inputs, the parser produces a correct AST node matching the expected structure (from Preservation Requirements in design)
  - Establish the preservation oracle: run the FULL existing test suite (2979 tests across 181 test files) on UNFIXED code via `dotnet test`
  - **EXPECTED OUTCOME**: All tests PASS (confirms the baseline behavior to preserve)
  - Mark task complete when the baseline full-suite pass is confirmed on unfixed code
  - _Preservation: All existing type reference parsing (unions, primitives, named, tuples, void, unknown, parenthesized) and semantic resolution is unchanged_
  - _Requirements: 3.1, 3.2, 3.3, 3.4, 3.5, 3.6, 3.7, 3.8, 3.9, 3.10_

- [ ] 3. Fix for exponential backtracking in TypeArgumentList parsing

  - [ ] 3.1 Restructure the TypeReference grammar rule in Mist.storm
    - Replace the 6-alternative discriminated `TypeReference` rule with a single production
    - New rule: `rule TypeReference = first:SingleTypeRef (PIPE rest:SingleTypeRef)*;`
    - Remove the `UnionTypeRef` rule entirely (union detection is now implicit when the PIPE-separated continuations are non-empty)
    - `ParenthesizedTypeRef` stays an alternative of `SingleTypeRef`; its `inner:TypeReference` field still works because `TypeReference` still exists as a rule
    - _Bug_Condition: isBugCondition(input) where input contains a TypeReference with nested TypeArgumentList and no PIPE token_
    - _Expected_Behavior: Parser parses one SingleTypeRef then optionally consumes PIPE-separated continuations — no speculative backtracking_
    - _Preservation: All type reference variants continue to produce correct AST nodes_
    - _Requirements: 2.1, 2.2, 2.3, 3.1, 3.2, 3.3, 3.4, 3.5, 3.6, 3.7_

  - [ ] 3.2 Rebuild and CONFIRM the exact generated AST shape (do this BEFORE touching any consumer)
    - **CRITICAL**: Prior attempts failed by ASSUMING the generated shape. Do not guess — confirm from the regenerated code.
    - Rebuild the project so the Storm source generator regenerates the AST: run `dotnet build` on `Source/Mist`
    - Read the regenerated `Mist.Ast.g.cs` (under `Source/Mist/obj/.../Mist.Ast.g.cs`) and record the EXACT generated node names and field shape, distinguishing the two possibilities:
      - a flat `TypeReferenceNode` exposing `First` (a `SingleTypeRefNode`) and `SingleTypeRefs` (an `ImmutableArray<SingleTypeRefNode>`) directly, OR
      - a wrapper that keeps `UnionTypeRefTypeReferenceNode` around a `UnionTypeRefNode`
    - Confirm the exact field names for: the first constituent (`First`), the PIPE-separated continuations list (`SingleTypeRefs`), and note that `Rest` is a NON-nullable `default!` codegen artifact
    - **Union predicate to use everywhere downstream**: `typeRef.SingleTypeRefs.Length > 0` (equivalently `!typeRef.SingleTypeRefs.IsEmpty`). DO NOT use `typeRef.Rest is not null` — `Rest` is a non-nullable codegen artifact and is NOT a valid union signal.
    - Confirm the `SingleTypeRefNode` variant names that consumers will match on (e.g. `PrimitiveSingleTypeRefSingleTypeRefNode`, `VoidSingleTypeRefSingleTypeRefNode`, `NamedSingleTypeRefSingleTypeRefNode`, `TupleSingleTypeRefSingleTypeRefNode`, `UnknownSingleTypeRefSingleTypeRefNode`)
    - Document the confirmed shape and names here before proceeding — ALL downstream consumer-update tasks (3.3–3.8) and test-migration tasks (3.9–3.13) depend on this confirmation
    - **EXPECTED OUTCOME**: Build regenerates the AST; the exact node names and field shape are confirmed in writing
    - _Bug_Condition: Grammar restructure changes the generated AST node types_
    - _Expected_Behavior: Confirmed generated shape exposes First + SingleTypeRefs; union detected via SingleTypeRefs.Length > 0_
    - _Preservation: Information content of the AST is preserved under the new node names_
    - _Requirements: 2.2, 3.1, 3.2, 3.3, 3.4, 3.5, 3.6, 3.7_

  - [ ] 3.3 Update SemanticModelBuilder.cs (68 references — largest consumer)
    - File: `Source/Mist/Semantics/Model/SemanticModelBuilder.cs`
    - Replace the `switch (typeRef)` in `ResolveTypeReference` with a union-first check using the CONFIRMED shape from 3.2: `if (typeRef.SingleTypeRefs.Length > 0) return ResolveUnionTypeRef(typeRef, ...); return ResolveSingleTypeRef(typeRef.First, ...);`
    - Update `ResolveUnionTypeRef` to take the confirmed type (flat `TypeReferenceNode` or the retained wrapper, per 3.2); collect constituents from `typeRef.First` then iterate `typeRef.SingleTypeRefs` (NEVER read `.Rest`)
    - Migrate all remaining `is`/`switch`/tuple-pattern checks against `TypeReferenceNode` variants to operate on `typeRef.First` (the `SingleTypeRefNode` variants)
    - Build MUST be green with ZERO warnings after this change (zero-warnings policy)
    - _Bug_Condition: Grammar restructure changes AST node types_
    - _Expected_Behavior: Same IType resolution for all type references; union detected via SingleTypeRefs.Length > 0_
    - _Preservation: Semantic model resolves identical types before and after the fix_
    - _Requirements: 2.2, 3.1, 3.2, 3.3, 3.4, 3.5, 3.6, 3.7_

  - [ ] 3.4 Update CSharpEmitter.cs (50 references)
    - File: `Source/Mist/Backends/CSharpTranspiler/CSharpEmitter.cs`
    - `EmitTypeReference`: replace `switch (typeRef)` with the union check (`typeRef.SingleTypeRefs.Length > 0`), then delegate to `EmitSingleTypeRef(typeRef.First, ...)`
    - `EmitTypeReferenceWithSubstitution`: change `typeRef is NamedTypeRefTypeReferenceNode` to `typeRef.First is NamedSingleTypeRefSingleTypeRefNode`
    - `GetExtensionReceiverTypeName` and `GetTypeArgName`: match `typeRef.First` variants instead of `TypeReferenceNode` variants
    - Build MUST be green with ZERO warnings after this change (zero-warnings policy)
    - _Bug_Condition: Grammar restructure changes AST node types_
    - _Expected_Behavior: Identical C# output for all type references_
    - _Preservation: C# emitter produces byte-for-byte identical output_
    - _Requirements: 2.2, 3.1, 3.2, 3.3, 3.7_

  - [ ] 3.5 Update TypeCheckerAnalyzer.cs (17 references — third-largest consumer)
    - File: `Source/Mist/Analysis/Builtin/TypeCheckerAnalyzer.cs`
    - This file uses `switch` statements, `switch` expressions, AND `is`-pattern checks on `TypeReference` variants
    - Migrate `is`/`switch` checks against `TypeReferenceNode` variants to operate on `typeRef.First`, e.g. `methodDecl.ReturnType.First is not VoidSingleTypeRefSingleTypeRefNode` and `typeRef.First is PrimitiveSingleTypeRefSingleTypeRefNode`
    - Where a check distinguished a union, use `typeRef.SingleTypeRefs.Length > 0` (NOT `Rest`)
    - Build MUST be green with ZERO warnings after this change (zero-warnings policy)
    - _Bug_Condition: Grammar restructure changes AST node types_
    - _Expected_Behavior: Type checking decisions are unchanged; union detected via SingleTypeRefs.Length > 0_
    - _Preservation: All type-checker diagnostics remain identical_
    - _Requirements: 2.2, 3.1, 3.2, 3.3, 3.4, 3.5, 3.6, 3.7_

  - [ ] 3.6 Update MistPrettyPrinter.cs (6 references)
    - File: `Source/Mist/Parsing/MistPrettyPrinter.cs`
    - `EmitTypeReference`: replace `switch (typeRef)` with the union check (`typeRef.SingleTypeRefs.Length > 0`), then delegate to `EmitSingleTypeRef(typeRef.First, ...)`
    - Build MUST be green with ZERO warnings after this change (zero-warnings policy)
    - _Bug_Condition: Grammar restructure changes AST node types_
    - _Expected_Behavior: Identical pretty-printed Mist source output_
    - _Preservation: Pretty printer produces identical output_
    - _Requirements: 2.2, 3.7_

  - [ ] 3.7 Update EntryPointAnalyzer.cs (3 references)
    - File: `Source/Mist/Analysis/Builtin/EntryPointAnalyzer.cs`
    - `IsStringArrayTypeRef`: change `typeRef is PrimitiveTypeRefTypeReferenceNode primitiveRef` to `typeRef.First is PrimitiveSingleTypeRefSingleTypeRefNode primitiveRef`
    - Build MUST be green with ZERO warnings after this change (zero-warnings policy)
    - _Bug_Condition: Grammar restructure changes AST node types_
    - _Expected_Behavior: Entry point / `string[] args` detection continues to work correctly_
    - _Preservation: Entry point detection unchanged_
    - _Requirements: 2.2, 3.3_

  - [ ] 3.8 Update ExhaustivenessChecker.cs (2 references) and CSharpTranspilerBackend.cs (2 references)
    - Files: `Source/Mist/Analysis/Builtin/ExhaustivenessChecker.cs` and `Source/Mist/Backends/CSharpTranspiler/CSharpTranspilerBackend.cs`
    - Both use `switch` expressions and `is` checks on Primitive/Void/Named variants — migrate these to match against `typeRef.First` (the corresponding `SingleTypeRefNode` variants)
    - Where a check distinguished a union, use `typeRef.SingleTypeRefs.Length > 0` (NOT `Rest`)
    - Build MUST be green with ZERO warnings after this change (zero-warnings policy)
    - _Bug_Condition: Grammar restructure changes AST node types_
    - _Expected_Behavior: Exhaustiveness analysis and transpiler entry-point handling unchanged_
    - _Preservation: Same diagnostics and same emitted entry-point shape_
    - _Requirements: 2.2, 3.3, 3.4, 3.5, 3.7_

  - [ ] 3.9 Migrate test batch — Parsing directory (scripted substitution + green-build checkpoint)
    - Scope: `Test/Mist.Tests/Parsing/**` (e.g. `UnionTypeRefParsingTests`, `UnknownTypeParserTests`, `UnknownTypeRoundTripPropertyTests`, `OptionalTypeSuffixPropertyTests`, `WithStatementParsingTests`)
    - Perform a SCRIPTED regex/find-replace substitution of node names (do NOT hand-edit file by file):
      - `new PrimitiveTypeRefTypeReferenceNode { ... }` → `new TypeReferenceNode { First = new PrimitiveSingleTypeRefSingleTypeRefNode { ... } }`
      - `new VoidTypeRefTypeReferenceNode()` → `new TypeReferenceNode { First = new VoidSingleTypeRefSingleTypeRefNode() }`
      - `new UnknownTypeRefTypeReferenceNode()` → `new TypeReferenceNode { First = new UnknownSingleTypeRefSingleTypeRefNode() }`
      - `new NamedTypeRefTypeReferenceNode { ... }` → `new TypeReferenceNode { First = new NamedSingleTypeRefSingleTypeRefNode { ... } }`
      - `new TupleTypeRefTypeReferenceNode { ... }` → `new TypeReferenceNode { First = new TupleSingleTypeRefSingleTypeRefNode { ... } }`
      - `Assert.IsType<UnionTypeRefTypeReferenceNode>(typeRef)` → `Assert.NotEmpty(typeRef.SingleTypeRefs)`
      - `Assert.IsType<PrimitiveTypeRefTypeReferenceNode>(typeRef)` → `Assert.IsType<PrimitiveSingleTypeRefSingleTypeRefNode>(typeRef.First)` (and likewise for Void/Named/Tuple/Unknown variants on `.First`)
    - Use the migrated union assertion `Assert.NotEmpty(typeRef.SingleTypeRefs)` — NEVER `Assert.NotNull(typeRef.Rest)`
    - **CHECKPOINT**: run `dotnet build` — it MUST be green before advancing, then run the Parsing test subset (`dotnet test --filter`) — it MUST pass before advancing.
    - _Bug_Condition: Grammar restructure changes AST node type names used in tests_
    - _Expected_Behavior: Parsing tests assert against First / SingleTypeRefs; unions via Assert.NotEmpty(SingleTypeRefs)_
    - _Preservation: Parsing tests pass with identical semantics under new node names_
    - _Requirements: 3.1, 3.2, 3.3, 3.6, 3.7, 3.9_

  - [ ] 3.10 Migrate test batch — Semantics directory (scripted substitution + green-build checkpoint)
    - Scope: `Test/Mist.Tests/Semantics/**` (e.g. `NameResolutionPropertyTests`, `ParameterScopingPropertyTests`, `InterfaceRoundTripPropertyTests`, `ExtensionRegistrationPropertyTests`, `GenericInheritanceResolutionPropertyTests`)
    - Apply the SAME scripted substitution set as 3.9 (construction + assertion rewrites; unions via `Assert.NotEmpty(typeRef.SingleTypeRefs)`, never `Rest`)
    - **CHECKPOINT**: run `dotnet build` — it MUST be green before advancing, then run the Semantics test subset (`dotnet test --filter`) — it MUST pass before advancing.
    - _Bug_Condition: Grammar restructure changes AST node type names used in tests_
    - _Expected_Behavior: Semantics tests construct/assert against First / SingleTypeRefs_
    - _Preservation: Semantics tests pass with identical semantics under new node names_
    - _Requirements: 3.1, 3.2, 3.3, 3.4, 3.5, 3.6, 3.7, 3.9_

  - [ ] 3.11 Migrate test batch — Backend directory (scripted substitution + green-build checkpoint)
    - Scope: `Test/Mist.Tests/Backend/**` (e.g. `VoidEmitterTests`, `TraitEmissionPropertyTests`, `TupleEmitterOutputTests`, `ValueTaskEmissionPropertyTests`, `PatternEmissionPropertyTests`)
    - Apply the SAME scripted substitution set as 3.9 (unions via `Assert.NotEmpty(typeRef.SingleTypeRefs)`, never `Rest`)
    - **CHECKPOINT**: run `dotnet build` — it MUST be green before advancing, then run the Backend test subset (`dotnet test --filter`) — it MUST pass before advancing.
    - _Bug_Condition: Grammar restructure changes AST node type names used in tests_
    - _Expected_Behavior: Backend tests construct/assert against First / SingleTypeRefs_
    - _Preservation: Backend emission tests pass with identical semantics under new node names_
    - _Requirements: 3.1, 3.2, 3.3, 3.7, 3.9_

  - [ ] 3.12 Migrate test batch — Analysis directory (scripted substitution + green-build checkpoint)
    - Scope: `Test/Mist.Tests/Analysis/**` (e.g. `TypeCheckerEdgeCaseTests`, `TypeCheckerPropertyTests`, `VoidReturnPropertyTests`, `VoidTypeCheckerTests`, `TypeUnionExhaustivenessPropertyTests`)
    - Apply the SAME scripted substitution set as 3.9 (unions via `Assert.NotEmpty(typeRef.SingleTypeRefs)`, never `Rest`)
    - **CHECKPOINT**: run `dotnet build` — it MUST be green before advancing, then run the Analysis test subset (`dotnet test --filter`) — it MUST pass before advancing.
    - _Bug_Condition: Grammar restructure changes AST node type names used in tests_
    - _Expected_Behavior: Analysis tests construct/assert against First / SingleTypeRefs_
    - _Preservation: Analysis tests pass with identical semantics under new node names_
    - _Requirements: 3.1, 3.2, 3.3, 3.4, 3.5, 3.6, 3.7, 3.9_

  - [ ] 3.13 Migrate test batch — remaining test directories (scripted substitution + green-build checkpoint)
    - Scope: all remaining test files referencing the node names, including `Test/Mist.Tests/Integration/**`, `Test/Mist.Tests/Generators/**`, and any root-level `Test/Mist.Tests/*.cs` (e.g. `ParserAstPropertyTests`, `PipelineBackendPropertyTests`, `SemanticModelDiagnosticsPropertyTests`)
    - Apply the SAME scripted substitution set as 3.9 across these directories (unions via `Assert.NotEmpty(typeRef.SingleTypeRefs)`, never `Rest`)
    - After this batch, no references to the old `*TypeRefTypeReferenceNode` / `UnionTypeRefTypeReferenceNode` variant names should remain in the test tree — verify with a search
    - **CHECKPOINT**: run `dotnet build` — it MUST be green before advancing, then run the test subset for the remaining directories (`dotnet test --filter`) — it MUST pass before advancing.
    - _Bug_Condition: Grammar restructure changes AST node type names used in tests_
    - _Expected_Behavior: Remaining tests construct/assert against First / SingleTypeRefs_
    - _Preservation: All remaining tests pass with identical semantics under new node names_
    - _Requirements: 3.1, 3.2, 3.3, 3.4, 3.5, 3.6, 3.7, 3.8, 3.9, 3.10_

  - [ ] 3.14 Verify bug condition exploration test now passes
    - **Property 1: Expected Behavior** - Linear Parsing Time
    - **IMPORTANT**: Re-run the SAME test from task 1 - do NOT write a new test
    - The test from task 1 encodes the expected behavior (linear-time parsing)
    - Run `FuncActionArityOverloads_ShouldNotGrowExponentially`
    - **EXPECTED OUTCOME**: Test PASSES — parsing N=0..16 completes in under 10 seconds total and the growth ratio is < 2.0
    - _Expected_Behavior: expectedBehavior(result) — linear parse time, identical semantic results_
    - _Requirements: 2.1, 2.2, 2.3_

  - [ ] 3.15 Verify preservation tests still pass
    - **Property 2: Preservation** - Full Test Suite Passes
    - **IMPORTANT**: Re-run the SAME tests from task 2 - do NOT write new tests
    - Run the full test suite (`dotnet test`)
    - **EXPECTED OUTCOME**: All 2979 tests PASS (confirms no regressions across the 181 migrated test files)
    - _Preservation: Preservation Requirements from design — unions, primitives, named, tuples, void, unknown, parenthesized all unchanged_
    - _Requirements: 3.1, 3.2, 3.3, 3.4, 3.5, 3.6, 3.7, 3.8, 3.9, 3.10_

- [ ] 4. Checkpoint - Ensure all tests pass
  - Run `dotnet test` on the full solution and confirm ZERO failures
  - Run `dotnet build` and confirm ZERO warnings (per zero-warnings policy)
  - Compile and run the AllFeatures sample to verify end-to-end correctness
  - Verify baseline output matches expected
  - Ensure all tests pass, ask the user if questions arise.
  - _Expected_Behavior: Linear parse time preserved; end-to-end output unchanged_
  - _Preservation: Full suite green, AllFeatures sample compiles and runs, baseline output matches_
  - _Requirements: 3.9, 3.10_

## Task Dependency Graph

```json
{
  "waves": [
    ["1", "2"],
    ["3.1"],
    ["3.2"],
    ["3.3", "3.4", "3.5", "3.6", "3.7", "3.8"],
    ["3.9"],
    ["3.10"],
    ["3.11"],
    ["3.12"],
    ["3.13"],
    ["3.14"],
    ["3.15"],
    ["4"]
  ]
}
```

## Notes

- Tasks 1 and 2 are independent and run in parallel first (confirm the bug, establish the preservation baseline).
- Task 3.1 (grammar change) is followed IMMEDIATELY by task 3.2 (rebuild + confirm the regenerated AST shape). Prior attempts failed by ASSUMING the generated shape — 3.2 removes that risk and ALL downstream tasks depend on it.
- The union predicate is `typeRef.SingleTypeRefs.Length > 0` (equivalently `!typeRef.SingleTypeRefs.IsEmpty`) everywhere. `Rest` is a non-nullable codegen artifact and MUST NOT be used. The migrated union assertion is `Assert.NotEmpty(typeRef.SingleTypeRefs)`, never `Assert.NotNull(typeRef.Rest)`.
- The 7 source-file updates (3.3–3.8; ExhaustivenessChecker + CSharpTranspilerBackend combined in 3.8) all depend on the confirmed AST from 3.2 but are independent of each other and can proceed in parallel.
- The 181-test-file migration is performed as a SCRIPTED, directory-batched substitution (3.9–3.13), sequenced so each batch depends on the prior batch's green build. This replaces the previous monolithic migration task, which had no intermediate checkpoints and made failures impossible to bisect.
- Scope language: the migration touches **7 source files** and **181 test files**. "2979" is the TOTAL test count (the preservation oracle), NOT a file count.
- Per the zero-warnings policy, each source-file and test-batch task must leave the build warning-free.
- Per the test-process-spawning policy, the performance test (task 1) uses in-process parsing only — no external process spawning.
