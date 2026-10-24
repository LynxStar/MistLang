# Storm Parser Repetition Bugs — Bugfix Design

## Overview

Two related bugs in the Storm source generator cause generated AST nodes to lose data from grammar patterns involving repetitions (`(COMMA X)*`) and optional sequences (`(EQUAL Expression)?`). Bug 1: `AstEmitter.GetPropertyInfoForList` returns `null` when a `ZeroOrMoreIR` wraps a `SequenceIR` (e.g., `(COMMA Parameter)*`), so no list property is generated for repeated elements; additionally, `ParserEmitter`'s `ZeroOrMoreIR` handler in `EmitFullExprParse` parses the repeated tokens but never accumulates the rule-ref results into a list. Bug 2: `AstEmitter.GetPropertyInfoForOptionalInner` falls through to its default `Token?` case when an `OptionalIR` wraps a `SequenceIR` (e.g., `(EQUAL Expression)?`), discarding the `ExpressionNode`; the parser emitter's `EmitAtomicSequenceBody` only captures tokens, not rule-ref nodes. The fix targets four methods across two files with minimal, surgical changes.

## Glossary

- **Bug_Condition (C)**: The set of grammar patterns that trigger data loss — `ZeroOrMoreIR` or `OneOrMoreIR` wrapping a `SequenceIR`, and `OptionalIR` wrapping a `SequenceIR` with a labeled field
- **Property (P)**: The desired behavior — all repeated rule-ref nodes accumulated into a list property, and all elements of optional sequences preserved in accessible properties
- **Preservation**: Existing behavior for simple `ZeroOrMoreIR(RuleRefIR)`, simple `OptionalIR(TokenRefIR)`, simple `OptionalIR(RuleRefIR)`, and all non-repetition/non-optional patterns that must remain unchanged
- **AstEmitter**: The class in `Source/Storm.SourceGenerator/Emit/AstEmitter.cs` that generates C# AST node classes (properties, `Children()` overrides) from the grammar IR
- **ParserEmitter**: The class in `Source/Storm.SourceGenerator/Emit/ParserEmitter.cs` that generates a recursive-descent parser from the grammar IR
- **GetPropertyInfoForList**: Method in `AstEmitter` that determines the property type for `ZeroOrMoreIR`/`OneOrMoreIR` inner expressions — currently only handles `TokenRefIR` and `RuleRefIR`, returns `null` for `SequenceIR`
- **GetPropertyInfoForOptionalInner**: Method in `AstEmitter` that determines the property type for labeled `OptionalIR` inner expressions — currently falls through to `Token?` for `SequenceIR`
- **EmitFullExprParse**: Method in `ParserEmitter` that generates parsing code for each expression type — its `ZeroOrMoreIR` handler pre-declares labeled variables but does not accumulate repeated rule-ref results
- **EmitAtomicSequenceBody**: Method in `ParserEmitter` that generates parsing code for optional sequences — only captures tokens, not rule-ref nodes
- **SequenceIR**: IR node representing a sequence of sub-expressions (e.g., `COMMA, Parameter`)
- **ZeroOrMoreIR**: IR node representing `E*` repetition
- **OptionalIR**: IR node representing `E?` optional

## Bug Details

### Bug Condition

The bugs manifest when the Storm source generator processes grammar rules containing repetition or optional patterns whose inner expression is a `SequenceIR`. The AST emitter generates incomplete node types (missing list properties or wrong property types), and the parser emitter generates code that parses tokens correctly but discards the parsed rule-ref results.

**Formal Specification:**
```
FUNCTION isBugCondition(input)
  INPUT: input of type GrammarIR
  OUTPUT: boolean

  // Bug 1: ZeroOrMoreIR/OneOrMoreIR wrapping a SequenceIR that contains a RuleRefIR
  FOR EACH rule IN input.Rules
    FOR EACH alt IN rule.Alternatives
      IF alt.Expression contains a ZeroOrMoreIR or OneOrMoreIR node Z
         AND Z.Inner is SequenceIR
         AND Z.Inner.Elements contains at least one RuleRefIR
      THEN RETURN true

  // Bug 2: Labeled OptionalIR wrapping a SequenceIR that contains a RuleRefIR
  FOR EACH rule IN input.Rules
    FOR EACH alt IN rule.Alternatives
      FOR EACH label IN alt.FieldLabels
        IF label.Expression is OptionalIR O
           AND O.Inner is SequenceIR
           AND O.Inner.Elements contains at least one RuleRefIR
        THEN RETURN true

  RETURN false
END FUNCTION
```

### Examples

- **Bug 1 — ParameterList**: `rule ParameterList = Parameter (COMMA Parameter)*;` — the `(COMMA Parameter)*` is a `ZeroOrMoreIR(SequenceIR([TokenRefIR(COMMA), RuleRefIR(Parameter)]))`. `GetPropertyInfoForList` returns `null` for the `SequenceIR` inner, so no `ImmutableArray<ParameterNode>` property is generated. The parser parses repeated parameters but discards them. Result: `ParameterListNode` only has the first `Parameter`.
- **Bug 1 — ArgumentList**: `rule ArgumentList = Expression (COMMA Expression)*;` — same pattern. `ArgumentListNode` only stores the first `Expression`, losing all subsequent arguments.
- **Bug 1 — QualifiedIdentifier**: `rule QualifiedIdentifier = IDENTIFIER (DOT IDENTIFIER)*;` — the `(DOT IDENTIFIER)*` is a `ZeroOrMoreIR(SequenceIR([TokenRefIR(DOT), TokenRefIR(IDENTIFIER)]))`. `GetPropertyInfoForList` returns `null`. Result: `QualifiedIdentifierNode` only stores the first `IDENTIFIER`, so `System.Console` becomes just `System`.
- **Bug 2 — FieldDeclaration initializer**: `rule FieldDeclaration = ... initializer:(EQUAL Expression)? SEMICOLON;` — the labeled optional `initializer:(EQUAL Expression)?` is an `OptionalIR(SequenceIR([TokenRefIR(EQUAL), RuleRefIR(Expression)]))`. `GetPropertyInfoForOptionalInner` falls through to `Token?` for the `SequenceIR`, generating only a `Token?` property that captures the EQUAL token. The `ExpressionNode` is discarded. Result: `FieldDeclarationNode.Initializer` is `Token?` instead of preserving both the token and the expression.
- **Working case — simple repetition**: `rule Start = Expr*;` — `ZeroOrMoreIR(RuleRefIR(Expr))` correctly generates `ImmutableArray<ExprNode>` because `GetPropertyInfoForList` handles `RuleRefIR` directly.
- **Working case — simple optional**: `rule Expr = Number Ident?;` — `OptionalIR(TokenRefIR(Ident))` correctly generates `Token?` because `GetPropertyInfoForOptional` handles `TokenRefIR` directly.

## Expected Behavior

### Preservation Requirements

**Unchanged Behaviors:**
- Simple `ZeroOrMoreIR(RuleRefIR)` patterns (e.g., `members:MemberDeclaration*`) must continue to generate `ImmutableArray<T>` list properties and populate them correctly
- Simple `ZeroOrMoreIR(TokenRefIR)` patterns must continue to generate `ImmutableArray<Token>` list properties
- Simple `OptionalIR(TokenRefIR)` patterns (e.g., `arrayRank:(LEFT_BRACKET RIGHT_BRACKET)?`) must continue to generate `Token?` properties
- Simple `OptionalIR(RuleRefIR)` patterns (e.g., `chain:ConstructorChain?`) must continue to generate nullable node properties and yield them from `Children()`
- Rules with no repetitions or optional groups must continue to generate identical AST node classes
- Rules with named alternatives must continue to generate abstract base + sealed subtypes with correct PEG ordered-choice backtracking
- Labeled fields on simple expressions (e.g., `name:IDENTIFIER`, `body:Block`) must continue to generate correctly-typed properties
- The parser's hidden-token attachment, error recovery, and diagnostic reporting must remain unchanged

**Scope:**
All grammar patterns that do NOT involve `ZeroOrMoreIR`/`OneOrMoreIR`/`OptionalIR` wrapping a `SequenceIR` should be completely unaffected by this fix. This includes:
- All token definitions and lexer emission
- All simple rule references and token references
- All alternation patterns
- All existing `Children()` implementations for non-affected node types

## Hypothesized Root Cause

Based on code analysis of `AstEmitter.cs` and `ParserEmitter.cs`:

1. **Bug 1 — AstEmitter.GetPropertyInfoForList returns null for SequenceIR**: The method (line ~310) has a `switch` on the inner expression with cases for `TokenRefIR` and `RuleRefIR` only. When the inner is a `SequenceIR` (as in `(COMMA Parameter)*`), it falls through to `default: return null`. This means no property is generated for the repeated elements. The fix needs to inspect the `SequenceIR`'s elements, find the `RuleRefIR` (or `TokenRefIR`) within, and generate the appropriate list property type.

2. **Bug 1 — AstEmitter.GetPropertyInfoForListInner falls through for SequenceIR**: Similarly, the labeled variant `GetPropertyInfoForListInner` (line ~325) has cases for `TokenRefIR` and `RuleRefIR` only. Its `default` case generates `ImmutableArray<Token>` which is wrong for sequences containing rule refs. It needs the same `SequenceIR` handling.

3. **Bug 1 — ParserEmitter.EmitFullExprParse ZeroOrMoreIR handler doesn't accumulate**: The `ZeroOrMoreIR` case in `EmitFullExprParse` (line ~530) pre-declares variables for labeled sub-expressions and runs a while-loop that parses the inner sequence. However, it calls `EmitFullExprParseAssignOnly` which, for unlabeled `RuleRefIR` elements inside the sequence, emits `_Parse{Rule}()` and discards the result (the `RuleRefIR` case in `EmitFullExprParseAssignOnly` just parses and discards). No list builder is created, and no `.Add()` calls are emitted. The fix needs to detect `RuleRefIR` elements within the `SequenceIR` inner, create an `ImmutableArray` builder before the loop, add parsed nodes each iteration, and produce the final immutable array after the loop.

4. **Bug 2 — AstEmitter.GetPropertyInfoForOptionalInner falls through for SequenceIR**: The method (line ~300) has cases for `TokenRefIR` and `RuleRefIR` only. Its `default` case returns `Token?`, which is wrong for sequences like `(EQUAL Expression)` that contain a `RuleRefIR`. The fix needs to inspect the `SequenceIR`'s elements and generate a property type that preserves the rule-ref node (likely the node type of the first `RuleRefIR` found).

5. **Bug 2 — ParserEmitter.EmitAtomicSequenceBody only captures tokens**: The method (line ~1070) iterates sequence elements and only handles `TokenRefIR` and `StringLiteralIR`. When it encounters a `RuleRefIR` element (like `Expression` in `(EQUAL Expression)`), it is silently skipped. The fix needs to handle `RuleRefIR` elements by emitting `_Parse{Rule}()` calls and assigning the result to the labeled variable.

6. **Bug 2 — AstEmitter.GetPropertyInfoForOptional returns null for SequenceIR**: The unlabeled variant (line ~285) has cases for `TokenRefIR` and `RuleRefIR` only, returning `null` for `SequenceIR`. This affects unlabeled optional sequences. The fix should extract the significant element from the sequence.

## Correctness Properties

Property 1: Bug Condition — Repeated Rule-Ref Elements Accumulated in List

_For any_ grammar rule containing a `ZeroOrMoreIR` or `OneOrMoreIR` whose inner expression is a `SequenceIR` containing at least one `RuleRefIR`, the fixed AST emitter SHALL generate an `ImmutableArray<TNode>` list property (where `TNode` is the referenced rule's node type), and the fixed parser emitter SHALL accumulate all parsed rule-ref nodes from each iteration into that list property.

**Validates: Requirements 2.1, 2.2, 2.6**

Property 2: Bug Condition — Repeated Token Elements Accumulated in List

_For any_ grammar rule containing a `ZeroOrMoreIR` or `OneOrMoreIR` whose inner expression is a `SequenceIR` containing only `TokenRefIR` elements (no `RuleRefIR`), the fixed AST emitter SHALL generate an `ImmutableArray<Token>` list property for the significant repeated tokens.

**Validates: Requirements 2.3**

Property 3: Bug Condition — Optional Sequence Preserves Rule-Ref Node

_For any_ grammar rule containing a labeled `OptionalIR` whose inner expression is a `SequenceIR` containing at least one `RuleRefIR`, the fixed AST emitter SHALL generate a nullable node property (e.g., `ExpressionNode?`) that preserves the rule-ref node, and the fixed parser emitter SHALL parse and assign the rule-ref result to the labeled variable.

**Validates: Requirements 2.4, 2.5**

Property 4: Preservation — Simple Repetitions Unchanged

_For any_ grammar rule containing a `ZeroOrMoreIR` or `OneOrMoreIR` whose inner expression is a simple `RuleRefIR` or `TokenRefIR` (not a `SequenceIR`), the fixed code SHALL produce the same AST node properties and parser code as the original code, preserving existing list property generation.

**Validates: Requirements 3.1**

Property 5: Preservation — Simple Optionals Unchanged

_For any_ grammar rule containing an `OptionalIR` whose inner expression is a simple `TokenRefIR` or `RuleRefIR` (not a `SequenceIR`), the fixed code SHALL produce the same AST node properties and parser code as the original code, preserving existing nullable property generation.

**Validates: Requirements 3.2, 3.3**

Property 6: Preservation — Non-Repetition Rules Unchanged

_For any_ grammar rule that contains no `ZeroOrMoreIR`, `OneOrMoreIR`, or `OptionalIR` expressions wrapping `SequenceIR` inner expressions, the fixed code SHALL produce identical AST node classes and parser code as the original code.

**Validates: Requirements 3.4, 3.5, 3.6**

## Fix Implementation

### Changes Required

Assuming our root cause analysis is correct:

**File**: `Source/Storm.SourceGenerator/Emit/AstEmitter.cs`

**1. Fix `GetPropertyInfoForList` — handle SequenceIR inner**

Add a `case SequenceIR seq:` that inspects the sequence elements. If the sequence contains a `RuleRefIR`, generate an `ImmutableArray<TNode>` property for that rule's node type. If the sequence contains only `TokenRefIR` elements, generate an `ImmutableArray<Token>` property. Use the rule-ref or last token name for the auto-generated property name.

```csharp
case SequenceIR seq:
{
    // Find the first RuleRefIR in the sequence (e.g., Parameter in COMMA Parameter)
    var ruleRef = seq.Elements.OfType<RuleRefIR>().FirstOrDefault();
    if (ruleRef != null)
    {
        var name = EnsureUnique(PascalCase(ruleRef.Rule.Name) + "s", usedNames);
        var elementType = ruleRef.Rule.Name + "Node";
        return new PropertyInfo(name, $"ImmutableArray<{elementType}>", false, true, true,
            $" = ImmutableArray<{elementType}>.Empty;");
    }
    // Fallback: sequence of tokens only (e.g., DOT IDENTIFIER)
    var tokenRef = seq.Elements.OfType<TokenRefIR>().LastOrDefault();
    if (tokenRef != null)
    {
        var name = EnsureUnique(PascalCase(tokenRef.Token.Name) + "s", usedNames);
        return new PropertyInfo(name, "ImmutableArray<Token>", false, false, true,
            " = ImmutableArray<Token>.Empty;");
    }
    return null;
}
```

**2. Fix `GetPropertyInfoForListInner` — handle SequenceIR inner**

Add a `case SequenceIR seq:` with the same logic as above but using the provided `propName` instead of auto-generating one.

**3. Fix `GetPropertyInfoForOptionalInner` — handle SequenceIR inner**

Change the `default` case (or add a `SequenceIR` case) to inspect the sequence elements. If a `RuleRefIR` is found, generate a nullable node property (`TNode?`). Otherwise fall through to `Token?`.

```csharp
case SequenceIR seq:
{
    var ruleRef = seq.Elements.OfType<RuleRefIR>().FirstOrDefault();
    if (ruleRef != null)
        return new PropertyInfo(propName, ruleRef.Rule.Name + "Node?", true, true, false, "");
    return new PropertyInfo(propName, "Token?", true, false, false, "");
}
```

**4. Fix `GetPropertyInfoForOptional` — handle SequenceIR inner**

Add a `case SequenceIR seq:` that extracts the significant element and generates the appropriate nullable property, similar to the labeled variant.

**File**: `Source/Storm.SourceGenerator/Emit/ParserEmitter.cs`

**5. Fix `EmitFullExprParse` ZeroOrMoreIR handler — accumulate rule-ref results**

The current `ZeroOrMoreIR` handler pre-declares labeled variables and runs a while-loop but never accumulates unlabeled rule-ref results. The fix needs to:
- Before the loop, detect if the inner `SequenceIR` contains a `RuleRefIR` and create an `ImmutableArray` builder
- Inside the loop, after parsing each iteration, add the parsed rule-ref node to the builder
- After the loop, finalize the builder and add it to `assignedProps`

**6. Fix `EmitAtomicSequenceBody` — handle RuleRefIR elements**

The current method only handles `TokenRefIR` and `StringLiteralIR` elements. Add handling for `RuleRefIR` elements:
- When a `RuleRefIR` is encountered in the sequence, emit a `_Parse{Rule}()` call
- If the element has a label, assign the result to the pre-declared labeled variable
- If the parse returns null, set the sequence variable to null (atomic failure)

## Testing Strategy

### Validation Approach

The testing strategy follows a two-phase approach: first, surface counterexamples that demonstrate the bugs on unfixed code, then verify the fix works correctly and preserves existing behavior.

### Exploratory Bug Condition Checking

**Goal**: Surface counterexamples that demonstrate the bugs BEFORE implementing the fix. Confirm or refute the root cause analysis. If we refute, we will need to re-hypothesize.

**Test Plan**: Write tests that construct grammar IR with `SequenceIR`-wrapped repetitions and optionals, run them through `AstEmitter` and `ParserEmitter`, and inspect the generated code for missing properties and missing accumulation logic. Run these tests on the UNFIXED code to observe failures.

**Test Cases**:
1. **ParameterList AST Test**: Create grammar `rule PList = P (COMMA P)*;` and verify the generated AST node has an `ImmutableArray<PNode>` property (will fail on unfixed code — `GetPropertyInfoForList` returns null)
2. **ParameterList Parser Test**: Create the same grammar and verify the generated parser code contains a list builder and `.Add()` calls inside the while-loop (will fail on unfixed code — no accumulation)
3. **QualifiedIdentifier AST Test**: Create grammar `rule QId = IDENT (DOT IDENT)*;` and verify the generated AST node has an `ImmutableArray<Token>` property for repeated identifiers (will fail on unfixed code)
4. **FieldDeclaration Initializer AST Test**: Create grammar `rule Field = init:(EQ Expr)? SEMI;` and verify the generated property type is `ExprNode?` not `Token?` (will fail on unfixed code — falls through to `Token?`)
5. **FieldDeclaration Initializer Parser Test**: Verify the generated parser code calls `_ParseExpr()` inside the optional sequence and assigns the result (will fail on unfixed code — `EmitAtomicSequenceBody` skips rule refs)

**Expected Counterexamples**:
- AST nodes missing list properties for `(COMMA X)*` patterns
- Parser code that parses repeated elements but discards rule-ref results
- Optional sequence properties typed as `Token?` instead of `TNode?`
- Parser code that skips `_Parse{Rule}()` calls inside optional sequences

### Fix Checking

**Goal**: Verify that for all inputs where the bug condition holds, the fixed functions produce the expected behavior.

**Pseudocode:**
```
FOR ALL grammar WHERE isBugCondition(grammar) DO
  astOutput := AstEmitter_fixed.EmitAstNodes(grammar)
  parserOutput := ParserEmitter_fixed.Emit(grammar)
  ASSERT astOutput contains correct list/nullable properties
  ASSERT parserOutput contains accumulation logic / rule-ref parsing
END FOR
```

### Preservation Checking

**Goal**: Verify that for all inputs where the bug condition does NOT hold, the fixed functions produce the same output as the original functions.

**Pseudocode:**
```
FOR ALL grammar WHERE NOT isBugCondition(grammar) DO
  ASSERT AstEmitter_original.EmitAstNodes(grammar) = AstEmitter_fixed.EmitAstNodes(grammar)
  ASSERT ParserEmitter_original.Emit(grammar) = ParserEmitter_fixed.Emit(grammar)
END FOR
```

**Testing Approach**: Property-based testing is recommended for preservation checking because:
- It generates many grammar IR configurations automatically
- It catches edge cases in property naming, type generation, and code emission
- It provides strong guarantees that non-affected grammars produce identical output

**Test Plan**: Observe behavior on UNFIXED code first for simple repetitions, simple optionals, and non-repetition rules, then write property-based tests capturing that behavior.

**Test Cases**:
1. **Simple Repetition Preservation**: Verify `rule Start = Expr*;` continues to generate `ImmutableArray<ExprNode>` and correct parser accumulation
2. **Simple Optional Token Preservation**: Verify `rule Expr = Number Ident?;` continues to generate `Token?` property
3. **Simple Optional Rule Preservation**: Verify `rule Start = chain:Chain?;` continues to generate `ChainNode?` property
4. **Named Alternatives Preservation**: Verify `rule Expr = Number as Lit | Ident as Var;` continues to generate abstract base + sealed subtypes
5. **No-Repetition Rule Preservation**: Verify `rule Stmt = RETURN Expr SEMI;` generates identical output before and after fix

### Unit Tests

- Test `GetPropertyInfoForList` with `SequenceIR([TokenRefIR(COMMA), RuleRefIR(Param)])` inner — should return `ImmutableArray<ParamNode>`
- Test `GetPropertyInfoForList` with `SequenceIR([TokenRefIR(DOT), TokenRefIR(IDENT)])` inner — should return `ImmutableArray<Token>`
- Test `GetPropertyInfoForOptionalInner` with `SequenceIR([TokenRefIR(EQ), RuleRefIR(Expr)])` inner — should return `ExprNode?`
- Test full AST emission for `rule ParameterList = Parameter (COMMA Parameter)*;` — verify list property exists
- Test full parser emission for same rule — verify builder, `.Add()`, and `.ToImmutable()` in output
- Test full AST emission for `rule Field = init:(EQUAL Expression)? SEMI;` — verify `ExpressionNode?` property
- Test full parser emission for same rule — verify `_ParseExpression()` call in optional sequence

### Property-Based Tests

- Generate random grammar IRs with `ZeroOrMoreIR(SequenceIR(...))` patterns containing varying numbers of token-refs and rule-refs, and verify the AST emitter produces a list property typed to the first rule-ref's node type
- Generate random grammar IRs with `OptionalIR(SequenceIR(...))` patterns and verify the AST emitter produces a nullable node property when a rule-ref is present
- Generate random grammar IRs with simple `ZeroOrMoreIR(RuleRefIR)` and `OptionalIR(TokenRefIR)` patterns and verify the output is identical before and after the fix (preservation)
- Generate random grammar IRs with no repetitions or optionals and verify identical output (preservation)

### Integration Tests

- Parse the full Mist grammar (`Source/Mist/Grammar/Mist.storm`) through the fixed source generator and verify:
  - `ParameterListNode` has an `ImmutableArray<ParameterNode>` property
  - `ArgumentListNode` has an `ImmutableArray<ExpressionNode>` property
  - `QualifiedIdentifierNode` has an `ImmutableArray<Token>` property for repeated identifiers
  - `FieldDeclarationNode.Initializer` preserves the `ExpressionNode`
- Compile the `AllFeatures` sample end-to-end with the fixed generator and verify no regressions
- Verify that `Point(int x, int y)` constructor correctly receives both parameters
- Verify that `private int value = 0;` field initializer is accessible to the semantic model builder
