# Implementation Plan: Local Functions

## Overview

Grammar first, so the node exists; then the model and scope; then the binder; then the tables every
statement kind must appear in; then emission; then the analysis rules; then the proof. Verification
is batched per phase as `Source/Mist/ProgramModel/AGENTS.md` asks.

## Tasks

- [x] 1. Grammar
  - [x] 1.1 Add `rule LocalFunctionDeclaration : HasModifiers` to `Source/Mist/Grammar/Mist.storm` and
        make it the first alternative of `Statement` as `LocalFunctionDeclarationStatement`
    - _Requirements: 1.1, 1.2, 1.3, 1.4, 1.5_
  - [x] 1.2 Build; zero STORM warnings; confirm the generated `LocalFunctionDeclarationNode` and
        `LocalFunctionDeclarationStatementStatementNode`
  - [x] 1.3 Amend `specs/top-level-statements/requirements.md` Req 1.2 to list local function
        declarations
    - _Requirements: 1.2_

- [x] 2. Model and scope
  - [x] 2.1 Add `LocalFunctionStatement` under `ProgramModel/Model/Statements/`
  - [x] 2.2 Add local functions to `BodyScope`: `DeclareLocalFunction`, lookup as a one-candidate
        method group, the per-node map, and the two duplicate refusals
    - _Requirements: 3.1, 3.2, 3.4, 3.5, 3.6_
  - [x] 2.3 Add the three construction descriptors to `ProgramModelDiagnostics`

- [x] 3. Binder
  - [x] 3.1 Add `LocalFunctionBinder` with `Declare` (hoist pass: modifiers, refusals, parameters,
        return type, `where` clauses, the `Method`) and `BindBody` (scope, parameter locals, the
        overridden context)
    - _Requirements: 2.1, 2.3, 2.4, 2.5, 3.8, 4.1, 4.2, 4.4, 4.5, 6.1, 6.2_
  - [x] 3.2 Call `Declare` from `BodyBinder.BindList` before the loop; dispatch the statement from
        `StatementBinder.BindStatement`; make `BodyBinder.ParameterLocal` reachable
    - _Requirements: 3.1, 3.3, 4.6_
  - [x] 3.3 Build; run a local-function program on v2 to a bound model (the CLI, `--pipeline experimental`)

- [x] 4. Tables
  - [x] 4.1 `BodyWalker` row; `BodyWalkerCoverageTest` green
  - [x] 4.2 `FlowGraphBuilder` nested graph with parameter Introduce steps and an Evaluate step
  - [x] 4.3 `Captures` (renamed from `LambdaCaptures`): `Inside(object)`, `OwnNames(parameters, inside)`, row for a nested local function
  - [x] 4.4 `LocalNames`: statement case, function renaming, `Of(Method)`
    - _Requirements: 7.5_
  - [x] 4.5 `MistPrettyPrinter.EmitStatement` case

- [x] 5. Emission
  - [x] 5.1 Extract `MethodEmitter.EmitReturnType`; add a name selector to `ParameterEmitter.Emit`
        and to `BodyEmitter.EmitMethodBody`'s parameter drops
  - [x] 5.2 Add `LocalFunctionEmitter`; dispatch from `BodyEmitter.EmitStatement`; `blockingAwaits`
        false inside; spell `MethodReferenceExpression` and `FunctionReferenceExpression` through
        `LocalNames.Of(Method)`
    - _Requirements: 7.1, 7.2, 7.3, 7.4, 7.6_
  - [x] 5.3 `AsyncPromotion.Compute`: enumerate local functions in the retroactive scan, under
        method bodies and the top-level body
    - _Requirements: 6.5_
  - [x] 5.4 Build; run the restored `fire-and-forget` shape end to end on v2

- [x] 6. Analysis
  - [x] 6.1 `EscapeAnalyzer`: judge a non-static local function's captures; widen the `CapturedBinding`
        message to name the closure kind
    - _Requirements: 4.3_
  - [x] 6.2 `PositionAnalyzer`: `StaticLocalFunctionCapture` for a static local function
    - _Requirements: 2.2_
  - [x] 6.3 `OwnershipAnalyzer`, `RefinementAnalyzer`: nested-graph rows
  - [x] 6.4 `LocalFunctionReads` and the `DefiniteAssignmentAnalyzer` call-site judgement, body entry
        with tracked locals taken as assigned, `LocalFunctionReadsUnassigned`
    - _Requirements: 5.1, 5.2, 5.3, 5.4, 5.5_
  - [x] 6.5 Build; full test suite

- [x] 7. Proof
  - [x] 7.1 Refusal cases: `binding/` (modifier, generic, duplicate function, local after function,
        wrong arity), `flow-framework/` (call before capture assigned, through another function,
        function reference before capture assigned, own local read), `operator-and-positional-checks/`
        (static captures a local, a field), `ownership-borrow-checking/` (captures an owned binding,
        a borrowed binding); `RefusalCorpusTest` green
    - _Requirements: 9.4_
  - [x] 7.2 `LocalFunctionDecisionTests` (Tier 3) for what binds and what is admitted
    - _Requirements: 9.4_
  - [x] 7.3 `LocalFunctionEmissionTests` (Tier 3) on emitted text, including a top-level file
    - _Requirements: 7.2, 7.3, 7.5, 9.4_
  - [x] 7.4 `FlowGraphTests`: a local function body is a graph of its own
  - [x] 7.5 Classic: compile a local-function program on classic; a diagnostic, no exception
    - _Requirements: 8.1_
  - [x] 7.6 `samples/LocalFunctions`; reference and call from `AllFeaturesV2`; baseline lines appended and
        read against the program; `AllFeatures` unchanged; both baselines identical
    - _Requirements: 9.1, 9.2, 9.5_
  - [x] 7.7 Restore the ten snippets from `3f24c741`; `dotnet run --project tools/SnippetEmitter` on
        both chapters, zero failed
    - _Requirements: 9.3_
  - [x] 7.8 Book: local-functions section in the Methods chapter with a snippet pair
    - _Requirements: 10.1_

- [x] 8. Records
  - [x] 8.1 [`backlog/emitter-v2-capability-progression.yaml` at da1aaa8f](https://github.com/LynxStar/MistLang/blob/da1aaa8fd416bd3bdf93e496281e7b2cbf52594c/backlog/emitter-v2-capability-progression.yaml): capability `local-functions` (order 187)
        and sample `LocalFunctions`; YAML parses
  - [x] 8.2 [`backlog/analysis-v2-capability-progression.yaml` at da1aaa8f](https://github.com/LynxStar/MistLang/blob/da1aaa8fd416bd3bdf93e496281e7b2cbf52594c/backlog/analysis-v2-capability-progression.yaml): evidence on orders 9, 10 and 11
  - [x] 8.3 [`backlog/program-model-body-tasks.yaml` at da1aaa8f](https://github.com/LynxStar/MistLang/blob/da1aaa8fd416bd3bdf93e496281e7b2cbf52594c/backlog/program-model-body-tasks.yaml), [`backlog/general.md` at da1aaa8f](https://github.com/LynxStar/MistLang/blob/da1aaa8fd416bd3bdf93e496281e7b2cbf52594c/backlog/general.md), `CLAUDE.md` and the
        orchestrator test's comment: the v2-only sample and the restored snippets
    - _Requirements: 9.2, 9.6_
  - [x] 8.4 `dotnet format Mist.slnx --verify-no-changes`; zero warnings; full suite green
