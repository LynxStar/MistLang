# Requirements Document

## Introduction

Expands the Storm parser memoization subsystem with two complementary mechanisms: an automatic multi-caller alternation heuristic that detects rules attempted multiple times at the same position from different failing branches, and a manual `@memo` annotation that grammar authors place on rule declarations to unconditionally request memoization. Both mechanisms feed the same `MemoizationPlan`, and the existing `IsSpeculativeSafe` filter applies to all candidates regardless of source.

## Glossary

- **Storm_Lexer**: The hand-written lexer (`StormLexer.cs`) that tokenizes Storm grammar source files into `StormToken` values.
- **Storm_Parser**: The hand-written recursive-descent parser (`StormParser.cs`) that consumes Storm tokens and produces a `StormFile` AST.
- **Memoization_Analysis_Pass**: The static analysis pass (`MemoizationAnalysisPass.cs`) that selects rules for packrat memoization based on structural properties of the grammar IR.
- **Memoization_Plan**: The immutable data structure (`MemoizationPlan.cs`) that maps memoized rule names to dense, 0-based integer ids consumed by the parser emitter.
- **Grammar_IR**: The lowered intermediate representation of a complete Storm grammar (`GrammarIR.cs`), holding ordered lists of tokens and rules.
- **Rule_IR**: The lowered representation of a single rule declaration (`RuleIR.cs`).
- **Parser_Emitter**: The code generator (`ParserEmitter.cs`) that emits C# parser source from the Grammar IR and MemoizationPlan.
- **Multi_Caller_Heuristic**: The automatic detection mechanism that identifies rules attempted two or more times at the same input position from different failing branches of a parent alternation.
- **Annotation_Token**: A new lexer token kind representing `@` followed by an identifier (e.g., `@memo`).
- **IsSpeculativeSafe**: The existing filter that excludes rules whose failing invocations could latch parser error state or commit diagnostics.
- **Mist_Grammar**: The Storm grammar file (`Mist.storm`) defining the Mist programming language syntax.

## Requirements

### Requirement 1: Annotation Token Lexing

**User Story:** As a grammar author, I want to write `@memo` before a rule declaration, so that I can explicitly control which rules are memoized.

#### Acceptance Criteria

1. WHEN the Storm_Lexer encounters `@` followed immediately by one or more identifier characters (`[a-zA-Z_][a-zA-Z0-9_]*`), THE Storm_Lexer SHALL produce a single Annotation_Token with the lexeme containing the full text including the `@` prefix (e.g., `@memo`).
2. WHEN the Storm_Lexer encounters `@` not followed by an identifier-start character, THE Storm_Lexer SHALL emit a diagnostic error reporting an unrecognized character and produce an Error token.
3. THE Storm_Lexer SHALL produce the Annotation_Token before any keyword or identifier classification — the `@` prefix distinguishes it from all other token kinds.

### Requirement 2: @memo Annotation Parsing

**User Story:** As a grammar author, I want the parser to recognize `@memo` placed before `rule` declarations and reject it on other declaration kinds, so that annotation usage is validated early.

#### Acceptance Criteria

1. WHEN the Storm_Parser encounters an Annotation_Token with lexeme `@memo` immediately followed by the `rule` keyword, THE Storm_Parser SHALL parse the declaration as an annotated rule and record the memo annotation on the resulting `StormRuleDecl` AST node.
2. WHEN the Storm_Parser encounters an Annotation_Token with lexeme `@memo` followed by `token`, `fragment`, or `hidden`, THE Storm_Parser SHALL emit a diagnostic error indicating that `@memo` is only valid on rule declarations.
3. WHEN the Storm_Parser encounters an Annotation_Token with a lexeme other than `@memo`, THE Storm_Parser SHALL emit a diagnostic error indicating that the annotation is unrecognized.
4. THE Storm_Parser SHALL accept zero or one `@memo` annotation per rule declaration — duplicate `@memo` annotations on the same rule SHALL produce a diagnostic error.

### Requirement 3: Memo Annotation Propagation to IR

**User Story:** As the source generator pipeline, I want `@memo` annotations to propagate from the AST through lowering to the Grammar IR, so that the memoization analysis pass can consume them.

#### Acceptance Criteria

1. WHEN a `StormRuleDecl` has a memo annotation, THE Grammar_IR lowering pass SHALL preserve that annotation on the corresponding Rule_IR.
2. THE Rule_IR SHALL expose a boolean property indicating whether the rule was annotated with `@memo` in the source grammar.

### Requirement 4: Multi-Caller Alternation Heuristic

**User Story:** As a parser performance engineer, I want the system to automatically detect rules that are redundantly attempted at the same position from different failing alternation branches, so that obvious memoization candidates are selected without manual annotation.

#### Acceptance Criteria

1. THE Memoization_Analysis_Pass SHALL identify a rule as a multi-caller candidate WHEN two or more distinct alternatives of any parent rule's alternation contain a reference (direct or transitive through single-path sequences) to that candidate rule.
2. WHEN a rule qualifies as a multi-caller candidate, THE Memoization_Analysis_Pass SHALL add that rule to the memoization candidate set regardless of whether the rule participates in a recursion cycle.
3. THE Multi_Caller_Heuristic SHALL operate as a static structural analysis over the Grammar_IR, requiring no runtime profiling or instrumentation.
4. THE Memoization_Analysis_Pass SHALL apply the Multi_Caller_Heuristic to every rule in the grammar in declaration order, producing a deterministic candidate set across builds.

### Requirement 5: Candidate Union and IsSpeculativeSafe Filter

**User Story:** As the memoization subsystem, I want all candidate sources (existing cycle-based analysis, multi-caller heuristic, and @memo annotations) to merge into a single candidate set filtered by IsSpeculativeSafe, so that memoization never introduces correctness regressions.

#### Acceptance Criteria

1. THE Memoization_Analysis_Pass SHALL compute the final memoization candidate set as the union of: (a) rules identified by the existing cycle-based backtracking analysis, (b) rules identified by the Multi_Caller_Heuristic, and (c) rules annotated with `@memo`.
2. WHEN a candidate rule (from any source) fails the IsSpeculativeSafe check, THE Memoization_Analysis_Pass SHALL exclude that rule from the final MemoizationPlan.
3. WHEN a `@memo`-annotated rule fails the IsSpeculativeSafe check, THE Memoization_Analysis_Pass SHALL emit a diagnostic warning informing the grammar author that the annotation was ignored due to speculative-safety constraints.
4. THE Memoization_Analysis_Pass SHALL assign dense, 0-based ids to the final candidate set in grammar declaration order, producing a deterministic MemoizationPlan.

### Requirement 6: Mist Grammar @memo Application

**User Story:** As a Mist language developer, I want the known hot-path rules annotated with `@memo` in the Mist grammar, so that parsing performance improves for large Mist source files.

#### Acceptance Criteria

1. THE Mist_Grammar SHALL annotate the following rules with `@memo`: Expression, Statement, Primary, Postfix, Unary, TypeReference, TopLevelItem.
2. WHEN the annotated Mist_Grammar is processed by the source generator, THE Memoization_Analysis_Pass SHALL include all seven annotated rules in the MemoizationPlan (assuming all pass IsSpeculativeSafe).

### Requirement 7: Correctness Preservation

**User Story:** As a language implementor, I want memoization to be transparent to parsing results, so that enabling or expanding memoization never changes the AST produced for any valid or invalid input.

#### Acceptance Criteria

1. THE Parser_Emitter SHALL generate memoized rule implementations that produce identical parse results (AST structure, node positions, and diagnostic output) as non-memoized implementations for all inputs.
2. WHEN a memoized rule fails at a given input position, THE generated parser SHALL cache the failure and replay it on subsequent attempts at the same position without re-executing the rule body.
3. WHEN a memoized rule succeeds at a given input position, THE generated parser SHALL cache the result (including the advanced position and produced AST node) and return the cached result on subsequent attempts at the same position.

### Requirement 8: Performance Target

**User Story:** As a Mist language developer, I want expanded memoization to measurably reduce parse times for large grammars, so that the AllFeatures integration test suite completes faster.

#### Acceptance Criteria

1. WHEN the Mist_Grammar uses the expanded memoization (union of heuristic and @memo annotations), THE generated parser SHALL parse the AllFeatures test corpus in under 2 seconds wall-clock time (down from the baseline of approximately 4.3 seconds).
