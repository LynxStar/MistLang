# Design Document: Local Functions

## Overview

A local function is a method-shaped declaration that is a statement. This design adds one grammar
alternative, one model statement kind, one binder, one emitter, and a row in every table that
enumerates statement kinds -- and reuses the method's machinery for everything a method already
does: modifiers, parameters, overload resolution at the call, argument judgement, async return
transform, async promotion, and signature emission.

Three decisions shape it, each stated with its alternative:

1. **A local function is a `Method` in the model, found through the scope chain as a method group
   of one.** The alternative -- a local of `func` type -- would have made every call a function-value
   invocation, which carries no `Parameter`: no `ref`, no `params`, no `owned` provenance, no
   parameter `where`, and no `Method` for async promotion to key on. As a `Method` with no
   `DeclaringType`, every consumer that reads `InvocationExpression.Method` -- ownership,
   refinements, tiers, argument keywords, promotion -- works unchanged, and the accessibility
   analyzer skips it by its own null check.

2. **Names hoist; definite assignment is judged at the call.** C# allows calling a local function
   declared later, and the `no-orphans` snippet needs it (`Caller` calls `GetTask`, declared after).
   Hoisting alone would let a call run before a captured local is assigned, which a lambda cannot
   do (it is created after the assignment or not at all). So a captured read is not judged where it
   is written but where the function is entered: each call site, and each conversion of the name to
   a function value, must have every local the body reads definitely assigned. This is Roslyn's rule
   minus its "assigned inside counts outside" half, which the flow framework already declines for
   lambdas.

3. **The ledger is a v2-only sample.** Classic cannot parse the construct, so the sample cannot be
   baselined from classic. It is banked in `AllFeaturesV2` alone, with its output captured from v2
   and read against the program by hand; that is recorded on the orchestrator and in `CLAUDE.md`.

## Architecture

| Layer | File | Change |
|---|---|---|
| Grammar | `Source/Mist/Grammar/Mist.storm` | `LocalFunctionDeclaration` rule; first alternative of `Statement` |
| Model | `ProgramModel/Model/Statements/LocalFunctionStatement.cs` | new statement kind holding the `Method` and its parameter locals |
| Scope | `ProgramModel/Model/Scopes/BodyScope.cs` | local functions declared beside locals; `MethodGroupResult` on lookup |
| Binder | `ProgramModel/Construction/Binding/LocalFunctionBinder.cs` | signature at hoist time, body at the statement |
| Binder | `BodyBinder.BindList`, `StatementBinder.BindStatement` | hoist pre-pass; dispatch |
| Emitter | `Backends/CSharpTranspiler/Emitter/LocalFunctionEmitter.cs` | the C# local function |
| Emitter | `BodyEmitter`, `MethodEmitter`, `ParameterEmitter`, `LocalNames` | dispatch; shared return-type spelling; name selector; renaming |
| Lowering | `AsyncPromotion` | local functions join the retroactive scan |
| Analysis | `BodyWalker`, `Flow/FlowGraphBuilder`, `Captures` | row; nested graph; own names |
| Analysis | `DefiniteAssignmentAnalyzer`, `LocalFunctionReads` | call-site judgement |
| Analysis | `OwnershipAnalyzer`, `RefinementAnalyzer`, `EscapeAnalyzer`, `PositionAnalyzer` | nested-graph and capture rows |
| Parser | `Parsing/MistPrettyPrinter.cs` | prints the declaration |
| Diagnostics | `ProgramModelDiagnostics`, `ProgramAnalysisDiagnostics` | five descriptors, one message widened |

## Detailed Design

### 1. Grammar

```
rule LocalFunctionDeclaration : HasModifiers
    = modifiers:Modifier* returnType:TypeReference name:IDENTIFIER typeParams:TypeParameterList?
      LEFT_PARENTHESIS parameters:ParameterList? RIGHT_PARENTHESIS
      constraints:WhereClause*
      body:Block
    ;
```

Placed as the FIRST alternative of `Statement`. No existing statement has the shape
`Modifier* TypeReference IDENTIFIER (`: a declaration has `=` or `;` after the name, an expression
statement has no identifier after a type-shaped prefix, `readonly {` fails at the type, and every
other statement starts with a keyword that is not a modifier. PEG ordered choice therefore takes
this alternative only for a local function and backtracks for everything else; `Statement` is
`@memo`, so the extra attempt is paid once per position.

`typeParams` and `constraints` are admitted by the grammar so that the binder can refuse a generic
local function with a clear diagnostic, and so a parameter `where` (adhoc-refinement) works. A
return ownership specifier, `with` clauses and attributes are not admitted; they fail to parse,
and the spec says so (Req 1.4).

`: HasModifiers` gives the generated node `IHasModifiersNode`, which is what `ModifierBuilder.Build`
takes. The body is `Block` rather than `CallableBody`: a local function always has a body.

Storm generates `LocalFunctionDeclarationNode` and the wrapper
`LocalFunctionDeclarationStatementStatementNode { LocalFunction }`.

### 2. Model

```csharp
internal sealed class LocalFunctionStatement(Method function, IReadOnlyList<LocalVariable> parameters) : Statement
{
    public Method Function { get; }                         // DeclaringType null, Body set, DeclarationNode the AST node
    public IReadOnlyList<LocalVariable> Parameters { get; } // the body scope's locals, index-aligned with Function.ParametersList
    public Block Body => Function.Body!;
}
```

`Parameters` exists for the same reason `LambdaExpression.Parameters` does: the flow graph's
Introduce steps and the emitter's renaming work on `LocalVariable`, and a `Parameter` is not one.

### 3. Scope

`BodyScope` gains a second dictionary, `functions`, and `DeclareLocalFunction(Method, DiagnosticBag)`.
A name is refused if either dictionary already holds it: two functions report
`DuplicateLocalFunction`; a local after a function reports `DuplicateLocalVariable` (hoisting
declares the function first, so that is the only order). `LookupSelf` answers locals first, then
`new MethodGroupResult([function])`. The scope also keeps the `Method` built for each declaration
node, so the statement binder finds its own function even when its name was refused as a duplicate.

### 4. Binder

**Hoist pass.** `BodyBinder.BindList` calls `LocalFunctionBinder.Declare(nodes, bindingCtx, context)`
before the statement loop. For each `LocalFunctionDeclarationStatementStatementNode`:

- `ModifierBuilder.Build(node, diagnostics, declaringType: null)`; then each of `IsSealed`,
  `IsIntrinsic`, `IsReadonly`, `IsOwned`, `IsRef`, `Discipline` set reports
  `LocalFunctionModifier` with the modifier's spelling.
- `TypeParams` present reports `LocalFunctionGeneric`; constraints are then not resolved, so a
  `where T : ...` does not cascade.
- Parameters through a `ParameterBuilder(context.Resolver, context.Attributes, context.Diagnostics)`
  over the enclosing scope; the return type through `context.Resolver`; `async` wraps it with
  `ConstructTask` exactly as `MethodBuilder` does.
- Parameter `where` clauses through a `ConstraintResolver(context, context.Resolver)`, which makes
  the `where` the parameter's type as it does for a method.
- `new Method(name, returnType, parameters, modifiers) { DeclarationNode = node, Body = new Block(node.Body), Scope = ctx.Scope }`,
  declared in `ctx.Scope`.

The same list is bound as one unit whether it is a block body, a `with` tail, or the top-level
statements, because `BindList` is the one place every statement list passes through.

**Body pass.** `StatementBinder.BindStatement` dispatches the node to `LocalFunctionBinder.BindBody`,
which mirrors `BodyBinder.BindDelegate`: a `BodyScope` under the declaring scope, one
`LocalVariable` per parameter (`BodyBinder.ParameterLocal`), and a `BindingContext` copied from the
enclosing one -- so whatever the context carries is inherited -- then overridden:
`ReturnType` is the declared return type unwrapped from the task for an `async` function,
`IsAsyncContext` is the function's own `async` and not the enclosing body's, and `EnclosingMethod`
is the function's `Method`, so `AwaitUses` records the function's own awaits and calls against it.

**Calls.** Nothing new. `IdentifierBinder` maps the `MethodGroupResult` to a `MethodGroupExpression`
with no receiver; `PostfixBinder.BindInvocation` selects the one candidate, binds and settles the
arguments, records the call, and applies `AutoAwait` for a sync context. A bare name in a
function-typed position converts through `MethodGroupConversion` to a `FunctionReferenceExpression`.

### 5. Emission

`BodyEmitter.EmitStatement` dispatches to `LocalFunctionEmitter.Emit`:

```
[static ][async ]<return type> <name>(<parameters>)
{
    <body>
}
```

- `static` iff `Modifiers.IsStatic`; `async` iff `AsyncPromotion.EmitsAsync(function)`; never an
  accessibility modifier.
- The return type is spelled by `MethodEmitter.EmitReturnType`, extracted from `EmitSignature` so
  the two cannot disagree: the promoted task wrap, `void`, and the `ref` keyword.
- Parameters through `ParameterEmitter.Emit` with a name selector, since a parameter may have been
  renamed apart (below); the method path passes none and spells `Parameter.Name`.
- The body through `BodyEmitter.EmitMethodBody`, which emits the owned-parameter drops and then the
  block. `blockingAwaits` is false inside it whatever encloses it: a local function can be made
  async on its own, so an await inside it is a real one even inside a sync lambda.
- The name is spelled through `LocalNames.Of(Method)` here and at `MethodReferenceExpression` and
  `FunctionReferenceExpression`, which is the whole of Req 7.6.

**Renaming.** `LocalNames.Walk` gains a statement case: the function's name enters the current
scope's names, its parameters are declared in a header scope, and its body walks under that. Local
functions are collected beside locals and renamed by the same rule, exposed as `Of(Method)`; a type
method is never in the map and spells as itself.

**Async promotion.** `AsyncPromotion.Compute`'s retroactive scan enumerates local functions too --
every `LocalFunctionStatement` under each method body and under a top-level statement body -- so a
sync local function holding a task it must complete at scope exit is promoted. The propagation
over `AwaitUses.Callers` already includes local functions, because their bodies were bound with
`EnclosingMethod` set to them.

### 6. Analysis

- **`BodyWalker`**: `add<LocalFunctionStatement>(s => [s.Body])`.
- **`FlowGraphBuilder`**: the body is a nested graph under `Nested`, keyed by the statement, with
  an Introduce step per parameter; the statement gets one Evaluate step at its declaration so
  rules reach the nested graph as they reach a lambda's.
- **`Captures`** (renamed from `LambdaCaptures`, since it now serves two closures): `Inside(object body)`
  and `OwnNames(parameters, inside)`, with a row so a nested local function's parameters are its
  own names.
- **`EscapeAnalyzer`**: a non-static local function's captures are judged as a lambda's, reported
  under `CapturedBinding`, whose message now says which closure.
- **`PositionAnalyzer`**: a static local function's captures report
  `StaticLocalFunctionCapture`, the local-function twin of `StaticLambdaCapture`.
- **`OwnershipAnalyzer`, `RefinementAnalyzer`**: the nested graph is judged from the state at the
  declaration, as a lambda's is. A call before the declaration therefore sees the declaration's
  state; this is the lambda's own gap and is recorded as such.
- **`DefiniteAssignmentAnalyzer`**: per body root, `LocalFunctionReads.Of(root)` computes for each
  local function the enclosing locals its body references without declaring, unioned with the
  sets of every local function it invokes or names, to a fixpoint. Then:
  - at an `InvocationExpression` whose receiver is a `MethodReferenceExpression` to a local
    function, and at a `FunctionReferenceExpression` to one, every tracked local in the set that
    is not in the state reports `LocalFunctionReadsUnassigned`, and the transfer adds the set to
    the state so one unassigned local is one complaint per path;
  - the nested graph is judged with every tracked local taken as assigned at entry, so a captured
    read is never reported inside the body -- the call sites cover it -- while the body's own
    `int y;` is removed by its Declare step and judged as anywhere.
  The read set is flow-insensitive within a body: a function that assigns a captured local before
  reading it still counts as reading it, which refuses a call made while that local is unassigned.
  That is conservative and is the spec's stated limit (Req 5.4 and its neighbour).

### 7. Classic

The parser is shared; classic's builder receives the new statement node and must not throw. This
is verified by compiling a local-function program on classic and observing a diagnostic. Classic's
code is not read (clean-room rule).

### 8. Pretty Printer

`MistPrettyPrinter.EmitStatement` gains a case printing modifiers, return type, name, type
parameters, parameters, constraints and the block, reusing the method's helpers.

## Diagnostics

| Id | Stage | Message |
|---|---|---|
| `MIST_PM_LOCAL_FUNCTION_MODIFIER` | construction | A local function cannot be declared '{0}'. |
| `MIST_PM_LOCAL_FUNCTION_GENERIC` | construction | Local function '{0}' cannot declare type parameters. |
| `MIST_PM_DUPLICATE_LOCAL_FUNCTION` | construction | A local function named '{0}' is already declared in this scope. |
| `MIST_PA_STATIC_LOCAL_FUNCTION_CAPTURE` | analysis | A static local function cannot reference {0} '{1}'. |
| `MIST_PA_LOCAL_FUNCTION_READS_UNASSIGNED` | analysis | Local function '{0}' reads '{1}', which is not definitely assigned at this call. |
| `MIST_PA_CAPTURED_BINDING` (existing) | analysis | Cannot capture {0} variable '{1}' in a {2}: {3}. |

## Testing Strategy

- **Tier 3, `LocalFunctionDecisionTests`** (`ProgramModelProbe`): what binds -- the invocation's
  `Method`, the async return type, the auto-await in a sync context and its absence in an async
  one, the function reference -- and what is deliberately admitted: a call before the declaration,
  recursion, mutual recursion, captures, `public`, `static` without captures, the definite
  assignment admits (Req 5.5, 5.4), nesting in lambdas and functions, a parameter `where`.
- **Tier 3, `LocalFunctionEmissionTests`**: emitted text -- `public` dropped, `static` kept, `async`
  and the task type, a promoted sync function and the `await` at its call, a shadowing parameter
  and a shadowing name renamed apart, a top-level file emitting the function among its statements.
- **Tier 3, `FlowGraphTests`**: the body is a nested graph with Introduce steps for its parameters.
- **Tier 5, `RefusalCorpusTest`**: one case per refused shape under `refusals/binding/`,
  `refusals/flow-framework/`, `refusals/operator-and-positional-checks/` and
  `refusals/ownership-borrow-checking/`.
- **Tier 6**: `samples/LocalFunctions` in `AllFeaturesV2`.
