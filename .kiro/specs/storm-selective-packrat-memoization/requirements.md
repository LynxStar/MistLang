# Requirements Document

## Introduction

The Storm source generator emits a recursive-descent, PEG ordered-choice parser from `Mist.storm`. The emitted parser performs no memoization, so any `(rule, position)` pair can be re-entered an exponential number of times during backtracking. A measured investigation proved a single `(TypeArgument, position)` pair is entered `2^(N-1)` times for an `N`-element type-argument list, making large generic signatures parse in exponential time.

This feature adds **selective packrat memoization** to the generated parser. A new static analysis pass (`MemoizationAnalysisPass`) selects a small set of rules (~15-20 of ~150) using a pure structural heuristic over the grammar IR, with no runtime profiling. `ParserEmitter` then wraps each selected rule's parse method with a per-parse memo-table probe/store: a cache hit restores the saved end position and returns the cached subtree in O(1) without re-running the body or re-emitting diagnostics; a miss runs the existing body and stores the result.

These requirements are derived from the approved design document (`design.md`) and are organized into six groups that the design's Correctness Properties reference: Output Preservation (1.x), Soundness (2.x), Heuristic Selection (3.x), Performance (4.x), Determinism (5.x), and Quality (6.x). The non-negotiable bar is that the feature eliminates the exponential while producing structurally identical AST, diagnostics, and emitted C# across the full suite and the AllFeatures sample.

## Glossary

- **Storm_Generator**: The Storm source generator that emits a recursive-descent PEG parser (`MistParser.g.cs`) from the `Mist.storm` grammar.
- **Memoization_Analysis_Pass**: The new pure static analysis component that consumes the lowered grammar IR and produces a Memoization_Plan. Runs after IR lowering and before parser emission.
- **Memoization_Plan**: The immutable result of the Memoization_Analysis_Pass: the ordered set of memoized rule names plus a stable integer id per memoized rule.
- **Parser_Emitter**: The Storm_Generator component that emits the generated parser's rule-parsing methods.
- **Generated_Parser**: The emitted recursive-descent parser runtime (`MistParser.g.cs`) that the Mist compiler consumes.
- **Memo_Table**: The per-parse, thread-local cache in the Generated_Parser that maps a packed `(rule id, token position)` key to a `(node-or-failure, end position)` value.
- **Emission_Context**: The object that wraps the lowered grammar IR and carries the Memoization_Plan to the Parser_Emitter.
- **Memoized_Rule**: A grammar rule selected by the Memoization_Analysis_Pass and emitted with a probe/store wrapper.
- **Recursion_Cycle**: A set of rules where a rule is reachable from itself through one or more rule references (a self-edge, or a strongly connected component of size greater than one).
- **Backtracking_Construct**: A grammar construct that can cause re-attempt of a position: a rule with more than one alternative (ordered choice), an optional (`?`), a zero-or-more repetition (`*`), or a one-or-more repetition (`+`).
- **Speculative_Safe**: A property of a rule whose failing invocation leaves the parser error flag unset and produces zero net change to the diagnostic bag, and whose succeeding invocation produces zero net change to the diagnostic bag.
- **Pre_Feature_Baseline**: The behavior of the Storm_Generator and Generated_Parser immediately before this feature is added, used as the comparison oracle.
- **Token_Cursor**: The Generated_Parser's current token index (`_pos`).
- **AllFeatures_Sample**: The Mist sample program exercising the full language surface, used as an end-to-end emission oracle.

## Requirements

### Requirement 1: Output Preservation

**User Story:** As a Mist compiler maintainer, I want the memoized parser to produce output identical to the pre-feature parser, so that adding memoization carries zero correctness risk.

#### Acceptance Criteria

1. WHEN the Generated_Parser parses a Mist source file that parsed successfully on the Pre_Feature_Baseline, THE Generated_Parser SHALL produce an abstract syntax tree structurally equal to the Pre_Feature_Baseline tree for that file, where "structurally equal" means the same node type at each position, the same parent-child nesting with the same left-to-right ordering of children, and the same source-token span for each node, disregarding reference identity.
2. WHEN the Generated_Parser parses a Mist source file, THE Generated_Parser SHALL produce a diagnostic sequence identical to the Pre_Feature_Baseline diagnostic sequence for that file, where "identical" means the same total diagnostic count and, at each ordinal position in the sequence, the same severity, diagnostic code, source span, and message text.
3. WHEN the Mist compiler emits C# for the AllFeatures_Sample, THE Mist compiler SHALL produce emitted C# byte-for-byte identical to the Pre_Feature_Baseline emitted C#.
4. WHEN the full Mist test suite runs against the Generated_Parser, THE test suite SHALL report a passing-test count no lower than the Pre_Feature_Baseline passing-test count, with zero tests transitioning from passing on the Pre_Feature_Baseline to failing with the feature.
5. WHERE the Memoization_Plan contains zero rules, THE Parser_Emitter SHALL emit parser source byte-for-byte identical to the Pre_Feature_Baseline parser source.

### Requirement 2: Soundness

**User Story:** As a Mist compiler maintainer, I want every memoized rule to be position-pure and speculative-safe and the cache to be scoped per parse, so that memoization cannot alter parse results or diagnostics.

#### Acceptance Criteria

1. WHEN a Memoized_Rule is invoked twice at the same Token_Cursor position over the same token stream, THE Generated_Parser SHALL return the same node reference and leave the Token_Cursor at the same end position for both invocations.
2. WHEN a Memo_Table cache hit occurs for a Memoized_Rule, THE Generated_Parser SHALL restore the Token_Cursor to the cached end position and return the cached result — a node reference on a cached success or null on a cached failure — without executing the rule body.
3. WHEN a Memo_Table cache hit occurs for a Memoized_Rule, THE Generated_Parser SHALL make no addition to or removal from the diagnostic bag and SHALL leave the parser error flag value unchanged from its value at invocation entry.
4. IF a candidate rule is not Speculative_Safe — that is, a failing invocation can leave the parser error flag set or produce a net change to the diagnostic bag, or a succeeding invocation can produce a net change to the diagnostic bag — THEN THE Memoization_Analysis_Pass SHALL exclude that rule from the Memoization_Plan.
5. WHILE the parser error flag is set when checked at entry to a Memoized_Rule, THE Generated_Parser SHALL return null without probing or storing the Memo_Table and without advancing the Token_Cursor, evaluating the error flag exactly once at entry and not re-checking it before the probe within the same invocation.
6. WHEN a Memoized_Rule invocation fails at a given position, THE Generated_Parser SHALL store the failure as a cached entry whose end position equals the entry position.
7. WHEN a `Parse()` call begins, THE Generated_Parser SHALL allocate a fresh Memo_Table scoped to that single `Parse()` call.
8. THE Memo_Table SHALL be thread-local and SHALL restrict each cached entry to the `Parse()` call that produced it.
9. THE Generated_Parser SHALL generate a distinct Memo_Table key for each distinct `(Memoized_Rule id, token position)` pair, such that no two distinct pairs map to the same key for any parse of up to 2,147,483,647 tokens.

### Requirement 3: Heuristic Selection

**User Story:** As a Mist compiler maintainer, I want the heuristic to mark exactly the rules on backtracking recursion cycles, so that the type-argument exponential is eliminated without over-including rules.

#### Acceptance Criteria

1. THE Memoization_Analysis_Pass SHALL include in the Memoization_Plan every rule that participates in a Recursion_Cycle whose member rules include at least one Backtracking_Construct.
2. IF a set of rules forms a detected Recursion_Cycle containing at least one Backtracking_Construct in the analyzed grammar, THEN THE Memoization_Analysis_Pass SHALL include each rule of that set in the Memoization_Plan by the predicate of criterion 3.1, independent of the rules' names, such that the rules `TypeArgument`, `TypeReference`, `SingleTypeRef`, and `TypeArgumentList` are included when they form such a cycle.
3. WHEN building the rule-reference graph, THE Memoization_Analysis_Pass SHALL include every rule reference that appears anywhere in a rule body at any nesting depth, including references inside sequence, optional, repetition, and alternation constructs.
4. IF a rule does not participate in any Recursion_Cycle, THEN THE Memoization_Analysis_Pass SHALL exclude that rule from the Memoization_Plan.
5. IF a rule's only Recursion_Cycle membership is in cycles whose member rules contain no Backtracking_Construct, THEN THE Memoization_Analysis_Pass SHALL exclude that rule from the Memoization_Plan.
6. IF a rule has a self-edge and its own body contains a Backtracking_Construct, THEN THE Memoization_Analysis_Pass SHALL include that rule in the Memoization_Plan.

### Requirement 4: Performance

**User Story:** As a Mist developer, I want parse time to grow linearly with type-argument list size, so that large generic signatures compile quickly instead of timing out.

#### Acceptance Criteria

1. WHEN the Generated_Parser parses a `Func`/`Action` arity-overload input of size N for each N in 0 through 16, THE Generated_Parser SHALL complete the lex-and-parse of each N step within 10,000 milliseconds, completing the full N=0 through N=16 sweep without hitting the per-step bail-out that the Pre_Feature_Baseline exponential behavior triggers.
2. WHEN the `FuncActionArityOverloads_ShouldNotGrowExponentially` test runs, THE test SHALL measure lex-and-parse elapsed time for each N in 0 through 16 and SHALL complete every step within the 10,000-millisecond per-step bound with no step hitting the bail-out.
3. WHEN the `TypeParameterParsing_ShouldNotGrowExponentially` test runs, THE test SHALL complete the lex-and-parse of each N step in 0 through 20 within the 10,000-millisecond per-step bound with no step hitting the bail-out.

### Requirement 5: Determinism

**User Story:** As a Mist compiler maintainer, I want generated parser output to be deterministic across builds, so that builds are reproducible and diffs are stable.

#### Acceptance Criteria

1. WHEN the Memoization_Analysis_Pass produces a Memoization_Plan, THE Memoization_Analysis_Pass SHALL assign each Memoized_Rule a contiguous integer id in the range 0 through Count minus 1 with no gaps, where Count is the number of Memoized_Rules in the Memoization_Plan.
2. WHEN the Memoization_Analysis_Pass assigns Memoized_Rule ids, THE Memoization_Analysis_Pass SHALL assign id 0 to the Memoized_Rule declared earliest in grammar declaration order and increasing ids to each subsequent Memoized_Rule in grammar declaration order.
3. WHEN the Memoization_Analysis_Pass assigns Memoized_Rule ids, THE Memoization_Analysis_Pass SHALL assign each id to exactly one Memoized_Rule such that no two Memoized_Rules share the same id.
4. WHEN the Storm_Generator generates the parser two or more times from grammar source that is byte-for-byte identical across those builds, THE Storm_Generator SHALL assign each Memoized_Rule an identical id across all of those builds.
5. WHEN the Storm_Generator generates the parser two or more times from grammar source that is byte-for-byte identical across those builds, THE Storm_Generator SHALL emit parser source that is byte-for-byte identical across all of those builds.

### Requirement 6: Quality

**User Story:** As a Mist compiler maintainer, I want the feature to introduce no build warnings, so that the zero-warnings policy is upheld.

#### Acceptance Criteria

1. WHEN the Storm_Generator is built with the feature using the same build configuration and analyzer settings as the Pre_Feature_Baseline, THE build SHALL emit zero `STORM*` warnings that were absent from the Pre_Feature_Baseline build, such that the total `STORM*` warning count does not exceed the Pre_Feature_Baseline `STORM*` warning count.
2. WHEN the Mist consumer is built against the Generated_Parser using the same build configuration and analyzer settings as the Pre_Feature_Baseline, THE build SHALL emit zero `CS*` warnings that were absent from the Pre_Feature_Baseline build, excluding trim warnings originating from third-party packages, such that the comparable `CS*` warning count does not exceed the Pre_Feature_Baseline `CS*` warning count.
3. THE feature SHALL satisfy criteria 6.1 and 6.2 without introducing any `STORM*` or `CS*` warning suppression that was absent from the Pre_Feature_Baseline.
