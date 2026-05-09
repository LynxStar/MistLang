# Requirements Document

## Introduction

Newtype clauses extend the basic newtype feature (see `.kiro/specs/newtypes/`) with member filtering (`where` clauses) and member transformation (`with` clauses). This enables compile-time metaprogramming over type members — selectively including, excluding, or modifying members when cloning a type.

Where clauses use a predicate expression syntax with `and`, `or`, `not`, and parentheses for boolean logic over member attributes. Multiple `where` clauses targeting the same category produce a union (OR) of their results. For any category without an explicit `where` clause, all members of that category are implicitly included. With clauses transform member attributes (types, modifiers) on the filtered set.

**Prerequisite**: This spec depends on the basic newtypes spec (`newtypes`) being implemented first.

## Glossary

- **Mist_Compiler**: The Mist language compiler, encompassing lexing, parsing, semantic analysis, and code generation stages.
- **Parser**: The Storm-generated parser that produces an AST from a token stream according to the Mist grammar.
- **Pretty_Printer**: The `MistPrettyPrinter` component that formats a Mist AST back into syntactically valid Mist source text.
- **Semantic_Model_Builder**: The `SemanticModelBuilder` component that constructs the semantic model (symbol table, type information) from the parsed AST.
- **Type_Checker**: The type-checking analyzer that validates type correctness across the program.
- **CSharp_Transpiler**: The C# code generation backend that emits equivalent C# source from the Mist semantic model and AST.
- **Where_Clause**: A filtering clause of the form `where {category} : {predicate}` that selects members from a category based on a boolean predicate expression over member attributes. Multiple where clauses on the same category union their results.
- **With_Clause**: A transformation clause of the form `with {category} : {attribute} is {old_value} as {new_value}` that modifies matching members. Must appear after all where clauses.
- **Member_Category**: One of `fields`, `properties`, `methods`, `constructors`, `operators`, or `indexers` — identifies which group of members a clause targets.
- **Member_Attribute**: A named property of a member within a category (e.g., `Type`, `Modifier`, `ReturnType`, `Parameters`, `Symbol`) used in predicate expressions.
- **Predicate_Expression**: A boolean expression over member attributes using `{Attribute} is {Value}` as atoms, combined with `and`, `or`, `not`, and parentheses. Also supports the literal values `true` (keep all) and `false` (exclude all).
- **Clause_Pipeline**: The ordered sequence of clauses in a newtype declaration. All `where` clauses are evaluated first (same-category clauses unioned, different categories independent), then all `with` clauses execute sequentially.

## Requirements

### Requirement 1: Where Clause Syntax

**User Story:** As a Mist developer, I want to use `where` clauses with boolean predicate expressions to filter members of the source type, so that the newtype only includes members matching specific criteria.

#### Acceptance Criteria

1. WHEN the Parser encounters `where CATEGORY : PredicateExpression` following a newtype source type, THE Parser SHALL produce a WhereClause AST node containing the member category and the predicate expression tree.
2. THE Parser SHALL parse predicate expressions supporting `{Attribute} is {Value}` atoms combined with `and`, `or`, `not`, and parentheses for grouping, following the same precedence rules as Mist's pattern matching (`and` binds tighter than `or`, `not` is unary prefix, parentheses override precedence).
3. THE Parser SHALL recognize `where {category} : true` as a predicate that matches all members in the category (keep all).
4. THE Parser SHALL recognize `where {category} : false` as a predicate that matches no members in the category (exclude all).
5. THE Parser SHALL recognize `{Attribute} is not {Value}` as a negated attribute test within a predicate expression.
6. IF the Parser encounters a `with` clause before all `where` clauses have been declared, THEN THE Parser SHALL report a diagnostic error indicating that all `where` clauses must precede `with` clauses.

### Requirement 2: Where Clause Semantics

**User Story:** As a Mist developer, I want where clauses to filter members using union semantics for same-category clauses, so that I can express complex inclusion criteria.

#### Acceptance Criteria

1. WHEN the Semantic_Model_Builder evaluates where clauses, THE Semantic_Model_Builder SHALL group all where clauses by their target category and compute the union of their predicate results for each category. A member is included if it satisfies ANY of the where clauses targeting its category.
2. FOR any member category that has no explicit where clause in the newtype declaration, THE Semantic_Model_Builder SHALL include all members of that category (implicit `true`).
3. IF a where clause references a category that does not apply to the source type (e.g., `where fields : ...` on a type with no fields), THEN THE Semantic_Model_Builder SHALL report a diagnostic warning and produce an empty member set for that category.

### Requirement 3: With Clause Syntax and Semantics

**User Story:** As a Mist developer, I want to use `with` clauses to transform member attributes, so that I can modify types or modifiers of cloned members without manually redefining the entire type.

#### Acceptance Criteria

1. WHEN the Parser encounters `with CATEGORY : Attribute is OldValue as NewValue` following where clauses or other with clauses, THE Parser SHALL produce a WithClause AST node containing the member category, the attribute name, the match value, and the replacement value.
2. WHEN the Semantic_Model_Builder evaluates a with clause, THE Semantic_Model_Builder SHALL find all members in the specified category whose named attribute is equal to the match value and replace that attribute with the replacement value.
3. WHEN the Semantic_Model_Builder evaluates a with clause, THE Semantic_Model_Builder SHALL leave members whose named attribute does not equal the match value unchanged, passing them through to subsequent clauses unmodified.
4. IF a with clause specifies a match value or replacement value whose kind does not correspond to the attribute's expected type (e.g., a modifier value used with a Type attribute, or a type value used with a Modifier attribute), THEN THE Type_Checker SHALL report a diagnostic error indicating the category mismatch.
5. IF a with clause matches zero members in the current member set, THEN THE Semantic_Model_Builder SHALL produce no transformation and pass the member set unchanged to subsequent clauses without reporting a diagnostic.

### Requirement 4: Member Categories and Attributes

**User Story:** As a Mist developer, I want to use named categories and attributes to target specific member properties, so that I can precisely control which aspect of a member is filtered or transformed.

#### Acceptance Criteria

1. THE Parser SHALL recognize `fields` as a member category with valid attributes: `Type` (declared type) and `Modifier` (access modifier).
2. THE Parser SHALL recognize `properties` as a member category with valid attributes: `Type` (declared type) and `Modifier` (access modifier).
3. THE Parser SHALL recognize `methods` as a member category with valid attributes: `ReturnType` (return type), `Modifier` (access modifier), and `Parameters` (parameter type tuple matched by exact arity and positional type equality).
4. THE Parser SHALL recognize `constructors` as a member category with valid attributes: `Modifier` (access modifier) and `Parameters` (parameter type tuple matched by exact arity and positional type equality).
5. THE Parser SHALL recognize `operators` as a member category with valid attributes: `ReturnType` (return type), `Symbol` (operator symbol, e.g., `+`, `-`, `==`), and `Parameters` (parameter type tuple matched by exact arity and positional type equality).
6. THE Parser SHALL recognize `indexers` as a member category with valid attributes: `Type` (element type), `Modifier` (access modifier), and `Parameters` (parameter type tuple matched by exact arity and positional type equality).
7. WHEN a `Parameters` value is specified as a parenthesized tuple (e.g., `(int, string)`), THE Semantic_Model_Builder SHALL match members whose parameter list has the exact same arity and whose parameter types match positionally.
8. WHEN a `Parameters` value contains `_` (underscore) in a position, THE Semantic_Model_Builder SHALL treat that position as a wildcard matching any single parameter type.
9. IF the Parser encounters a category that is not one of `fields`, `properties`, `methods`, `constructors`, `operators`, or `indexers`, THEN THE Parser SHALL report a diagnostic error listing the valid member categories.
10. IF the Parser encounters an attribute that is not valid for the specified category, THEN THE Parser SHALL report a diagnostic error listing the valid attributes for that category.

### Requirement 5: Clause Pipeline Semantics

**User Story:** As a Mist developer, I want clauses to execute with predictable semantics, so that I can compose multiple filtering and transformation steps reliably.

#### Acceptance Criteria

1. THE Semantic_Model_Builder SHALL evaluate all `where` clauses before any `with` clauses.
2. WHEN evaluating where clauses, THE Semantic_Model_Builder SHALL group them by target category. For each category with one or more where clauses, a member is included in the result if it satisfies the predicate of ANY where clause targeting that category (union/OR semantics).
3. FOR categories without any explicit where clause, THE Semantic_Model_Builder SHALL include all members of that category unchanged (implicit `where {category} : true`).
4. WHEN evaluating with clauses, THE Semantic_Model_Builder SHALL process them sequentially in declaration order, where each with clause operates on the member set produced by the preceding clause.
5. THE Semantic_Model_Builder SHALL start the pipeline with the set of all field, property, method, constructor, operator, cast operator, and indexer members declared on the source type (including inherited members).

### Requirement 6: Grammar Integration

**User Story:** As a compiler developer, I want the Storm grammar to define the clause rules, so that the source generator produces the correct AST node types.

#### Acceptance Criteria

1. THE Mist_Compiler grammar SHALL extend the `NewtypeDeclaration` rule to: `TYPE name:IDENTIFIER EQUAL NEW sourceType:TypeReference whereClauses:NewtypeWhereClause* withClauses:NewtypeWithClause* SEMICOLON`.
2. THE Mist_Compiler grammar SHALL define a `NewtypeWhereClause` rule matching `WHERE category:IDENTIFIER COLON predicate:PredicateExpression`.
3. THE Mist_Compiler grammar SHALL define a `NewtypeWithClause` rule matching `WITH category:IDENTIFIER COLON attribute:IDENTIFIER IS matchValue:ClauseValue AS replacementValue:ClauseValue`.
4. THE Mist_Compiler grammar SHALL define a `PredicateExpression` rule using precedence-climbing with: `OrPredicate` = `AndPredicate (OR AndPredicate)*`, `AndPredicate` = `UnaryPredicate (AND UnaryPredicate)*`, `UnaryPredicate` = `NOT? PrimaryPredicate`, `PrimaryPredicate` = `AttributeTest | TRUE | FALSE | LEFT_PARENTHESIS PredicateExpression RIGHT_PARENTHESIS`.
5. THE Mist_Compiler grammar SHALL define an `AttributeTest` rule matching `attribute:IDENTIFIER IS negated:NOT? value:ClauseValue`.
6. THE Mist_Compiler grammar SHALL define a `ClauseValue` rule with labeled alternatives for a type reference (`type:TypeReference` labeled `as TypeClauseValue`), a modifier keyword (`modifier:Modifier` labeled `as ModifierClauseValue`), an operator symbol (`symbol:OperatorSymbol` labeled `as SymbolClauseValue`), and a parameters tuple (`LEFT_PARENTHESIS params:ParametersParamList? RIGHT_PARENTHESIS` labeled `as ParametersClauseValue`).
7. THE Mist_Compiler grammar SHALL define a `ParametersParamList` rule matching `ParametersParam (COMMA ParametersParam)*`.
8. THE Mist_Compiler grammar SHALL define a `ParametersParam` rule with alternatives for a type reference (`type:TypeReference` labeled `as TypedParam`) and a discard wildcard (`IDENTIFIER` where the identifier text is `_`, labeled `as DiscardParam`).

### Requirement 7: Pretty-Printing Clauses

**User Story:** As a compiler developer, I want the pretty printer to format newtype clauses back into valid Mist source text, so that round-trip fidelity is maintained.

#### Acceptance Criteria

1. WHEN the Pretty_Printer encounters a NewtypeDeclaration node with one or more clauses, THE Pretty_Printer SHALL emit the declaration header as `type {name} = new {source_type}` (without a trailing semicolon) on its own line, emit each clause on a subsequent line indented one level (4 spaces) relative to the declaration header, and append a terminating `;` at the end of the last clause line.
2. WHEN emitting a where clause, THE Pretty_Printer SHALL format it as `where {category} : {predicate_expression}` preserving the boolean structure with `and`, `or`, `not`, and parentheses as needed.
3. WHEN emitting a with clause, THE Pretty_Printer SHALL format it as `with {category} : {attribute} is {old_value} as {new_value}`.
4. WHEN a NewtypeDeclaration node contains a mix of where and with clauses, THE Pretty_Printer SHALL emit all where clauses first, then all with clauses, in their original declaration order within each group.
5. FOR ALL valid NewtypeDeclaration AST nodes with clauses, parsing the pretty-printed output and then pretty-printing again SHALL produce identical text (round-trip property).

### Requirement 8: TextMate Grammar Update

**User Story:** As a Mist developer using VS Code, I want newtype clause syntax to be highlighted correctly, so that I get visual cues for the filtering and transformation syntax.

#### Acceptance Criteria

1. THE TextMate grammar SHALL recognize `is` in predicate expressions as a keyword (scope `keyword.operator.comparison.mist`).
2. THE TextMate grammar SHALL recognize `not`, `and`, `or` in predicate expressions as logical keywords (scope `keyword.operator.logical.mist`).
3. THE TextMate grammar SHALL recognize `true` and `false` in where clause predicate position as boolean literals (scope `constant.language.boolean.mist`).

### Requirement 9: Sample Application Update

**User Story:** As a Mist developer, I want the newtypes sample application to demonstrate clause features, so that I can see filtering and transformation patterns.

#### Acceptance Criteria

1. The sample at `samples/Newtypes/Program.mist` SHALL be extended to demonstrate: newtype with single where clause, newtype with compound where clause (and/or/not), newtype with `true`/`false` predicates, newtype with single with clause, newtype with multiple where clauses on same category (union), filtering by type, filtering by modifier, filtering by parameters, transforming types, transforming modifiers, and combined filtering and transformation.
2. The baseline file at `samples/__baselines__/Newtypes.txt` SHALL be updated to include the expected output from the new clause demonstrations.
3. WHEN the updated sample is compiled and run using the Mist compiler, THE System SHALL produce a zero exit code and emit no diagnostic errors or warnings.
