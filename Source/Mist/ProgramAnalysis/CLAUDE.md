# Program Analysis (Stage 5)

The rules that judge a finished program model. What they are and in what order:
`backlog/analysis-v2-capability-progression.yaml`.

## Isolation

The same rule as `Source/Mist/ProgramModel/`: nothing here may reference `Mist.Semantics` or
`Mist.Analysis`. The second is classic's analyzer suite and is named in the clean-room rule. Classic's
diagnostic ids and messages are observable behaviour and may be matched; its code may not be read.

## The contract

- A rule is an `IProgramAnalyzer`. It reads the model and reports. It changes nothing.
- Report through `AnalysisContext.Report`, naming the expressions the finding is **about**. A value that
  already failed is suppressed there, in one place, so no rule has to remember that the model may hold
  `ErrorType`.
- Report at the syntax node the model already carries — `Expression.Node`, `Pattern.Node`, a
  declaration's `DeclarationNode`. There is no `SourceOrigin` type, whatever older docs say.
- Expression-level rules read `AnalysisContext.Nodes`, one walk shared by every rule. The walker is a
  table (`BodyWalker`); a new node kind in the model means a new row, and `BodyWalkerCoverageTest` fails
  until it has one. Never replace the table with reflection — see the class comment for why.
- Scope is the project's own source, exactly as emission's is. Never Standard.

## Proving a rule

A rule is not done until `refusals/<rule-id>/` holds a case for each thing it reports (see
`refusals/README.md`), and each thing it deliberately does **not** report is pinned by a Tier 3 test.
The 72 banked samples are the false-positive half: a rule that fires on a working program is wrong.
