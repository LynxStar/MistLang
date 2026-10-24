# Implementation Plan: Storm Selective Packrat Memoization

## Overview

This plan implements selective packrat memoization in the Storm source generator's emitted recursive-descent PEG parser. Work proceeds in dependency order: first the pure analysis artifacts (`MemoizationPlan`, `MemoizationAnalysisPass`), then threading the plan through `EmissionContext`, then the `ParserEmitter` code-shape changes (memo fields/helper + per-rule probe/store wrapper), and finally wiring the pass into `StormIncrementalGenerator` so the full pipeline produces a memoized parser. Each step builds on the previous one and ends wired into the generator so there is no orphaned code.

All new tests are **in-process** (analysis tests build in-memory grammars; emitter/soundness tests emit, compile, and inspect generated source in-process; the performance oracle parses in-process). **No new test spawns an external process** (`dotnet`/`Process.Start`), in compliance with the design's "Process-spawning review" and the workspace process-spawning policy. If any task is found to require process spawning during implementation, stop and request user approval before writing it.

The hard gate throughout: the full Mist suite (~2989 tests) and the AllFeatures sample must produce byte-for-byte / structurally identical AST, diagnostics, and emitted C# (Requirements 1.x), with no new `STORM*`/`CS*` warnings (Requirements 6.x).

## Tasks

- [x] 1. Implement the memoization analysis (pure, generator-side)
  - [x] 1.1 Create the `MemoizationPlan` immutable result type
    - Add `Source/Storm.SourceGenerator/Analyze/MemoizationPlan.cs` in namespace `Storm.SourceGenerator.Analyze`
    - Expose `IReadOnlyList<string> MemoizedRuleNames` (declaration order), `IReadOnlyDictionary<string,int> RuleIds`, `bool IsMemoized(string)`, `int Count`, and a static `Empty` plan (no rules)
    - Guarantee dense, 0-based ids (`0..Count-1`) assigned one-per-rule in the order of `MemoizedRuleNames`
    - _Requirements: 5.1, 5.2, 5.3_

  - [x] 1.2 Implement `MemoizationAnalysisPass.Run(GrammarIR)`
    - Add `Source/Storm.SourceGenerator/Analyze/MemoizationAnalysisPass.cs` as a pure static pass, mirroring `LeftRecursionPass`/`ReachabilityPass` structure
    - Phase 1 — `BuildReferenceGraph`: walk every `RuleAlternativeIR.Expression` and collect **all** `RuleRefIR` targets at any nesting depth (recurse through `SequenceIR`, `AlternationIR`, `OptionalIR`, `ZeroOrMoreIR`, `OneOrMoreIR`, `InlineFragmentIR`; terminals `TokenRefIR`/`StringLiteralIR`/`CharClassIR`/`RegexLiteralIR` add no edges)
    - Phase 2 — `FindRulesOnCycle`: run Tarjan SCC over the reference graph; a rule is on a cycle iff its SCC size > 1 or it has a self-edge
    - Phase 3 — backtracking intersection: compute `HasBacktrackingConstruct` (more than one alternative, OR any `OptionalIR`/`ZeroOrMoreIR`/`OneOrMoreIR` anywhere in the single alternative via `ContainsOptionalOrRepetition`); mark a cyclic rule only when some member of its SCC has a backtracking construct
    - SR-2 filter — exclude any candidate that is not speculative-safe (`IsSpeculativeSafe`: multi-alternative rules pass; single-alternative rules pass only if they contain no mandatory token match that can latch `_hasError`; if unsure, exclude)
    - Emit `marked` rule names in **grammar declaration order**, assign dense ids `0..Count-1`, return `MemoizationPlan` (or `MemoizationPlan.Empty` when nothing qualifies)
    - _Requirements: 3.1, 3.3, 3.4, 3.5, 3.6, 2.4, 5.1, 5.2, 5.3_

  - [x] 1.3 Write property test for heuristic coverage
    - Add `Test/Storm.SourceGenerator.Tests/MemoizationAnalysisPassTests.cs` using the in-memory grammar pattern from `ValidationPassTests.cs`
    - **Property 4: Heuristic coverage**
    - Assert a grammar mirroring the `TypeArgument → TypeReference → SingleTypeRef → TypeArgumentList → TypeArgument` cycle marks all four rules; assert acyclic rules and backtracking-free cycles are never marked
    - **Validates: Requirements 3.1, 3.2, 3.4, 3.5**

  - [x] 1.4 Write unit tests for the analysis pass cases
    - Extend `MemoizationAnalysisPassTests.cs` with the design's case list: expression precedence ladder marked (`Expr → … → Primary → '(' Expr ')'`); straight-line grammar → empty plan; cycle of single-alternative rules with no `?`/`*`/`+` → not marked; direct self-recursive rule with repetition/ordered choice → marked (self-edge); a `Require`-committing single-alternative rule excluded by the SR-2 filter
    - _Requirements: 3.1, 3.3, 3.4, 3.5, 3.6, 2.4_

  - [x] 1.5 Write property test for dense, stable, declaration-order ids
    - Extend `MemoizationAnalysisPassTests.cs`
    - **Property 6: Determinism of generated output (id assignment)**
    - Assert ids are exactly `0..Count-1` with no gaps, unique per rule, assigned in grammar declaration order, and identical across repeated `Run` invocations on the same grammar
    - **Validates: Requirements 5.1, 5.2, 5.3, 5.4**

- [x] 2. Checkpoint - analysis pass
  - Ensure all tests pass, ask the user if questions arise.

- [x] 3. Thread the memoization plan into emission
  - [x] 3.1 Add the `Memoization` plan to `EmissionContext`
    - Modify `Source/Storm.SourceGenerator/Emit/EmissionContext.cs` to add a never-null `MemoizationPlan Memoization { get; }` property
    - Add an optional `MemoizationPlan? memoization = null` constructor parameter that defaults to `MemoizationPlan.Empty`, so existing callers and `AstEmitter`/`LexerEmitter` are unaffected
    - _Requirements: 1.5_

- [x] 4. Emit memoized parser code in `ParserEmitter`
  - [x] 4.1 Emit the memo-table field, key helper, and per-parse allocation
    - Modify `Source/Storm.SourceGenerator/Emit/ParserEmitter.cs`
    - Only when `context.Memoization.Count > 0`: emit `[ThreadStatic] private static Dictionary<long,(SyntaxNode? Node,int End)>? _memo;` alongside existing parser state, emit `private static long MemoKey(int ruleId,int pos) => ((long)ruleId << 32) | (uint)pos;`, and allocate a fresh `_memo` in `Init()` per `Parse()` call
    - When `context.Memoization.Count == 0`: emit none of the above so parser source is byte-for-byte identical to today
    - _Requirements: 2.7, 2.8, 2.9, 1.5_

  - [x] 4.2 Emit the probe/store wrapper and rename the existing body
    - Modify `ParserEmitter.cs` so that for a memoized rule `R` it renames the existing emitted method to `_ParseR_Body()` (leaving the `EmitSingleAlt`/`EmitMultiAlt`/`EmitFullExprParse` machinery untouched) and emits a thin `_ParseR()` wrapper
    - Wrapper logic: leading `if (_hasError) return null;` (error flag evaluated exactly once at entry, no re-check before probe); probe `MemoKey(ruleId, _pos)`; on hit restore `_pos = End` and return the cached node/null **without** running the body or touching diagnostics; on miss capture `_entryPos`, run `_ParseR_Body()`, store `(result, _pos)` (failure stores `End == _entryPos`), return result
    - Non-memoized rules emit exactly as today
    - _Requirements: 2.1, 2.2, 2.3, 2.5, 2.6, 1.5_

  - [x] 4.3 Write property test for empty-plan byte-identical output
    - Add `Test/Storm.SourceGenerator.Tests/MemoizationEmitterTests.cs` (in-process emit/inspect, extending the `EmitterTests`/`ParserEmitterDiagnosticTests` patterns)
    - **Property 1: Output preservation (empty-plan emission)**
    - Assert that emitting a grammar whose `MemoizationPlan` is `Empty` produces parser source with no `_memo` field, no `MemoKey`, and no wrapper — byte-for-byte identical to the no-plan emission
    - **Validates: Requirements 1.5**

  - [x] 4.4 Write unit test for memoized wrapper shape
    - Extend `MemoizationEmitterTests.cs`
    - Assert that emitting a grammar with a marked rule produces both `_ParseR()` and `_ParseR_Body()`, the `_memo` field, the `MemoKey` helper, and the `Init()` allocation; assert a grammar with no marked rule produces none of these
    - _Requirements: 2.7, 2.9, 1.5_

  - [x] 4.5 Write property test for packrat position purity (SR-1)
    - Extend `MemoizationEmitterTests.cs`; emit a memoized grammar, compile it in-process, and drive the generated parser
    - **Property 2: Packrat determinism (SR-1)**
    - Assert two invocations of a memoized rule at the same token position over the same stream return a reference-identical node and leave `_pos` at the same end position, and that the cached result equals the unmemoized body's result
    - **Validates: Requirements 2.1, 2.2**

  - [x] 4.6 Write unit test for cached-failure diagnostic neutrality (SR-2)
    - Extend `MemoizationEmitterTests.cs`; compile and drive the generated parser in-process
    - **Property 3: Diagnostic neutrality (SR-2)**
    - Assert a cache hit on a cached failure restores `_pos` to the entry position, returns null, makes zero net change to the diagnostic bag, and leaves `_hasError` unchanged from entry
    - **Validates: Requirements 2.3, 2.6**

- [x] 5. Checkpoint - emission
  - Ensure all tests pass, ask the user if questions arise.

- [x] 6. Wire the pass into the generator pipeline and validate end-to-end
  - [x] 6.1 Call `MemoizationAnalysisPass` from `StormIncrementalGenerator`
    - Modify `Source/Storm.SourceGenerator/Generator/StormIncrementalGenerator.cs` to call `MemoizationAnalysisPass.Run(loweringResult.Grammar)` after lowering succeeds and before emission
    - Pass the resulting plan into the `EmissionContext` constructor so `ParserEmitter` consumes it; `AstEmitter`/`LexerEmitter` calls are unchanged
    - _Requirements: 3.1, 3.2, 5.4, 5.5, 1.1, 1.2, 1.3_

  - [x] 6.2 Verify the `FuncActionArityOverloads` performance oracle
    - Confirm `FuncActionArityOverloads_ShouldNotGrowExponentially` in `Test/Mist.Tests/Parsing/TypeArgumentParsingPerformanceTest.cs` runs in-process and asserts each N in 0..16 completes lex-and-parse within the 10,000 ms per-step bound with no step hitting the bail-out; tighten the assertion only if the existing one does not enforce the bound
    - **Property 5: Linear parse time**
    - **Validates: Requirements 4.1, 4.2**

  - [x] 6.3 Verify the `TypeParameterParsing` performance oracle still passes
    - Confirm `TypeParameterParsing_ShouldNotGrowExponentially` in the same file completes each N in 0..20 within the 10,000 ms per-step bound with no bail-out, running in-process
    - _Requirements: 4.3_

  - [x] 6.4 Write property test for generated-output determinism across builds
    - Extend `Test/Storm.SourceGenerator.Tests/StormGeneratorDeterminismPropertyTests.cs`
    - **Property 6: Determinism of generated output (emission)**
    - Assert generating the parser two or more times from byte-identical grammar source yields identical memoized rule ids and byte-for-byte identical parser source
    - **Validates: Requirements 5.4, 5.5**

- [x] 7. Final checkpoint - preservation and zero-warnings gates
  - Run the full Mist test suite (~2989 tests) and confirm the passing-test count is no lower than the pre-feature baseline with zero passing→failing transitions (structurally identical AST and identical diagnostic sequences) — Requirements 1.1, 1.2, 1.4
  - Build the AllFeatures sample and confirm emitted C# is byte-for-byte identical to the pre-feature baseline — Requirements 1.3
  - Build the generator and the Mist consumer and confirm zero new `STORM*`/`CS*` warnings and no new suppressions (Property 7) — Requirements 6.1, 6.2, 6.3
  - Ensure all tests pass, ask the user if questions arise.

## Notes

- All tasks, including every test sub-task (1.3, 1.4, 1.5, 4.3, 4.4, 4.5, 4.6, 6.2, 6.3, 6.4), are now required and must be implemented — there are no optional tasks.
- Each task references specific requirement sub-clauses for traceability; property-test sub-tasks additionally name the design's Correctness Property they validate.
- Under-inclusion by the heuristic is a performance tradeoff only, never a correctness risk — the preservation oracle in task 7 is the ultimate confirmation of soundness (SR-1).
- All new tests are in-process; none spawn an external process, per the design's process-spawning review and the workspace policy.
- Per the zero-warnings policy, any new `STORM*`/`CS*` warning stops work and is raised to the user before proceeding.

## Task Dependency Graph

```json
{
  "waves": [
    { "id": 0, "tasks": ["1.1"] },
    { "id": 1, "tasks": ["1.2", "3.1"] },
    { "id": 2, "tasks": ["1.3", "4.1"] },
    { "id": 3, "tasks": ["1.4", "4.2"] },
    { "id": 4, "tasks": ["1.5", "6.1", "4.3"] },
    { "id": 5, "tasks": ["4.4", "6.2"] },
    { "id": 6, "tasks": ["4.5", "6.3"] },
    { "id": 7, "tasks": ["4.6", "6.4"] }
  ]
}
```
