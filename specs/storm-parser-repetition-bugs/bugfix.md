# Bugfix Requirements Document

## Introduction

The Storm source generator (`Source/Storm.SourceGenerator/`) reads `.storm` grammar files and generates C# AST node classes, a lexer, and a recursive-descent parser for consumer grammars like Mist. Two related bugs in the AST emitter and parser emitter cause generated AST nodes to lose data from grammar repetitions (`(COMMA X)*`) and optional groups containing sequences (`(EQUAL Expression)?`). This prevents the Mist compiler from correctly handling multi-parameter methods/constructors, multi-argument method calls, dotted qualified identifiers, and field initializer expressions — producing misleading downstream diagnostics like `MIST_UNDEFINED_IDENTIFIER (historical)`, `MIST_UNDEFINED_TYPE (historical)`, and `MIST_TC_0017 (historical)`.

## Bug Analysis

### Current Behavior (Defect)

1.1 WHEN a grammar rule uses a repetition pattern like `Parameter (COMMA Parameter)*` THEN the system generates an AST node (`ParameterListNode`) that only stores the first `Parameter` as a single property and discards all repeated `(COMMA Parameter)*` elements, because `AstEmitter.GetPropertyInfoForList` returns `null` when the inner expression of `ZeroOrMoreIR` is a `SequenceIR` rather than a simple `TokenRefIR` or `RuleRefIR`

1.2 WHEN a grammar rule uses a repetition pattern like `Expression (COMMA Expression)*` THEN the system generates an AST node (`ArgumentListNode`) that only stores the first `Expression` and discards all repeated expressions, causing method calls with 2+ arguments to lose arguments beyond the first

1.3 WHEN a grammar rule uses a repetition pattern like `IDENTIFIER (DOT IDENTIFIER)*` THEN the system generates an AST node (`QualifiedIdentifierNode`) that only stores the first `IDENTIFIER` token and discards all repeated `(DOT IDENTIFIER)*` segments, causing dotted names like `System.Console` to lose all segments after the first

1.4 WHEN a grammar rule uses a labeled optional group containing a sequence like `initializer:(EQUAL Expression)?` THEN the system generates a property typed as `Token?` (storing only the EQUAL token) instead of preserving both the token and the `ExpressionNode`, because `AstEmitter.GetPropertyInfoForOptionalInner` falls through to its default case for `SequenceIR` inner expressions

1.5 WHEN a `FieldDeclarationNode` has an initializer expression (e.g., `private int value = 0;`) THEN the `ExpressionNode` inside the optional group is not stored in any property and is not yielded by `Children()`, causing the semantic model builder to never find or resolve the initializer expression

1.6 WHEN the parser emitter generates code for a `ZeroOrMoreIR` whose inner expression is a `SequenceIR` (e.g., `(COMMA Parameter)*`) THEN the generated parser code parses the tokens correctly but the parsed sub-nodes are not assigned to any property on the AST node, because the `EmitFullExprParse` handler for `ZeroOrMoreIR` only pre-declares variables for labeled sub-expressions within the loop body but does not accumulate the repeated rule-ref results into a list property

### Expected Behavior (Correct)

2.1 WHEN a grammar rule uses a repetition pattern like `Parameter (COMMA Parameter)*` THEN the system SHALL generate an AST node with a list property (e.g., `ImmutableArray<ParameterNode>`) that stores ALL `ParameterNode` instances from the repetition, and `Children()` SHALL yield every `ParameterNode` in the list

2.2 WHEN a grammar rule uses a repetition pattern like `Expression (COMMA Expression)*` THEN the system SHALL generate an AST node with a list property that stores ALL `ExpressionNode` instances from the repetition, and `Children()` SHALL yield every `ExpressionNode` in the list

2.3 WHEN a grammar rule uses a repetition pattern like `IDENTIFIER (DOT IDENTIFIER)*` THEN the system SHALL generate an AST node with a list property that stores ALL `IDENTIFIER` tokens from the repetition, and `Children()` SHALL continue to yield break (since tokens are not syntax nodes)

2.4 WHEN a grammar rule uses a labeled optional group containing a sequence like `initializer:(EQUAL Expression)?` THEN the system SHALL generate properties that preserve ALL elements of the optional sequence — both the token and any rule-ref nodes — so that the `ExpressionNode` is accessible and yielded by `Children()`

2.5 WHEN a `FieldDeclarationNode` has an initializer expression THEN the `ExpressionNode` SHALL be stored in an accessible property and yielded by `Children()`, so that the semantic model builder can find and resolve the initializer expression without crashing or dropping the file's AST

2.6 WHEN the parser emitter generates code for a `ZeroOrMoreIR` whose inner expression is a `SequenceIR` THEN the generated parser code SHALL accumulate all parsed rule-ref nodes from each iteration into the corresponding list property on the AST node

### Unchanged Behavior (Regression Prevention)

3.1 WHEN a grammar rule uses a simple `ZeroOrMoreIR` with a `RuleRefIR` inner expression (e.g., `members:MemberDeclaration*`) THEN the system SHALL CONTINUE TO generate a correctly-typed `ImmutableArray` list property and populate it via `Children()`

3.2 WHEN a grammar rule uses a simple `OptionalIR` with a `TokenRefIR` inner expression (e.g., `arrayRank:(LEFT_BRACKET RIGHT_BRACKET)?`) THEN the system SHALL CONTINUE TO generate a `Token?` property

3.3 WHEN a grammar rule uses a simple `OptionalIR` with a `RuleRefIR` inner expression (e.g., `chain:ConstructorChain?`) THEN the system SHALL CONTINUE TO generate a nullable node property and yield it from `Children()` when present

3.4 WHEN a grammar rule has no repetitions or optional groups THEN the system SHALL CONTINUE TO generate the same AST node classes, properties, and `Children()` implementations as before

3.5 WHEN a grammar rule uses labeled fields on simple expressions (e.g., `name:IDENTIFIER`, `body:Block`) THEN the system SHALL CONTINUE TO generate correctly-typed properties for those labels

3.6 WHEN the parser emitter generates code for rules with named alternatives (e.g., `as ClassType | as StructType`) THEN the system SHALL CONTINUE TO generate correct PEG ordered-choice backtracking with abstract base and sealed subtypes
