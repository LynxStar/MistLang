# Implementation Plan: Inline Rule Prefix

## Overview

This plan implements the `inline` keyword feature for the Storm parser generator. The feature allows grammar authors to declare shared-prefix alternative groups, enabling the emitter to parse the prefix once and predict which arm to take — eliminating redundant backtracking and false-positive STORM0006 warnings. The implementation proceeds bottom-up through the pipeline: lexer → parser → AST → validation → reachability → IR → emitters → grammar migration → TextMate.

## Tasks

- [ ] 1. Lexer extension — add `InlineKeyword` and `Comma` tokens
  - [ ] 1.1 Add `InlineKeyword` and `Comma` variants to `StormTokenKind` enum
    - In `Source/Storm.SourceGenerator/Frontend/StormLexer.cs`, add `InlineKeyword` to the keywords section and `Comma` to the operators/punctuation section of the `StormTokenKind` enum
    - Add `"inline" => StormTokenKind.InlineKeyword` case to `ClassifyKeyword`
    - Add `',' => (StormTokenKind?)StormTokenKind.Comma` case to `ClassifyOperator`
    - _Requirements: 1.1, 1.2, 1.3, 1.4, 11.1, 11.2, 11.3_

  - [ ]* 1.2 Write unit tests for lexer keyword and comma classification
    - Test that standalone `inline` produces `InlineKeyword`
    - Test that `inlineFoo`, `myinline` produce `Identifier`
    - Test that `,` produces `Comma` with correct offset/line/column
    - Test that `inline` inside string literals or regex literals is not classified as keyword
    - **Property 1: Keyword Classification — inline Produces InlineKeyword**
    - **Property 2: Non-Standalone inline Remains Identifier**
    - **Property 3: Comma Classification**
    - **Validates: Requirements 1.1, 1.2, 1.3, 1.4, 11.1, 11.2, 11.3**

- [ ] 2. AST node types — define `StormInlineBlockExpr` and `StormInlineArm`
  - [ ] 2.1 Add `StormInlineBlockExpr` and `StormInlineArm` to `StormSyntax.cs`
    - In `Source/Storm.SourceGenerator/Frontend/StormSyntax.cs`, add `StormInlineBlockExpr` sealed record extending `StormExpression` with `Prefix` (StormExpression), `Arms` (IReadOnlyList<StormInlineArm>), `Offset`, `Line`, `Column`
    - Add `StormInlineArm` sealed record (not extending StormExpression) with `Expression` (StormExpression), `AlternativeName` (string), `Label` (string?)
    - _Requirements: 3.1, 3.2, 3.3_

- [ ] 3. Parser extension — parse inline blocks within alternations
  - [ ] 3.1 Add `ParseInlineBlock()` method to `StormParser.cs`
    - In `Source/Storm.SourceGenerator/Frontend/StormParser.cs`, add `StormTokenKind.InlineKeyword` to the `IsExpressionStart()` check
    - Add a `case StormTokenKind.InlineKeyword:` in `ParsePrimary()` that calls `ParseInlineBlock()`
    - Implement `ParseInlineBlock()`: consume `inline`, expect identifier (prefix name), expect colon, expect left brace, parse comma-separated arms (with trailing comma support), expect right brace
    - Implement `ParseInlineArm()`: parse optional `label:`, parse inner expression (identifier or nested inline block), expect `as`, expect identifier (alt name)
    - Handle error cases: empty arm list, missing `as`, malformed arms — emit diagnostics and synchronize
    - _Requirements: 2.1, 2.2, 2.3, 2.4, 2.5, 2.6, 2.7, 2.8, 2.9_

  - [ ]* 3.2 Write unit tests for inline block parsing
    - Test valid inline block with 1 arm, 2 arms, trailing comma
    - Test labeled arms (`label:RuleRef as Name`)
    - Test nested inline blocks
    - Test error recovery: empty braces, missing `as`, missing identifier after `inline`
    - **Property 4: Inline Block Parsing Round-Trip**
    - **Property 5: Nested Inline Block Parsing**
    - **Validates: Requirements 2.1, 2.2, 2.3, 2.4, 2.5, 2.6, 2.7, 2.8, 2.9**

- [ ] 4. Checkpoint — Ensure all tests pass
  - Ensure all tests pass, ask the user if questions arise.

- [ ] 5. Nominal prefix verification — new validation pass
  - [ ] 5.1 Create `InlinePrefixVerification.cs` validation pass
    - Create `Source/Storm.SourceGenerator/Validate/InlinePrefixVerification.cs`
    - Implement `public static List<StormDiagnostic> Verify(StormGrammar grammar)` that walks all rule bodies finding `StormInlineBlockExpr` nodes
    - For each inline block: resolve prefix rule (must exist, must have single-sequence body — not alternation), resolve each arm's rule (must exist, must have single-sequence body)
    - Check that each arm's rule body begins with a `StormReferenceExpr` matching the prefix rule name (unwrap `StormLabeledExpr` if present)
    - Emit STORM0017 on mismatch; emit error if prefix rule is unresolved or has alternation body; emit error if arm rule has alternation body
    - Recursively verify nested inline blocks
    - _Requirements: 4.1, 4.2, 4.3, 4.4, 4.5, 4.6, 4.7, 9.1, 9.2, 9.3, 9.4, 9.5_

  - [ ] 5.2 Register STORM0017 diagnostic ID and wire verification into the generator pipeline
    - Add `STORM0017` constant to `StormDiagnosticIds`
    - Wire `InlinePrefixVerification.Verify()` into the source generator pipeline after grammar construction but before IR lowering — if errors are emitted, suppress all code generation
    - _Requirements: 9.4_

  - [ ]* 5.3 Write unit tests for nominal prefix verification
    - Test passing case: arm's rule starts with prefix rule reference
    - Test failing case: arm's rule starts with different reference → STORM0017
    - Test labeled prefix reference (unwrap label before checking)
    - Test unresolved prefix rule → error
    - Test prefix rule with alternation body → error
    - Test arm rule with alternation body → error
    - Test multiple failing arms → one diagnostic per arm
    - **Property 6: Nominal Prefix Verification Correctness**
    - **Validates: Requirements 4.1, 4.2, 4.3, 4.4, 4.5, 4.6, 9.1, 9.2, 9.3, 9.5**

- [ ] 6. Reachability pass integration
  - [ ] 6.1 Extend `ReachabilityPass` to handle `StormInlineBlockExpr`
    - In `Source/Storm.SourceGenerator/Validate/ReachabilityPass.cs`, add a `case StormInlineBlockExpr` branch in `ComputePrefixes()` and `ComputeFirst()` that returns the FIRST-k set of the prefix rule (opaque treatment for sibling analysis)
    - Add a new method to perform intra-block analysis: for each arm, compute FIRST-k of the tail (arm's rule body minus the first element), check subset relationship against union of earlier arms' tail FIRST-k sets, emit STORM0006 if unreachable
    - Call intra-block analysis from `CheckRule()` when an inline block is encountered in the top-level alternatives
    - Handle nested inline blocks recursively
    - _Requirements: 5.1, 5.2, 5.3, 5.4, 5.5_

  - [ ]* 6.2 Write unit tests for reachability pass with inline blocks
    - Test that inline block is treated as opaque (no false STORM0006 against siblings)
    - Test that tail divergence between arms with distinct FIRST tokens emits no warning
    - Test that a later arm with subset tail FIRST-k emits STORM0006
    - Test FIRST-k of inline block as whole equals prefix FIRST-k
    - **Property 7: Reachability — Inline Block Opaque for Sibling Analysis**
    - **Property 8: Reachability — Intra-Block Tail Divergence**
    - **Property 9: Reachability — Block FIRST-k Equals Prefix FIRST-k**
    - **Validates: Requirements 5.1, 5.2, 5.3, 5.4, 5.5**

- [ ] 7. Checkpoint — Ensure all tests pass
  - Ensure all tests pass, ask the user if questions arise.

- [ ] 8. IR lowering — produce `InlineBlockIR` and `InlineArmIR`
  - [ ] 8.1 Define `InlineBlockIR` and `InlineArmIR` types
    - In `Source/Storm.SourceGenerator/IR/ExpressionIR.cs`, add `InlineBlockIR` sealed class extending `ExpressionIR` with `PrefixElements` (IReadOnlyList<ExpressionIR>), `PrefixLabels` (IReadOnlyList<FieldLabelIR>), `Arms` (IReadOnlyList<InlineArmIR>)
    - Add `InlineArmIR` sealed class with `AlternativeName` (string), `Label` (string?), `TargetRule` (RuleIR), `TailExpression` (ExpressionIR), `TailLabels` (IReadOnlyList<FieldLabelIR>)
    - _Requirements: 6.1, 6.2_

  - [ ] 8.2 Extend `GrammarIRLowering` to lower `StormInlineBlockExpr`
    - In `Source/Storm.SourceGenerator/IR/GrammarIRLowering.cs`, add a `case StormInlineBlockExpr` in `LowerRuleExpression()` and `LowerSingleAlternative()`
    - Resolve the prefix rule → get its body sequence elements and labels, lower them
    - For each arm: resolve target rule, get its body sequence, strip first element (prefix ref, unwrap label), lower tail elements with labels
    - Produce `InlineBlockIR` node wrapped in a `RuleAlternativeIR` with null alt name
    - Handle nested inline blocks recursively
    - Also add `case StormInlineBlockExpr` in `CollectAnonymousStringLiterals()` to walk into inline block prefix and arm rule bodies
    - _Requirements: 6.1, 6.2, 6.3, 6.4, 6.5, 6.6_

  - [ ]* 8.3 Write unit tests for IR lowering of inline blocks
    - Test that prefix elements are correctly extracted
    - Test that arm tail expressions have the prefix stripped
    - Test empty tail case (arm is pure prefix)
    - Test label preservation on prefix and tail
    - **Property 10: IR Lowering — Prefix Stripping by First-Element Removal**
    - **Validates: Requirements 6.1, 6.2, 6.3, 6.5**

- [ ] 9. Parser emitter — emit parse-once-then-predict code
  - [ ] 9.1 Extend `ParserEmitter` to handle `InlineBlockIR`
    - In `Source/Storm.SourceGenerator/Emit/ParserEmitter.cs`, add a method `EmitInlineBlock()` that handles the case where a `RuleAlternativeIR.Expression` is an `InlineBlockIR`
    - Emit code: save position, parse prefix elements into local variables, on prefix failure restore position
    - After successful prefix parse: emit prediction code that peeks at next token(s) to determine arm
    - Build FIRST set dispatch (switch/if-else) from each arm's tail expression; arms sharing a FIRST token are tried in declaration order with backtracking
    - On successful arm tail parse: construct flat node with prefix locals + tail locals
    - On no arm match: restore position to before prefix
    - Handle nested inline blocks by recursive call to `EmitInlineBlock()`
    - Wire the inline block emission into `EmitMultiAlt()` and `EmitAltBodyWithCheck()` where `alt.Expression is InlineBlockIR`
    - _Requirements: 7.1, 7.2, 7.3, 7.4, 7.5, 7.6, 7.7, 7.8, 7.9, 7.10_

  - [ ]* 9.2 Write unit tests for parser emitter inline block code generation
    - Test generated code structure: save/restore pattern present, prefix parsed once, prediction dispatch present
    - Test flat node construction with prefix + tail fields
    - **Validates: Requirements 7.1, 7.2, 7.3, 7.4, 7.5, 7.10**

- [ ] 10. AST emitter — emit flat node classes with hoisted prefix fields
  - [ ] 10.1 Extend `AstEmitter` to handle `InlineBlockIR`
    - In `Source/Storm.SourceGenerator/Emit/AstEmitter.cs`, add handling for `InlineBlockIR` within a rule's alternatives
    - For each arm: emit a sealed node class named `{ArmAltName}{ParentRuleName}Node` extending the parent rule's base node
    - Include properties for prefix fields (hoisted, in sequence order) followed by tail fields
    - Do NOT generate a wrapper node type for the inline block itself
    - Respect arm labels for property naming on tail expressions
    - _Requirements: 8.1, 8.2, 8.3, 8.4, 8.5_

  - [ ]* 10.2 Write unit tests for AST emitter inline block handling
    - Test correct class naming per arm
    - Test property ordering: prefix fields then tail fields
    - Test no wrapper class generated
    - Test label-based property naming
    - **Property 11: AST Emission — Correct Node Class Per Arm**
    - **Property 12: AST Emission — Property Ordering Matches Prefix+Tail**
    - **Validates: Requirements 8.1, 8.2, 8.3, 8.4, 8.5**

- [ ] 11. Checkpoint — Ensure all tests pass
  - Ensure all tests pass, ask the user if questions arise.

- [ ] 12. Grammar migration — refactor `MemberDeclaration` to use inline block
  - [ ] 12.1 Add `ModifiedTypeHead` rule and refactor arm rules in `Mist.storm`
    - In `Source/Mist/Grammar/Mist.storm`, add a new rule: `rule ModifiedTypeHead = modifiers:Modifier+ TYPE name:IDENTIFIER typeParams:TypeParameterList?;`
    - Refactor `MemberTypeAliasDeclaration` to begin with `head:ModifiedTypeHead` instead of repeating `modifiers:Modifier+ TYPE name:IDENTIFIER typeParams:TypeParameterList?`
    - Refactor `ModifiedAssociatedTypeSlotDeclaration` to begin with `head:ModifiedTypeHead` instead of repeating the same prefix
    - In the `MemberDeclaration` rule, replace the `memberTypeAlias` and `modifiedAssociatedType` alternatives with an inline block: `inline ModifiedTypeHead: { memberTypeAlias:MemberTypeAliasDeclaration as MemberTypeAliasMember, modifiedAssociatedType:ModifiedAssociatedTypeSlotDeclaration as ModifiedAssociatedTypeMember, }`
    - _Requirements: 13.1, 13.2, 13.3, 13.5_

  - [ ] 12.2 Verify zero STORM0006 warnings and AST compatibility
    - Build the solution (`dotnet build Mist.slnx`) and verify zero new STORM0006 warnings for `MemberDeclaration`
    - Verify that the generated AST node types `MemberTypeAliasMemberMemberDeclarationNode` and `ModifiedAssociatedTypeMemberMemberDeclarationNode` have identical class names, property names, property types, and property order as before the migration
    - Verify no downstream compiler code changes are needed (semantic analysis, type checking, code emission all use the same AST contract)
    - _Requirements: 13.1, 13.2, 13.4, 13.5_

- [ ] 13. TextMate grammar update — highlight `inline` keyword
  - [ ] 13.1 Add `inline` keyword pattern to Storm TextMate grammar
    - In `Editor/storm-syntax-highlighting/syntaxes/storm.tmLanguage.json`, add `inline` to the `storage-types` pattern: update the match from `\\b(token|hidden|fragment|rule|mode)\\b` to `\\b(token|hidden|fragment|rule|mode|inline)\\b`
    - This assigns `inline` the `storage.type.storm` scope, consistent with other declaration/modifier keywords
    - Verify that `inline` before a rule reference gets the keyword scope and the adjacent identifier does not
    - _Requirements: 14.1, 14.2, 14.3_

- [ ] 14. Non-regression verification and final test suite run
  - [ ] 14.1 Verify byte-for-byte output identity for non-inline grammars
    - Confirm that grammars not using `inline` produce byte-for-byte identical generated parser and AST output compared to the pre-feature baseline
    - Run `dotnet build Mist.slnx` and verify zero new CS* or STORM* warnings
    - Run `dotnet format --verify-no-changes` against modified projects to ensure no formatting violations
    - _Requirements: 10.1, 10.2, 10.3, 12.1, 12.2, 12.3, 12.4_

  - [ ] 14.2 Run full test suite to prove zero regression
    - Run `dotnet test` on the entire solution
    - Verify passing-test count is no lower than pre-feature baseline with zero tests transitioning from passing to failing
    - _Requirements: 10.2, 13.4_

## Notes

- Tasks marked with `*` are optional and can be skipped for faster MVP
- Each task references specific requirements for traceability
- Checkpoints ensure incremental validation
- Property tests validate universal correctness properties from the design document
- Unit tests validate specific examples and edge cases
- The implementation language is C# (matching the existing Storm source generator codebase)
- The solution builds with `dotnet build Mist.slnx` and tests run with `dotnet test`

## Task Dependency Graph

```json
{
  "waves": [
    { "id": 0, "tasks": ["1.1", "2.1"] },
    { "id": 1, "tasks": ["1.2", "3.1"] },
    { "id": 2, "tasks": ["3.2", "5.1"] },
    { "id": 3, "tasks": ["5.2", "5.3", "6.1"] },
    { "id": 4, "tasks": ["6.2", "8.1"] },
    { "id": 5, "tasks": ["8.2"] },
    { "id": 6, "tasks": ["8.3", "9.1"] },
    { "id": 7, "tasks": ["9.2", "10.1"] },
    { "id": 8, "tasks": ["10.2"] },
    { "id": 9, "tasks": ["12.1"] },
    { "id": 10, "tasks": ["12.2", "13.1"] },
    { "id": 11, "tasks": ["14.1"] },
    { "id": 12, "tasks": ["14.2"] }
  ]
}
```
