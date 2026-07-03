# Requirements Document

## Introduction

Storm is a PEG parser generator (source generator for C#). It defines grammars using `rule`, `token`, `fragment`, and `hidden token` declarations. The generated parser uses PEG ordered-choice backtracking for multi-alternative rules.

When multiple alternatives in a rule share a common prefix (e.g., `Modifier+ TYPE IDENTIFIER TypeParams?`), the bounded k=3 reachability analysis flags later alternatives as unreachable (STORM0006) because it cannot see past the shared prefix to the disambiguating token. The PEG runtime handles this correctly via backtracking, but it is inefficient (parsing the same prefix multiple times) and produces false positive warnings.

This feature introduces an `inline` keyword that allows grammar authors to explicitly declare that a set of alternatives share a common prefix. The emitter parses the shared prefix once, predicts which arm to take based on the next token(s) after the prefix, and constructs distinct flat node types (no wrapper nodes). The motivating case is `MemberDeclaration` in `Mist.storm`, where `MemberTypeAliasDeclaration` and `ModifiedAssociatedTypeSlotDeclaration` both start with `Modifier+ TYPE IDENTIFIER TypeParameterList?` but diverge on `EQUAL` vs `COLON`/`SEMICOLON`.

## Glossary

- **Storm_Generator**: The Storm source generator that emits a recursive-descent PEG parser and AST node classes from `.storm` grammar files.
- **Storm_Lexer**: The hand-written lexer (`StormLexer.cs`) that tokenizes `.storm` files into `StormToken` values.
- **Storm_Parser**: The hand-written recursive-descent parser (`StormParser.cs`) that parses Storm tokens into a `StormFile` AST.
- **Storm_Syntax**: The set of AST node types (`StormSyntax.cs`) representing parsed Storm grammar structures.
- **Parser_Emitter**: The component (`ParserEmitter.cs`) that emits the generated C# parser code using PEG backtracking.
- **AST_Emitter**: The component (`AstEmitter.cs`) that emits C# AST node classes for each rule and named alternative.
- **Reachability_Pass**: The validation pass (`ReachabilityPass.cs`) that checks for unreachable alternatives using bounded FIRST-k sequence analysis and emits STORM0006 warnings.
- **Grammar_IR**: The intermediate representation (`RuleIR.cs`) used by the emission pipeline, containing lowered rules, alternatives, and expressions.
- **Inline_Block**: The new syntactic construct `inline <PrefixClause>: { arms }` that occupies a single `|` slot in a parent alternation and declares a shared prefix with multiple diverging arms.
- **Prefix_Clause**: The portion of an `inline` block that names the shared prefix — a rule reference (e.g., `ModifiedTypeHead`). Literal sequences are not supported; the prefix must always be a named rule.
- **Inline_Arm**: A single arm within an Inline_Block, taking the form `RuleRef as AltName` or `label:RuleRef as AltName`.
- **Nominal_Prefix_Verification**: The compile-time check that each Inline_Arm's referenced rule begins with a rule reference (by name) to the prefix rule.
- **Flat_Node**: An AST node type where the prefix fields are hoisted into the arm's node type, identical to what a standalone alternative would produce (no wrapper or intermediate node).
- **Tail_Token_Divergence**: The property that after consuming the shared prefix, the next token(s) uniquely identify which arm to take.
- **Prediction**: The parse-time strategy where, after parsing the shared prefix once, the parser peeks at the next token(s) to determine which arm to continue with.
- **Pre_Feature_Baseline**: The behavior of the Storm_Generator and generated parser immediately before this feature is added, used as the comparison oracle.
- **Mist_Grammar**: The grammar file (`Mist.storm`) defining the Mist language syntax.

## Requirements

### Requirement 1: Lexer Extension

**User Story:** As a Storm grammar author, I want the Storm lexer to recognize `inline` as a keyword, so that I can use it in grammar rule declarations.

#### Acceptance Criteria

1. WHEN the Storm_Lexer encounters the identifier text `inline` in a `.storm` source file, THE Storm_Lexer SHALL produce a token with kind `InlineKeyword` rather than `Identifier`.
2. WHEN the Storm_Lexer encounters the identifier text `inline` embedded inside a longer identifier (e.g., `inlineFoo`, `myinline`), THE Storm_Lexer SHALL produce a token with kind `Identifier` for that longer identifier.
3. WHEN the Storm_Lexer encounters `inline` preceded by whitespace or at the start of input and followed by a non-identifier character, THE Storm_Lexer SHALL produce exactly one `InlineKeyword` token for that occurrence.
4. WHEN the Storm_Lexer encounters the character `,` (comma) in a `.storm` source file, THE Storm_Lexer SHALL produce a token with kind `Comma`.

### Requirement 2: Parser Extension

**User Story:** As a Storm grammar author, I want the Storm parser to parse `inline` blocks within rule alternations, so that shared-prefix groups are represented in the grammar AST.

#### Acceptance Criteria

1. WHEN the Storm_Parser encounters `inline` followed by an identifier (the Prefix_Clause rule name), a colon, a left brace, one or more Inline_Arms separated by commas, and a right brace within an alternation, THE Storm_Parser SHALL produce a `StormInlineBlockExpr` AST node containing the prefix rule reference, the list of arms, and source location.
2. WHEN an Inline_Arm takes the form `RuleRef as AltName`, THE Storm_Parser SHALL produce a `StormInlineArm` AST node with the rule reference expression, the alternative name, and no label.
3. WHEN an Inline_Arm takes the form `label:RuleRef as AltName`, THE Storm_Parser SHALL produce a `StormInlineArm` AST node with the rule reference expression, the alternative name, and the label string.
4. WHEN an Inline_Arm is itself an `inline` block (nested), THE Storm_Parser SHALL parse the nested inline block recursively and produce a `StormInlineBlockExpr` as the arm's expression.
5. WHEN the arm list contains a trailing comma after the last arm and before the closing brace, THE Storm_Parser SHALL accept it without emitting a diagnostic.
6. IF the Storm_Parser encounters `inline` not followed by a valid identifier and colon, THEN THE Storm_Parser SHALL emit a syntax error diagnostic and synchronize to the next recovery point.
7. IF the Storm_Parser encounters a malformed arm (missing `as` clause or missing rule reference), THEN THE Storm_Parser SHALL emit a syntax error diagnostic for the malformed arm and continue parsing remaining arms.
8. IF the arm list is empty (left brace immediately followed by right brace), THEN THE Storm_Parser SHALL emit a syntax error diagnostic indicating that at least one arm is required.
9. THE `as AltName` clause SHALL be required on every Inline_Arm. If it is absent, THE Storm_Parser SHALL emit a syntax error diagnostic.

### Requirement 3: Syntax AST Nodes

**User Story:** As a Storm source generator developer, I want AST node types representing inline blocks and their arms, so that downstream passes can inspect and process them.

#### Acceptance Criteria

1. THE Storm_Syntax SHALL define a `StormInlineBlockExpr` sealed record type that extends `StormExpression` and contains: a `Prefix` property of type `StormExpression` (always a `StormReferenceExpr` naming the prefix rule), an `Arms` property of type `IReadOnlyList<StormInlineArm>`, and source location properties `Offset` (int), `Line` (int), and `Column` (int).
2. THE Storm_Syntax SHALL define a `StormInlineArm` sealed record type (not extending `StormExpression`) containing: an `Expression` property of type `StormExpression` (the rule reference or nested inline block), an `AlternativeName` property of type `string`, and an optional `Label` property of type `string?`.
3. WHEN a `StormInlineBlockExpr` appears within a `StormAlternationExpr`, THE Storm_Syntax SHALL represent it as exactly one element in the alternation's `Alternatives` list, occupying the position where the inline block was declared.

### Requirement 4: Nominal Prefix Verification

**User Story:** As a Storm grammar author, I want the source generator to verify at compile time that each arm's referenced rule begins with a call to the prefix rule, so that misuse of `inline` is caught early as a hard error.

#### Acceptance Criteria

1. WHEN the Storm_Generator processes an Inline_Block, THE Storm_Generator SHALL verify for each Inline_Arm that the referenced rule's body begins with a rule reference (by name) to the prefix rule declared in the Inline_Block.
2. IF an Inline_Arm's referenced rule body does NOT begin with a rule reference to the prefix rule (ignoring any field label on that reference), THEN THE Storm_Generator SHALL emit a hard error diagnostic (STORM0017) identifying the arm, the rule, and the expected prefix rule name.
3. THE verification SHALL unwrap any `StormLabeledExpr` wrapping the first element of the arm's rule body before checking whether it is a `StormReferenceExpr` matching the prefix rule name.
4. IF the Prefix_Clause references a rule that does not exist in the grammar, THEN THE Storm_Generator SHALL emit a hard error diagnostic indicating an unresolved prefix rule reference.
5. IF the Prefix_Clause rule's body is an alternation (multiple alternatives), THEN THE Storm_Generator SHALL emit a hard error diagnostic indicating that a prefix rule must resolve to a single sequence, not an alternation.
6. IF an Inline_Arm references a rule whose body is an alternation (multiple alternatives), THEN THE Storm_Generator SHALL emit a hard error diagnostic indicating that inline arms must reference rules with a single-sequence body.
7. WHEN verifying nested inline blocks, THE Storm_Generator SHALL recursively apply the same nominal verification to the nested prefix and arm rules.

### Requirement 5: Reachability Pass Integration

**User Story:** As a Storm grammar author, I want the reachability pass to recognize inline blocks and only check tail-token divergence among the arms, so that false positive STORM0006 warnings are eliminated for shared-prefix groups.

#### Acceptance Criteria

1. WHEN the Reachability_Pass encounters an Inline_Block within an alternation, THE Reachability_Pass SHALL treat the entire Inline_Block as a single opaque alternative for inter-alternative reachability analysis, meaning the pass SHALL NOT compare individual arms of the Inline_Block against sibling alternatives outside the block.
2. WHEN the Reachability_Pass analyzes the arms within an Inline_Block, THE Reachability_Pass SHALL compute the FIRST-k set (where k equals the configured MaxLookahead, currently 3) for each arm using only the tail portion of that arm's rule body (the elements remaining after the first element — the prefix rule reference — is removed), and SHALL determine that two arms diverge when neither arm's tail FIRST-k set is a subset of the other.
3. IF two arms within the same Inline_Block have tail FIRST-k sets where the later arm's set (by declaration order within the arms list) is a subset of the union of all earlier arms' tail FIRST-k sets, THEN THE Reachability_Pass SHALL emit a STORM0006 warning identifying the later arm as unreachable within the inline block.
4. WHEN the Reachability_Pass computes the FIRST-k set for an Inline_Block as a whole (for comparison against sibling alternatives in the parent alternation), THE Reachability_Pass SHALL use the FIRST-k set of the Prefix_Clause.
5. WHEN the Reachability_Pass encounters a nested Inline_Block (an Inline_Block appearing as an arm within a parent Inline_Block), THE Reachability_Pass SHALL recursively apply the same opaque-treatment and tail-divergence analysis to the nested block's arms.

### Requirement 6: IR Lowering

**User Story:** As a Storm source generator developer, I want inline blocks to be lowered into the Grammar IR so that the Parser_Emitter and AST_Emitter can generate appropriate code.

#### Acceptance Criteria

1. WHEN the IR lowering pass encounters a `StormInlineBlockExpr`, THE IR lowering pass SHALL produce an `InlineBlockIR` node (a subtype of `ExpressionIR`) containing the lowered prefix expression sequence, the list of lowered `InlineArmIR` nodes, and a `RuleIR` reference for each arm linking it to its resolved target rule.
2. WHEN the IR lowering pass lowers an Inline_Arm, THE IR lowering pass SHALL produce an `InlineArmIR` node containing the alternative name, the optional label, the `RuleIR` reference to the target rule, and the lowered tail expression (the arm's rule body with the prefix stripped).
3. WHEN the IR lowering pass strips the prefix from an arm's rule body, THE IR lowering pass SHALL remove the first element of the arm's rule body sequence (the rule reference to the prefix rule, possibly wrapped in a label), leaving only the remaining tail portion as the arm's lowered expression.
4. WHEN a parent rule contains an Inline_Block, THE IR lowering pass SHALL represent the Inline_Block as a single `RuleAlternativeIR` entry (with a null alternative name) in the parent rule's alternatives list, where the `RuleAlternativeIR.Expression` is the `InlineBlockIR` node.
5. IF the prefix stripping consumes all elements of an arm's rule body leaving an empty tail, THEN THE IR lowering pass SHALL produce an `InlineArmIR` whose tail expression is an empty `SequenceIR` (zero elements).
6. WHEN the IR lowering pass encounters a nested `StormInlineBlockExpr` within an arm's expression, THE IR lowering pass SHALL recursively produce a nested `InlineBlockIR` node applying the same lowering rules to the nested prefix and arms.

### Requirement 7: Parser Emission

**User Story:** As a Storm source generator developer, I want the Parser_Emitter to generate efficient parsing code that parses the shared prefix once and then predicts the arm, so that redundant backtracking is eliminated.

#### Acceptance Criteria

1. WHEN the Parser_Emitter emits code for an Inline_Block alternative, THE Parser_Emitter SHALL emit code that parses the prefix expression sequence exactly once, storing parsed values in local variables.
2. WHEN the prefix has been parsed successfully, THE Parser_Emitter SHALL emit prediction code that peeks at the next token(s) after the prefix to determine which arm to continue with.
3. IF the prediction determines the arm, THE Parser_Emitter SHALL emit code that parses only the tail portion of the selected arm (the part after the prefix), without re-parsing the prefix.
4. IF the prefix fails to parse (any element in the prefix sequence does not match), THE Parser_Emitter SHALL emit code that treats the inline block as a failed alternative and restores the parser position to before the prefix was parsed.
5. IF the prefix parses successfully but no arm's prediction matches the next token(s), THE Parser_Emitter SHALL emit code that treats the inline block as a failed alternative and restores the parser position to before the prefix was parsed.
6. WHEN multiple arms share a FIRST token in their tail, THE Parser_Emitter SHALL try those arms in declaration order (PEG ordered-choice semantics preserved within inline blocks).
7. WHEN emitting prediction code, THE Parser_Emitter SHALL use the FIRST set of each arm's tail expression to build a token-based dispatch (switch or if-else chain), with arms sharing a FIRST token grouped into a sequential try-and-backtrack within that dispatch branch.
8. WHEN emitting code for nested inline blocks, THE Parser_Emitter SHALL recursively apply the same parse-once-then-predict pattern for the nested prefix.
9. WHEN the Inline_Block is one alternative among others in a multi-alternative rule, THE Parser_Emitter SHALL emit the inline block's parsing code within the standard PEG ordered-choice structure (save position, try, restore on failure).
10. WHEN the Parser_Emitter constructs the arm's AST node after successful tail parsing, THE Parser_Emitter SHALL assign both the prefix-parsed local variables and the tail-parsed local variables to the node's properties, producing a complete flat node.

### Requirement 8: AST Emission (Flat Nodes)

**User Story:** As a Storm grammar author, I want the generated AST nodes for inline arms to be flat (prefix fields hoisted into each arm's node type), so that consumers see the same AST shape as if each arm were a standalone alternative.

#### Acceptance Criteria

1. WHEN the AST_Emitter emits node types for an Inline_Block's arms, THE AST_Emitter SHALL generate a distinct node class for each arm, named according to the arm's `AltName` and the parent rule name (e.g., `MemberTypeAliasMemberMemberDeclarationNode`).
2. WHEN the AST_Emitter generates an arm's node class, THE AST_Emitter SHALL include properties for all fields from the prefix clause (hoisted into the arm's node, in expression order) followed by the arm's own tail fields, such that the property order matches the concatenation of prefix fields then tail fields.
3. THE AST_Emitter SHALL NOT generate a wrapper or intermediate node type for the Inline_Block itself — each arm's node SHALL be directly a subtype of the parent rule's base node type.
4. WHEN an Inline_Arm has a label (e.g., `label:RuleRef as AltName`), THE AST_Emitter SHALL use the label as the property name for the tail expression's value on the arm's node class.
5. WHEN grammars that currently define rules as standalone alternatives are refactored to use an Inline_Block grouping the same alternatives, THE AST_Emitter SHALL produce node types with the same class names, property names, property types, property order, base class, and `Children()` enumeration as the standalone-alternative grammar produced — ensuring no AST breaking change for consumers.

### Requirement 9: Diagnostic for Nominal Prefix Mismatch

**User Story:** As a Storm grammar author, I want a clear diagnostic when an inline arm's rule does not structurally match the declared prefix, so that I can fix the grammar quickly.

#### Acceptance Criteria

1. WHEN the Nominal_Prefix_Verification detects a mismatch, THE Storm_Generator SHALL emit a diagnostic with diagnostic code `STORM0017` at severity Error.
2. THE diagnostic message SHALL include the prefix rule name, the failing arm's rule name, and an explanation that the arm's rule body does not begin with a reference to the prefix rule.
3. THE diagnostic location SHALL reference the source span of the failing Inline_Arm declaration within the `.storm` file.
4. WHEN the Storm_Generator emits a prefix-mismatch diagnostic, THE Storm_Generator SHALL suppress generated parser and AST source output for the containing grammar (no partial code generation).
5. WHEN multiple arms in the same Inline_Block fail verification, THE Storm_Generator SHALL emit one diagnostic per failing arm, continuing verification of all remaining arms before halting code generation.

### Requirement 10: No Existing Grammar Breakage

**User Story:** As a Mist compiler maintainer, I want adding the `inline` keyword to Storm to cause zero changes in behavior for existing grammars that do not use `inline`, so that adoption is opt-in and risk-free.

#### Acceptance Criteria

1. WHERE a `.storm` grammar file does not contain the `inline` keyword, THE Storm_Generator SHALL produce parser source and AST source byte-for-byte identical to the Pre_Feature_Baseline output for that grammar.
2. WHEN the full Mist test suite runs against the generated parser from a grammar that does not use `inline`, THE test suite SHALL report a passing-test count no lower than the Pre_Feature_Baseline passing-test count, with zero tests transitioning from passing on the Pre_Feature_Baseline to failing with the feature.
3. THE `inline` keyword SHALL NOT share a token kind with any existing Storm grammar keyword (`rule`, `token`, `fragment`, `hidden`, `import`, `grammar`, `as`, `mode`, `default`, `push`, `pop`); the Storm_Lexer SHALL assign `inline` a distinct `InlineKeyword` token kind separate from all existing keyword token kinds.
4. WHEN a grammar uses `inline` as an identifier (e.g., a rule named `inline`), THE Storm_Lexer SHALL produce an `InlineKeyword` token, and the Storm_Parser SHALL report a syntax error if `inline` appears in a position expecting a rule name — this is acceptable because `inline` is now a reserved keyword.

### Requirement 11: Comma Token in Storm Lexer

**User Story:** As a Storm grammar author, I want the Storm lexer to recognize comma as a distinct token, so that comma-separated inline arms can be parsed unambiguously.

#### Acceptance Criteria

1. THE `StormTokenKind` enum SHALL include a `Comma` variant in the operators/punctuation section.
2. WHEN the Storm_Lexer encounters a `,` character outside of string literals, character classes, and regex literals, THE Storm_Lexer SHALL produce a token with kind `Comma`, lexeme `,`, a zero-based byte offset equal to the position of the `,` in the source text, and 1-based line and column values matching the position reported by `StormSourceText.GetLineAndColumn`.
3. THE `ClassifyOperator` method SHALL map the `,` character to `StormTokenKind.Comma`.

### Requirement 12: Zero Build Warnings

**User Story:** As a Mist compiler maintainer, I want the feature to introduce no build warnings, so that the zero-warnings policy is upheld.

#### Acceptance Criteria

1. WHEN the Storm_Generator is built with the feature using the same build configuration and analyzer settings as the Pre_Feature_Baseline, THE build SHALL emit zero `STORM*` warnings that were absent from the Pre_Feature_Baseline build, such that the total `STORM*` warning count does not exceed the Pre_Feature_Baseline `STORM*` warning count.
2. WHEN the Mist consumer is built against the generated parser using the same build configuration and analyzer settings as the Pre_Feature_Baseline, THE build SHALL emit zero `CS*` warnings that were absent from the Pre_Feature_Baseline build, excluding trim warnings originating from third-party packages, such that the comparable `CS*` warning count does not exceed the Pre_Feature_Baseline `CS*` warning count.
3. THE feature SHALL satisfy criteria 12.1 and 12.2 without introducing any `STORM*` or `CS*` warning suppression that was absent from the Pre_Feature_Baseline, where "warning suppression" includes `#pragma warning disable` directives, `<NoWarn>` MSBuild properties, and `SuppressMessage` attributes.
4. WHEN `dotnet format --verify-no-changes` is run against the modified projects using the repository `.editorconfig`, THE command SHALL exit with code 0, indicating zero formatting or analyzer violations were introduced by the feature.

### Requirement 13: Grammar Migration — STORM0006 False Positive Elimination

**User Story:** As a Mist grammar author, I want to refactor `MemberDeclaration` in `Mist.storm` to use an inline block for the `MemberTypeAliasDeclaration` and `ModifiedAssociatedTypeSlotDeclaration` alternatives, so that the STORM0006 false positive warning is eliminated and the feature is proven end-to-end.

#### Before (current grammar)

```storm
rule MemberTypeAliasDeclaration = modifiers:Modifier+ TYPE name:IDENTIFIER
    typeParams:TypeParameterList?
    EQUAL type:RefinableTypeExpression
    constraints:WhereClause*
    SEMICOLON;

rule ModifiedAssociatedTypeSlotDeclaration = modifiers:Modifier+ TYPE name:IDENTIFIER
    typeParams:TypeParameterList?
    constraintClause:(COLON constraints:ConstraintList slotWhereConstraints:WhereClause*)?
    SEMICOLON;

rule MemberDeclaration
    = ...
    | memberTypeAlias:MemberTypeAliasDeclaration as MemberTypeAliasMember
    | associatedType:AssociatedTypeSlotDeclaration as AssociatedTypeMember
    | unconstrainedAssociatedType:UnconstrainedAssociatedTypeSlotDeclaration as UnconstrainedAssociatedTypeMember
    | modifiedAssociatedType:ModifiedAssociatedTypeSlotDeclaration as ModifiedAssociatedTypeMember
    ;
```

#### After (migrated grammar)

```storm
rule ModifiedTypeHead = modifiers:Modifier+ TYPE name:IDENTIFIER typeParams:TypeParameterList?;

rule MemberTypeAliasDeclaration = head:ModifiedTypeHead
    EQUAL type:RefinableTypeExpression
    constraints:WhereClause*
    SEMICOLON;

rule ModifiedAssociatedTypeSlotDeclaration = head:ModifiedTypeHead
    constraintClause:(COLON constraints:ConstraintList slotWhereConstraints:WhereClause*)?
    SEMICOLON;

rule MemberDeclaration
    = ...
    | inline ModifiedTypeHead: {
          memberTypeAlias:MemberTypeAliasDeclaration as MemberTypeAliasMember,
          modifiedAssociatedType:ModifiedAssociatedTypeSlotDeclaration as ModifiedAssociatedTypeMember,
      }
    | associatedType:AssociatedTypeSlotDeclaration as AssociatedTypeMember
    | unconstrainedAssociatedType:UnconstrainedAssociatedTypeSlotDeclaration as UnconstrainedAssociatedTypeMember
    ;
```

#### Acceptance Criteria

1. WHEN `Mist.storm` is migrated to the "After" form above, THE Reachability_Pass SHALL emit zero STORM0006 warnings for the `MemberDeclaration` rule.
2. WHEN the migrated grammar is compiled, THE generated AST node types for `MemberTypeAliasMemberMemberDeclarationNode` and `ModifiedAssociatedTypeMemberMemberDeclarationNode` SHALL have the same class names, property names, property types, property order, and base class as their Pre_Feature_Baseline definitions — zero impact on downstream compiler code.
3. WHEN the Mist compiler parses source files containing member type aliases and modified associated type slots, THE generated parser SHALL produce AST nodes with the same property values and tree structure as the Pre_Feature_Baseline parser output for the same inputs.
4. WHEN the full Mist test suite runs against the migrated grammar, THE test suite SHALL report a passing-test count no lower than the Pre_Feature_Baseline passing-test count, with zero tests transitioning from passing to failing.
5. THE migration SHALL NOT require any changes to semantic analysis, type checking, code emission, or any other compiler phase downstream of parsing — the inline block is purely a parser-level optimization that preserves the existing AST contract.

### Requirement 14: Storm TextMate Grammar Update

**User Story:** As a Storm grammar author using VS Code, I want the `inline` keyword to be syntax-highlighted correctly in `.storm` files, so that I get proper visual cues when writing inline blocks.

#### Acceptance Criteria

1. THE Storm TextMate grammar SHALL include a pattern that matches the token `\binline\b` and assigns it a keyword scope consistent with existing rule-modifier keywords (e.g., `storage.type.storm` as used for `hidden` and `fragment`).
2. THE Storm TextMate grammar SHALL place the `inline` keyword pattern at a priority such that `inline` is matched by the keyword rule rather than remaining unstyled plain text when it appears before a rule name or within rule body positions.
3. WHEN a `.storm` file contains `inline` adjacent to a rule reference (e.g., `inline ruleName`), THE Storm TextMate grammar SHALL apply the keyword scope to `inline` and not to the adjacent rule reference identifier.
