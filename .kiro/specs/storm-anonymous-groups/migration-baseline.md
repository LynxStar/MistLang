# CS0618 Migration Baseline (Task 9.4 / 9.5)

This document records the CS0618 obsolete-usage warning baseline captured at the
Task 9 integration milestone: the first full-solution build with the new Storm
group-projection generator and `EmitObsoleteShims = true`. The warnings are the
migration worklist for the Phase-3 consumer-migration waves (Tasks 12-26): each
distinct warning is one call site that reads an `[Obsolete("Use Group0")]` shim
property and must be rewritten to read the new `Group0` data instead.

## Build Context

- Command: `dotnet build Mist.slnx -c Debug --no-incremental`
- Result: Build succeeded, 0 errors, 379 warnings (all CS0618).
- Baseline commit context: HEAD at `dfd5dcfe` on branch `anongroup-grammar`.
- `--no-incremental` is required: a plain incremental `dotnet build` reports 0
  warnings because MSBuild skips up-to-date projects and does not re-emit their
  warnings. A forced recompile is the only way to observe the full count.

## Totals

| Metric | Count |
|--------|-------|
| Raw CS0618 occurrences (`grep -c CS0618`) | 758 |
| Distinct call sites (`grep 'warning CS0618' \| sort -u \| wc -l`) | 379 |

The raw count is exactly 2x the distinct count because the MSBuild console
logger prints each warning twice (once at compile emission, once in the
per-project rollup). The distinct count (379) is authoritative and matches
MSBuild's own "379 Warning(s)" summary line.

## By Node Type

Distinct call sites tallied by the shim member's owning AST node.

| Node type | Distinct sites | In Requirement 4 list? |
|-----------|----------------|------------------------|
| AssignmentNode | 118 | No - beyond Req 4 |
| AdditiveNode | 76 | Yes |
| EqualityNode | 63 | Yes |
| RelationalComparisonRelationalNode | 58 | Yes |
| MultiplicativeNode | 55 | Yes |
| AssociatedTypeSlotDeclarationNode | 9 | No - beyond Req 4 |
| **Total** | **379** | |

### By Shim Member

The finer-grained per-member tally (node.member) behind the table above.

| Shim member | Distinct sites |
|-------------|----------------|
| AssignmentNode.Value | 109 |
| AdditiveNode.Right | 67 |
| EqualityNode.Right | 58 |
| RelationalComparisonRelationalNode.Right | 51 |
| MultiplicativeNode.Right | 50 |
| AssignmentNode.Operator | 9 |
| AdditiveNode.Operator | 8 |
| RelationalComparisonRelationalNode.Operator | 6 |
| AssociatedTypeSlotDeclarationNode.Constraints | 6 |
| MultiplicativeNode.Operator | 4 |
| EqualityNode.Operator | 4 |
| AssociatedTypeSlotDeclarationNode.SlotWhereConstraints | 3 |
| RelationalComparisonRelationalNode.Additives | 1 |
| MultiplicativeNode.Ranges | 1 |
| EqualityNode.Relationals | 1 |
| AdditiveNode.Multiplicatives | 1 |

The three list-shims (`.Multiplicatives`, `.Ranges`, `.Relationals`,
`.Additives`) each appear exactly once, confirming the old redundant
`ImmutableArray<T>` projections have almost no remaining consumers.

## By Area / File

### Source (340 distinct sites)

| File | Distinct sites |
|------|----------------|
| TypeCheckerAnalyzer.cs | 158 |
| CSharpEmitter.cs | 61 |
| SemanticModelBuilder.cs | 38 |
| MistPrettyPrinter.cs | 22 |
| OwnershipAnalyzer.cs | 12 |
| RefinementPredicate.cs | 9 |
| ReadonlyAnalyzer.cs | 7 |
| ThisTierInferencePass.cs | 6 |
| FrameEscapeAnalyzer.cs | 6 |
| SemanticModel.cs | 5 |
| CooperationContractVerifier.cs | 5 |
| ReadonlyInferenceEngine.cs | 4 |
| DropAnalyzer.cs | 4 |
| TypeSlotBuilder.cs | 1 |
| MoveBoundaryResolver.cs | 1 |
| ContextCheckerAnalyzer.cs | 1 |
| **Source total** | **340** |

### Test (39 distinct sites)

| File | Distinct sites |
|------|----------------|
| ResolveBinaryOperatorTests.cs | 9 |
| Vector3OperatorIntegrationTest.cs | 6 |
| LambdaPrecedencePropertyTests.cs | 5 |
| ParserRuleTests.cs | 4 |
| LambdaExpressionParserTests.cs | 4 |
| OptionalOperatorParsingPropertyTests.cs | 2 |
| OperatorEmissionDispatchTests.cs | 2 |
| MissingIntrinsicEmissionTests.cs | 2 |
| LambdaReachabilityPropertyTests.cs | 2 |
| AsyncParsingTests.cs | 2 |
| AssociatedTypeDisambiguationPropertyTests.cs | 1 |
| **Test total** | **39** |

The 39 remaining Test warnings are all READ sites (consuming the shim
properties). The Task 9 test-file migration in this wave fixed only the 34
CS0200 WRITE sites (constructing nodes with old scalar properties, which are
now get-only shims) to unblock the `Mist.Tests` assembly from compiling. The
READ-site migration is Phase-3 work (Task 15, 19, 22, and the new-rule waves
below).

## Rule Scope Notes (important corrections to the design's Requirement 4 list)

### Rules that DO produce shims but are BEYOND Requirement 4

Requirement 4 enumerates only: VoidCoalescing, LogicalOr, LogicalAnd, Equality,
Additive, Multiplicative, and the RelationalComparison alternative of Relational.
The build reveals two additional struct-projected rules that emit shims and
therefore need consumer migration but have no dedicated Phase-3 task:

- **Assignment** - `target:VoidCoalescing (operator:(EQUAL | QUESTION_QUESTION_EQUAL) value:Expression)?`
  The `?` group holds two value-bearing elements (`operator:(...)` and
  `value:Expression`), so it projects `AssignmentNodeGroup0` with shims
  `AssignmentNode.Operator` and `AssignmentNode.Value`. This is the LARGEST
  single-node migration in the whole worklist at 118 sites (109 of them
  `.Value`). No Phase-3 task currently covers it.

- **AssociatedTypeSlotDeclaration** - `... constraintClause:(COLON constraints:ConstraintList slotWhereConstraints:WhereClause*)?`
  The `?` group holds two value-bearing elements (`constraints:ConstraintList`
  and `slotWhereConstraints:WhereClause*`; the bare `COLON` is structural), so it
  projects `AssociatedTypeSlotDeclarationNodeGroup0` with shims
  `.Constraints` and `.SlotWhereConstraints` (9 sites). No Phase-3 task
  currently covers it.

A follow-up should add migration waves for these two node types before Task 27
(shim removal), or Task 27 will surface them as hard compile errors.

### Rules that produce NO shims (no migration needed)

Requirement 4.1 lists **VoidCoalescing**, **LogicalOr**, and **LogicalAnd** as
affected `*`-modified rules, but under the design's capture-count rule they are
NOT struct-projected and emit NO shims:

- `VoidCoalescing = left:LogicalOr (QUESTION_QUESTION right:LogicalOr)*`
- `LogicalOr = left:LogicalAnd (LOGICAL_OR right:LogicalAnd)*`
- `LogicalAnd = left:Equality (LOGICAL_AND right:Equality)*`

Each group contains a single fixed operator token (`??`, `||`, `&&`) that is
structural (consumed, not stored) plus exactly one value-bearing element
(`right:X`). One value-bearing element is below the two-element threshold for
struct projection, so these groups keep their existing single promotion
(`ImmutableArray<XNode>`) and no `[Obsolete]` shim is generated. Verified
directly against the generated `Mist.Ast.g.cs`: `VoidCoalescingNode`,
`LogicalOrNode`, and `LogicalAndNode` each have zero `[Obsolete]` members.

Consequence: Phase-3 Tasks 23 (LogicalAnd), 24 (LogicalOr), and 25
(VoidCoalescing) have nothing to migrate and are effectively no-ops. They should
be verified-and-closed rather than worked.

## Generated Shim Inventory (16 members across 6 nodes)

For reference, the generator emits exactly 16 shim members total, matching the
six struct-projected rules:

| Node | Group modifier | Shim members |
|------|----------------|--------------|
| AdditiveNode | `*` | Operator, Right, Multiplicatives |
| MultiplicativeNode | `*` | Operator, Right, Ranges |
| EqualityNode | `*` | Operator, Right, Relationals |
| RelationalComparisonRelationalNode | `*` | Operator, Right, Additives |
| AssignmentNode | `?` | Operator, Value |
| AssociatedTypeSlotDeclarationNode | `?` | Constraints, SlotWhereConstraints |

Migration is complete (and shims are removable per Requirement 5.5) when a
`--no-incremental` build reports 0 CS0618 warnings.
