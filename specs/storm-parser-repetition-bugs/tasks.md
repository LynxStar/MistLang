# Tasks

## Task 1: Fix AstEmitter — Handle SequenceIR in list and optional property generation

- [x] 1.1 Add `SequenceIR` case to `GetPropertyInfoForList` in `AstEmitter.cs` that extracts the first `RuleRefIR` from the sequence elements and generates an `ImmutableArray<TNode>` property, or falls back to `ImmutableArray<Token>` for token-only sequences
- [x] 1.2 Add `SequenceIR` case to `GetPropertyInfoForListInner` in `AstEmitter.cs` with the same logic using the provided `propName`
- [x] 1.3 Add `SequenceIR` case to `GetPropertyInfoForOptionalInner` in `AstEmitter.cs` that extracts the first `RuleRefIR` and generates a nullable node property (`TNode?`), falling back to `Token?` for token-only sequences
- [x] 1.4 Add `SequenceIR` case to `GetPropertyInfoForOptional` in `AstEmitter.cs` (unlabeled variant) that extracts the significant element and generates the appropriate nullable property
- [x] 1.5 Verify the fix compiles and existing `EmitterTests` pass

## Task 2: Fix ParserEmitter — Accumulate repeated rule-ref results in ZeroOrMoreIR handler

- [x] 2.1 Modify the `ZeroOrMoreIR` case in `EmitFullExprParse` to detect when the inner `SequenceIR` contains a `RuleRefIR`, create an `ImmutableArray` builder before the while-loop, accumulate parsed rule-ref nodes inside the loop via `.Add()`, and finalize with `.ToImmutable()` after the loop, adding the result to `assignedProps`
- [x] 2.2 Ensure the `EmitFullExprParseAssignOnly` `RuleRefIR` case inside the loop body assigns the parsed result to a local variable that can be added to the builder (instead of discarding it)
- [x] 2.3 Verify the fix compiles and existing `ParserEmitterPropertyTests` pass

## Task 3: Fix ParserEmitter — Handle RuleRefIR in EmitAtomicSequenceBody for optional sequences

- [x] 3.1 Add `RuleRefIR` handling to `EmitAtomicSequenceBody` that emits `_Parse{Rule}()` calls for rule-ref elements in the sequence
- [x] 3.2 When the rule-ref element has a label in the `labelMap`, assign the parsed result to the pre-declared labeled variable (e.g., `_Initializer = _ParseExpression();`)
- [x] 3.3 When the parse returns null, set the sequence variable to null (atomic failure — the optional sequence didn't fully match)
- [x] 3.4 Verify the fix compiles and existing tests pass

## Task 4: Write unit tests for Bug 1 — Repeated elements in (COMMA X)* patterns

- [x] 4.1 Write a test that creates a grammar with `rule PList = Param (COMMA Param)*;` and verifies `AstEmitter.EmitAstNodes` output contains an `ImmutableArray<ParamNode>` property
- [x] 4.2 Write a test that creates a grammar with `rule QId = IDENT (DOT IDENT)*;` and verifies the AST output contains an `ImmutableArray<Token>` property for repeated identifiers
- [x] 4.3 Write a test that creates a grammar with `rule PList = Param (COMMA Param)*;` and verifies `ParserEmitter.Emit` output contains list builder creation, `.Add()` calls, and `.ToImmutable()`
- [x] 4.4 Write a test that creates a grammar with `rule ArgList = Expr (COMMA Expr)*;` and verifies both AST and parser output handle repeated expressions correctly

## Task 5: Write unit tests for Bug 2 — Optional sequences like (EQUAL Expression)?

- [x] 5.1 Write a test that creates a grammar with `rule Field = init:(EQ Expr)? SEMI;` and verifies `AstEmitter.EmitAstNodes` output contains an `ExprNode?` property (not `Token?`)
- [x] 5.2 Write a test that creates a grammar with `rule Field = init:(EQ Expr)? SEMI;` and verifies `ParserEmitter.Emit` output contains a `_ParseExpr()` call inside the optional sequence handling
- [x] 5.3 Write a test for the unlabeled case: grammar with `rule Field = (EQ Expr)? SEMI;` and verify the AST emitter generates an appropriate nullable property

## Task 6: Write preservation tests

- [x] 6.1 Write a test verifying `rule Start = Expr*;` (simple `ZeroOrMoreIR(RuleRefIR)`) continues to generate `ImmutableArray<ExprNode>` — output unchanged from before fix
- [x] 6.2 Write a test verifying `rule Expr = Number Ident?;` (simple `OptionalIR(TokenRefIR)`) continues to generate `Token?` — output unchanged
- [x] 6.3 Write a test verifying `rule Start = chain:Chain?;` (simple `OptionalIR(RuleRefIR)`) continues to generate `ChainNode?` — output unchanged
- [x] 6.4 Write a test verifying `rule Expr = Number as Lit | Ident as Var;` (named alternatives) generates identical abstract base + sealed subtypes — output unchanged
- [x] 6.5 Write a test verifying a rule with no repetitions or optionals generates identical output before and after fix

## Task 7: Integration test with Mist grammar

- [x] 7.1 Write an integration test that parses the full Mist grammar through the source generator pipeline and verifies `ParameterListNode` AST output contains an `ImmutableArray<ParameterNode>` property
- [x] 7.2 Verify `ArgumentListNode` AST output contains an `ImmutableArray<ExpressionNode>` property
- [x] 7.3 Verify `QualifiedIdentifierNode` AST output contains an `ImmutableArray<Token>` property for repeated identifiers
- [x] 7.4 Verify `FieldDeclarationNode` AST output preserves the `ExpressionNode` from the optional initializer group
- [x] 7.5 Run the full test suite (`dotnet test`) and verify all existing tests pass with no regressions
