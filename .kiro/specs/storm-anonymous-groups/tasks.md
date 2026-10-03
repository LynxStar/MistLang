# Implementation Plan: Storm Anonymous Groups

## Overview

This plan implements anonymous group projection in the Storm source generator, replacing the broken flat-field pattern with correct Group_Struct types. The work proceeds in 4 phases: generator implementation, migration triage, per-rule consumer migration with adversarial verification, and shim removal.

**Key constraint**: CS0618 (obsolete usage) warnings are intentionally permitted during the migration phase — this overrides the project's zero-warnings policy temporarily. The warnings ARE the migration worklist.

**Adversarial verification protocol**: Every migration task (Phase 3) has a 2-stage commit. Stage 1: agent migrates and reports warning count. Stage 2: independent verification build confirms the count. This prevents the failure mode where an agent reports success without actually fixing all sites.

**Git safety and wave commits**: Progress MUST be committed to git at the end of every wave in the Task Dependency Graph, once that wave's verification passes. Committing per wave guarantees a restore point at each wave boundary, so a failure late in the plan cannot discard hours of earlier work. Subagents MUST NEVER run destructive git commands - `git stash`, `git checkout -- <file>`, `git checkout <branch>`, `git reset --hard`, or `git clean` - because parallel tasks within a wave share the working tree, and these commands silently destroy other agents' uncommitted progress. When a task hits bugs it cannot resolve, the correct action is to STOP and report with the working tree intact, never to discard changes.

## Tasks

- [x] 1. AnonymousGroupInfo data model and detection logic
  - [x] 1.1 Add `AnonymousGroupInfo` record struct and `GroupModifier` enum to `Source/Storm.SourceGenerator/Emit/AstEmitter.cs` (or a new file in the same directory)
  - [x] 1.2 Implement `DetectAnonymousGroups(RuleAlternativeIR alt)` — left-to-right walk of the expression tree collecting `SequenceIR` nodes inside `ZeroOrMoreIR`/`OneOrMoreIR`/`OptionalIR` that have `FieldLabelIR` entries
  - [x] 1.3 Implement `IsPureTokenAlternation(SequenceIR seq, IReadOnlyList<FieldLabelIR> labelsInGroup)` — returns true when the group has exactly one label whose expression is an `AlternationIR` with all `TokenRefIR` alternatives
  - [x] 1.4 Unit test: construct IR for `rule X = a:Y (op:(A | B) right:Y)*;` and verify `DetectAnonymousGroups` returns one group with index 0, modifier ZeroOrMore, and 2 labels
  - _Requirements: 1.1, 1.3, 2.1_

- [x] 2. Add `EmitObsoleteShims` flag to EmissionContext
  - [x] 2.1 Add `public bool EmitObsoleteShims { get; }` property to `EmissionContext`
  - [x] 2.2 Add `emitObsoleteShims` parameter to the constructor (default `true` during migration)
  - [x] 2.3 Thread the flag through from the source generator entry point (where `EmissionContext` is constructed)
  - [x] 2.4 Unit test: verify the flag propagates correctly
  - _Requirements: 5.1_

- [x] 3. Group_Struct emission in AstEmitter
  - [x] 3.1 Modify `CollectProperties` (or add a new method) to detect anonymous groups and emit `public struct {RuleName}NodeGroup{N}` types instead of flat fields
  - [x] 3.2 Implement field type derivation for group struct fields (Token for TokenRefIR, {Rule}Node for RuleRefIR, Token for pure-token alternation inside the group)
  - [x] 3.3 Emit the parent node property with correct type based on modifier (scalar, nullable, ImmutableArray)
  - [x] 3.4 Exclude labels inside anonymous groups from the parent node's direct property list
  - [x] 3.5 Handle nested groups — emit as nested struct inside the outer Group_Struct
  - [x] 3.6 Unit test: feed the `Additive` rule IR to the emitter, verify output contains `public struct AdditiveNodeGroup0` with `Token Operator` and `MultiplicativeNode Right` fields
  - [x] 3.7 Unit test: verify parent `AdditiveNode` has `ImmutableArray<AdditiveNodeGroup0> Group0` and NOT scalar `Operator`/`Right`
  - _Requirements: 1.1, 1.2, 1.3, 1.4, 1.5, 1.6, 4.2, 4.3_

- [x] 4. Pure_Token_Alternation optimization in AstEmitter
  - [x] 4.1 When `IsPureTokenAlternation` returns true for a group, skip struct emission and emit a `Token`/`Token?`/`ImmutableArray<Token>` property on the parent node instead
  - [x] 4.2 Use the label name (PascalCase) for the property name when the group has a label prefix
  - [x] 4.3 Unit test: rule with `(A | B)*` as a labeled pure-token group produces `ImmutableArray<Token>` property, no struct
  - [x] 4.4 Unit test: a lone alternation group -- pure-token or mixed, e.g. `(choice:(A | B | Y))*` with rule-ref `Y` -- is one value-bearing element under the capture-count rule and does NOT project a `...NodeGroup0` struct; the pure-token-vs-mixed distinction instead governs field typing inside a two-or-more value-bearing struct group (verify a genuine 2-field group like `(choice:(A | Y) tail:Z)*` still projects a struct)
  - _Requirements: 2.1, 2.2, 2.3, 2.4, 4.5_

- [x] 5. Obsolete shim emission in AstEmitter
  - [x] 5.1 When `EmitObsoleteShims` is true, emit `[Obsolete("Use Group{N}")]` properties for each old flat field that existed before the change
  - [x] 5.2 Scalar shims on `*`/`+` groups: expression body returns `Group{N}.Length > 0 ? Group{N}[^1].{Field} : default`
  - [x] 5.3 List shims (the old `ImmutableArray<T>` properties like `Multiplicatives`): expression body projects `Group{N}.Select(g => g.{Field}).ToImmutableArray()`
  - [x] 5.4 Scalar shims on `?` groups: expression body returns `Group{N}?.{Field} ?? default`
  - [x] 5.5 When `EmitObsoleteShims` is false, do NOT emit any shim properties
  - [x] 5.6 Unit test: with shims enabled, verify `[Obsolete("Use Group0")]` appears on the old property names
  - [x] 5.7 Unit test: with shims disabled, verify old property names are absent from the output
  - _Requirements: 5.1, 5.2, 5.3, 5.4, 5.5, 5.6_

- [x] 6. Children() override update in AstEmitter
  - [x] 6.1 For list-typed group properties (`*`/`+`), emit a `foreach` over the group array that yields each node-typed field
  - [x] 6.2 For scalar group properties, yield each node-typed field directly
  - [x] 6.3 For nullable group properties, conditionally yield node-typed fields
  - [x] 6.4 Unit test: verify `Children()` output iterates `Group0` and yields `Right` for `AdditiveNode`
  - _Requirements: 7.2_

- [x] 7. ParserEmitter — struct construction for `*`/`+` groups
  - [x] 7.1 Modify the `ZeroOrMoreIR` case in `EmitFullExprParse` to detect when the inner sequence is an anonymous group
  - [x] 7.2 Emit `ImmutableArray.CreateBuilder<{Rule}NodeGroup{N}>()` before the loop
  - [x] 7.3 Inside the loop body, parse each labeled field into a local, then construct the struct value and append to the builder
  - [x] 7.4 After the loop, emit `.ToImmutable()` and assign to the node's `Group{N}` property
  - [x] 7.5 Remove emission of scalar node property assignments from inside the loop
  - [x] 7.6 Unit test: verify parser output for `Additive` contains `CreateBuilder<AdditiveNodeGroup0>` and `new AdditiveNodeGroup0 { ... }` inside the loop
  - _Requirements: 3.1, 3.5_

- [x] 8. ParserEmitter — struct construction for `?` and bare groups
  - [x] 8.1 For `OptionalIR` anonymous groups: emit nullable local, construct struct on match, assign to node property
  - [x] 8.2 For bare (no-modifier) anonymous groups: construct struct directly, assign to node property
  - [x] 8.3 Unit test: verify parser output for a `?`-modified group uses nullable local pattern
  - [x] 8.4 Unit test: verify parser output for a bare group uses direct struct construction
  - _Requirements: 3.2, 3.3_

- [x] 9. Integration build — shims enabled
  - [x] 9.1 Build the full `Mist.slnx` solution with `EmitObsoleteShims = true`
  - [x] 9.2 Verify: zero compile errors
  - [x] 9.3 Verify: all existing tests pass
  - [x] 9.4 Record the CS0618 warning count (this becomes the migration baseline)
  - [x] 9.5 Document the per-rule warning breakdown (grep output grouped by rule name)
  - _Requirements: 5.3, 6.1, 6.2_

- [x] 10. Generator test coverage (property-based)
  - [x] 10.1 Property-based test: random grammar IR with anonymous groups produces struct emission (Property 1)
  - [x] 10.2 Property-based test: multiple groups produce unique sequential indices (Property 2)
  - [x] 10.3 Property-based test: modifier produces correct parent property type (Property 3)
  - [x] 10.4 Property-based test: shims disabled produces no flat fields (Property 4)
  - [x] 10.5 Property-based test: pure-token alternation produces Token collapse (Property 5)
  - [x] 10.6 Property-based test: `*`/`+` groups produce parser with CreateBuilder (Property 6)
  - [x] 10.7 Property-based test: non-group rules produce identical output (Property 7)
  - [x] 10.8 Property-based test: shim forwarding expressions are correct (Property 8)
  - [x] 10.9 Property-based test: Children() yields group node fields (Property 9)
  - _Requirements: 9.1, 9.2, 9.3, 9.4, 9.5, 9.6_

- [x] 11. Migration triage — count and categorize CS0618 warnings
  - [x] 11.1 Build with shims enabled, capture full warning output
  - [x] 11.2 Group warnings by affected node type (AdditiveNode, MultiplicativeNode, EqualityNode, LogicalAndNode, LogicalOrNode, VoidCoalescingNode, RelationalComparisonRelationalNode)
  - [x] 11.3 Within each node type, group by source file area (TypeCheckerAnalyzer, CSharpEmitter, MistPrettyPrinter, SemanticModelBuilder, tests)
  - [x] 11.4 Record counts per (rule, area) pair — this defines the scope of each migration task
  - [x] 11.5 Create a migration tracking document with the worklist
  - _Requirements: 5.4_

- [x] 12. Migrate AdditiveNode consumers — TypeCheckerAnalyzer
  - [x] 12.1 Update `AnalyzeAdditiveForReads` and all related methods in `TypeCheckerAnalyzer.cs` to iterate `Group0` instead of reading scalar `Operator`/`Right`
  - [x] 12.2 Build and report remaining CS0618 count for `AdditiveNode`
  - [x] 12.3 Verification: adversarial build confirms reported count
  - _Requirements: 7.1_

- [x] 13. Migrate AdditiveNode consumers — CSharpEmitter
  - [x] 13.1 Update `ScanAdditiveForValueReturningCalls` and all emission logic in `CSharpEmitter.cs` to iterate `Group0`
  - [x] 13.2 Update operator emission to iterate group entries (emit `left op1 right1 op2 right2` chain correctly)
  - [x] 13.3 Build and report remaining CS0618 count for `AdditiveNode`
  - [x] 13.4 Verification: adversarial build confirms reported count
  - _Requirements: 7.1, 7.3_

- [x] 14. Migrate AdditiveNode consumers — MistPrettyPrinter
  - [x] 14.1 Update `EmitAdditive` in `MistPrettyPrinter.cs` to iterate `Group0`
  - [x] 14.2 Build and report remaining CS0618 count for `AdditiveNode`
  - [x] 14.3 Verification: adversarial build confirms reported count
  - _Requirements: 7.1_

- [x] 15. Migrate AdditiveNode consumers — Tests
  - [x] 15.1 Update all test files that construct or read `AdditiveNode` (TypeCheckerPropertyTests, TypeCheckerEdgeCaseTests, OperatorEmissionDispatchTests, MissingIntrinsicEmissionTests, MistAsExpressionGenerator, etc.)
  - [x] 15.2 Build and report remaining CS0618 count for `AdditiveNode`
  - [x] 15.3 Verification: adversarial build confirms zero remaining `AdditiveNode` CS0618 warnings
  - _Requirements: 7.1_

- [x] 16. Migrate MultiplicativeNode consumers — TypeCheckerAnalyzer
  - [x] 16.1 Update `AnalyzeMultiplicativeForReads` and related methods
  - [x] 16.2 Verification: adversarial build confirms reported count
  - _Requirements: 7.1_

- [x] 17. Migrate MultiplicativeNode consumers — CSharpEmitter
  - [x] 17.1 Update multiplicative emission logic to iterate `Group0`
  - [x] 17.2 Verification: adversarial build confirms reported count
  - _Requirements: 7.1_

- [x] 18. Migrate MultiplicativeNode consumers — MistPrettyPrinter
  - [x] 18.1 Update `EmitMultiplicative` to iterate `Group0`
  - [x] 18.2 Verification: adversarial build confirms reported count
  - _Requirements: 7.1_

- [x] 19. Migrate MultiplicativeNode consumers — Tests
  - [x] 19.1 Update all test constructions of `MultiplicativeNode`
  - [x] 19.2 Verification: adversarial build confirms zero remaining `MultiplicativeNode` CS0618 warnings
  - _Requirements: 7.1_

- [x] 20. Migrate EqualityNode consumers — TypeCheckerAnalyzer
  - [x] 20.1 Update equality analysis methods
  - [x] 20.2 Verification: adversarial build confirms reported count
  - _Requirements: 7.1_

- [x] 21. Migrate EqualityNode consumers — CSharpEmitter
  - [x] 21.1 Update equality emission logic
  - [x] 21.2 Verification: adversarial build confirms reported count
  - _Requirements: 7.1_

- [x] 22. Migrate EqualityNode consumers — MistPrettyPrinter + Tests
  - [x] 22.1 Update pretty printer and all test constructions
  - [x] 22.2 Verification: adversarial build confirms zero remaining `EqualityNode` CS0618 warnings
  - _Requirements: 7.1_

- [x] 23. Migrate LogicalAndNode consumers — All areas
  - [x] 23.1 Update TypeCheckerAnalyzer, CSharpEmitter, MistPrettyPrinter, and tests
  - [x] 23.2 Verification: adversarial build confirms zero remaining `LogicalAndNode` CS0618 warnings
  - _Requirements: 7.1_

- [x] 24. Migrate LogicalOrNode consumers — All areas
  - [x] 24.1 Update TypeCheckerAnalyzer, CSharpEmitter, MistPrettyPrinter, and tests
  - [x] 24.2 Verification: adversarial build confirms zero remaining `LogicalOrNode` CS0618 warnings
  - _Requirements: 7.1_

- [x] 25. Migrate VoidCoalescingNode consumers — All areas
  - [x] 25.1 Update TypeCheckerAnalyzer, CSharpEmitter, MistPrettyPrinter, and tests
  - [x] 25.2 Verification: adversarial build confirms zero remaining `VoidCoalescingNode` CS0618 warnings
  - _Requirements: 7.1_

- [x] 26. Migrate RelationalComparisonRelationalNode consumers — All areas
  - [x] 26.1 Update TypeCheckerAnalyzer, CSharpEmitter, MistPrettyPrinter, and tests
  - [x] 26.2 Verification: adversarial build confirms zero remaining `RelationalComparisonRelationalNode` CS0618 warnings
  - _Requirements: 7.1_

- [x] 27. Remove obsolete shims
  - [x] 27.1 Set `EmitObsoleteShims = false` (or remove the parameter entirely)
  - [x] 27.2 Build the solution — verify zero errors and zero CS0618 warnings
  - [x] 27.3 All tests pass
  - [x] 27.4 The zero-warnings policy is fully restored
  - _Requirements: 5.5, 5.6, 7.4_

- [x] 28. Sample application
  - [x] 28.1 Create `samples/StormAnonymousGroups/Program.mist` with the proof code from Requirement 8
  - [x] 28.2 Add `StormAnonymousGroups.Program.RunDemo()` call to `samples/AllFeatures/Program.mist`
  - [x] 28.3 Compile and run — verify output matches expected (`x=8`, `y=24`, `z=10`)
  - [x] 28.4 Verify the AllFeatures baseline test passes
  - _Requirements: 8.1, 8.2, 8.3, 8.4, 8.5_

- [x] 29. Final verification
  - [x] 29.1 Full solution build: zero errors, zero warnings
  - [x] 29.2 Full test suite: all tests pass
  - [x] 29.3 AllFeatures sample run: output matches baseline
  - [x] 29.4 Confirm no `[Obsolete]` shim code remains in generated output
  - _Requirements: 5.6, 7.3, 7.4_

## Notes

- **CS0618 override**: During tasks 1–26, the zero-warnings policy is suspended for CS0618 warnings originating from `[Obsolete("Use Group{N}")]` shim usage. This is intentional — the warnings are the migration signal. The policy resumes at Task 27 (shim removal).
- **Phase 3 task scoping is provisional**: The per-rule, per-area breakdown for tasks 12–26 is based on preliminary code inspection. Task 11 (triage) may reveal that some rules have more/fewer consumers than expected, requiring task scope adjustments.
- **Adversarial verification**: Every migration task (12–26) has a 2-stage commit protocol. Stage 1: agent migrates and reports warning count. Stage 2: independent verification build confirms the count. This prevents the failure mode where an agent reports success without actually fixing all sites.
- **Pure_Token_Alternation does NOT apply to the outer group**: In `(operator:(PLUS | MINUS) right:Multiplicative)*`, the outer group has 2 labels (one token alternation, one rule ref) so it gets a struct. The `(PLUS | MINUS)` part is a nested group that qualifies for Pure_Token_Alternation and collapses to `Token Operator` on the struct.
- **Wave commit protocol (per-package green)**: Commit at the end of every wave. The commit gate is scoped to the package(s) a wave changed, NOT the full solution. During generator work (Tasks 1-8), a wave is committable when the `Storm.SourceGenerator` project builds and its unit tests pass; those tests assert on emitter output strings and do not require the downstream `Mist.slnx` solution to compile, so they stay green wave by wave. This satisfies the repo's build-before-commit rule without demanding full-solution green during the flag-day. Follow Conventional Commits (e.g., `feat(storm): wave 3 - group struct emission`). Do NOT push. A commit at every wave boundary is mandatory; more frequent commits are welcome.
- **Milestone full-solution green (Tasks 9, 27, 29 only)**: The full `Mist.slnx` solution is required green at three integration milestones, not at every wave: Task 9 (first full build with the new generator - green with only the expected CS0618 shim warnings), Task 27 (shim removal - zero warnings of any kind), and Task 29 (final verification). Do NOT build the full solution mid-generator-development (before Task 9) - it will be red because the new AST shape, parser changes, and shims land across several waves. Because the shims are emitted by the generator itself, deferring the first full build to Task 9 means the solution transitions directly from old-green to new-green (with CS0618), with no committed broken state.
- **Destructive git prohibition**: Subagents frequently attempt `git stash`, `git checkout`, `git reset --hard`, or `git clean` when they hit repeated build failures. This is FORBIDDEN. With parallel tasks in a wave, these commands destroy other agents' uncommitted progress and can wipe out hours of work. The only correct response to unresolvable bugs is to STOP and report, leaving the working tree untouched.
- **Anti-loss is the priority**: If a wave genuinely cannot reach even package-green (rare, given the wave structure), commit the work as `wip(storm): wave N - <summary>` rather than discard it. On a feature branch, a WIP commit is always better than losing hours of parallel progress. Never push WIP commits, and squash them away before the final CR.

## Task Dependency Graph

```json
{
  "waves": [
    { "id": 0, "tasks": ["1.1", "1.2", "1.3", "2.1", "2.2"] },
    { "id": 1, "tasks": ["1.4", "2.3", "2.4"] },
    { "id": 2, "tasks": ["3.1", "3.2", "3.3", "3.4", "3.5", "4.1", "4.2"] },
    { "id": 3, "tasks": ["3.6", "3.7", "4.3", "4.4", "5.1", "5.2", "5.3", "5.4", "5.5"] },
    { "id": 4, "tasks": ["5.6", "5.7", "6.1", "6.2", "6.3", "7.1", "7.2", "7.3", "7.4", "7.5"] },
    { "id": 5, "tasks": ["6.4", "7.6", "8.1", "8.2", "8.3", "8.4"] },
    { "id": 6, "tasks": ["9.1", "9.2", "9.3", "9.4", "9.5"] },
    { "id": 7, "tasks": ["10.1", "10.2", "10.3", "10.4", "10.5", "10.6", "10.7", "10.8", "10.9", "11.1", "11.2", "11.3", "11.4", "11.5"] },
    { "id": 8, "tasks": ["12.1", "12.2"] },
    { "id": 9, "tasks": ["12.3", "13.1", "13.2"] },
    { "id": 10, "tasks": ["13.3", "13.4", "14.1", "14.2"] },
    { "id": 11, "tasks": ["14.3", "15.1", "15.2"] },
    { "id": 12, "tasks": ["15.3", "16.1"] },
    { "id": 13, "tasks": ["16.2", "17.1", "17.2", "20.1", "20.2", "23.1", "24.1", "25.1", "26.1"] },
    { "id": 14, "tasks": ["18.1", "18.2", "21.1", "21.2", "23.2", "24.2", "25.2", "26.2"] },
    { "id": 15, "tasks": ["19.1", "19.2", "22.1", "22.2"] },
    { "id": 16, "tasks": ["27.1", "27.2", "27.3", "27.4"] },
    { "id": 17, "tasks": ["28.1", "28.2", "28.3", "28.4"] },
    { "id": 18, "tasks": ["29.1", "29.2", "29.3", "29.4"] }
  ]
}
```

## Capture fidelity amendment -- 2026-10-03

- [x] Preserve the union of unnamed alternative captures and allocate distinct property/struct names for differing group shapes.
- [x] Yield canonical captures in source order across direct properties and interleaved groups, using the parsed alternative’s ownership plan.

Storm `b4e5853`: 65 requested focused tests and 852 full-suite tests pass; four additional Tier 2 inline projection cases pass. Build, format verification and whitespace checks pass. The fidelity seam remains 20 cases / 27 parses. Consumer verification and exact remaining limits are recorded in `backlog/storm.md`.
