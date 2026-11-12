# Runtime dependent values: design

This design delivers [requirements.md](requirements.md). Anchors are to main at
`7047fd4ed`; later commits on main moved lines without renaming anything, so
find each by its name. Nothing here changes the grammar: a name in a slot already parses
as a type reference, and Requirement 5 admits nothing else. Nothing here
changes the proof engine: every proof is `Chain.Prove` over the bag
`FactFlow` already keeps. Where this document names a diagnostic number, the
number is the next free one at this revision and is taken when the change that
needs it lands.

## Shape of the change

Today a slot argument is one of three model types: a `LiftedType` for a
literal, a dependent `TypeParameter` for a forwarded parameter, or the
`unknown` of `UnknownType` and `UnknownValueType`. A name that is a value is
looked up as a type, found as a local, and reported as MIST0298
(`ScopeResolver.cs:847-849`). Two closings convert only when their arguments
are the same by name, or the target's is `unknown`
(`ConversionClassifier.cs:170-218`). The length of a sequence is known only
when its type holds a literal (`Lengths.cs:34-49`).

After this change there is a fourth slot argument, the captured value, and the
pieces are:

- `ProgramModel/Model/Types/CapturedValue.cs` and
  `ProgramModel/Model/Scopes/SlotCapture.cs`: the argument and the hidden
  readonly local it stands on.
- `ScopeResolver`: arguments resolved against the slot they fill.
- `ConversionClassifier`: two closings that differ in a slot whose values are
  not both literals convert, and Stage 5 owes the proof.
- `TypeMemberLookup`: a dependent parameter answers an instance request on
  every type.
- `ProgramAnalysis/Rules/SlotIdentity.cs`: the proof that two slot values are
  identical, called from the proof sites `RefinementAnalyzer` already has.
- `ProgramAnalysis/Rules/SlotReads.cs`: what a slot read through a value is, in
  place of the length-only path.
- `Backends/CSharpTranspiler/Lowering/SlotRoutes.cs`: which declarations keep
  today's carriers and which carry their value in the instance or the call.

## The captured value (Requirements 1, 3, 5)

A `SlotCapture` is a `LocalVariable` the binder makes where a type is written
in a body: readonly, unnameable, typed by the slot's contract (the parameter's
domain with its `where` predicate, as a parameter's `where` is already part of
its type), and initialized by the bound argument. `CapturedValue(SlotCapture)`
is the `MistType` that fills the slot.

Why a local and not a new kind of thing: a local is already a holder
(`Terms.Holder`, `Terms.cs:65-82`), already has a `Declare` step, is already
proven against its type at that step (`RefinementAnalyzer.cs:116`), and is
already captured by a lambda. Requirement 4 is then the existing proof of an
initializer against a local's type, and its refusals are the existing
MIST0271 family with their failing case.

Each written argument is its own capture. `Array<int, n>` written twice is two
captures, and the facts say whether they are one value. A capture's qualified
name is unique to it, its written name and its position, because constructed
types are compared by qualified name (`ConversionRelation.cs:229-258`) and two
captures of one name are two values. `TypeDisplay` prints the written name.
The interner already keys a construction by the reference of each argument
(`TypeInterner.cs:253-305`), so nothing changes there.

The node that wrote the type owns its captures: a local declaration, `new`,
an array creation or `stackalloc`, an invocation with stated arguments, a type
pattern, a cast, an `as`, and a read through a closing each gain
`Captures`. `BodyWalker` yields a capture's initializer as a child of that
node, so definite assignment, ownership and purity see the read of `n` with no
case of their own. `FlowGraphBuilder` emits one `Declare` per capture before
the node's operands, which is the order Requirement 1.2 states.

Where a capture ends. A capture is in scope from its node to the end of the
block, like any local, so a type that holds it can only be the type of
something declared after it in that block. A field, a signature and a local
declared earlier cannot name it. One path is left: the inferred result type of
a lambda or a local function whose body made the capture. That body runs many
times and the capture is a new value each time, so the binder replaces the
slot with `unknown` where it infers that result type (Requirement 1.6).

Outside a body there is nothing to capture. A field type, a signature, a base
clause, an alias target and a constraint take a literal or a dependent
parameter in scope, as today, and a value's name there is refused with a
diagnostic that says to declare a dependent parameter (Requirement 8.1).

## Resolving an argument by its slot (Requirement 5)

`ResolveNamed` finds the definition before it resolves any argument
(`ScopeResolver.cs:281-312`), but `ResolveTypeArgument` is not told which slot
it fills (`ScopeResolver.cs:678`). The three-argument `ResolveTypeArguments`
(`ScopeResolver.SpreadArguments.cs:12`) already has the definition and gains
the walk: for each argument, the parameter at that position.

- A type slot resolves as today.
- A dependent slot given a literal lifts as today.
- A dependent slot given a type reference that is a bare or dotted name is
  looked up as a value. A dependent parameter in scope is the forwarded
  argument, as today. Anything else is handed to the caller's value binder.

The value binder is the piece a body has and a declaration does not. The four
body sites pass one made from their `BindingContext`: the local declaration
(`StatementBinder.cs:270`), `BindNamedCreation` (`ExpressionBinder.cs:285-302`),
the type pattern (`PatternBinder.cs:144`), and stated method arguments. It
binds a bare name through `IdentifierBinder.BindIdentifier` and a dotted path
through the postfix binder, makes the `SlotCapture`, and returns the
`CapturedValue`. With no value binder the name is the Requirement 8.1 refusal.

Stated method arguments are resolved before the candidates are known
(`StatedMethodArguments.cs:26`). They are resolved per candidate instead, since
only a candidate says whether a position is a slot. A name that is a type for
one candidate and a value for another keeps both candidates, and overload
resolution proceeds as it does today.

`new T[e]` and `stackalloc T[e]` do not go through an argument list.
`BindArrayCreation` and `BindStackalloc` (`ExpressionBinder.cs:533-577`) pass a
literal size as today and otherwise make a capture whose initializer is the
size expression. `ConstructLengthIndexed` (`ScopeResolver.cs:912-934`) takes the
length argument instead of an `int?`. This is the only place a capture's
initializer is not a name (Requirement 5.4).

## One type when proven (Requirements 2, 3)

The binder cannot see facts, so it admits and records, and Stage 5 proves.

`ConversionClassifier.Within` gains a third test beside
`AgreesUpToUnknownValues`: the same definition, and each argument pair is the
same, or erases as today, or is a slot pair with at least one side that is not
a literal. A slot pair of two different literals stays refused as MIST0157.
The pairs admitted for proof are a capture against anything, a dependent
parameter against anything, and the `unknown` slot of a class or struct against
a named value (Requirement 7.4). The `unknown` slot of an abstract against a
named value stays refused (Requirement 11).

`SlotIdentity.Judge(FactState, Expression value, IMistType target)` is called
from `Need` at each site `Proving.Judge` already visits
(`RefinementAnalyzer.cs:112-175`): the initializer of a declaration, an
assignment, a return against its `TargetType`, an argument, a `new` argument,
an initializer entry and a cast operand. It walks the value's own type and the
target together, looking through `ImplicitConversionExpression` as `Lengths.Of`
does, and for each slot pair that is not one object it reads both sides as
terms and asks `Chain.Prove` to rule out their difference:

- a literal is a constant;
- a capture is the value its local holds;
- a dependent parameter is the value bound under that `TypeParameter`, the
  holder `FactReading.Holder` already gives it (`FactReading.cs:179`);
- the `unknown` slot of a class or struct is the slot read through the value
  expression, under the next section.

A capture whose initializer is a plain read of a holder is bound to that
holder's current `Value`, not to a fresh value with an equality fact. Two
captures of one unchanged variable are then the same value with no proof, and
a domain the flow records no facts for is covered (Requirement 6, last
paragraph). Any other initializer gets a fresh value with what `Terms.Read`
gives it.

`var` needs no change: it takes its initializer's type, and that type now
holds the capture.

## Reading the slot through a value (Requirements 7, 11)

Binding. `AddTypeNames` offers a type's own parameters to a qualified instance
request only when the type is intrinsic (`ProjectsInstead`,
`TypeMemberLookup.cs:306-311`), which is why `arr.Length` binds and
`ring.Capacity` is MIST0301. The dependent parameters are offered for every
type, so every slot read has the shape `arr.Length` has today: a member access
whose member is the `TypeResult` of a dependent parameter. `Ring<4>.Capacity`
keeps its `ValueProperty`. The one refusal, in `PostfixBinder.ResolveMember`:
the receiver's type is an abstract whose argument for that parameter is
`unknown` (Requirement 11.2).

Facts. `RangeKnowledge.IsLength` (`RangeKnowledge.cs:24-36`) becomes
`SlotReads.Parameter(access)`, true of any such access, with string's
`Length` kept as the case it is. `SlotReads.Value(ref FactState, access)` says
what the read is, from the receiver type's argument for that parameter:

- a literal: that constant, as `Lengths.Typed` answers today;
- a capture: the capture's value;
- a dependent parameter: that parameter's value;
- `unknown` on a class or struct: the value under
  `MemberHolder(receiver, parameter)`, as `Length` has today
  (`FactReading.cs:165-191`).

The second and third cases are the change. Today a length whose type names a
parameter or nothing gets a fresh value that knows only `>= 0`, and nothing
links it to the parameter (`FactReading.cs:89-92`). With them the read is the
slot's value itself, so `slots[at]` under `at < Capacity` and `a[i]` under
`i < n` are proven by the facts already in the bag. `Requirement.Closed`
(`Requirement.cs:250-280`), which renames a bound to a forwarded parameter,
renames to a capture's local the same way.

An abstract's slot is never keyed by the object (Requirement 11.4). Through a
named closing the read is one of the first three cases. Through `unknown` it
does not bind.

## Testing against a closing (Requirement 9)

The pattern's type is resolved through the same path, so it may hold a
capture, declared before the test evaluates. `PatternNeverMatches` (MIST0193)
is reported only when the two types differ in a slot whose values are both
literals.

On the branch a passing test selects, `FactFlow` adds, for each slot of the
tested type, that the scrutinee's read is identical to the slot's value, where
the scrutinee's type is a class or a struct or is closed with a dependent
parameter. The first gives `ring.Capacity == n`. The second gives `N == 5`
inside `Check<N:int>(Buffer<N> b)`. The pattern's binding has the tested type,
so its own reads are known from the type. The failing branch gains nothing in
the first delivery.

A cast is a proof site already (`RefinementAnalyzer.cs:167`) and gets
`SlotIdentity` with the others.

## Signatures and calls (Requirement 8)

A stated dependent argument is a capture on the invocation. Inside the method
the parameter is the `TypeParameter` holder it is today, so a parameter typed
`Array<int, N>` has `Length` identical to `N` by the section above, and a
`return` is proven at the existing return site.

Inference binds what the argument's slot holds (`TypeArgumentInference.cs:437`,
`Record` at `:544`):

- a literal or a dependent parameter, as today;
- a capture: that capture, so the result type shares it;
- the `unknown` slot of a class or struct: a capture on the invocation whose
  initializer is the slot read of that argument. Today the `UnknownValueType`
  itself is recorded and reaches C# as `int`, which is the first "wrong
  afterwards" row of the proposal;
- the `unknown` slot of an abstract: inference fails, and the diagnostic says
  to state the parameter (Requirement 11.5).

`Dominant` (`TypeArgumentInference.cs:603-608`) treats two different slot
values as one because `Normalize` strips them to their domain. The first bound
keeps the parameter, as Requirement 8.4 states, and each later argument is
then an ordinary argument against a parameter type, proven by `SlotIdentity`.

## What may close a type (Requirement 6)

`DependentDomains.Primitive` and `KindOf` (`DependentDomains.cs:17-43`) gain a
fourth kind beside integral, bool and char: a type for which
`Stampability.IsStampable` holds (`Stampability.cs:19`) and
`MemberType.IsReadonly` is set (`MemberType.cs:49`). `Holds` stays a question
about literals and the three kinds that have them. `Forwards` requires the
same type for the fourth kind. A slot of the fourth kind is always closed by
a name, and its identity proofs rest on the same-value rule above.

## Static storage (Requirement 10)

Nothing in the frontend or Stage 5 changes. A static member reached through a
closing already carries the closing. The work is the backend's.

## The C# backend

### Two routes

The backend has two whole-program passes that decide how a declaration is
emitted, `MonomorphSet.Discover` and `UnionSpecialization`
(`CSharpEmitterV2.cs:140-145`). `SlotRoutes` is a third. A dependent parameter
is value-routed when any of these holds, to a fixed point:

- some closing in the program fills it with a capture;
- a value-routed parameter is forwarded into it;
- its slot is read through `unknown`, or inferred from an `unknown` slot;
- its domain is of the fourth kind;
- another dependent parameter of its declaration is value-routed.

Array, Span and ReadOnlySpan are value-routed already: `LengthErasure` drops
the slot and the CLR object holds the value.

Every other dependent parameter is carrier-routed and emitted exactly as
today. No program that compiles today fills a slot with a capture, so no
existing program's C# changes, and the `AllFeaturesV2` baseline holds.

### The value route

A value-routed slot leaves the C# type parameter list. What is left:

- A class or struct keeps the value in a hidden readonly instance field, set
  from a hidden leading parameter of every constructor. A read of the
  parameter in an instance member reads the field. A static member takes the
  value as a hidden leading parameter.
- A method takes its value-routed parameters as hidden leading parameters.
- An abstract declares a hidden getter, and each implementing type answers it.
- A read through a value is the field. A read through a closing is the
  closing's value: the literal, the capture's initializer, or the enclosing
  parameter's read.
- A test appends the comparison: `x is Ring r && r.__Capacity == n`. A switch
  arm takes it as a `when`. `as` already lowers to a test when a shape has
  members to check (`BodyEmitter.Conversions.cs:88-98`).
- A closing with `unknown` in the slot is spelled as the type itself. Today it
  is `dynamic` (`TypeNameEmitter.cs:445-448`), and member calls on it dispatch
  dynamically.

A capture's value is consumed once, at the node that owns it, so its
initializer is emitted in place there and needs no C# local. A capture nothing
consumes at run time, as on a local declaration's written type, emits nothing
when its initializer is a read that runs no code, and a discarded read
otherwise. An inferred slot read needs its argument twice, and binds it once
with the `switch { var __eN => ... }` shape compound assignment already uses
(`BodyEmitter.CompoundAssignment.cs:43-84`).

`new Array<T, N>(fn)` emits `MistArray.Generate` only when the length is a
literal (`ArrayLowering.cs:43-55`) and otherwise falls through to an unsized
`new int[](...)`, the third "wrong afterwards" row. `Generated` takes the
length from any slot argument through the same rendering a read through a
closing uses.

### Where the C# backend falls short

Each is refused by the backend with its own diagnostic, never miscompiled, and
the language contract is not narrowed by it:

- Static storage in a value-routed type. Requirement 10 needs a table keyed by
  slot value. Nothing in the samples, Standard or the book snippets declares
  static storage in a type with a dependent parameter.
- A type value of a value-routed closing, `var t = Ring<n>;`. A CLR `Type`
  cannot hold the value.
- A type that implements a value-routed abstract at two values of a slot. The
  two implementations collide once the slot leaves the C# type.
- A test against a closing nested in another type's argument,
  `List<Array<int, n>>`, as MIST0013 refuses for a sized element today.

## Diagnostics

New, numbered from MIST0486 in the order they land:

- `ClosingNotProven`, Stage 5: "'{source}' is not proven to be '{target}':
  '{left}' and '{right}' are not proven identical{case}."
- `SlotValueOutsideBody`, Stage 4: "A type written here cannot name the value
  '{name}'. Declare a dependent parameter and pass '{name}' between angle
  brackets."
- `SlotReadThroughUnknown`, Stage 4: "'{parameter}' cannot be read through
  '{receiver}': an object may implement '{abstract}' at more than one value.
  Test it with an 'is' pattern instead."
- `SlotNotInferable`, Stage 4: "'{parameter}' cannot be inferred from an
  argument of type '{type}'. State it between angle brackets."
- Three backend refusals, one for each of the first three shortfalls above.

Changed: MIST0298 is no longer reported for a value's name in a dependent
slot. MIST0301 is no longer reported for a slot read through an instance.
MIST0193 is no longer reported across a slot that is not two literals.
MIST0407 states the domain rule of Requirement 6. A runtime argument that
breaks its slot's contract is reported by the MIST0271 family at the capture.
`tools/diagnostics/catalog.sh` regenerates the book page.

## Tests

Every test declares its tier with `[Trait("Tier", "N")]`.

Tier 1, the bulk: the classifier over two hand-built closings for each slot
pair (two literals, a capture and a literal, two captures, a parameter and a
literal, `unknown` on a class and on an abstract); `SlotIdentity.Judge` over a
hand-built state for each requirement example and its refusal twin with the
expected failing case; `SlotReads.Value` for each of its four cases;
`DependentDomains` over a readonly stampable struct, a mutable one, a class, a
float and an enum; `SlotRoutes` over a hand-built model for each of its five
rules; the inference decision for each argument slot.

Tier 2: `RefinementAnalyzer` over bodies built with `HandBuiltModel`, one
admit and one refuse for each of: reassignment of the named variable after a
capture; `if(n == m)`; `new T[e]` with and without `e >= 0`; a field typed by
the enclosing parameter indexed under a bound on that parameter; a test that
selects a branch; a return against a dependent return type; a second argument
that binds the same parameter; a capture inside a loop. Emission over a
hand-built model for the value route's class, struct, method, test and
`unknown` spelling, beside the existing `ErasedShape` tests.

Tier 3, few: `ProgramModelBuilder.Build` over minimal source with the stub
provider, for the three things only the binder's chain shows: a name in a
dependent slot becomes a capture and the same name in a type slot stays a
type; `new int[n]` and `var` take the capture; `ring.Capacity` binds.

Tier 4: none. Tier 5: none. Tier 6: one new sample,
`samples/RuntimeDependentValues`, banked in `AllFeaturesV2` as
`samples/AGENTS.md` describes, holding the accepted programs of the
requirements for Array and for `Ring`, with printed results. Refusals are
proven at Tiers 1 and 2 only.

## Sequencing

Each stage leaves `AllFeaturesV2` passing.

1. The model and the binder: `SlotCapture`, `CapturedValue`, resolution by
   slot, `new T[e]`, the classifier's third test, the instance read and its
   one refusal, stated and inferred method arguments, the pattern type.
   `SlotIdentity` lands in its strictest form, proving only two slots that are
   one value, so nothing unproven is admitted before stage 2. The backend
   refuses a capture outside Array and the spans. `var a = new int[n]` takes
   the length from this stage on.
2. Stage 5: `SlotIdentity` over the chain, `SlotReads`, the facts a test adds,
   and the capture's contract. `new T[n]` owes `n >= 0` from this stage on, and
   the eight creations in the samples that size by a plain `int` change with
   it. Mist programs inside tests that do the same are counted before this
   stage is planned.
3. The backend: `SlotRoutes`, the value route, `ArrayLowering`, the four
   refusals. The first three "wrong afterwards" rows of the proposal are
   closed here and each gets its test.
4. Requirement 6: the fourth kind of domain.
5. `samples/RuntimeDependentValues`, the book chapter, and the sentences the
   requirements revise in dependent-types, adhoc-refinement-constraints,
   stack-only-types, deep-ownership-types and
   flow-sensitive-refinement-inference.

## Not in this design

Requirement 1.4 where the binder chooses before the facts exist. After
`var n = 7;`, `new int[n]` is proven to be an `Array<int, 7>` wherever a value
meets a type, and diagnostics print it so. Overload choice and the common type
of switch arms run in the binder and read the capture as a value not yet
known, so a call only the facts could decide is refused as ambiguous.

A pointed diagnostic for an expression in a slot. It is a parse failure today,
MIST0123 at the enclosing member, and stays one. dependent-types Requirement
15.6 and Requirement 5.2 here ask for better, and that is the parser's work.

The table for static storage on the value route, and the other three
shortfalls of the C# backend.

Facts across a lambda. A nested body starts from an empty state, as today, so
a capture made outside is a value the inner body knows nothing about beyond
its type.

Three of the five defects found while probing. Stage 3 closes the other two,
the `unknown` slot inferred into a method's dependent parameter and the unsized
array creation. Not closed here: a type test against a type parameter closed
with a sized array ignores the length; switch arms of two sized arrays merge to
`IEnumerable<int>`; and `Both.N`, read through a type that implements two
closings, answers the first.
