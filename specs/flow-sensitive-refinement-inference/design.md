# Flow-sensitive refinement inference: design

This design delivers [requirements.md](requirements.md). It replaces the lattice
in `Source/Mist/ProgramAnalysis/Rules/RefinementFlow.cs` with one whose state is
the bag of Requirement 4, adds the chain of Requirement 1 as the way every
refinement proof is decided, and keeps each proof site in `RefinementAnalyzer`
where it is. Nothing here changes the grammar or the emitted C#. Where this
document names a number such as a cap, the number is a starting point to be
measured, and it lives in one place in the code.

## Shape of the change

Today the flow pass keeps, per local or field of `this`, an interval set
(`Admitted`), a set of "below this holder's Length" bounds, a literal, a real
interval set and slice knowledge, all in `Knowledge` (RefinementFlow.cs:15). A
proof is a subset test over the interval set plus a lookup of a bound
(RefinementAnalyzer.cs:601-629). A predicate whose operand is an expression over
two names, `count <= items.Length - start`, reads as null and every use of it is
refused as unreadable (Requirement.cs:29-51, RefinementFindings.cs:72).

After this change the state is a bag of facts about values. A proof adds the
denial of the contract to the bag and chains. The pieces:

- `Source/Mist/ProgramAnalysis/Facts/`: the value, the holder, the term reader,
  the fact, the bag, the chain, the budget and the exactness rules. Nothing in
  the folder depends on the flow framework or the analyzer, so every piece is
  callable from a Tier 1 test with hand-built inputs. Files stay small; the
  folder is the unit, not one class.
- `Rules/FactFlow.cs`: the `IFlowLattice` over `FactState`, replacing
  `RefinementFlow`. `RangeKnowledge` and `RealKnowledge` are rekeyed from holders
  to values and otherwise kept.
- `Rules/RefinementAnalyzer*.cs`: the proof sites stay. `Need` builds a denial and
  asks the chain instead of testing subsets.
- `Flow/FlowSolution.cs`: one new lattice member, `Widen`, with a default body,
  so the six other lattices are untouched.
- Diagnostics: MIST0271 to MIST0277 gain the failing case. MIST0485 is new.

## Values and holders (Requirement 3)

A `Value` is a reference-identity object carrying its `IMistType`, a display name
and the node that created it. "x@3" in a message means the value `x` took at
line 3. Every Declare, Introduce, Bind and assignment creates one, and so does a
call for every holder it may write.

A holder is what a name refers to: a `LocalVariable`, a `Field` of `this`, or a
`MemberHolder(Value receiver, object member)` for a member reached through a
value, which is how `backing.Length` and `items.Length` are named. `Bindings` is
an immutable map from holder to current value. Reading a holder yields its
current value. A holder with no binding gets a fresh value with its type's
facts, and the binding is recorded so the next read yields the same value.

Which holders carry facts beyond their type (Requirement 3.5): locals and
parameters not in `UnseenWrites.Of(root)`; fields of `this`, given new values
when `FieldEffects.MayWrite` fires (Requirement 3.6); and members whose value
cannot change while the receiver value is the same object: the `Length` of
`Array`, `Span`, `ReadOnlySpan` and `string`, readonly fields, and the members
`StableMembers.IsStable` admits. A holder in `UnseenWrites` yields a fresh value
at every read, as does a mutable member of an object the body does not own. This
is the conservative reading of 3.5. Extending fact-bearing members to objects the
body exclusively borrows, through `LocalOwnership`, is a later step that changes
no fact shape. `FieldEffects` answers yes or no for all mutable fields at once;
`ThisTiers.FieldUses` is the per-field summary available when that granularity
is wanted, also later.

The `Length` holder is the dependent type parameter `TypeResult` that
`RangeKnowledge.IsLength` recognizes (RangeKnowledge.cs:25-36) and string's
intrinsic `Length`. Its value's facts come from `Lengths.Typed` when the type
fixes the length, from `Lengths.Of` at a creation, and from `SequenceLengths` for
a copy or a slice, all as today, now written as facts about the length value.

## Terms, facts and bags (Requirements 1, 2, 7)

A `Term` is a sum: an immutable sorted map from value to whole-number multiplier,
plus a constant. `Terms.Read(Expression, FactState)` turns an expression into a
term when it has the shape Requirement 2.1 allows: a constant
(`Requirement.Constant`, `EnumConstants`), a read of a holder, `+` or `-` of two
readable operands, `*` with one constant operand, and intrinsic negation, with
`BinaryExpression.Operator` null or intrinsic. Anything else reads as null:
`/`, `%`, a call, a user operator. The expression's result is then a fresh value
with its type's facts (Requirement 2.6).

A `Fact` is a term and a relation: `term == 0`, `term <= 0`, or `term < 0` for
the real family only. `a <= b - 1` is stored as `a - b + 1 <= 0`. A fact is kept
normalized: multipliers divided by their common factor, and for an integral fact
the constant rounded toward the side that keeps every whole-number solution.
That rounding is the one place integers are treated differently from reals, and
it is what turns `2 * capacity + 1 <= capacity` into `capacity <= -1`.

An `Alternative` is an immutable list of facts true together. A `Bag` is
`Common`, the facts true under every alternative, and `Alternatives`, each
holding the facts true only under it. A bag always has at least one alternative;
when a contradiction removes the last one the state is `Unreachable`, every
proof on that line holds, and a join with it yields the other side. Adding
a fact adds to `Common`. Adding alternatives, from `a != b`, an `or` condition, a
bool initializer or a join, pairs each existing alternative with each new one,
under the cap of the lattice section.

Type facts: `Requirement.OfType(type)` gives an `Admitted`. One interval gives
two facts. Several intervals, as a `!=` refinement has, give one interval-set
fact about that value: a single-value fact that carries the `Admitted` itself,
which the chain expands into its intervals one at a time only when the value is
reachable from the contract, under the budget. That keeps the alternative count
for the pairing between variables, where it matters, and out of single-value
holes. `RealRequirement.OfType` does the same for the real family with `<` kept
as written. A `where` clause on
the enclosing member and a dependent parameter's predicate become facts at the
entry step, read by the contract reader below with the parameter's holder as the
constrained name (Requirement 2.4.2).

Match facts (Requirements 7.3, 7.4): `Matches(Value text, Value pattern)` is a
second fact kind beside the comparisons. A regex value knows its literal from
its creation or from `LiteralValue.Of`. A match is proven when the text value's
bag holds a literal and `LiteralPredicates.Decide` decides it under
`MatchLimit`, when the bag holds the same match fact, or when it holds
`text == other` with the match on `other`. String equality is an ordinary
`== 0` fact over two string values; strings have no other arithmetic.

Bools (Requirement 7.5): `true` is 1 and `false` is 0, so `ok == true` is an
ordinary fact. `var ok = i < n;` records two alternatives, `ok == true` with
`i - n + 1 <= 0`, and `ok == false` with `n - i <= 0`.

## The chain (Requirement 1)

`Chain.Prove(Bag bag, IReadOnlyList<Alternative> denial, IReadOnlySet<Value>
contract, Budget budget)` returns `Proven`, `Refuted(Alternative failingCase,
bool afterMerge)` or `Exhausted`.

For each alternative of the bag paired with each alternative of the denial, over
their facts plus `Common`:

1. Projection (Requirement 4.3). Drop every fact that mentions no value
   reachable from the contract's values through facts they share. Two pairs
   identical after projection run once.
2. Removal of values, one at a time, fewest mentions first. To remove a value:
   solve every equality that mentions it for that value and substitute into the
   other facts; then add every pair of facts that bound it from opposite sides,
   scaled so the value cancels; then drop every fact that mentioned it, and
   normalize. A fact left with no value is a statement about a constant:
   `3 <= 0` is the contradiction, and the pair is proven; `-3 <= 0` is dropped.
3. If every value is removed and no contradiction appeared, the pair is not
   proven. The failing case is what the same removal leaves when it stops at the
   contract's own values: facts over the program's names, `capacity <= -1`,
   rendered through the values' display names.

The contract is proven only if every pair is proven. The first unproven pair's
failing case is reported, with `afterMerge` set when a fact in it came from a
merged alternative (Requirement 4.4).

The step count is bounded by the facts and values involved, as Requirement 1.3
asks: each removal takes one value out, and the facts it adds are pairs of the
facts it drops. This design does not promise to find every whole-number
contradiction. A system with no whole-number solution but with fractional ones is
reported unproven, never proven. That is the safe direction, and no example in
the requirements meets it.

`Budget(TimeSpan time, int facts)`. The chain counts the facts it derives and
reads the clock every few hundred steps; exceeding either returns `Exhausted`.
Starting values: 50 ms and 20,000 derived facts per proof, declared once in
`Facts/Budget.cs`, so the library owns its own limits the way
`LiteralPredicates.MatchLimit` owns the regex one, and threaded through
`RefinementAnalyzer`'s existing `matchLimit` constructor path.

## The lattice (Requirements 3, 4, 5)

`FactFlow : IFlowLattice<FactState>`, with `FactState(Bindings bindings, Bag bag,
ImmutableDictionary<Value, StoredRange> ranges)`. The `ranges` side map carries
today's Index and Range knowledge keyed by value instead of holder; `StoredRange`
and `RangeBound` are unchanged.

Transfer, by step kind:

- Declare, Introduce, Bind: a new value for the variable with its type's facts,
  plus the initializer's fact when `Terms.Read` reads it and exactness holds.
  `var a = b;` is `a - b == 0`. A pattern binding to a copied value is equality
  with the scrutinee's value. For a reference type, an initializer or assignment
  that reads a holder binds the target to that holder's value instead of
  creating one, so member holders reached through either name coincide
  (Requirement 3.4).
- Evaluate of an assignment: the same for the target holder, whether a local, a
  field of `this` or a member holder (Requirement 3.1, 3.4).
- Evaluate of a call, a `new` or a user operator: when `FieldEffects.MayWrite`
  says so, every mutable field of `this` gets a new value with type facts, and so
  does the holder behind every `ref` or `out` argument. The result is a fresh
  value with the return type's facts, `LiftedReturns.Produced` included
  (Requirement 6.1).
- Assume(condition, holds): a comparison reads both sides as terms and adds the
  fact, or its denial when `holds` is false; the denial of `==` is two
  alternatives. An `is` pattern goes through the contract reader against the
  scrutinee's term. A regex pattern adds a match fact. A bool holder read as the
  condition adds `ok == true` or `ok == false`. After every Assume, each
  alternative is checked for a direct contradiction among its single-value facts
  and dropped if it has one; that is how `if(ok)` keeps the alternative the
  condition selects. `and`, `or` and `not` never reach Assume: `FlowGraphBuilder`
  splits them into paths, so an `or` arrives as two paths that Join turns into
  alternatives (Requirement 2.4.4).
- Leave: drop the bindings of the scope's locals. Facts stay, and projection
  ignores values nothing reaches.

`Join(left, right)`: where both sides bind a holder to the same value, it stays.
Where they differ, a merged value `m` is created and bound; left's alternatives
gain `m == vLeft`, right's gain `m == vRight`. The result's alternatives are
left's followed by right's, and a fact in both `Common`s stays common. When the
count exceeds `AlternativeCap`, starting at 16, the oldest alternatives are
merged per variable: for each value they mention, each alternative is projected
onto that value alone, the intervals are unioned with `Admitted.Or`, and the
merged alternative holds one interval-set fact per value, marked merged. The
union is never replaced by its hull: `a == 1 or a == 100` stays two intervals. `Same` is
structural equality of bindings and bag.

Loops: `FlowSolution.Solve` already knows each block's entry and whether a join
changed it. It gains a per-block change count and calls
`lattice.Widen(previous, joined, round)` when the block is a loop header
(`graph.Loops`) and the join changed its entry. `IFlowLattice` gives `Widen` a
default body returning `joined`, so no other lattice changes. `FactFlow.Widen`
does nothing until `round` passes `Rounds`, starting at 3; then, for every
holder bound to different values in `previous` and `joined`, it applies
Requirement 5.1: project both states onto the holder's value, keep an end of the
interval that is equal in both, replace an end that moved with the type's own
end, and keep a fact over several values only when it appears identically on
both sides. The worklist re-runs the body from that summary (Requirement 5.2); if
the head changes again, `Widen` runs again and drops more. Each run drops at
least one fact or leaves the state the same, so the loop ends.

## Exactness (Requirements 2.5, 7.2)

Before recording `r == term`, `Exactness.Holds(operation, term, state)` proves
the operation stays within the result type with two small chain proofs over the
current bag, `term >= min` and `term <= max`, under a budget of its own. Integral
`+`, `-`, `*` by a constant and negation are exact when those hold. For `float`
and `double`, negation is always exact, and `*` or `/` by a power of two is exact
when the operand's magnitude is proven within the type's range scaled the same
way; `+` and `-` are never recorded. For `decimal`, `+`, `-`, negation and `*` by
a whole-number constant are exact when the result is proven within decimal's
range; `/` is never recorded. Nothing else is exact. When the proof fails, the
result value gets only its type's facts.

## Contracts at proof sites (Requirements 1.2, 6.2)

`ContractReader.Denial(IMistType target, Term value, Func<SymbolicBound, Term?>
symbols)` builds the denial alternatives: the complement of
`Requirement.OfType(target).Values` as one alternative per interval over
`value`, and each `SymbolicBound` denied as a fact between `value` and the term
the resolver gives for the symbol: a sibling parameter's argument term through
`Reading`, the `Length` holder of the indexed receiver through `Measured`, a
`this` member's holder, a dependent parameter's value. A predicate operand that
is an expression, `items.Length - start`, is read by `Terms.Read` with the
contract's names bound to those terms. `Requirement.Symbol` gains that path, so a
predicate that reads as null today reads as a term. `Requirement.Of` keeps giving
`Admitted`-only answers to exhaustiveness and closings; the new reading sits
beside it, not in place of it.

Each proof site in `Proving.Judge` keeps its shape (RefinementAnalyzer.cs:98-149)
and calls `Need`, which now reads the value into a term, or binds its fresh
value, builds the denial, and calls `Chain.Prove` with the state's bag. The
`Considered` walk over union constituents, `LiteralPredicates` for string
literals, `RangeKnowledge` for Index and Range, `JudgeClosing` for dependent
arguments and `ContractImplication` for conformance are unchanged. A string
target with a regex refinement is a match-fact proof. Nested graphs for lambdas,
async blocks and local functions start from an empty state, as today.

## Diagnostics

MIST0271 to MIST0277 keep their numbers and gain a `{case}` slot filled from the
failing case: "fails when capacity <= -1", with ", after merging paths" appended
when `afterMerge` is set (Requirements 1.4, 4.4). The templates change, so
`tools/diagnostics/catalog.sh` regenerates the book page that
`DiagnosticCatalogTests` pins.

MIST0485 `RefinementProofExhausted`, category Typ, policy Fixed, severity Error:
"Could not finish proving '{predicate}' for '{value}' within the compiler's
limit: add an explicit check on this path." (Requirement 1.6). It is the next
free number at this revision, allocated as diagnostics Requirement 1.4 states.

## Performance

Measured before this change on `samples/AllFeaturesV2`, Debug: Stage 5 takes
1.81 s of a 9.87 s compile, and `refinement-proving` 0.11 s. The budget for this
change is that `refinement-proving` stays under 0.5 s on that sample, read from
`--verbose`. The levers, in order: projection before every proof; the
`Common`/`Alternatives` split, so a join whose bindings agree adds nothing; one
`FlowGraph` per body root built once and shared through
`AnalysisContext.GraphOf(SourceRoot)` instead of the eight rebuilds today; the
cap and the rounds. If the budget is missed, the cap and the rounds are tuned
first and the fact language last.

## Tests

Every test declares its tier with `[Trait("Tier", "N")]`, as `Test/AGENTS.md`
requires.

Tier 1, the bulk: `Fact` normalization and rounding; `Terms.Read` over
expressions built with `HandBuiltRefinements`; `ContractReader.Denial` for an
interval set, a symbolic bound and an expression operand; `Chain.Prove` for each
requirement example as a proven case, its refusal twin with the expected failing
case, the alternatives of `!=`, projection, and `Exhausted` under a tiny budget;
`Exactness.Holds` for each listed operation and a failing wrap; `Bag` pairing and
the cap's per-variable merge; `FactFlow.Widen` over two hand-built states. One
FsCheck property, under 50 iterations, draws small fact systems over values in
0 to 7 and checks that `Proven` never contradicts a brute-force search over
those values, which pins the safe direction the chain section promises.

Tier 2: `FactFlow` over bodies built with `HandBuiltModel` and judged through
`HandBuiltModel.Analyze(new RefinementAnalyzer(), ...)`, the shape
`RefinementFlowRefusalTests` already uses: one admit and one refuse per
requirement example, the `if(ok)` selection, a loop that keeps `i >= 0` and drops
its upper bound, a join that keeps the pairing `Needs(a, b)` needs, and a field
invalidated by a writing call.

Tier 3: none expected. Tier 4: the Stage 4 to 5 no-cascade seam is unchanged and
gets no new test. Tier 6: one new sample, `samples/FlowFacts`, holding the
admitted programs from the requirements with printed results, banked in
`AllFeaturesV2` as `samples/AGENTS.md` describes. Refusals are proven at Tiers 1
and 2 only.

## Sequencing

1. `Facts/`: `Value`, `Term`, `Fact`, `Bag`, `Chain`, `Budget` and `Exactness`,
   with Tier 1 tests, integers only. No caller yet. `Exactness` is here and not
   in stage 4 because stage 2 records arithmetic facts and may not record one it
   cannot prove.
2. `FactFlow` for the integral family, replacing `RefinementFlow` behind the
   existing proof sites, with `ContractReader` for interval and symbolic
   contracts. The existing Tier 2 refinement tests and `AllFeaturesV2` must pass
   unchanged before anything new is admitted.
3. Expression operands in predicates, the failing case in MIST0271 to MIST0277,
   MIST0485, the catalog regenerated.
4. Alternatives, the cap, and `Widen` in `FlowSolution`. Until this stage,
   `FactFlow.Join` merges per variable, which is Requirement 4.4's fallback
   applied everywhere, and widens as `RefinementFlow` does today.
5. The real family, strings, regex and bools (Requirement 7).
6. `AnalysisContext.GraphOf`, the timing check against the budget, and
   `samples/FlowFacts` banked.
7. Compound assignment and increment: grammar, node, binder, the analyzer
   cases, emission, and the sample's loops rewritten with them. Independent of
   stages 1 to 6 except for the `FactFlow` case, so it may run alongside them.

## Compound assignment and increment (Requirement 8)

Grammar: new tokens `PLUS_EQUAL`, `MINUS_EQUAL`, `STAR_EQUAL`, `SLASH_EQUAL`,
`PERCENT_EQUAL`, `PLUS_PLUS` and `MINUS_MINUS` in the lexer; `rule Assignment`
(`Mist.storm:573`) gains the five compound operators beside `EQUAL` and
`QUESTION_QUESTION_EQUAL`; prefix `++`/`--` join the unary rule and postfix
`++`/`--` the postfix rule. Storm regenerates the AST classes under
`Source/Mist/Generated/`.

Model: one new node, `CompoundAssignmentExpression(target, BinaryOperatorKind,
value, type, Operator, IncrementForm)`, where `IncrementForm` is None, Prefix or
Postfix and an increment's `value` is the literal 1. One node kind is one
`BodyWalker` row, and `BodyWalkerCoverageTest` enforces it. The binder selects
the operator as it does for `BinaryExpression` and requires the result to be
assignable to the target, so `/=` on an int target is refused or made a `Result`
exactly as `x = x / y` would be.

Why a node and not a lowering: `x = x op e` evaluates the target twice, and the
model has no temporary local to hold a receiver or an index inside an
expression. The emitter writes C#'s own `op=`, `++` and `--`, and C# evaluates
the target once, which is how Requirement 8.2 is kept without inventing a
temporary. The `FlowGraphBuilder` evaluates the target's parts, then the value,
then one `Evaluate` of the node, in the order 8.2 states.

Analyzers: each one that reads `AssignmentExpression` gains a case for the node
with the same meaning, a read of the target followed by a write of
`target op value`: definite assignment (the target must be assigned before, and
is assigned after), ownership, readonly, purity, borrow and escape. `FactFlow`
on `Evaluate` of the node reads `target` as the old value's term, builds
`old op value` through `Terms` and `Exactness`, and binds the target holder to a
new value with `new == old op value` when exactness holds, or the type's facts
when it does not. A postfix increment used as an expression yields the old
value's term and a prefix one the new value's. Requirement 8.6 allows the first
delivery to give an increment inside a larger expression a fresh value with its
type's facts instead; the statement form is the one the tests must prove.

Tests: Tier 1 for the binder's operator selection and result typing over
hand-built targets; Tier 2 for `FactFlow` over `x += i` with and without a
bound on `i`, and for definite assignment and ownership over a hand-built body
with the node; the `samples/FlowFacts` sample uses `i++` and `+=` in its loops.

## Not in this design

The two stretch goals in Status and scope. Per-field write summaries beyond
`FieldEffects`. Fact-bearing members of exclusively borrowed objects. Index and
Range values inside the chain; they stay in `StoredRange`. A loop summary
stronger than Requirement 5 (Requirement 5.6).
