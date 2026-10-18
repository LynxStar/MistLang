# Program Analysis (Stage 5)

The rules that judge a finished program model. Their contracts live in the requirements,
current rule implementations and tests. Outstanding work, deliberate policy limits and
closure evidence are tracked in Carbonek project MIST; see the root `AGENTS.md`.

## Isolation

The same rule as `Source/Mist/ProgramModel/`: this is the only analyzer suite in the tree, and the one it
replaced, classic's `Mist.Analysis`, exists only in git history. Nothing here may reintroduce its types
or be derived from that source; see the clean-room rule in the root `CLAUDE.md`. Classic's diagnostic
ids and messages, as the specs and canonical evidence record them, are observable behaviour and may be matched.

## The contract

- A rule is an `IProgramAnalyzer`. It reads the model and reports. It changes nothing.
- Report through `AnalysisContext.Report`, naming the expressions the finding is **about**. Use positive
  failure evidence to suppress consequential diagnostics: `ErrorType` and `UnresolvedExpression` alone
  do not mean a diagnostic was reported. Value rules retain their indeterminate-type guard; deferred
  fallbacks must report when there is no positive owner. Actual expression refusals live in the per-analysis
  `ExpressionFailures` store; the finished model stays immutable. A deferred diagnostic on an
  `ErrorExpression` is a fallback awaiting that analysis, never evidence of a prior report.
- Failed expressions retain their inputs in `ErrorExpression.Children`. Walk them through `BodyWalker`
  before settling deferred overload/await fallbacks, including nested failures and local initializer
  provenance. Preserve independent findings and report the fallback when no positive owner is found.
  The combined Stage 4/5 gate must still stop emission of a refused model.
- Report at the syntax node the model already carries — `Expression.Node`, `Pattern.Node`, a
  declaration's `DeclarationNode`. There is no `SourceOrigin` type, whatever older docs say.
- Expression-level rules read `AnalysisContext.Nodes`, one walk shared by every rule. The walker is a
  table (`BodyWalker`); a new node kind in the model means a new row, and `BodyWalkerCoverageTest` fails
  until it has one. Never replace the table with reflection — see the class comment for why.
- Scope is every assembly with Mist source, Standard included, exactly as emission's is.
- Anything flow-sensitive stands on `Flow/`: one `FlowGraph` per body root from `FlowGraphBuilder`,
  one forward solver, `FlowSolution`, over whatever `IFlowLattice` the rule defines. A rule that
  walks statements with its own notion of a join breaks the shared-flow-framework invariant.
- What a predicate admits has one home too: `Refinements/` reads a bound predicate into an interval
  set (`Requirement`, `Admitted`) and every proof -- a constant, an implication, a narrowing -- is a
  subset question there. What a receiver gives `this` and what a method needs of it is `ThisTiers`.
- Where predicates are declared has one home as well: `DeclaredPredicates` yields every alias, parameter
  and dependent-parameter predicate -- extension blocks and local functions included -- with the type it
  counts as and how far it is visible. A rule that walks declarations for predicates itself will miss one.
- Where a type is NAMED has one home as well: the binder records every written name it resolves to a
  declared type on `ProgramModel.TypeNamings`, with the type and assembly whose code the name sits in,
  because resolution expands an alias and keeps only what a qualified name reached. A rule about a type
  name reads `AnalysisContext.Namings` (the program pass only), never the resolved types a model holds.
- A target the binder knew but the model dropped — a return's, an argument's, a chain's, an
  initializer entry's — is recorded ON the model (`ReturnStatement.TargetType`,
  `InvocationExpression.ParameterTypes`, `Constructor.ChainParameterTypes`) rather than re-derived by
  a rule. Re-deriving means repeating substitution and overload selection, and disagreeing with the
  binder silently.

## Proving a rule

Every test of a rule follows the testing philosophy, `.claude/skills/testing-philosophy/SKILL.md`
(`/testing-philosophy`), strictly. Each thing a rule reports, and each thing it deliberately does
**not** report, is a test at:

- **Tier 1, by default.** Call the rule's decision with inputs built directly —
  `Assignability.Judge(from, to, interner)` takes two types and returns the diagnostic or null. No
  lexer, no parser, no source text.
- **Tier 2** when the rule walks a body — `Flow/` rules, ownership and borrow checking — over a body
  or graph built by hand, not parsed.
- **Tier 3, rarely**, when the finding exists only over a whole model.

A decision that cannot be called without compiling a program is a rule to refactor, not a reason to
climb a tier.

Every new or migrated rule test declares its tier with `[Trait("Tier", "N")]`, as specified in
`Test/CLAUDE.md` under "Test tier classification". Class traits apply only to classes whose tests
all share a tier; mixed classes use method traits. Existing unclassified tests may remain unmarked.

The one claim that crosses stages is no-cascade — one mistake, one complaint, across Stages 4 and 5.
That is the Stage 4→5 seam: a handful of Tier 4 tests in total, never one per rule or per case.

The samples banked in `AllFeaturesV2` are the false-positive half: a rule that fires on a working
program fails the Tier 6 baseline.
