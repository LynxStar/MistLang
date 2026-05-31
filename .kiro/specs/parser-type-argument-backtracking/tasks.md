# Implementation Plan

## Overview

This plan fixes exponential backtracking in the Mist parser's `TypeArgumentList` parsing by restructuring grammar rules that try a union alternative first. There are **TWO instances of the identical defect** (confirmed during execution): the `TypeReference` rule (type references at usage sites) and the `RefinableTypeExpression` rule (the type-alias right-hand side). The fix follows the exploratory bugfix workflow: first confirm the bug exists via a failing performance test, establish the preservation baseline, change the grammar, then — critically — rebuild and CONFIRM the exact generated AST shape before touching any consumer. The downstream work for the first fix (`TypeReference`) updates 7 source files and migrates 181 test files via a scripted, directory-batched substitution with a green-build checkpoint after each batch. The second fix (`RefinableTypeExpression`) re-touches 2 of those same source files plus 1 of the already-counted test files. Finally, verify both the fix and preservation. The bug-condition performance test (`FuncActionArityOverloads_ShouldNotGrowExponentially`) only reaches linear time after BOTH grammar fixes are applied — fix (a) alone leaves the parse exponential at ~2.2x/N.

**Scope note:** The **first fix** (`TypeReference`) migration touches **7 source files** (excluding generated `obj/` files) and **181 test files**. The **second fix** (`RefinableTypeExpression`) does NOT add net-new files: it re-touches `SemanticModelBuilder.cs` and `MistPrettyPrinter.cs` — both ALREADY among the first fix's 7 source files — and `TypeAliasRegistrationPropertyTests.cs`, which lives in `Test/Mist.Tests/Semantics/` and is ALREADY among the 181 test files. So the second fix adds **2 source files re-touched (0 net-new source files)** and **1 test file re-touched (0 net-new test files)**. The number **2979** is the TOTAL test count in the suite — it is the preservation oracle, NOT a count of files to edit.

## Tasks

- [x] 1. Write bug condition exploration test
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

- [x] 2. Write preservation property tests (BEFORE implementing fix)
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

  - [x] 3.1 Restructure the TypeReference grammar rule in Mist.storm
    - Replace the 6-alternative discriminated `TypeReference` rule with a single production
    - New rule: `rule TypeReference = first:SingleTypeRef (PIPE rest:SingleTypeRef)*;`
    - Remove the `UnionTypeRef` rule entirely (union detection is now implicit when the PIPE-separated continuations are non-empty)
    - `ParenthesizedTypeRef` stays an alternative of `SingleTypeRef`; its `inner:TypeReference` field still works because `TypeReference` still exists as a rule
    - _Bug_Condition: isBugCondition(input) where input contains a TypeReference with nested TypeArgumentList and no PIPE token_
    - _Expected_Behavior: Parser parses one SingleTypeRef then optionally consumes PIPE-separated continuations — no speculative backtracking_
    - _Preservation: All type reference variants continue to produce correct AST nodes_
    - _Requirements: 2.1, 2.2, 2.3, 3.1, 3.2, 3.3, 3.4, 3.5, 3.6, 3.7_

  - [x] 3.2 Rebuild and CONFIRM the exact generated AST shape (do this BEFORE touching any consumer)
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
    - **COMMIT** (fixed point): once the project builds with the regenerated AST, stage the grammar change and any generated/source changes that compile, and commit locally — e.g. `git commit -m "parser-backtracking: restructure TypeReference grammar + confirm generated AST"`. Do not push yet. Note: if the tree does not yet compile because consumers are not updated, defer this commit until task 3.8 and instead record the confirmed AST shape in this task's notes.

    #### ✅ CONFIRMED AST SHAPE (Task 3.2 result — verified from regenerated `Mist.Ast.g.cs`)

    Source of truth: `Source/Mist/obj/Debug/net10.0/generated/Storm.SourceGenerator/Storm.SourceGenerator.Generator.StormIncrementalGenerator/Mist.Ast.g.cs` (regenerated by `dotnet build Source/Mist/Mist.csproj`). The build FAILED with 15 `CS0246` errors (downstream consumers still reference old node names) and **0 warnings** — this is the expected/correct state for 3.2. The Storm generator ran successfully despite the failed compile. (The `obj/Release/...` copy is STALE — it still shows the old discriminated `abstract class TypeReferenceNode` + `UnionTypeRefTypeReferenceNode`; ignore it. A Release build will regenerate it identically to Debug.)

    **Outcome = possibility (a): a FLAT `TypeReferenceNode`.** It is NOT a wrapper; there is no `UnionTypeRefTypeReferenceNode` and no `UnionTypeRefNode` anywhere in the regenerated file.

    Generated `TypeReferenceNode` (sealed, `: SyntaxNode`):
    ```csharp
    public sealed class TypeReferenceNode : SyntaxNode
    {
        public SingleTypeRefNode First { get; set; } = default!;                                  // first constituent (non-null)
        public SingleTypeRefNode Rest { get; set; } = default!;                                   // NON-nullable codegen artifact — DO NOT USE
        public ImmutableArray<SingleTypeRefNode> SingleTypeRefs { get; set; } = ImmutableArray<SingleTypeRefNode>.Empty; // PIPE-separated continuations
    }
    ```
    - `First` : `SingleTypeRefNode`, non-nullable (`= default!`). The first (and, for non-unions, the ONLY) constituent.
    - `Rest` : `SingleTypeRefNode`, **non-nullable** (`= default!`). This is the `default!` codegen artifact called out in the design. It is NOT nullable, so `Rest is not null` / `Rest != null` is ALWAYS true and is **NOT** a valid union signal. **Never read `.Rest`.**
    - `SingleTypeRefs` : `ImmutableArray<SingleTypeRefNode>`, initialized to `.Empty`. Holds the additional PIPE-separated constituents. Its non-emptiness is the union signal.

    **✅ CONFIRMED UNION PREDICATE (use everywhere downstream):** `typeRef.SingleTypeRefs.Length > 0` (equivalently `!typeRef.SingleTypeRefs.IsEmpty`). A single (non-union) type ref has `SingleTypeRefs.Length == 0` and is fully described by `First`. A union of K constituents has `First` + `SingleTypeRefs.Length == K-1`. **DO NOT use `typeRef.Rest is not null`** — `Rest` is non-nullable and always "present".
    - Migrated test union assertion: `Assert.NotEmpty(typeRef.SingleTypeRefs)` — NEVER `Assert.NotNull(typeRef.Rest)`.

    **✅ CONFIRMED: old variant classes are GONE.** A search of the regenerated file for `PrimitiveTypeRefTypeReferenceNode`, `VoidTypeRefTypeReferenceNode`, `UnknownTypeRefTypeReferenceNode`, `NamedTypeRefTypeReferenceNode`, `TupleTypeRefTypeReferenceNode`, `UnionTypeRefTypeReferenceNode`, and `UnionTypeRefNode` returns ZERO matches. Consumers must stop matching on these.

    **✅ CONFIRMED `SingleTypeRefNode` variants** (abstract base `public abstract class SingleTypeRefNode : SyntaxNode`). These are the EXACT names consumers/tests must match on, via `typeRef.First` (or `ParenthesizedTypeRef.Inner` / `TupleTypeElement.Type` etc.):

    | Variant class | Fields (type / nullability) |
    |---|---|
    | `PrimitiveSingleTypeRefSingleTypeRefNode` | `PrimitiveType : PrimitiveTypeNode` (`= default!`), `ArrayRank : ArrayRankSuffixNode?` (nullable), `Optional : Token?` (nullable) |
    | `VoidSingleTypeRefSingleTypeRefNode` | `VOID : Token` (`= default`) — no children |
    | `UnknownSingleTypeRefSingleTypeRefNode` | `UNKNOWN : Token` (`= default`) — no children |
    | `NamedSingleTypeRefSingleTypeRefNode` | `Name : QualifiedIdentifierNode` (`= default!`), `TypeArgs : TypeArgumentListNode?` (nullable), `ArrayRank : ArrayRankSuffixNode?` (nullable), `Optional : Token?` (nullable) |
    | `ParenthesizedTypeRefSingleTypeRefNode` | `Inner : TypeReferenceNode` (`= default!`), `ArrayRank : ArrayRankSuffixNode?` (nullable), `LEFT_PARENTHESIS : Token`, `RIGHT_PARENTHESIS : Token` |
    | `TupleSingleTypeRefSingleTypeRefNode` | `First : TupleTypeElementNode` (`= default!`), `Rest : TupleTypeElementNode` (`= default!`), `Additional : TupleTypeElementNode` (`= default!`), `TupleTypeElements : ImmutableArray<TupleTypeElementNode>` (`.Empty`), plus `LEFT_PARENTHESIS`/`COMMA`/`RIGHT_PARENTHESIS` tokens |

    Note: the names match the expected list from the task EXCEPT cosmetics — there is no separate "Tuple" naming surprise; the six confirmed names are exactly `PrimitiveSingleTypeRefSingleTypeRefNode`, `VoidSingleTypeRefSingleTypeRefNode`, `UnknownSingleTypeRefSingleTypeRefNode`, `NamedSingleTypeRefSingleTypeRefNode`, `ParenthesizedTypeRefSingleTypeRefNode`, `TupleSingleTypeRefSingleTypeRefNode`.

    Supporting node shapes (unchanged, for reference when constructing test nodes):
    - `QualifiedIdentifierNode` : `IDENTIFIER : Token`, `IDENTIFIERs : ImmutableArray<Token>`
    - `TypeArgumentListNode` : `LESS_THAN : Token`, `TypeArgument : TypeArgumentNode`, `TypeArguments : ImmutableArray<TypeArgumentNode>`, `GREATER_THAN : Token`
    - `TupleTypeElementNode` : `Type : TypeReferenceNode` (`= default!`), `Name : Token?` (nullable)
    - `ArrayRankSuffixNode` : abstract base with `SizedArrayRank` / `UnsizedArrayRank` variants (unchanged)

    **Downstream construction/migration mapping (confirmed):**
    - `new PrimitiveTypeRefTypeReferenceNode { ... }` → `new TypeReferenceNode { First = new PrimitiveSingleTypeRefSingleTypeRefNode { ... } }`
    - `new VoidTypeRefTypeReferenceNode()` → `new TypeReferenceNode { First = new VoidSingleTypeRefSingleTypeRefNode() }`
    - `new UnknownTypeRefTypeReferenceNode()` → `new TypeReferenceNode { First = new UnknownSingleTypeRefSingleTypeRefNode() }`
    - `new NamedTypeRefTypeReferenceNode { Name = ..., TypeArgs = ... }` → `new TypeReferenceNode { First = new NamedSingleTypeRefSingleTypeRefNode { Name = ..., TypeArgs = ... } }`
    - `new TupleTypeRefTypeReferenceNode { ... }` → `new TypeReferenceNode { First = new TupleSingleTypeRefSingleTypeRefNode { ... } }`
    - `typeRef is XxxTypeRefTypeReferenceNode` → `typeRef.First is XxxSingleTypeRefSingleTypeRefNode`
    - `Assert.IsType<UnionTypeRefTypeReferenceNode>(typeRef)` → `Assert.NotEmpty(typeRef.SingleTypeRefs)`

    **Build status:** Tree does NOT compile (15 `CS0246` errors across `CSharpEmitter.cs`, `TypeCheckerAnalyzer.cs`, `SemanticModelBuilder.cs` referencing the removed variant names) — expected, these are exactly the downstream consumers updated in 3.3–3.8. **COMMIT DEFERRED to task 3.8** per the note above (tree doesn't compile yet). 0 warnings.
    - _Bug_Condition: Grammar restructure changes the generated AST node types_
    - _Expected_Behavior: Confirmed generated shape exposes First + SingleTypeRefs; union detected via SingleTypeRefs.Length > 0_
    - _Preservation: Information content of the AST is preserved under the new node names_
    - _Requirements: 2.2, 3.1, 3.2, 3.3, 3.4, 3.5, 3.6, 3.7_

  - [x] 3.3 Update SemanticModelBuilder.cs (68 references — largest consumer)
    - File: `Source/Mist/Semantics/Model/SemanticModelBuilder.cs`
    - Replace the `switch (typeRef)` in `ResolveTypeReference` with a union-first check using the CONFIRMED shape from 3.2: `if (typeRef.SingleTypeRefs.Length > 0) return ResolveUnionTypeRef(typeRef, ...); return ResolveSingleTypeRef(typeRef.First, ...);`
    - Update `ResolveUnionTypeRef` to take the confirmed type (flat `TypeReferenceNode` or the retained wrapper, per 3.2); collect constituents from `typeRef.First` then iterate `typeRef.SingleTypeRefs` (NEVER read `.Rest`)
    - Migrate all remaining `is`/`switch`/tuple-pattern checks against `TypeReferenceNode` variants to operate on `typeRef.First` (the `SingleTypeRefNode` variants)
    - Build MUST be green with ZERO warnings after this change (zero-warnings policy)
    - _Bug_Condition: Grammar restructure changes AST node types_
    - _Expected_Behavior: Same IType resolution for all type references; union detected via SingleTypeRefs.Length > 0_
    - _Preservation: Semantic model resolves identical types before and after the fix_
    - _Requirements: 2.2, 3.1, 3.2, 3.3, 3.4, 3.5, 3.6, 3.7_

  - [x] 3.4 Update CSharpEmitter.cs (50 references)
    - File: `Source/Mist/Backends/CSharpTranspiler/CSharpEmitter.cs`
    - `EmitTypeReference`: replace `switch (typeRef)` with the union check (`typeRef.SingleTypeRefs.Length > 0`), then delegate to `EmitSingleTypeRef(typeRef.First, ...)`
    - `EmitTypeReferenceWithSubstitution`: change `typeRef is NamedTypeRefTypeReferenceNode` to `typeRef.First is NamedSingleTypeRefSingleTypeRefNode`
    - `GetExtensionReceiverTypeName` and `GetTypeArgName`: match `typeRef.First` variants instead of `TypeReferenceNode` variants
    - Build MUST be green with ZERO warnings after this change (zero-warnings policy)
    - _Bug_Condition: Grammar restructure changes AST node types_
    - _Expected_Behavior: Identical C# output for all type references_
    - _Preservation: C# emitter produces byte-for-byte identical output_
    - _Requirements: 2.2, 3.1, 3.2, 3.3, 3.7_

  - [x] 3.5 Update TypeCheckerAnalyzer.cs (17 references — third-largest consumer)
    - File: `Source/Mist/Analysis/Builtin/TypeCheckerAnalyzer.cs`
    - This file uses `switch` statements, `switch` expressions, AND `is`-pattern checks on `TypeReference` variants
    - Migrate `is`/`switch` checks against `TypeReferenceNode` variants to operate on `typeRef.First`, e.g. `methodDecl.ReturnType.First is not VoidSingleTypeRefSingleTypeRefNode` and `typeRef.First is PrimitiveSingleTypeRefSingleTypeRefNode`
    - Where a check distinguished a union, use `typeRef.SingleTypeRefs.Length > 0` (NOT `Rest`)
    - Build MUST be green with ZERO warnings after this change (zero-warnings policy)
    - _Bug_Condition: Grammar restructure changes AST node types_
    - _Expected_Behavior: Type checking decisions are unchanged; union detected via SingleTypeRefs.Length > 0_
    - _Preservation: All type-checker diagnostics remain identical_
    - _Requirements: 2.2, 3.1, 3.2, 3.3, 3.4, 3.5, 3.6, 3.7_

  - [x] 3.6 Update MistPrettyPrinter.cs (6 references)
    - File: `Source/Mist/Parsing/MistPrettyPrinter.cs`
    - `EmitTypeReference`: replace `switch (typeRef)` with the union check (`typeRef.SingleTypeRefs.Length > 0`), then delegate to `EmitSingleTypeRef(typeRef.First, ...)`
    - Build MUST be green with ZERO warnings after this change (zero-warnings policy)
    - _Bug_Condition: Grammar restructure changes AST node types_
    - _Expected_Behavior: Identical pretty-printed Mist source output_
    - _Preservation: Pretty printer produces identical output_
    - _Requirements: 2.2, 3.7_

  - [x] 3.7 Update EntryPointAnalyzer.cs (3 references)
    - File: `Source/Mist/Analysis/Builtin/EntryPointAnalyzer.cs`
    - `IsStringArrayTypeRef`: change `typeRef is PrimitiveTypeRefTypeReferenceNode primitiveRef` to `typeRef.First is PrimitiveSingleTypeRefSingleTypeRefNode primitiveRef`
    - Build MUST be green with ZERO warnings after this change (zero-warnings policy)
    - _Bug_Condition: Grammar restructure changes AST node types_
    - _Expected_Behavior: Entry point / `string[] args` detection continues to work correctly_
    - _Preservation: Entry point detection unchanged_
    - _Requirements: 2.2, 3.3_

  - [x] 3.8 Update ExhaustivenessChecker.cs (2 references) and CSharpTranspilerBackend.cs (2 references)
    - Files: `Source/Mist/Analysis/Builtin/ExhaustivenessChecker.cs` and `Source/Mist/Backends/CSharpTranspiler/CSharpTranspilerBackend.cs`
    - Both use `switch` expressions and `is` checks on Primitive/Void/Named variants — migrate these to match against `typeRef.First` (the corresponding `SingleTypeRefNode` variants)
    - Where a check distinguished a union, use `typeRef.SingleTypeRefs.Length > 0` (NOT `Rest`)
    - Build MUST be green with ZERO warnings after this change (zero-warnings policy)
    - **COMMIT** (fixed point): after all 7 source files (3.3–3.8) build green with zero warnings, stage the updated source files and commit locally — e.g. `git commit -m "parser-backtracking: update all source consumers to new TypeReference AST shape"`. Do not push yet.
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
    - **COMMIT** (fixed point): after the checkpoint passes (green build + Parsing test subset green), stage the migrated Parsing test files and commit locally — e.g. `git commit -m "parser-backtracking: migrate Parsing test batch to new AST node names"`. Do not push yet.
    - _Bug_Condition: Grammar restructure changes AST node type names used in tests_
    - _Expected_Behavior: Parsing tests assert against First / SingleTypeRefs; unions via Assert.NotEmpty(SingleTypeRefs)_
    - _Preservation: Parsing tests pass with identical semantics under new node names_
    - _Requirements: 3.1, 3.2, 3.3, 3.6, 3.7, 3.9_

  - [ ] 3.10 Migrate test batch — Semantics directory (scripted substitution + green-build checkpoint)
    - Scope: `Test/Mist.Tests/Semantics/**` (e.g. `NameResolutionPropertyTests`, `ParameterScopingPropertyTests`, `InterfaceRoundTripPropertyTests`, `ExtensionRegistrationPropertyTests`, `GenericInheritanceResolutionPropertyTests`)
    - Apply the SAME scripted substitution set as 3.9 (construction + assertion rewrites; unions via `Assert.NotEmpty(typeRef.SingleTypeRefs)`, never `Rest`)
    - **CHECKPOINT**: run `dotnet build` — it MUST be green before advancing, then run the Semantics test subset (`dotnet test --filter`) — it MUST pass before advancing.
    - **COMMIT** (fixed point): after the checkpoint passes, stage the migrated Semantics test files and commit locally — e.g. `git commit -m "parser-backtracking: migrate Semantics test batch to new AST node names"`. Do not push yet.
    - _Bug_Condition: Grammar restructure changes AST node type names used in tests_
    - _Expected_Behavior: Semantics tests construct/assert against First / SingleTypeRefs_
    - _Preservation: Semantics tests pass with identical semantics under new node names_
    - _Requirements: 3.1, 3.2, 3.3, 3.4, 3.5, 3.6, 3.7, 3.9_

  - [ ] 3.11 Migrate test batch — Backend directory (scripted substitution + green-build checkpoint)
    - Scope: `Test/Mist.Tests/Backend/**` (e.g. `VoidEmitterTests`, `TraitEmissionPropertyTests`, `TupleEmitterOutputTests`, `ValueTaskEmissionPropertyTests`, `PatternEmissionPropertyTests`)
    - Apply the SAME scripted substitution set as 3.9 (unions via `Assert.NotEmpty(typeRef.SingleTypeRefs)`, never `Rest`)
    - **CHECKPOINT**: run `dotnet build` — it MUST be green before advancing, then run the Backend test subset (`dotnet test --filter`) — it MUST pass before advancing.
    - **COMMIT** (fixed point): after the checkpoint passes, stage the migrated Backend test files and commit locally — e.g. `git commit -m "parser-backtracking: migrate Backend test batch to new AST node names"`. Do not push yet.
    - _Bug_Condition: Grammar restructure changes AST node type names used in tests_
    - _Expected_Behavior: Backend tests construct/assert against First / SingleTypeRefs_
    - _Preservation: Backend emission tests pass with identical semantics under new node names_
    - _Requirements: 3.1, 3.2, 3.3, 3.7, 3.9_

  - [ ] 3.12 Migrate test batch — Analysis directory (scripted substitution + green-build checkpoint)
    - Scope: `Test/Mist.Tests/Analysis/**` (e.g. `TypeCheckerEdgeCaseTests`, `TypeCheckerPropertyTests`, `VoidReturnPropertyTests`, `VoidTypeCheckerTests`, `TypeUnionExhaustivenessPropertyTests`)
    - Apply the SAME scripted substitution set as 3.9 (unions via `Assert.NotEmpty(typeRef.SingleTypeRefs)`, never `Rest`)
    - **CHECKPOINT**: run `dotnet build` — it MUST be green before advancing, then run the Analysis test subset (`dotnet test --filter`) — it MUST pass before advancing.
    - **COMMIT** (fixed point): after the checkpoint passes, stage the migrated Analysis test files and commit locally — e.g. `git commit -m "parser-backtracking: migrate Analysis test batch to new AST node names"`. Do not push yet.
    - _Bug_Condition: Grammar restructure changes AST node type names used in tests_
    - _Expected_Behavior: Analysis tests construct/assert against First / SingleTypeRefs_
    - _Preservation: Analysis tests pass with identical semantics under new node names_
    - _Requirements: 3.1, 3.2, 3.3, 3.4, 3.5, 3.6, 3.7, 3.9_

  - [ ] 3.13 Migrate test batch — remaining test directories (scripted substitution + green-build checkpoint)
    - Scope: all remaining test files referencing the node names, including `Test/Mist.Tests/Integration/**`, `Test/Mist.Tests/Generators/**`, and any root-level `Test/Mist.Tests/*.cs` (e.g. `ParserAstPropertyTests`, `PipelineBackendPropertyTests`, `SemanticModelDiagnosticsPropertyTests`)
    - Apply the SAME scripted substitution set as 3.9 across these directories (unions via `Assert.NotEmpty(typeRef.SingleTypeRefs)`, never `Rest`)
    - After this batch, no references to the old `*TypeRefTypeReferenceNode` / `UnionTypeRefTypeReferenceNode` variant names should remain in the test tree — verify with a search
    - **CHECKPOINT**: run `dotnet build` — it MUST be green before advancing, then run the test subset for the remaining directories (`dotnet test --filter`) — it MUST pass before advancing.
    - **COMMIT** (fixed point): after the checkpoint passes, stage the remaining migrated test files and commit locally — e.g. `git commit -m "parser-backtracking: migrate remaining test directories to new AST node names"`. Do not push yet.
    - _Bug_Condition: Grammar restructure changes AST node type names used in tests_
    - _Expected_Behavior: Remaining tests construct/assert against First / SingleTypeRefs_
    - _Preservation: All remaining tests pass with identical semantics under new node names_
    - _Requirements: 3.1, 3.2, 3.3, 3.4, 3.5, 3.6, 3.7, 3.8, 3.9, 3.10_

  - [ ] 3.16 Restructure the RefinableTypeExpression grammar rule in Mist.storm
    - **SECOND FIX**: This is the second instance of the identical "union-first ordered choice, then single" defect (see design "Second Fix: RefinableTypeExpression", items 17–22). It is the type-alias RHS and is the dominant cost in the Func/Action arity-overload scenario.
    - File: `Source/Mist/Grammar/Mist.storm`
    - Replace the discriminated `RefinableTypeExpression` rule (which tries `RefinableUnionTypeExpr` first) with a single production: `rule RefinableTypeExpression = first:RefinableSingleTypeExpr (PIPE rest:RefinableSingleTypeExpr)*;`
    - Remove the `RefinableUnionTypeExpr` rule entirely (union detection becomes implicit when the PIPE-separated continuation collection is non-empty)
    - Leave `RefinableSingleTypeExpr = type:SingleTypeRef refinement:RefinementPredicate?;` unchanged
    - Note: the tree will NOT compile until consumers are updated (expected — same as task 3.1)
    - _Bug_Condition: isBugCondition(input) where input contains a non-union type-alias RHS (RefinableTypeExpression) with nested TypeArgumentList and no PIPE token_
    - _Expected_Behavior: Parser parses one RefinableSingleTypeExpr then optionally consumes PIPE-separated continuations — no speculative backtracking on the alias RHS_
    - _Preservation: All type-alias RHS variants (single, refined, union) continue to produce correct AST nodes_
    - _Requirements: 2.1, 2.2, 2.3_

  - [ ] 3.17 Rebuild and CONFIRM the regenerated RefinableTypeExpression AST shape (BEFORE touching any consumer)
    - **CRITICAL**: Same lesson as task 3.2 — do NOT assume the generated shape. Confirm it from the regenerated code.
    - Rebuild so the Storm source generator regenerates the AST: run `dotnet build` on `Source/Mist/Mist.csproj`
    - Read the regenerated `Mist.Ast.g.cs` and record the EXACT flat field names for `RefinableTypeExpressionNode`: the first constituent field (expected `First`, a `RefinableSingleTypeExprNode`), the PIPE-separated continuation collection field (an `ImmutableArray<RefinableSingleTypeExprNode>`), and the non-nullable `Rest` `default!` codegen artifact
    - Confirm the old variants `RefinableUnionTypeRefinableTypeExpressionNode` and `RefinableSingleTypeRefinableTypeExpressionNode` are GONE (zero matches in the regenerated file)
    - **Union predicate to use everywhere downstream**: the continuation collection's `.Length > 0` (equivalently `!IsEmpty`). DO NOT use `.Rest` — it is a non-nullable codegen artifact and is NOT a valid union signal.
    - Document the confirmed shape and field names here before proceeding — tasks 3.18 and 3.19 depend on this confirmation
    - **EXPECTED OUTCOME**: Build regenerates the AST (it will FAIL to compile because consumers still reference the old variant names — expected, with 0 warnings); the exact node name and field shape are confirmed in writing
    - _Bug_Condition: Grammar restructure changes the generated RefinableTypeExpression AST node types_
    - _Expected_Behavior: Confirmed generated shape exposes First + a continuation collection; union detected via continuation-collection.Length > 0_
    - _Preservation: Information content of the type-alias RHS AST is preserved under the new node name_
    - _Requirements: 2.2, 3.1, 3.2, 3.3, 3.4, 3.5, 3.6, 3.7_

  - [ ] 3.18 Update RefinableTypeExpression source consumers (SemanticModelBuilder.cs 13 refs, MistPrettyPrinter.cs 5 refs)
    - **SemanticModelBuilder** (`Source/Mist/Semantics/Model/SemanticModelBuilder.cs` — 13 references): migrate `ResolveRefinableTypeExpression`, `HasRefinementPredicate`, `ExtractRefinableTypeExpressionName`, and the `ResolveTypeAlias` call site from the old variant `switch` to the flat node
      - Replace each `switch (refinableExpr)` over `RefinableSingleTypeRefinableTypeExpressionNode` / `RefinableUnionTypeRefinableTypeExpressionNode` with a union check on the continuation-collection-non-empty predicate (from the CONFIRMED shape in 3.17)
      - Read constituents via `First` then iterate the continuation collection (mirrors the old `union.UnionType.First` + `union.UnionType.RefinableSingleTypeExprs`); for the non-union case operate on `First` directly (equivalent to the old `single.SingleType`)
      - `HasRefinementPredicate`: check `First.Refinement` then iterate the continuation collection checking each `.Refinement`; `ExtractRefinableTypeExpressionName`: read the name from `First.Type`
    - **MistPrettyPrinter** (`Source/Mist/Parsing/MistPrettyPrinter.cs` — 5 references): migrate `EmitRefinableTypeExpression` to the flat node — replace the `node switch` with a union check (continuation collection non-empty), folding the `EmitRefinableUnion` logic into the flat path and reusing the existing `EmitRefinableSingle` helper unchanged
    - NEVER read `.Rest` (non-nullable codegen artifact)
    - Build MUST be green with ZERO warnings after these changes (zero-warnings policy)
    - **COMMIT** (fixed point, local, no push): once both files build green with zero warnings, stage the specific changed paths only and commit — `git commit -m "parser-backtracking: restructure RefinableTypeExpression grammar + update source consumers"`
    - _Bug_Condition: Grammar restructure changes the RefinableTypeExpression AST node types_
    - _Expected_Behavior: Same IType resolution for all type-alias RHS forms; union detected via continuation-collection.Length > 0_
    - _Preservation: Semantic model resolves identical alias types, and the pretty printer produces identical output, before and after the fix_
    - _Requirements: 2.2, 3.1, 3.2, 3.3, 3.4, 3.5, 3.6, 3.7_

  - [ ] 3.19 Migrate RefinableTypeExpression test references (TypeAliasRegistrationPropertyTests.cs 3 refs) + verify
    - File: `Test/Mist.Tests/Semantics/TypeAliasRegistrationPropertyTests.cs` (3 references)
    - Migrate the `MakeRefinableTypeExpr` helper construction from `new RefinableSingleTypeRefinableTypeExpressionNode { SingleType = new RefinableSingleTypeExprNode { ... } }` to the flat `new RefinableTypeExpressionNode { First = new RefinableSingleTypeExprNode { ... } }` shape (confirm the EXACT field name from the regenerated AST in 3.17)
    - Verify no references to the removed `RefinableUnionTypeExpr*` / `RefinableSingleTypeRefinableTypeExpressionNode` / `RefinableUnionTypeRefinableTypeExpressionNode` variant names remain under `Test/` — verify with a search
    - Build `Test/Mist.Tests/Mist.Tests.csproj` green with ZERO warnings (zero-warnings policy)
    - Run the affected test subset (`TypeAliasRegistration` + any refinement/alias tests) via `dotnet test --filter` — it MUST pass
    - **COMMIT** (fixed point, local, no push): after the build is green and the subset passes, stage the specific changed paths only and commit — `git commit -m "parser-backtracking: migrate RefinableTypeExpression test references to flat node"`
    - _Bug_Condition: Grammar restructure changes the RefinableTypeExpression AST node type names used in tests_
    - _Expected_Behavior: Type-alias tests construct/assert against the flat RefinableTypeExpressionNode (First + continuation collection)_
    - _Preservation: Type-alias registration tests pass with identical semantics under the new node name_
    - _Requirements: 3.1, 3.2, 3.9_

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
    - **COMMIT** (fixed point): after the full suite passes, commit locally — e.g. `git commit -m "parser-backtracking: full suite green after migration"` — then PUSH the branch (`git push`) to back up the completed migration milestone.
    - _Preservation: Preservation Requirements from design — unions, primitives, named, tuples, void, unknown, parenthesized all unchanged_
    - _Requirements: 3.1, 3.2, 3.3, 3.4, 3.5, 3.6, 3.7, 3.8, 3.9, 3.10_

- [ ] 4. Checkpoint - Ensure all tests pass
  - Run `dotnet test` on the full solution and confirm ZERO failures
  - Run `dotnet build` and confirm ZERO warnings (per zero-warnings policy)
  - Compile and run the AllFeatures sample to verify end-to-end correctness
  - Verify baseline output matches expected
  - **COMMIT & PUSH** (final fixed point): after all verifications pass (zero test failures, zero build warnings, AllFeatures sample runs, baseline output matches), commit any remaining changes locally and push the branch (`git push`).
  - Ensure all tests pass, ask the user if questions arise.
  - _Expected_Behavior: Linear parse time preserved; end-to-end output unchanged_
  - _Preservation: Full suite green, AllFeatures sample compiles and runs, baseline output matches_
  - _Requirements: 3.9, 3.10_

## Task Dependency Graph

```json
{
  "waves": [
    { "id": 0, "tasks": ["1", "2"] },
    { "id": 1, "tasks": ["3.1"] },
    { "id": 2, "tasks": ["3.2"] },
    { "id": 3, "tasks": ["3.3", "3.4", "3.5", "3.6", "3.7", "3.8"] },
    { "id": 4, "tasks": ["3.9"] },
    { "id": 5, "tasks": ["3.10"] },
    { "id": 6, "tasks": ["3.11"] },
    { "id": 7, "tasks": ["3.12"] },
    { "id": 8, "tasks": ["3.13"] },
    { "id": 9, "tasks": ["3.16"] },
    { "id": 10, "tasks": ["3.17"] },
    { "id": 11, "tasks": ["3.18"] },
    { "id": 12, "tasks": ["3.19"] },
    { "id": 13, "tasks": ["3.14"] },
    { "id": 14, "tasks": ["3.15"] },
    { "id": 15, "tasks": ["4"] }
  ]
}
```

## Notes

- Tasks 1 and 2 are independent and run in parallel first (confirm the bug, establish the preservation baseline).
- Task 3.1 (grammar change) is followed IMMEDIATELY by task 3.2 (rebuild + confirm the regenerated AST shape). Prior attempts failed by ASSUMING the generated shape — 3.2 removes that risk and ALL downstream tasks depend on it.
- The union predicate is `typeRef.SingleTypeRefs.Length > 0` (equivalently `!typeRef.SingleTypeRefs.IsEmpty`) everywhere. `Rest` is a non-nullable codegen artifact and MUST NOT be used. The migrated union assertion is `Assert.NotEmpty(typeRef.SingleTypeRefs)`, never `Assert.NotNull(typeRef.Rest)`.
- The 7 source-file updates (3.3–3.8; ExhaustivenessChecker + CSharpTranspilerBackend combined in 3.8) all depend on the confirmed AST from 3.2 but are independent of each other and can proceed in parallel.
- The 181-test-file migration is performed as a SCRIPTED, directory-batched substitution (3.9–3.13), sequenced so each batch depends on the prior batch's green build. This replaces the previous monolithic migration task, which had no intermediate checkpoints and made failures impossible to bisect.
- There are **TWO grammar fixes**, both the same "union-first ordered choice, then single" defect: (a) `TypeReference` (tasks 3.1–3.13) and (b) `RefinableTypeExpression`, the type-alias RHS (tasks 3.16–3.19). The second fix is placed AFTER the test-migration batches (3.13) and BEFORE the perf verification (3.14) because the perf test (`FuncActionArityOverloads_ShouldNotGrowExponentially`) only reaches linear time once BOTH fixes are in — fix (a) alone leaves the parse exponential at ~2.2x/N (bails at N=15).
- The second fix is ADDITIVE in scope and does NOT inflate the file counts: `SemanticModelBuilder.cs` and `MistPrettyPrinter.cs` are ALREADY among the first fix's 7 source files, and `TypeAliasRegistrationPropertyTests.cs` (in `Test/Mist.Tests/Semantics/`) is ALREADY among the 181 test files. The second fix re-touches these already-counted files (2 source + 1 test); net-new files = 0.
- Tasks 3.16–3.19 mirror the first-fix structure: 3.16 changes the grammar (like 3.1), 3.17 rebuilds and CONFIRMS the regenerated AST shape before touching consumers (like 3.2 — do not assume the shape), 3.18 updates the source consumers, and 3.19 migrates the test references. For the second fix the union predicate is the regenerated continuation collection's `.Length > 0` — confirm its exact field name in 3.17; `Rest` is again a non-nullable codegen artifact and MUST NOT be used.
- Scope language: the migration touches **7 source files** and **181 test files**. "2979" is the TOTAL test count (the preservation oracle), NOT a file count.
- Per the zero-warnings policy, each source-file and test-batch task must leave the build warning-free.
- Per the test-process-spawning policy, the performance test (task 1) uses in-process parsing only — no external process spawning.
- Commit at each checkpoint to record recoverable fixed points (per-checkpoint commits are LOCAL; push at the 3.15 and Task 4 milestones). All commits stage specific changed paths (never `git add .`), preserve hooks, and follow the repo's git-safety practices (no force-push, no history rewriting). If a batch goes wrong, reset to the previous checkpoint commit rather than manually unwinding the scripted substitution.
