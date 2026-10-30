# Implementation Plan: As Operator

## Overview

Add the `as` operator to the Mist programming language. The `as` operator provides safe type conversion: provable conversions (widening, identity) yield bare `T`, while non-provable conversions (narrowing, union extraction, unknown narrowing) yield `T?` (i.e., `T | void`). Impossible conversions are compile-time errors. The implementation proceeds bottom-up through the compiler pipeline: grammar first, then semantic model resolution, type checker classification, C# emission, syntax highlighting, documentation, and sample application.

## Tasks

- [x] 1. Add `AsExpression` grammar rule
  - [x] 1.1 Add `AsExpression` variant to the `Relational` rule in `Mist.storm`
    - Add `| left:Additive AS targetType:TypeReference as AsExpression` to the `Relational` rule
    - Place it after `IsExpression` and before `RelationalComparison` (the PEG parser tries alternatives in order; since `IS` and `AS` are distinct tokens, ordering between them doesn't affect correctness but keeps them logically grouped)
    - No changes to `keywords.storm` — the `AS` token already exists
    - _Requirements: 1.1, 1.2, 1.3, 1.4, 9.1, 9.2, 9.3, 9.4_

  - [x] 1.2 Verify the project builds with the new grammar
    - Run `dotnet build` to confirm the Storm source generator produces `AsExpressionRelationalNode` with `Left` and `TargetType` fields
    - _Requirements: 1.3_

- [x] 2. Add pretty-printer support for `as` expressions
  - [x] 2.1 Add `AsExpressionRelationalNode` formatting to `MistPrettyPrinter`
    - In the method that handles Relational nodes, add a case for `AsExpressionRelationalNode`
    - Format as `{left} as {targetType}` with spaces around `as`, consistent with `is` formatting
    - _Requirements: 1.3, 9.1_

  - [x] 2.2 Write round-trip test for `as` expressions
    - **Property 1: As expression round-trip**
    - Verify that parsing → pretty-printing → parsing → pretty-printing produces identical output for `as` expressions with varying operand and target type combinations
    - _Requirements: 1.1, 1.2, 1.3_

- [x] 3. Implement Pass 2 resolution for `as` expressions
  - [x] 3.1 Resolve `as` expressions in SemanticModelBuilder Pass 2
    - Add `AsExpressionRelationalNode` case to the method that resolves Relational expressions
    - Resolve the target type reference to an `IType`
    - Resolve the left operand expression to determine its Compile_Time_Type
    - Record the resolved target type on the node (set it as a preliminary type; the type checker will override with the final result type — `T` or `T | void`)
    - _Requirements: 5.1, 5.2_

- [x] 4. Implement `AsConversionKind` metadata infrastructure
  - [x] 4.1 Add `AsConversionKind` enum and semantic model storage
    - Define `AsConversionKind` enum: `Provable`, `NonProvable`
    - Add `RecordAsConversionKind(SyntaxNode, AsConversionKind)` and `GetAsConversionKind(SyntaxNode)` methods to the semantic model interface and implementation
    - Add a `Dictionary<SyntaxNode, AsConversionKind>` backing store
    - _Requirements: 5.4_

- [x] 5. Implement type checker `as` expression classification
  - [x] 5.1 Implement `CheckAsExpression` in TypeCheckerAnalyzer
    - Add `AsExpressionRelationalNode` case to the method that checks Relational nodes
    - Implement `CheckAsExpression`:
      - Get source type from left operand, target type from resolved type reference
      - Reject void source (emit `MIST_TC_0072 (historical)`)
      - Reject void target (emit `MIST_TC_0071 (historical)`)
      - Call `ClassifyAsConversion` to determine provable/non-provable/invalid
      - For Provable: set expression type to `T`, record `AsConversionKind.Provable`
      - For NonProvable: set expression type to `T | void` (construct union type), record `AsConversionKind.NonProvable`
      - For Invalid: emit `MIST_TC_0070 (historical)`
    - _Requirements: 2.1, 2.2, 2.3, 2.4, 2.5, 3.1, 3.2, 3.3, 3.4, 3.5, 4.1, 4.2, 4.3, 4.4, 5.3, 5.4, 8.1, 8.2, 8.3, 8.4_

  - [x] 5.2 Implement `ClassifyAsConversion` helper
    - Provable if `IsDirectlyAssignable(source, target, model)` (identity, subtype, interface, trait, constituent→union)
    - NonProvable if `IsDirectlyAssignable(target, source, model)` (narrowing: base→derived)
    - NonProvable if source is `UnknownType`
    - NonProvable if source is `UnionType` and target is a constituent or related to a constituent
    - NonProvable if target is interface/trait and source is a class/struct (might implement it at runtime)
    - NonProvable if source and target share a class hierarchy (sibling types)
    - Invalid otherwise
    - _Requirements: 2.1, 2.2, 2.3, 2.4, 2.5, 3.1, 3.2, 3.3, 3.4, 3.5, 4.1, 7.5_

  - [x] 5.3 Write tests for `as` expression type classification
    - Test provable cases: identity, class widening, struct widening, interface widening, constituent→union
    - Test non-provable cases: class narrowing, struct narrowing, union→constituent, unknown→concrete, class→interface (unproven)
    - Test invalid cases: unrelated types (int→string, Dog→Cat with no common hierarchy), void source, void target
    - Verify result types: `T` for provable, `T | void` for non-provable, error for invalid
    - **Validates: Properties 2, 3, 4**
    - _Requirements: 2.1–2.5, 3.1–3.5, 4.1–4.4_

  - [x] 5.4 Write test for `as` operator not triggering user-defined conversions
    - Define a type with `implicit operator T(...)` and verify that `as T` does NOT use it (classified as Invalid when no hierarchy relationship exists)
    - **Validates: Property 7**
    - _Requirements: 7.5_

- [x] 6. Checkpoint — Ensure all tests pass
  - Run `dotnet test` and verify all existing and new tests pass.

- [x] 7. Implement C# emission for `as` expressions
  - [x] 7.1 Implement `EmitAsExpression` in CSharpEmitter
    - Add `AsExpressionRelationalNode` case to the method that emits Relational expressions
    - Dispatch based on `GetAsConversionKind`:
      - **Provable + identity**: emit operand unchanged
      - **Provable + widening (struct)**: emit `((BaseType)operand)` (triggers the implicit conversion operator)
      - **Provable + widening (class/interface)**: emit `((TargetType)operand)`
      - **NonProvable + reference type target**: emit `(operand as TargetType)`
      - **NonProvable + value type target from unknown**: emit `(operand is TargetType __t ? __t : default(TargetType?))`
      - **NonProvable + union source**: emit union extraction using existing union emission infrastructure
    - _Requirements: 6.1, 6.2, 6.3, 6.4, 6.5, 6.6, 6.7_
    - Standard-pipeline sized-target correction (2026-10-03): a checked target whose C# spelling drops a concrete length, `int[3]` or a tuple holding one, is emitted as a pattern rather than C#'s `as`: `(x is int[] { Length: 3 } __asN ? __asN : default(int[]))`, and a tuple target names the ValueTuple (`System.ValueTuple<int[], string> { Item1: { Length: 3 } }`) instead of the positional pattern over `object` it used to write, which also repairs an unsized `as (int, string)`. A mismatch answers void, the operand is evaluated once, and unsized and nominal targets keep `(x as T)`. The relation is unchanged: `int[] as int[3]` stays refused. See emitter orders 103 and 139. _Requirements: 3.3, 6.4, 6.5, 6.7._

  - [x] 7.2 Write emission tests for `as` expressions
    - Verify provable conversions emit no runtime checks (Property 5)
    - Verify non-provable conversions emit safe runtime checks (Property 6)
    - Verify emitted code compiles and produces correct results for each conversion category
    - _Requirements: 6.1–6.7_

- [x] 8. Checkpoint — Ensure all tests pass
  - Run `dotnet test` and verify all existing and new tests pass.

- [x] 9. Update syntax highlighting and documentation
  - [x] 9.1 Verify/update TextMate grammar for `as` in expression context
    - Verify that `as` is highlighted as `keyword.operator.mist` (or `keyword.operator.type.mist`) in expression contexts
    - If needed, add or adjust pattern to cover `as` followed by a type reference
    - _Requirements: 10.1, 10.2_

  - [x] 9.2 Update `CSHARP_FEATURES.md`
    - Add `as operator (safe type conversion)` entry and mark it ✅
    - Update the Summary table counts
    - _Requirements: 13.1, 13.2_

- [x] 10. Create sample application
  - [x] 10.1 Create `samples/AsOperator/Program.mist` with proof code
    - Use the exact proof code from Requirement 11 verbatim
    - Create `samples/AsOperator/mist.toml` with appropriate project configuration
    - _Requirements: 11.1, 11.2, 11.3, 11.5_

  - [x] 10.2 Integrate into AllFeatures orchestrator
    - Add `AsOperator.Program.RunDemo()` call to `samples/AllFeatures/Program.mist`
    - Add project reference to `samples/AllFeatures/mist.toml`
    - _Requirements: 11.4_

  - [x] 10.3 Verify sample compiles and produces expected output
    - Build and run the sample
    - Verify output matches the expected output from Requirement 11 exactly
    - _Requirements: 11.5_

- [x] 11. Book documentation
  - [x] 11.1 Add `as` operator section to the book
    - Create or update the appropriate chapter (likely Type System or Expressions)
    - Cover: identity, widening, narrowing, union narrowing, unknown narrowing, composition with `??` and `?.`, difference from explicit casts
    - Add compiled snippets to `book/snippets/`
    - Update `book/src/SUMMARY.md`
    - _Requirements: 12.1, 12.2, 12.3, 12.4_

- [x] 12. Final checkpoint — Ensure all tests pass
  - Run `dotnet test` and verify all existing and new tests pass. Build all samples and verify no regressions.

## Notes

- The `AS` keyword token already exists in `keywords.storm` — no lexer changes are needed. It's currently used in trait with-block member mappings (`sourceName as targetName`). The grammar contexts are completely disjoint (Relational rule vs MemberMapping rule), so there's no ambiguity.
- The `as` operator does NOT interact with user-defined implicit/explicit cast operators. It operates purely on type hierarchy relationships, union membership, and unknown narrowing. This keeps semantics simple and predictable.
- The key implementation challenge is in the C# emission for non-provable conversions, especially for union types. The emitter must produce code that constructs the `T | void` union representation correctly. Examining how existing `is` patterns emit narrowing logic for unions will inform this implementation.
- The `IsDirectlyAssignable` method (already exists in the type checker) is the foundation for provability classification. It checks identity, subtype, interface, and trait relationships WITHOUT falling through to implicit cast operators.
- The `AsConversionKind` metadata is a lightweight addition to the semantic model — just a dictionary keyed by syntax node, similar to how `RecordResolvedOperator` works.
- For struct widening, the existing implicit conversion operators (generated by struct inheritance) handle the actual conversion. The emitter just needs to emit a cast expression that triggers the C# compiler's implicit operator resolution.

## Task Dependency Graph

```json
{
  "waves": [
    { "id": 0, "tasks": ["1.1"] },
    { "id": 1, "tasks": ["1.2", "2.1"] },
    { "id": 2, "tasks": ["2.2", "3.1"] },
    { "id": 3, "tasks": ["4.1"] },
    { "id": 4, "tasks": ["5.1", "5.2"] },
    { "id": 5, "tasks": ["5.3", "5.4"] },
    { "id": 6, "tasks": ["7.1"] },
    { "id": 7, "tasks": ["7.2"] },
    { "id": 8, "tasks": ["9.1", "9.2"] },
    { "id": 9, "tasks": ["10.1", "10.2"] },
    { "id": 10, "tasks": ["10.3", "11.1"] }
  ]
}
```
