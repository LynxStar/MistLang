# Runtime dependent values: design

This design delivers [requirements.md](requirements.md). Anchors are to main at
`7047fd4ed`; later commits on main moved lines without renaming anything, so
find each by its name. The grammar changes once, for Requirement 8 items 7 to
10; elsewhere a name in a slot already parses as a type reference. Nothing here
changes the proof engine: every proof is `Chain.Prove` over the bag
`FactFlow` already keeps. Where this document names a diagnostic number, the
number is the next free one at this revision and is taken when the change that
needs it lands.

## The refinement meaning

The owner set this on 2026-11-21, picking up his idea of 2026-11-15: "Type of a
runtime dependent is whatever is provable about it." It replaces the value
meaning stages 1 to 4 were built for, and the requirements were revised for it
on 2026-11-21 and 2026-11-22. The mechanism those stages delivered stays:
captures, slot reads, value routes and holders. What a slot argument means, and
how a pair of them is judged, changes. Stage 8 of *Sequencing* delivers it.

- **A variable's type is what is provable; an object's type is exact.** With
  `x : Range`, `new Cell<x>()` gives a variable of type `Cell<Range>` and an
  object that is exactly `Cell<15>` when `x` is 15. The object carries its value.
  A literal closing, `Cell<15>`, is the refinement with one member.
- **A name in a body's slot stands for its refinement.** The capture stays the
  slot argument the binder makes, but Stage 5 no longer reads it as one value.
  It reads it as the refinement the facts prove for the capture's value at the
  capture's `Declare` step: bounds, a constant, excluded values, path conditions,
  and no relation to another name (Requirement 1.4). Only a fact relates a value
  to the name: `new Cell<n>()` records that its slot holds `n`'s value, as
  `new int[n]` records `Length == n`, and a write ends it. `Cell<n> c = make();`
  states that `c`'s slot lies inside `n`'s refinement and nothing more.
- **A dependent parameter in a declaration states a relation.** In a parameter,
  return or field type, `Array<int, N>` means that the length is `N`'s value
  (Requirements 7.3, 8.1 and 8.6), as stages 1 to 3 built it. Inside a body,
  `N` written in a type is a name like any other under Requirement 5.1, so it is
  captured and stands for its refinement. This is the one reading of the
  requirements below that the owner has not ruled on in these words; see
  *Readings to confirm*.
- **Arrays.** `new int[x]` gives an `Array<int, Range>` with `Length == x`
  recorded. `int[n]` returns (Requirement 8 items 7 to 10) carry the relation
  through a signature.
- **A slot's domain must be stampable** (2026-11-12). The argument of a closing
  may be a refinement of that domain, and the refinement itself need not be.
- **A closing's default is the closing with its own slot value** (2026-11-21,
  "Agree"). `new Cell<x>[5]` with `x` equal to 15 stamps five `Cell<15>`, and
  `default(Cell<x>)` is `Cell<15>`. `default(Cell<Range>)`, written with the
  refinement itself, has no value to hand and no default, as `default(Range)`
  has none. In generic code the captured value travels with the closing
  witness, so `Blank<Cell<x>>()` stamps `Cell<15>`.
- **.NET writing zeros into a closing's slot.** A value-routed slot is stored
  relative to an anchor, a member of its refinement that is a constant of the
  refinement's C# type, so all-zero bits read as the anchor: after a .NET call
  clears a `Cell<Range>[]`, its elements are valid `Cell<11>` values. The anchor
  is a backend detail and never appears in a value Mist itself creates
  (2026-11-21, "Agree"). The owner rejected refusing to hand such arrays to
  .NET, putting `| void` on their elements afterwards, and passing copies. A
  refinement no member of which can be named (regex, cross-member patterns,
  string constants, bounds that name another value) falls back to `T?` at the
  boundary. A refinement no value satisfies is MIST0505 on main since 74a6640b.
  A refined field of a Mist struct that is not a slot, and a `Range[]` that .NET
  clears, are out of this spec's scope: the hole needs no dependent slot, so it
  belongs to refinement types.
- **Superseded.** The containers ruling of 2026-11-21 ("Choose by value", under
  *A type for every literal closing*) was made under the value meaning. Under
  this one `new Cell<x>[k]` is a `Cell<Range>[]`, and `Cell<15>[] a = b` with `b`
  a `Cell<Range>[]` is not a valid program.

### Readings to confirm

Each follows from a revised requirement, and each is shown to the owner with
its program before the stage 8 step that builds it.

1. Inside a body, `Array<int, N> b = new int[5];` is accepted where 5 lies in
   `N`'s domain, because `N` there stands for its refinement, and `b.Length == N`
   is then not known. The stricter reading, that a dependent parameter always
   states the relation, refuses it.
2. A test's answer depends on what the facts prove at the test (Requirement
   9.1). With `n : Range`, `x is Ring<n>` passes for a capacity of 20, and the
   same test inside `if(n == 15)` does not. Everywhere else a stronger proof
   only lets more programs compile; here it changes what a compiled program
   prints, and the flow spec leaves some facts optional, so two compilers that
   both meet it may answer one test differently.

## Shape of the change

Before stage 1 a slot argument was one of three model types: a `LiftedType` for a
literal, a dependent `TypeParameter` for a forwarded parameter, or the
`unknown` of `UnknownType` and `UnknownValueType`. A name that is a value is
looked up as a type, found as a local, and reported as MIST0298
(`ScopeResolver.cs:847-849`). Two closings convert only when their arguments
are the same by name, or the target's is `unknown`
(`ConversionClassifier.cs:170-218`). The length of a sequence is known only
when its type holds a literal (`Lengths.cs:34-49`).

Stages 1 to 4 added a fourth slot argument, the captured value, and these
pieces:

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

Stage 8 changes what these mean under the refinement meaning, and adds:

- a fifth slot argument, a refinement type written in a slot, `Cell<Range>`
  (Requirement 5.3), which the binder resolves where no value of that name is
  in scope;
- `SlotIdentity` becomes the fit judgement of *A closing fits by refinement*:
  the target's refinement against what the facts prove of the source;
- `SlotReads` reads a closing's slot through the value's own holder, never
  through the capture its type was written with;
- member signatures reached through a value read that value's slot
  (Requirement 7.6);
- the abstract conversions of Requirement 11 in `ConversionClassifier`;
- a Stage 5 rule for a static member reached through a refinement
  (Requirement 10.4);
- in the backend, a test's comparison becomes the refinement's predicate, and a
  value-routed slot is stored relative to its anchor.

## The captured value (Requirements 1, 3, 5)

A `SlotCapture` is a `LocalVariable` the binder makes where a type is written
in a body: readonly, unnameable, typed by the slot's contract (the parameter's
domain with its `where` predicate, as a parameter's `where` is already part of
its type), and initialized by the bound argument. `CapturedValue(SlotCapture)`
is the `MistType` that fills the slot. Under the refinement meaning a dependent
parameter written in a body's type is captured too, so every slot argument a
body writes is a capture, a literal or a refinement type.

What a capture means is Stage 5's. At the capture's `Declare` step the
analyzer keeps its refinement: the intervals and excluded values the bag holds
for the capture's value there, with no relational fact. That refinement is the
slot of every type written with the capture, for the life of that type, as
Requirement 1.2 fixes it. A later fact about `n` narrows `n`, not a type
already written with it.

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
times and the capture is a new value each time, but what is provable about it
is the same each time. A nested body starts from an empty state (*Not in this
design*), so what is provable there is the type of the capture's initializer.
The binder closes the inferred result type's slot with that type as a
refinement slot argument: with `n : Range`, `k => new int[n]` returns an
`int[Range]` (Requirement 1.6). Stages 1 to 3 put `unknown` there.

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
  looked up as a value first, as an expression's name is (Requirement 5.4).
  Outside a body a dependent parameter in scope is the forwarded argument, as
  today. Inside a body any value, a dependent parameter included, is handed to
  the caller's value binder and captured.
- Where no value of that name is in scope, the name is looked up as a type. A
  refinement of the slot's domain becomes the refinement slot argument,
  `Cell<Range>` (Requirement 5.3), and any other type is refused as MIST0298 is
  today. A dotted name whose first part is no value in scope is looked up the
  same way, which is how `Cell<Probe.Size>` names a type that a local `Size`
  hides.

The value binder is the piece a body has and a declaration does not. The four
body sites pass one made from their `BindingContext`: the local declaration
(`StatementBinder.cs:270`), `BindNamedCreation` (`ExpressionBinder.cs:285-302`),
the type pattern (`PatternBinder.cs:144`), and stated method arguments. It
binds a bare name through `IdentifierBinder.BindIdentifier` and a dotted path
through the postfix binder, makes the `SlotCapture`, and returns the
`CapturedValue`. With no value binder the name is the Requirement 8.1 refusal.

Four sites were too few. Requirement 5 puts no limit on where in a body a
closing may name a value, so every place a body writes a type owns the
captures of that type. At a2d87f2f these bind: a typed local with or without an
initializer, a typed `for` initializer, `new`, a cast, `as`, a type pattern
with or without a binding, a switch arm, stated method arguments on every call
form, and a member or a call reached through a closed type.

These are still refused as MIST0487 and are to bind. Each needs its node to
own the capture, a step in the flow so Stage 5 judges it, and the backend to
evaluate it once where it is written:

- a typed `foreach` variable;
- a typed deconstruction, `(Ring<n> x, int y) = pair`;
- a `with` binding;
- `default(Ring<n>)`;
- the element type of an array creation or a stack allocation,
  `new Cell<n>[3]`;
- a method group named as a value with stated arguments, `First<Ring<n>>`,
  which owns its captures as a call does.

A union check has the same fault from the other side. With
`Ring<n> | string a`, the test `a is Ring<n> x` is MIST0345, because the
constituent and the tested type are two captures compared as written. The
check admits a tested type that differs from a constituent only in slots
that are not both literals, as MIST0193 already does, and Stage 5 judges the
pair.

Both landed with MIST-1373. `ForEachStatement`, `TupleDeconstructionStatement`,
`WithBlockStatement` and `DefaultExpression` own the captures of the type they
write, an array creation or a stack allocation owns its element type's ahead
of its length, and a method group converted to a function type hands its stated
captures to the `FunctionReferenceExpression`. The flow declares them before
the node's own operand. A group whose closed signature differs from the
function type only in slot pairs converts where no candidate is identical, and
Stage 5 judges each parameter and the result as one type. The backend
evaluates each capture once where its type is written: a statement ahead of
itself, `default`, an array and a stack allocation as the stamp's hidden value
or else ahead of the node, and a method group ahead of the reference. A later
`default` or target-typed `new` that reads a capture it does not own reads the
C# local its owner assigned. The union test compares the slot when the program
runs, as any test against a closing does.

Four rules keep that one evaluation correct wherever the value is read later.
A struct closing nested in another type's argument, as in
`default(Duo<Cell<s.Size>>)`, is built by a factory that reads the value its
owner evaluated, not the initializer again. An array whose length runs code
evaluates its element type's capture first, as written. An expression lambda
declares the C# locals its own captures are kept in inside its body, so one
call does not see another's value. And a method group's delegate reads the
value as it stood when the reference was evaluated. A switch arm compares the
slot as a `when` would, so exhaustiveness counts it as covering the constituent
only where both slots are one value: the same capture, literal or parameter, or
two captures that read one holder the body never writes. Otherwise MIST0148
names the constituent, and a value that reaches no arm throws when the program
runs, as it does for any switch that warning names.

Two sites are the owner's to rule on, because each is a signature written
inside a body and Requirement 8.1 was written for a method's own parameters:
a local function's parameter and return types, and a typed lambda parameter.
A type value, `var t = Ring<n>;`, is refused for the backend's reason and is
listed under *Identity by value*.

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

## A closing fits by refinement (Requirements 2, 3, 8.4)

The binder cannot see facts, so it admits and records, and Stage 5 proves.

`ConversionClassifier.Within` has a third test beside
`AgreesUpToUnknownValues`, `AgreesUpToProof`: the same definition, and each
argument pair is the same, or erases as today, or is a slot pair with at least
one side that is not a literal. A pair of two different literals stays refused
as MIST0157. The pairs admitted for proof are a capture, a refinement slot
argument or a dependent parameter against anything, and the `unknown` slot of a
class or struct against anything. For an abstract the classifier applies
Requirement 11 first, under *Abstracts* below.

Stage 5 judges each admitted pair at the sites `Proving.Judge` already visits
(`RefinementAnalyzer.cs:112-175`): the initializer of a declaration, an
assignment, a return against its `TargetType`, an argument, a `new` argument,
an initializer entry and a cast operand. `SlotIdentity.Judge` keeps its walk
over the two types, looking through `ImplicitConversionExpression` as
`Lengths.Of` does, and becomes the fit judgement. It reads the source's slot as
a term through `SlotReads` (next section) and asks `Chain.Prove` whether that
term lies inside the target slot's refinement:

- a literal is the refinement with one member, so the question is the one
  stages 1 to 3 ask, whether the term is that constant;
- a capture is the refinement kept at its `Declare` step;
- a refinement slot argument is its type's predicate;
- the `unknown` slot is the whole domain and owes nothing, as today;
- a dependent parameter in a declaration's type states the relation, so the
  question is equality with the value bound under that `TypeParameter`, the
  holder `FactReading.Holder` gives it (`FactReading.cs:179`), as stages 1 to 3
  built it.

A refinement is proven as an argument is proven against a parameter predicate
(flow-sensitive-refinement-inference Requirement 1): the facts on the source
term must leave no value outside the predicate, and the failing case names the
values they leave outside, `fails when m <= 14 or m >= 16`. `ClosingNotProven`
states the refinement the source missed instead of an identity.

What the delivered code does not yet do:

- `Cell<n> c = new Cell<m>();` with both a `Range` is accepted, and so is a
  later `c = new Cell<m>();` (Requirement 3.2). `c`'s slot equals `n` only while
  a creation's fact says so, and a write to `c` ends that fact, as
  flow-sensitive Requirement 3 ends every fact.
- Two types written with one capture no longer share a value by construction.
  A program that needs two slots equal is proven by the facts at the site that
  needs it.

Equality stays where a dependent parameter states a relation: a declaration's
type, above, and one dependent parameter bound by more than one argument
(Requirement 8.4, under *Signatures and calls*). Both ask `Chain.Prove` to rule
out a difference, as `SlotIdentity` does today.

Assignability judges a conversion in more places than `Proving.Judge` visits: a
switch arm, the right of `??`, an array literal element, an expression-bodied
lambda's result, a `foreach` element, a deconstruction target, a `with` value,
a field or property initializer, and a getter's expression body. At each of
those places the assignability rule refuses any slot pair that is not the same
capture, literal, refinement or parameter, with `ClosingNotProven`. A place
moves onto the chain where the flow graph has a step for it, and a place with
none keeps refusing a pair the refinement meaning admits. That refusal is a
stand-in, not the language.

A capture whose initializer is a plain read of a holder is bound to that
holder's current `Value`, not to a fresh value with an equality fact. Two
captures of one unchanged variable then read one value, so a creation's fact
that its slot holds `n` serves both, and a domain the flow records no facts for
is covered: its refinement is the capture's type, and only the same value or a
creation's fact relates two slots (Requirement 6, last paragraph). Any other
initializer gets a fresh value with what `Terms.Read` gives it.

## Reading the slot through a value (Requirements 7, 11)

Binding. `AddTypeNames` offers a type's own parameters to a qualified instance
request only when the type is intrinsic (`ProjectsInstead`,
`TypeMemberLookup.cs:306-311`), which is why `arr.Length` bound and
`ring.Capacity` was MIST0301. The dependent parameters are offered for every
type, so every slot read has the shape `arr.Length` has: a member access whose
member is the `TypeResult` of a dependent parameter. `Ring<4>.Capacity` keeps
its `ValueProperty`. Stages 1 to 3 refused a read through an abstract closed
with `unknown` in `PostfixBinder.ResolveMember` (Requirement 11.2 as first
written). Under Requirement 11 a value held as an abstract holds one closing,
so the read always binds, and the refusal moves to the conversion that would
have made the value ambiguous (*Abstracts*).

Facts. `SlotReads.Value(ref FactState, access)` says what the read is, from the
receiver type's argument for that parameter:

- a literal: that constant;
- a dependent parameter of the declaration the type was written in, outside a
  body: that parameter's value, because the declaration states the relation;
- anything else on a class or struct, a capture, a refinement slot argument or
  `unknown`: the value under `MemberHolder(receiver, parameter)`, as `Length`
  has it (`FactReading.cs:165-191`). Stages 1 to 3 read a capture as the
  capture's own value. Under the refinement meaning that is wrong once the
  variable is written: `c = new Cell<m>()` changes `c`'s slot and not the
  capture. The holder is keyed by the receiver's value, so `a = b` shares it, a
  creation records it equal to the creation's slot value, and the receiver
  type's refinement is a fact on it;
- an abstract: the holder keyed by the receiver's value together with the
  closing its type names. A value held as an abstract holds the one closing of
  the object that fits its type (Requirement 11.2), and two types over one
  object may name different closings, so `ISized<1> one` and `ISized<int > 1> b`
  over one `Both` read two holders (Requirement 11.7).

The type of a slot read stays the slot's domain, as dependent-types
Requirement 7.1 gives the property read through the type, and what the
receiver's type proves about it is a fact on the value (Requirement 7.3). That
keeps a value out of a type position (MIST-1357): `[a.Length]` is an array of
`int`, and `Id(a.Length)` binds `T` to `int`. With the read being the holder,
`slots[at]` under `at < Capacity` and `a[i]` under `i < n` are proven by the
facts already in the bag.

**A member reached through a value (Requirement 7.6).** Where the binder
substitutes a closing's arguments into a member's signature, a dependent slot
the receiver's type closes with anything but a literal is replaced by the
receiver's own slot read, not by the capture or refinement written in the
receiver's type. The substitution makes a capture on the member access whose
initializer is that read, as inference makes one for an `unknown` slot, and the
capture stands for the receiver's value: an argument meets it by equality, and
a result is closed with it. Through `t : ITake<Range>`, `Take(int[N] a)` takes
an `int[t.N]`, and `Items()` returns an `int[t.N]` whose length the facts know
to be `t.N`. Substituting the receiver type's refinement instead would let an
array of one length reach an object of another.

## Testing against a closing (Requirement 9)

The pattern's type is resolved through the same path, so its slots may hold a
capture, a literal or a refinement slot argument, and a capture is declared
before the test evaluates. `PatternNeverMatches` (MIST0193) is reported only
when the two types differ in a slot whose values are both literals.

On the branch a passing test selects, `SlotReads.Tested` adds, for each slot of
the tested type, that the binding's slot read lies inside the slot's
refinement:

- a literal: equality, so `ring is Ring<5> r` gives `r.Capacity == 5`, and
  `b is Buffer<5>` inside `Check<N:int>(Buffer<N> b)` gives `N == 5`;
- a capture: the refinement kept at its `Declare` step, and no equality with
  the capture's value (Requirement 9.3). Stages 1 to 3 add `== n` here;
- a refinement slot argument: its predicate.

The failing branch gains nothing. A test against a closing of an abstract
passes only where exactly one of the object's closings fits (Requirement 11.6),
which the backend answers under *Identity by value: unions and abstracts*. A
cast is a proof site already (`RefinementAnalyzer.cs:167`) and gets the fit
judgement with the others.

The run-time check is the refinement's predicate over the object's slot, which
the backend emits as it emits a test against a refinement type (refinement-types,
runtime validation). For a capture that predicate is the refinement Stage 5
kept, so Stage 5 hands each such test's refinement to the backend with the
model. That makes the test's answer depend on what the facts prove at the test;
see *Readings to confirm*.

## Signatures and calls (Requirement 8)

A stated dependent argument is a capture on the invocation. Inside the method
the parameter is the `TypeParameter` holder it is today, so a parameter typed
`Array<int, N>` has `Length` equal to `N`, because a declaration's type states
the relation, and a `return` is proven at the existing return site.

Inference binds what the argument's slot holds (`TypeArgumentInference.cs:437`,
`Record` at `:544`):

- a literal or a dependent parameter, as today;
- anything else, a capture, a refinement slot argument, the `unknown` slot of a
  class or struct, or the slot of an abstract: a capture on the invocation whose
  initializer is the slot read of that argument, through the holder of the
  previous section. Stages 1 to 3 bound a capture to itself, which under the
  refinement meaning reads the name instead of the argument, and refused an
  abstract's `unknown` slot, which Requirement 11.8 now admits.

A capture on the invocation stands for the method's parameter, so it is judged
by equality against the invocation's own arguments: each argument after the
first that binds the parameter is proven equal to it (Requirement 8.4), and a
shared refinement is not enough. `Dominant` (`TypeArgumentInference.cs:603-608`)
treats two different slot values as one because `Normalize` strips them to
their domain; the first bound keeps the parameter, and each later argument is
then proven by equality. Everywhere else, the result type a `var` takes
included, it is a capture like any other: it stands for its refinement, and the
facts record that the result's slot equals its value (Requirement 8.6).

**A regular parameter in the return type (Requirement 8 items 7 to 10).**
The grammar comes first. `ArrayRankSuffix` (`Mist.storm:393-395`) sizes a
rank only with an integer literal, so `int[n]` is MIST0123 in every type
position on main and on this branch at 652d20d3, and so is `int[N]` for a
dependent parameter, in a signature and in a body alike. Only the
`Array<int, n>` spelling reaches the binder. `SizedArrayRank` takes a name or
a dotted path of names as well as a literal, the forms Requirement 5.1 admits
in a slot, in every type position. What the name may refer to stays the
binder's: a capture in a body under Requirement 5, the return type's
parameter under item 7, a dependent parameter anywhere, and MIST0487
otherwise. `a[i]` stays an index wherever it is one today.

`ScopeResolver.SlotValue` (`ScopeResolver.SlotArguments.cs`) reports MIST0487
for a value's name in any slot outside a body. While it resolves a method's
return type, it binds a name in the return type's own length slot, when the
return type is an array, to the method's regular parameter of that name
instead. Every other slot in a signature keeps MIST0487. The binder checks the
parameter where the name resolves: a parameter that is not a `readonly` value
parameter is refused there, and the return type is not built.

The slot holds the parameter, not a capture, and states the relation as a
declaration's dependent parameter does. Inside the body it reads the
parameter's value, which nothing can write, so `return new int[n]` is proven
at the existing return site: the creation records `Length == n`. The slot's own contract, item 9, gets a new Stage 5 site that
reads the facts a body starts with, the parameters' types and the method's
`where` clause (flow-sensitive Requirement 2 items 4.1 and 4.2).

At a call, substitution puts a capture on the invocation in the parameter's
place, which stands for the parameter as a stated argument's does. Its initializer is the argument bound to that parameter after named and
default arguments are placed, as inference already does for an `unknown`
slot. The argument is evaluated once, as an argument. Array's slot is erased
(`LengthErasure`), and a read of it through the value is the array's `Length`
(Requirement 7), so the capture is never emitted, and the method's C#
signature is `int[] Zeroes(int n)`.

## What may close a type (Requirement 6)

`DependentDomains.Primitive` and `KindOf` (`DependentDomains.cs:17-43`) gain a
fourth kind beside integral, bool and char: a type for which
`Stampability.IsStampable` holds (`Stampability.cs:19`) and
`MemberType.IsReadonly` is set (`MemberType.cs:49`). `Holds` stays a question
about literals and the three kinds that have them. `Forwards` requires the
same type for the fourth kind. A slot of the fourth kind is always closed by
a name, and its proofs rest on the same-value rule above.

## Static storage (Requirement 10)

Each value has its own storage, and a static member reached through a closing
already carries the closing, so `Counter<n>.hits` reaches the storage of the
value the capture's initializer produced, whatever its refinement. An instance
member reaches the storage of its own object's value (Requirement 10.3), which
the value route's hidden instance field already gives it.

A closing written with a refinement slot argument names no value
(Requirement 10.4). The binder refuses a static field or an auto-property
reached through it, where `PostfixBinder` resolves a static member through a
closed type, and the diagnostic says to name a value. A static method or a
computed property reached through it is admitted by the binder and judged by a
Stage 5 rule, `StaticThroughRefinement`: it walks the member's body and the
bodies it calls, within the declaration and through its closings, for a read
of one of the declaration's dependent parameters or of one of its static
fields, and refuses the access at the first, naming the read. The rule walks
bodies, so it is proven at Tier 2. `new` and `default` written with a
refinement slot argument are refused by the binder for the same reason
(*The refinement meaning*).

## Abstracts (Requirement 11)

A value held as an abstract holds exactly one of the object's closings of that
abstract, and every conversion keeps that true. `ConversionClassifier` judges a
conversion whose target is a closing of an abstract before the third test of
*A closing fits by refinement*:

- **From a value whose exact class is known**, a `new`, a struct or a sealed
  class (Requirement 11.3). The classifier lists the class's closings of the
  abstract, through its bases. A closing whose slots are literals is outside the
  target when a target literal differs, or when a refinement slot argument's
  predicate refuses the constant; any other closing may fit. More than one that
  may fit is refused, and the diagnostic names them. Exactly one is admitted,
  the value holds it, and Stage 5 proves it fits as it proves any pair.
  A `new Both()` converts to `ISized<2>` and to `ISized<int > 1>`, and to
  `ISized<ZeroOrPositiveInt>` not at all.
- **From a value whose exact class is not known** (Requirement 11.4): only to a
  closing whose slots are literals, which the class implements as today. A
  subclass can add a closing the value's type does not show.
- **From a value held as a closing of the same abstract** (Requirement 11.5):
  admitted for proof, and Stage 5 proves the target's refinement lies inside the
  source's as written, not as the facts narrow it, and the source's slot inside
  the target's. One refinement inside another is the fit judgement asked of a
  fresh term whose only facts are the inner refinement. A widening is refused
  with its own diagnostic, which suggests writing the method over the slot,
  `void Use<N:Range>(ISized<N> s)`.

A type test against a closing of an abstract asks the object and passes where
exactly one of its closings fits (Requirement 11.6); its binding holds that
closing, and its facts are keyed as *Reading the slot through a value* keys an
abstract's. Inference from an argument held as an abstract binds the slot of
the closing it holds (Requirement 11.8, under *Signatures and calls*).

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
  parameter's read. A closing written with a refinement slot argument has no
  value, and every read that would need one is refused in the frontend.
- A struct's hidden field is stored relative to the anchor of the refinement
  its C# type carries, `TN.Anchor` (*The refinement meaning*), so a struct .NET
  zeroes reads the anchor. Each refinement a struct closing is written with is
  then its own C# type, and converting a value from one to another copies it
  and re-encodes the field. A literal closing is the one-member case, whose
  anchor is its literal: the carrier route stages 1 to 4 kept. A class keeps
  the plain value and one C# class for all its closings; its zeroed storage is
  a null reference, which is not this spec's.
- A test appends the slot's refinement: `x is Ring r && r.__Capacity == 5` for
  a literal, and the refinement's predicate over `r.__Capacity` for a capture
  or a refinement slot argument, `r.__Capacity > 10 && r.__Capacity < 30`, the
  predicate a test against the refinement type itself emits. Stages 1 to 4
  append `== n`. A switch arm takes it as a `when`. `as` already lowers to a test when a shape has
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

### Identity by value

The owner ruled on 2026-11-13 that the backend makes these programs work. An
object's closing is identified by its value, however it was written, and under
the refinement meaning it still is: what changes is what a test asks of that
value (*Testing against a closing*).

```mist
int n = 5;
var x = new Ring<n>();
var y = new Ring<5>();

x is Ring<n>    // true
x is Ring<5>    // true
```

C# fixes an object's type when the object is created, so `x` and `y` are
instances of one C# class, or `x` could never be a `Ring<5>`. That is why a
value-routed declaration is one C# type for all its closings. It follows that
the C# type answers nothing about which closing a value has, and the backend
answers from the value wherever the program can observe the difference. A
runtime closing in one place never changes whether code in another place
compiles.

The refusals in the two sections below are stand-ins. Each is replaced by a
delivery of its own. A delivery first adds its design section here, with the
list of every place its mechanism touches, and its before-and-after programs
are shown to the owner before it is dispatched. In order:

1. A test with no binding, and a test under `not` or `or` (MIST0494). The
   comparison is an expression beside the C# test, not part of a C# pattern.
2. A slot inferred from a closing nested in an argument (MIST0498). The caller
   knows the argument's type, so it passes the closing's value: the literal,
   the capture or the parameter. Nothing is read back from the object.
3. Overloads that differ only in a slot (MIST0497). The binder has already
   chosen the overload, so the coinciding members take distinct C# names.
4. An array or a stack allocation of a struct closing (MIST0496). The elements
   are built with the value.
5. Static storage of a value-routed type (MIST0491). One storage for each
   value of the slots, created on first use, as Requirement 10 states.
6. A union that holds two closings of one type (MIST0501), and an abstract
   implemented at two values (MIST0493). The carrier and the implementing type
   answer by value.
7. A conversion into the type, and a nested type that reads the enclosing
   slot (MIST0499). A conversion is a call and takes the value as one. A
   nested type carries the enclosing slots as its own.
8. A closing as a type argument (the section of that name, and MIST0495). A
   generic that tests, creates or holds static storage over its type
   parameter takes a hidden description of the argument, which carries the
   slot values. `SlotSensitivity` decides which parameters need one.

Not settled. Each is the owner's to rule on, with an example, when its turn
comes:

- A type value of a runtime closing (MIST0492). A CLR `Type` cannot carry the
  value.
- A test against a closing inside a CLR container, `u is List<Ring<n>>`, and a
  CLR generic method that tests its type parameter. A CLR object has nowhere to
  keep the description.
- An attribute argument that reads the slot. An attribute's arguments are
  constants.
- A dependent parameter written as a type, `x is N` (MIST0500, MIST-1363).

### A type for every literal closing

The owner adopted this on 2026-11-15 ("I agree to all of that"): a literal
closing keeps its own C# type, the overloads task MIST-1371 is dropped, and
the open items below are ruled one at a time before the task that meets each.
The `T | void` rule at the .NET boundary is his proposal; its details are
confirmed before its own task. No task builds from this section until its
before-and-after programs have been shown to him.

**What the value route costs literal closings.** The value route makes a
declaration one C# class for every closing as soon as one closing anywhere is
known only at run time. A closing written with a literal then loses its own
C# type. Measured at 87d653d0, with `Ring` holding nothing but its slot:

```mist
var fives = ArrayPool<Ring<5>>.Shared.Rent(2);
fives[0] = new Ring<5>();
ArrayPool<Ring<5>>.Shared.Return(fives);

var sevens = ArrayPool<Ring<7>>.Shared.Rent(2);
unknown got = sevens[0];
got is Ring<5>      // False; True once `new Ring<n>()` is added anywhere
```

Without a run-time closing the two are `Ring<Const_5>` and `Ring<Const_7>`,
two C# types the CLR keeps apart. With one, both are `Ring`, and the CLR's
pool hands the `Ring<5>` back as a `Ring<7>`. Overloads that differ only in a
slot, static storage, unions of two closings, an abstract at two values and a
closing as a type argument are refused or rebuilt for the same reason: items
3, 5, 6 and 8 restore, for literal closings, what the carrier route gave them.

**A type for every literal closing.** A closing whose value is known when the
program is compiled keeps its carrier type, `Ring<Const_5>`, whatever else the
program does. Only a closing known at run time uses a shared form:

```csharp
public abstract class Ring__Any { public abstract int __Capacity { get; } }
public sealed class Ring<TCapacity> : Ring__Any where TCapacity : IConstIntegral { ... }
public sealed class Ring__Run : Ring__Any { ... }   // a value no literal closing names
```

- A place whose type is `Ring<n>` is `Ring__Any` in C#.
- `new Ring<n>()` chooses by the value among the literal closings the program
  writes: `n == 5` makes a `Ring<Const_5>`, and a value no literal closing
  names makes a `Ring__Run`. An object's C# class is therefore fixed by its
  value, so `x is Ring<5>` is `x is Ring<Const_5>`, and a proven
  `Ring<5> y = x` is a C# cast that cannot fail.
- `Ring<n>.hits` reads `Ring<Const_5>.hits` when `n` is 5, and the holder
  table of item 5 for a value no literal closing names. Requirement 10's one
  storage per value holds.
- `Foo(Ring<3>)` and `Foo(Ring<4>)` are different C# signatures again.
- A struct has no base class. A literal closing is `Cell<Const_5>`. A closing
  written with a capture or a refinement is a struct generic over its
  refinement's carrier, storing its value relative to that carrier's anchor
  (*The value route*). A proven assignment between two closings copies and
  re-encodes, as struct assignment copies. Stages 1 to 4 planned a `Cell__Run`
  with a flag set when Mist creates it; the anchor replaces the flag, since a
  zeroed struct is now a valid one.

**A run-time closing at the .NET boundary.** A CLR generic sees one class,
`Ring__Any`, for every closing of `Ring` a capture or a refinement writes, and
may hand back another refinement's object. The owner proposed treating such a
value as Mist already treats a CLR reference (assembly-type-provider
Requirement 14.2): a class closing written with a capture or a refinement,
coming back from CLR code, has type `T | void`, and narrowing it to `T` checks
that the value is present and that its carried value lies inside the type's
refinement. A value that fails is `void`. A struct closing is its own C# type
for each refinement, so a CLR generic hands back only a value of that
refinement or a zeroed one, which the anchor makes valid; it needs no check.

```mist
var rings = new List<Ring<n>>();
rings.Add(new Ring<n>());

var got = rings[0];                 // Ring<n> | void
if (got is Ring<n> r)
{
    Console.WriteLine(r.Capacity);  // n
}

var rented = ArrayPool<Ring<n>>.Shared.Rent(2);
rented[0] is Ring<n>                // false: null, or a ring outside n's refinement

var cells = new List<Cell<n>>();
cells.Add(new Cell<n>());
Cell<n> first = cells[0];           // a Cell<n>: a struct closing needs no check
```

A literal closing is unaffected: `List<Cell<5>>` hands back a `Cell<5>`, since
`Cell<Const_5>` is its own CLR type. The check runs once per value that
crosses back, not on reads in Mist code. This replaces MIST0502's third kept
case and closes the class leak, which a presence check alone does not.

**Containers and generics, decided 2026-11-21 ("Choose by value"), superseded
the same day by *The refinement meaning* above.** An
array or generic built from a run-time closing gets its C# type by value, as a
single object does: with `n == 5`, `new Cell<n>[k]` makes a `Cell<Const_5>[]`
and `new Tester<Ring<n>>()` a `Tester<Ring<Const_5>>`. A proven
`Cell<5>[] a = b` with `b` a `Cell<n>[]` is then a cast that cannot fail, and
`a` aliases `b`. `x is Ring<5>[]` is answered by the CLR type, and so is
`Tester<Ring<5>> t = u` with `u` a `Tester<Ring<n>>`. A place typed with a
run-time closing needs a C# type both forms fit. For a class closing's array
that is an array of the shared base, which CLR array covariance allows at the
price of a type check on each element write. A struct closing's array and a
generic class need a wrapper or a base type the backend emits. My delivery
order, not part of the ruling: class arrays first, then struct arrays and
generic classes in their own task, refused as a backend stand-in until it
lands.

**Left open.** Each is the owner's, with an example, before a task is written:

- A Mist array or span of a run-time struct closing handed to CLR code that
  writes into it, `Array.Resize(ref cells, 10)`. The new elements are zeroed
  inside Mist's own array, with no boundary between them and the next read.

### Where refinements are enough

The owner's direction on 2026-11-15: "So it's both what you already wrote and
the extra refinement improve could provide a way you don't need dependent
typing for certain cases." The design above stands. Alongside it, three
refinement facts were proposed to the refinement work in
[runtime-length-facts](../runtime-length-facts/requirements.md), so that a
program that only sizes arrays at run time proves what it needs without
closing a type at run time. Its Requirement 1 was delivered on main at
1742180d3, its Requirement 2 was withdrawn, and its Requirement 3 moved here
as Requirement 8 items 7 to 10.

Measured with this feature's six gaps written without any run-time closing,
on main at e2656913 (refinements, no run-time closings) and on this branch at
1424a04b:

| Gap | main | branch |
|---|---|---|
| 1. A value in a size or a constructor argument | works | works |
| 2. Evaluated once, value kept | works | works |
| 6. Member access and tests through a field | works | works |
| 3. `a[i]` under `m == n` and `i < m` | MIST0275 | works |
| 4. Two lengths related at a call by a `where` bound | MIST0277 | works |
| 5. `a[i]` under `i < n` | MIST0275 | works |
| 4. A length through a return, `int[] Zeroes(n)` | MIST0277 | MIST0277 |

Rows 3 to 5 fail on main for one reason: `new int[n]` records no
`a.Length == n`. The branch gets it from the type `int[n]`; the refinement
proposal records it as a fact instead. The last row needs a return type that
states a relation to its arguments, which neither compiler has.

Measured again on main at fdd6dc74e on 2026-11-20: rows 3 to 5 are proven
there, and the last row is still refused there, since `int[n] Zeroes(readonly
ZeroOrPositiveInt n)` does not parse (MIST0123). It is built here, stage 7 of
*Sequencing*.

A CLR array keeps its own length, so a fact about it holds wherever the array
goes. A user type's slot has no such keeper. A run-time closing of one stays
with this design in every slot domain, integers included, and comes back from
CLR code as `T | void` ("Cell<n> if you want a dependent runtime int will
still need to be Cell<n>? when it crosses the barrier to be safe"). So do a
relation stored inside data, identity by value, and a slot whose domain is a
struct value.

### Identity by value: the first delivery

Items 1 to 4. Each program was run at afd43acab and is refused there with the
diagnostic its comment names. After the delivery it prints what the comment
says.

**A test under `not` or `or`.**

```mist
public class Ring<Capacity:int>
{
    public bool Differs(unknown other)
    {
        return other is not Ring<Capacity>;     // MIST0494
    }
}

var x = new Ring<n>();      // n is 5
x.Differs(new Ring<5>())    // False
x.Differs(new Ring<6>())    // True
```

A C# pattern has no place for the comparison under `not` or `or`. The emitter
moves a comparison out of a pattern only from a conjunction, through the
`hoisted` list of `EmitPattern`, and refuses where there is no list. A pattern
that holds a runtime test under a combinator is emitted as a boolean
expression over the scrutinee instead. The scrutinee is bound once, each test
is its own `s is Ring t && t.__Capacity == v`, and `not`, `or` and `and`
become `!`, `||` and `&&`. A `switch` arm becomes `var s when <expression>`.

**A slot inferred from a closing nested in an argument.**

```mist
static int Len<N:int>(List<Ring<N>> rings) { return N; }

var list = new List<Ring<n>>();    // n is 5
n = 9;
Len(list)                          // 5; MIST0498
```

The value is the capture's, taken where `list`'s type was written. It cannot
be read from `n` again, and a list has no element to read it from. A capture
that a later node reads, and that no receiver or argument carries, is kept in
a C# local. The local is declared before the statement that owns the capture
and assigned where the type is written, so the initializer still runs once
and in order. `CallSlotArguments` and `GroupSlotArguments` read that local
where they refuse today. A slot nested under `unknown`, as in
`List<Ring<unknown>>`, has no value anywhere. It stays refused and belongs
with the CLR container limit above.

**Overloads that differ only in a slot.**

```mist
static string Name(Ring<3> r) { return "three"; }
static string Name(Ring<4> r) { return "four"; }    // MIST0497

Name(new Ring<3>())    // three
Name(new Ring<4>())    // four
```

The binder chooses the overload from the Mist types, and a call with no proof
of the closing does not compile, so the choice never needs the value at run
time. Members of one name whose emitted parameter lists coincide take distinct
C# names. A name is derived from the member's own Mist signature, so it does
not change when another overload is added. Every reference to the member
spells that name: a call, a method group, an implementing type's member for an
abstract's, and a restated default. A constructor and an indexer cannot be
renamed in C#: a constructor takes a discriminating parameter, and an indexer
is emitted as its accessor methods. `ReportCoincidingOverloads` finds the
coinciding sets today.

**An array or a stack allocation of a struct closing.**

```mist
public struct Cell<N:int> { public int Size() { return N; } }

var cells = new Cell<7>[2];    // MIST0496, once Cell is closed at run time anywhere
cells[1].Size()                // 7
```

Every element is the closing's stamp, made once with the slot's value and
copied into each element. definite-assignment Requirement 4.1b already asks
for this wherever a stamp differs from the CLR zero, and a hidden field that
holds the slot's value makes it differ. The creation in a generic body,
`new T[k]`, is item 8.

The delivery hands in, for each of the four, the list of every place its
mechanism touches, each marked done, cannot occur, or left with its reason.
Stage 4 had no such list until its fourth review.

Items 1, 2 and 4 landed with MIST-1370, over ad262e7e. MIST0494, MIST0496 and
MIST0503 are retired as tombstones. A test under a combinator goes through
`BooleanPatternConditions`, which already rendered a regex or a promoted
comparison under `not` or `or` as a boolean expression over the scrutinee
bound once. The capture local is decided over the whole body before any of it
is written (`BodyEmitter.CaptureLocals`): a capture some call or method group
reads without a receiver or an argument that carries it is kept, declared
before its owner's statement and assigned where the type is written. The same
walk makes every owner evaluate an initializer that runs code exactly once: a
call or a member access that consumes none of its captures as a hidden
argument evaluates them before its arguments, after a receiver written before
the type, and a test, a switch arm and an `as` evaluate theirs between the
scrutinee and the type test, so a failed test does not skip them (MIST-1369).
What runs before the type is bound ahead of the capture, and a value-type
receiver, an assignment's target and an argument passed by reference are
bound by what locates them (the object holding the field, the index) and
never copied, as `UnitHoisting` stores them. An owner written as a location,
a property through a closed type that is assigned or a call that returns by
reference, stays where it is written, and the expression or the statement
holding it evaluates the capture. A call returning by reference runs in
place, and a capture written after it is evaluated as it returns. A call
that yields nothing has no value to wrap, so in a lambda body, an accessor
body and a `for` clause its capture is a statement ahead of it. A call that
`UnitHoisting` moves ahead of its statement, or stores in a temporary ahead of
a later operand, evaluates its capture once where it moves to, and what stands
in its place, the unit instance or the temporary, evaluates nothing. A lambda's
result type holds `unknown` where its body closed a type with a capture
(Requirement 1.6), so an inference from it outside the lambda is the slot
nested under `unknown`. A list of rings of unknown capacity carries no single
value, so a dependent parameter whose only source is a slot under `unknown`
nested inside another type's argument, or a function type's position, is
MIST0490 in the frontend (Requirement 8.3, MIST-1373); a `Ring<unknown>`
argument passed directly still infers through the object's own read.
The stamp of a struct element is `StructStamp`: the type's own default where
the backend synthesizes one, so a struct holding the closing in a field, an
auto-property or a nested struct starts from its default with the inner stamp
inside, and otherwise the struct built from its parts through `__FromParts`
with the hidden slot values filled in; `MistArray.Stamp` copies it into every
element of the array or the span. MIST0498 stays for what the frontend does
not infer: a slot stated as `unknown`, a method group converted against a
type with a slot nested under `unknown`, and an extension block's receiver.
Item 3 is MIST-1371.

### Identity by value: static storage

Item 5. This is Requirement 10's own example. At afd43acab the declaration of
`hits` is refused with MIST0491 because of the last two lines:

```mist
public class Counter<N:int>
{
    public static int hits;
}

Counter<1>.hits = 5;
Console.WriteLine(Counter<2>.hits);     // 0
var n = GetOne();                       // 1 when the program runs
Console.WriteLine(Counter<n>.hits);     // 5
```

Closed only with literals, the same type keeps separate storage today: a
static field, a static initializer that reads `N`, a static auto-property, and
a static method and an instance method that read the field all answer per
closing.

A value-routed type's static storage moves into a holder. The holder is a
nested C# class with one instance field for each static field and each static
auto-property. Its constructor takes the type's value-routed slots and runs
the static initializers in declaration order, so an initializer that reads
`N` reads the holder's own value. The type keeps one table from the slot
values to the holder.

A read or a write of static storage through a closing, `Counter<n>.hits`,
finds the holder for the closing's values and uses its field. Inside the
type's own members the values are the hidden field or the hidden parameters.
A holder is made on first use and exactly once for a value, as a C# static
initializer runs exactly once, so the table hands out one holder when two
threads ask together.

Two closings that are one type under Requirement 2 hold the same values, so
they reach the same holder, which is Requirement 10.2. The table grows with
the distinct values a program uses, as the requirement says it does. A type
that also has type parameters, `Pair<T, N>`, keeps its C# type parameter, so
its table is one per `T` with no further work. The key is the slot's value; a
slot of the fourth kind needs value equality on its domain, which stage 5
supplies.

Static storage of an ordinary generic closed with a closing of a
value-routed type, `Counter<Ring<3>>`, is item 8.

The delivery hands in the list of every form of static storage and every
place that reads or writes one, each marked as above.

Item 5 landed with MIST-1372, over 6ae40868. MIST0491 is retired as a
tombstone. The holder is `__Statics`, nested in the type, with a readonly
field for each value-routed slot and one member for each static field and
static property, computed properties included, since a getter that reads the
slot needs the value as much as an initializer does. Its constructor runs
the initializers in the order the type's C# statics run them on the carrier
route, the fields, then the properties, then the static constructor's body,
so a program prints the same whichever route its types take. The table is
`__statics` and the lookup `__StaticsOf`, which takes the slot values as the
type's static methods take them. The table enters a holder before the
constructor runs anything, so an initializer that reaches its own value's
storage, directly or through a static method, reads the holder being built,
as a C# static initializer reads its own statics; one lock across all the
tables builds each holder exactly once and lets no two types deadlock. The
holder's own constructor and members reach its storage through `this`, which
is where a readonly static is assigned. A type that declares a static
constructor touches its holder from every instance constructor and every
static method, because C# runs a static constructor on any use of the type
and not only on a read of its storage. A read through a closing evaluates a
capture's initializer once, as the argument of the lookup, and a compound
assignment that C# cannot write in place binds the holder once. A bare name
inside the type's members supplies the hidden field or parameter; inside an
operator, the operand that carries the slot; inside a nested type, the slot
the nested type carries as its own (item 7). A struct's static is held by the struct that
copies it, as its C# static is today, and a struct's ancestor factory carries
no static member. A static spread member's per-element projections are
members of the holder. A static with no initializer that holds a value-routed
struct by value starts from the struct's stamp, in the holder and in an
ordinary type's C# static alike, as an array element does. A static lambda
or a static local function keeps its modifier only where it reads no hidden
value, since C# lets it capture none.

### Identity by value: unions and abstracts

Item 6. Each program below runs at afd43acab while its types are closed only
with literals, and prints what its comments say. Each is refused once the type
is closed at run time anywhere in the program.

**A union that holds two closings of one type.** Add `var x = new Ring<n>();`
anywhere and `Which` is MIST0501.

```mist
static string Which(Ring<5> | Ring<7> u)
{
    if(u is Ring<5> a) { return "five"; }
    if(u is Ring<7> b) { return "seven"; }
    return "neither";
}

Which(new Ring<5>())    // five
Which(new Ring<7>())    // seven
```

A union's carrier has one entry for each constituent, named for the
constituent's C# type, and two closings of a value-routed declaration are one
C# type (`EmitUnion`, `UnionCarrierNames.Coinciding`). Constituents that
coincide share one entry. A value needs no tag to enter it, because the object
carries its slot's value. A test against one of them is the entry's test and
the comparison every runtime test already appends: `u is Ring<5> a` is the
entry test and `a.__Capacity == 5`.

A union with a runtime closing as a constituent cannot be tested at all today,
for a frontend reason. With `Ring<n> | string a`, the test `a is Ring<n> x` is
MIST0345, "not one of the types the union can hold": the constituent and the
tested type are two captures of `n`, and the check compares them as written
instead of as Requirement 2 judges them. That is fixed in the frontend, with
the other body sites, and is not part of this delivery.

**An abstract implemented at two values.** Add a test `u is ISized<n> sized`
anywhere and `Both` is MIST0493.

```mist
public abstract ISized<N:int> { string Describe(); }

public class Both : ISized<1>, ISized<2>
{
    public string Describe() { return "both"; }
}

ISized<1> one = new Both();
one.N                       // 1
x is ISized<2> two          // true for a Both, false for a One : ISized<1>
```

A value-routed abstract declares a hidden getter, and each implementing type
answers it with one value (`ValueRouteEmitter`, `Answered`). A type that
conforms at two values has two answers. The getter becomes a hidden test that
takes the values and answers whether the object conforms at them. `Both`
answers for 1 and for 2. A type that conforms at its own parameter,
`Ring<N> : ISized<N>`, compares with its hidden field.

Under Requirement 11 a value held as a refinement of an abstract, `ISized<int >
1> b`, holds the one closing of its object that fits that refinement, and no
conversion lets it hold another, so the backend can find that closing again at
every use instead of carrying it. A read of `b.N` asks the object for the value
of its closing of `ISized` that the refinement's predicate admits, a hidden
member beside the hidden test; a call through `b` passes that value to the
dispatcher below. A type test against a refinement closing counts the object's
closings the predicate admits and passes on exactly one. A read through a
literal closing is the literal, as before.

A member may differ by value, and this runs today too:

```mist
public abstract ITake<N:int> { string Take(Ring<N> ring); }

public class Both : ITake<1>, ITake<2>
{
    public string Take(Ring<1> ring) { return "took one"; }
    public string Take(Ring<2> ring) { return "took two"; }
}

one.Take(new Ring<1>())     // took one, through an ITake<1>
two.Take(new Ring<2>())     // took two, through an ITake<2>
```

Once `ITake` is value-routed there is one C# interface member for both
conformances. It becomes a dispatcher: it takes the abstract's slot values as
hidden leading parameters, as a value-routed method does, and calls the
implementation for those values. The implementations already have distinct C#
names from item 3, and a caller passes its closing's values. A member that
does not differ by value, as `Describe` above, has one implementation and
needs no dispatcher. Two members cannot differ in their return type alone
(MIST0176), so a difference is always in the parameters.

The delivery hands in two lists, each entry marked as above: every way a value
enters, leaves and is tested in a union whose constituents coincide; and every
way an abstract's member is declared, restated, implemented and called,
through a literal closing, a capture, a refinement slot argument, a dependent
parameter and `unknown`.

### Identity by value: conversions and nested types

Item 7. Both programs were run at ad262e7e.

**A conversion into the type.** The first cast runs today and reads back 5
and 9. The second makes the conversion's body MIST0499.

```mist
public class Ring<Capacity:int>
{
    public int Tag;

    public static explicit operator Ring<Capacity>(int x)
    {
        var made = new Ring<Capacity>();
        made.Tag = x;
        return made;
    }
}

var five = (Ring<5>)9;
var other = (Ring<n>)4;     // Capacity n, Tag 4
```

A C# conversion operator takes its operand and nothing else, and an `int`
carries no slot (`SlotReach.OfOperator`). The cast names its target, so the
place that writes the cast knows the closing's value. A conversion of a
value-routed type whose operand does not carry the slot is emitted as a
static method that takes the slot values as hidden leading parameters, and
each cast calls it with its target's values. A cast is a call to a conversion
function already, so nothing changes for the binder. An implicit conversion
takes the values from the type it converts to.

**A nested type that reads the enclosing slot.** With the last line this is
MIST0499. Without it, closed only with literals, the generated C# does not
compile, on main as well. Measured at 87d653d0, the read itself is fine: on the
carrier route it is `((int)TCapacity.Value)`, which C# accepts inside a class
nested in `Ring<TCapacity>`. What C# does not know is the nested type's name
where `Make` spells it, `Probe.Ring<Capacity>.Node`, written with the Mist
name of the parameter instead of its C# name `TCapacity`.

```mist
public class Ring<Capacity:int>
{
    public class Node
    {
        public int Limit() { return Capacity; }
    }

    public Node Make() { return new Node(); }
}

new Ring<5>().Make().Limit()    // 5
new Ring<n>().Make().Limit()    // n
```

A type nested in a generic type is a type of each closing of it, so it carries
the enclosing type's value-routed slots as its own: a hidden field, set from a
hidden constructor parameter, which the creation fills from the enclosing
instance's field or from the closing that is written. The literal case is a
spelling fix on the carrier route, which it keeps: a type closed only with
literals has no value-routed slot to carry, and C# nesting already gives each
closing its own nested type (operator ruling, 2026-11-14).

The delivery hands in two lists, each entry marked as above: every conversion
and operator form whose operands may not carry the slot; and every way a
nested type is declared, created, named and tested, at one and at two levels
of nesting.

Item 7 landed with MIST-1435, over 87d653d0. MIST0499 stays for an attribute
argument and a union-specialized copy, neither of them item 7's.
`OperatorMethods` decides which operators and conversions become methods. A
conversion or operator of a value-routed type that some slot reaches through
no operand (`BodyEmitter.OperandRead`) is a static method named `__Explicit`,
`__Implicit` or `__op_{Name}`. A conversion out of the declaring type also
spells its declared target in the name, since C# keeps two conversions from
one source apart by target but two methods only by parameters:
`explicit operator int(Ring<1> r)` and `explicit operator long(Ring<1> r)`
would otherwise both be `__Explicit(int, Ring)`. A conversion into the
declaring type needs no more, because its target erases to that type at every
closing. The method takes the slot values as the hidden leading parameters
every static member takes, and its body reads them by their bare names. A cast passes the values of its target. An implicit conversion passes
the values of the type it converts to. An operator passes the values of the
closing it was found through. A cast's own capture is the hidden argument,
evaluated after an operand that runs code, as the operator route evaluates
it. An implicit conversion's target was written by another node, so it reads
that node's capture from a kept local, never the variable's value at the time
of the conversion. A conversion or operator none of whose operands, nor its
target, is the declaring type as declared is a method on either route, since
C# refuses it as an operator: `operator +(Ring<1> a, int b)` and
`explicit operator int(Ring<1> r)` inside `Ring<Capacity>`. MIST-1371's
renaming of coinciding conversions lands on the same static-method shape, so
one name decides both.

`SlotRoutes.ValueRouted` of a type nested in a value-routed type lists the
enclosing slots before its own. Everything keyed by a type's value-routed
slots follows from that list: the hidden fields and constructor parameters,
the hidden parameters of its static members, its statics holder, an abstract's
hidden getter and an implementer's answer, and the comparison a test appends.
`SlotRoutes.Argument` finds a slot's value through a nested closing's
`Enclosing` chain, `Ring<n>.Node.Leaf` included. A nested type named bare
inside its enclosing type is that type's own closing (`SlotRoutes.Closing`),
so its slot values are the reads of the enclosing slots where it is written:
the enclosing instance's field, a static member's hidden parameter, or the
nested type's own field. A creation with no semantic default witness to
pass, of a nested type holding the enclosing `T`, calls the compatibility
constructor, which takes the slot values all the same. A nested type reaches the enclosing type's static
storage through the values it carries. Its own statics are held per value,
as the carrier route's C# nesting holds them per closing.

The frontend refuses or cannot write these forms from the two lists, and the
backend sees none of them:

- `new Ring<n>.Node()`. The grammar's object creation takes a qualified name
  and its arguments, with no nested type through an instantiation. A
  target-typed `Ring<n>.Node x = new();` works.
- A static member reached through a nested type of a runtime closing,
  `Ring<n>.Node.Twice()`, and an enum member, `Ring<n>.Mode.On`, are MIST0487.
  The static member works through a literal or a dependent parameter.
- A conversion from a closing of the same declaration into a runtime closing,
  `(Ring<n>)one` with `one` a `Ring<1>`, is MIST0486. The binder judges it as
  a conversion between closings, which needs a proof. With a literal target it
  works.
- A cast, or an implicit conversion, into a nullable target `Ring<n>?` is
  MIST0149 or MIST0157, for literal closings too.

### Identity by value: a closing as a type argument

Item 8. The four programs of *A closing as a type argument* printed wrong
answers at 7926a184 and are refused as MIST0502 since ad262e7e. After this
delivery they print the right ones, and so does a test forwarded through two
generics, which runs today while its types are closed only with literals:

| Program | After |
|---|---|
| `new Tester<Ring<5>>().Is(three)`, then `.Is(five)` | `False`, `True` |
| `Counter<Ring<3>>.hits = 1; Counter<Ring<4>>.hits = 2;` and then both are read | `1`, `2` |
| `Blank<Cell<7>>()[0].Size()` | `7` |
| `Outer<Ring<5>>(three)`, where `Outer<T>` calls `Inner<T>`, which asks a `Tester<T>` | `False` |

The backend already passes a hidden witness wherever the CLR erases what Mist
tells apart. `DefaultConstraintWitness` passes a factory for
`where T : default`, `ConstructorWitnesses` passes one for a constructor
constraint, and `ContextKeyWitnesses` passes a `System.Type` for a `with` key
because `string?` and `string` are one CLR type. A closing witness is the same
answer for a slot.

A type parameter gains a witness when `SlotSensitivity` marks it and some
instantiation in the program fills it with a type that mentions a closing of a
value-routed type. On a method the witness is a hidden leading parameter. On a
type it is a hidden field set from a hidden constructor parameter. A
declaration no such instantiation reaches is emitted as it is today.

The place that writes the closing builds the witness, because it knows the
slot's value: the literal, the capture or the parameter. The witness answers
by the reason the parameter was marked:

- A test against the parameter asks the witness. It holds the test the writer
  would have emitted for the closing itself: the C# test and the slot
  comparison.
- `new T[k]` and a stack allocation copy the witness's stamp into every
  element.
- Static storage of the generic type uses the holder table of item 5, keyed by
  the witness's key: the declaration and its slot values, compared by value.
- A body that passes its parameter on passes its witness on. An abstract's
  generic method slot declares the witness, and each method that answers the
  slot takes it.

Three things a witness cannot answer. They keep MIST0502, with their reason,
until the owner rules on each:

- The type value of the parameter. A CLR `Type` cannot carry the value.
- A test against a type that nests the parameter in a CLR container,
  `x is List<T>`. The list object has nowhere to keep it.
- A CLR generic. No witness can be passed into a CLR body. A class closing is
  safe there, since a CLR body that hands back a zeroed reference hands back
  null. A struct closing is not: `ArrayPool<Cell<7>>.Shared.Rent(2)[0].Size()`
  prints 0 once `Cell` is value-routed, and any CLR generic may hand back a
  zeroed value.

`new T[k]` in a generic body also zeroes an ordinary struct whose declared
default is not zero: `Blank<Pt>()` with `X = 5` prints 0 at ad262e7e, against
definite-assignment Requirement 4.1b. The stamp a witness carries would fix
it, and it changes the C# of existing programs, so it is a delivery of its
own.

The delivery hands in the list `SlotSensitivity` already implies, each entry
marked as above: every reason a parameter is marked, and for each the way its
witness is built, stored, forwarded and asked. `SlotSensitivity.cs` is past
700 lines and is split when it is next changed.

### Where the C# backend falls short

These were this design's first answer, written before the ruling above. Until
the delivery that replaces it lands, each is refused by the backend with its
own diagnostic and never miscompiled:

- Static storage in a value-routed type. Requirement 10 needs a table keyed by
  slot value. Nothing in the samples, Standard or the book snippets declares
  static storage in a type with a dependent parameter. Retired by item 5 of
  *Identity by value*.
- A type value of a value-routed closing, `var t = Ring<n>;`. A CLR `Type`
  cannot hold the value.
- A type that implements a value-routed abstract at two values of a slot. The
  two implementations collide once the slot leaves the C# type.
- A test against a closing nested in another type's argument,
  `List<Array<int, n>>`, as MIST0013 refuses for a sized element today.

Stage 4 delivered the first three as MIST0491 to MIST0493 and added these as
its reviews found them. Each is a place where a slot that has left the C# type
cannot be answered:

- A test against a runtime closing under `not` or `or`, MIST0494. The
  comparison has no binding to read through and no guard to carry it.
  Retired by item 1 of *Identity by value*.
- A constructor constraint closed by a value-routed type, MIST0495. Neither
  C#'s `new()` nor the synthesized factory supplies the hidden constructor
  argument.
- An array creation or a stack allocation whose element is a closing of a
  value-routed struct, MIST0496, or a struct that holds one by value,
  MIST0503. The elements start from the CLR zero, the hidden field with them.
  Both retired by item 4 of *Identity by value*.
- Overloads that differ only in a slot the backend does not carry in the type,
  MIST0497. `Name(int[3])` beside `Name(int[4])` is one of them.
- A method's slot inferred from a closing nested in another type's argument,
  MIST0498. No member reads the value back. Item 2 of *Identity by value*
  carries a capture in a local; the number stays for a slot nested under
  `unknown`, which has no value anywhere.
- A read of a value-routed parameter where no value reaches, MIST0499: an
  attribute argument, and a copy that union specialization makes with a
  closing of the parameter's owner in a type argument. A conversion into the
  type and a nested type were retired from it by item 7 of *Identity by
  value*.
- A value-routed parameter written as a type, MIST0500: `x is N`, `List<N>`.
- A union that holds two closings of one value-routed type, MIST0501.

### A closing as a type argument

A generic declaration's C# body is shared by every closing of its type
parameters. Once a slot leaves the C# type, `Tester<Ring<3>>`,
`Tester<Ring<5>>` and `Tester<Ring<n>>` are one C# type, and the body cannot
tell them apart. Stage 4 delivered with four such programs printing a wrong
answer. They were measured at 7926a184, with `Ring` and `Cell` value-routed by
a `new Ring<n>()` or `new Cell<n>()` elsewhere in the program:

| Program | Prints | Correct |
|---|---|---|
| `new Tester<Ring<5>>().Is(three)`, where `Is` is `return x is T;` and `three` is a `Ring<3>` | `True` | `False` |
| `Counter<Ring<3>>.hits = 1; Counter<Ring<4>>.hits = 2;` and then both are read | `2 2` | `1 2` |
| `Blank<Cell<7>>()[0].Size()`, where `Blank` is `return new T[2];` and `Cell` is a struct | `0` | `7` |
| `Same<Ring<3>, Ring<5>>()`, where `Same` compares the type values of its two parameters | `True` | `False` |

All four close the type with literals. One runtime closing anywhere routes the
declaration, and every closing of it loses the slot.

Until item 8 of *Identity by value* lands, the backend refuses these where the
closing is written; the pass below stays, and then decides where a description
is passed. `SlotSensitivity`, a
whole-program pass beside `SlotRoutes`, marks a type parameter of a
declaration slot-sensitive, to a fixed point, when the declaration:

- tests a value against a type that mentions the parameter, in an `is`, a type
  pattern or an `as`, unless the value's own type already admits only that
  closing: the value is of the parameter's type, or of a union that holds the
  parameter as a constituent. `T | Void` and `T | string` are tested as today;
- creates storage of the parameter that starts from the CLR zero: `new T[k]`
  or a stack allocation;
- declares static storage, where the declaration is a type;
- reads the parameter as a type value;
- passes a type that mentions the parameter to a slot-sensitive parameter of
  another declaration, as a type argument, a base or an implemented abstract.

A type argument that mentions a closing of a value-routed type and fills a
slot-sensitive parameter is refused with one backend diagnostic, reported at
the closing whether it is stated or inferred, and naming the parameter, its
declaration and the reason. The storage reason is measured for a struct
closing. Whether a class closing can reach it is for the stage that builds
this to measure: the class `Ring` of these probes is refused by the frontend
as not `stampable` (MIST0178). A cast is not a reason: a cast does not narrow
(MIST0149).

Everything else a body does with its parameter is unaffected: holding it,
passing it, returning it, and calling members through it. `List<Ring<n>>` runs
today and still does.

Three things this leaves:

- A copy that union specialization makes is refused more widely than it needs
  to be (MIST0499 on a read the member's own body wrote, MIST-1366). With the
  first exemption above the copy's test of `T | string` needs no comparison,
  and the read can be told from one that came through the argument.
- A CLR generic's body is not visible. Whether a CLR method that tests its type
  parameter, as `OfType` does, can be reached with such a closing is measured
  by the stage that builds this, and reported, not assumed.
- Array, Span and ReadOnlySpan have been erased since before this spec, and
  `IsA<int[3]>(new int[5])` has answered `True` throughout. That is not
  changed here.

## Diagnostics

New, numbered from MIST0486 in the order they land:

- `ClosingNotProven`, Stage 5: "'{source}' is not proven to be '{target}':
  '{left}' and '{right}' are not proven identical{case}."
- `SlotValueOutsideBody`, Stage 4: "A type written here cannot name the value
  '{name}'. Declare a dependent parameter and pass '{name}' between angle
  brackets."
- `SlotReadThroughUnknown`, Stage 4: "'{parameter}' cannot be read through
  '{receiver}': an object may implement '{abstract}' at more than one value.
  Test it with an 'is' pattern instead." Retired in stage 8: under
  Requirement 11 the read always has one answer, and the refusal moves to the
  conversion.
- `SlotNotInferable`, Stage 4: "'{parameter}' cannot be inferred from an
  argument of type '{type}'. State it between angle brackets." From stage 8
  only for a slot nested under `unknown`, which has no value anywhere.
- `ReturnLengthNotReadonly`, Stage 4, at the name in the return type:
  "'{parameter}' is named in the return type, so it must be declared as a
  readonly value parameter."
- The backend refusals of *Where the C# backend falls short*, MIST0491 to
  MIST0501, and one more for *A closing as a type argument*.

Stage 8 adds, numbered from the next free one when each lands (MIST0505 is
main's):

- `ClosingNotProven` gains its refinement form: "'{source}' is not proven to be
  '{target}': '{left}' is not proven to lie in '{refinement}'{case}." The
  identity form stays for a declaration's dependent parameter and for
  Requirement 8.4.
- `ClosingHasNoValue`, Stage 4: "'{type}' names no single value of
  '{parameter}', so it cannot be created, defaulted or have its static field
  '{member}' read. Name a value instead." (Requirements 1, 10.4).
- `StaticThroughRefinement`, Stage 5: "'{member}' is reached through '{type}',
  which names no single value of '{parameter}', and it reads '{read}'."
- `AbstractClosingAmbiguous`, Stage 4: "'{source}' converts to '{target}'
  through more than one of its closings: {closings}." (Requirement 11.3).
- `AbstractClosingNeedsExactClass`, Stage 4: "'{source}' may be a subclass with
  closings of '{abstract}' its type does not show, so it converts only to a
  closing whose slots are literals." (Requirement 11.4).
- `AbstractValueWidens`, Stage 5: "A value held as '{source}' cannot widen to
  '{target}'. Write the method over the slot, as '{suggestion}'."
  (Requirement 11.5).

Changed: MIST0487 is not reported for a `readonly` value parameter named in an
array return type's own length slot, and its description says so. MIST0298 is
no longer reported for a value's name in a dependent slot. MIST0301 is no longer reported for a slot read through an instance.
MIST0193 is no longer reported across a slot that is not two literals.
MIST0407 states the domain rule of Requirement 6. A runtime argument that
breaks its slot's contract is reported by the MIST0271 family at the capture.
`tools/diagnostics/catalog.sh` regenerates the book page.

## Tests

Every test declares its tier with `[Trait("Tier", "N")]`.

Tier 1, the bulk: the classifier over two hand-built closings for each slot
pair (two literals, a capture and a literal, two captures, a parameter and a
literal, `unknown` on a class and on an abstract); `SlotValue` over a return
type's length slot naming a `readonly` value parameter, a parameter that is
not `readonly`, a `readonly ref` parameter, a nested slot and another type's
slot; `SlotIdentity.Judge` over a
hand-built state for each requirement example and its refusal twin with the
expected failing case; `SlotReads.Value` for each of its four cases;
`DependentDomains` over a readonly stampable struct, a mutable one, a class, a
float and an enum; `SlotRoutes` over a hand-built model for each of its five
rules; the inference decision for each argument slot.

Tier 2: `RefinementAnalyzer` over bodies built with `HandBuiltModel`, one
admit and one refuse for each of: reassignment of the named variable after a
capture; `if(n == m)`; `new T[e]` with and without `e >= 0`; a field typed by
the enclosing parameter indexed under a bound on that parameter; a test that
selects a branch; a return against a dependent return type; a return against
`int[n]` naming a `readonly` parameter, and its slot contract with and
without `n >= 0`; two results of one such call proven identical; a second argument
that binds the same parameter; a capture inside a loop. Emission over a
hand-built model for the value route's class, struct, method, test and
`unknown` spelling, beside the existing `ErasedShape` tests.

Tier 3, few: `ProgramModelBuilder.Build` over minimal source with the stub
provider, for the three things only the binder's chain shows: a name in a
dependent slot becomes a capture and the same name in a type slot stays a
type; `new int[n]` and `var` take the capture; `ring.Capacity` binds.

Stage 8, Tier 1: the fit judgement over a hand-built state for each target
kind (a literal, a capture's kept refinement, a refinement slot argument,
`unknown`, a declaration's dependent parameter) with an admit and a refusal
each; the refinement a capture keeps at `Declare`, with and without a path
condition and with a relational fact it must leave out; `SlotReads.Value` for a
capture-closed receiver after a write to it; the classifier over each
Requirement 11 conversion and its refusal; resolution of a name in a dependent
slot that is a value, a type, both, and a fully qualified type. Tier 2: a
reassignment Requirement 3.2 admits and the fact it ends; `Blit` over two
arrays of one refinement refused and over one creation's two arrays proven; a
test's facts on the passing branch for a literal, a capture and a refinement; a
member call through a refinement whose argument must equal the receiver's
slot; `StaticThroughRefinement` over a static method that reads `N` directly,
through a call, and not at all.

Tier 4: none. Tier 5: none. Tier 6: one new sample,
`samples/RuntimeDependentValues`, banked in `AllFeaturesV2` as
`samples/AGENTS.md` describes, holding the accepted programs of the
requirements for Array and for `Ring`, with printed results. Refusals are
proven at Tiers 1 and 2 only.

## Sequencing

Each stage leaves `AllFeaturesV2` passing. Stages 3 and 4 may run side by side.

1. The model and the binder's core: `SlotCapture`, `CapturedValue`, resolution
   by slot for a local declaration, `new` and a pattern's type, `new T[e]`, the
   classifier's third test, and Requirement 1.6. `SlotIdentity` lands in its
   strictest form, proving only two slots that hold one value, and the places
   no proof site covers refuse a pair outright, so nothing unproven is
   admitted before stage 3. The capture's contract is proven from
   this stage on, because the capture is a local and the existing site judges
   it: `new T[n]` owes `n >= 0`, the eight creations in the samples that size by
   a plain `int` change here, and Mist programs inside tests that do the same
   are counted and changed with them. `var a = new int[n]` takes the length
   from this stage on. The backend refuses a capture outside Array and the
   spans.
2. The rest of the binder: the instance read and its one refusal, stated and
   inferred method arguments, and the refusal of a value's name outside a body
   in a signature.
3. Stage 5: `SlotIdentity` over the chain, `SlotReads`, the facts a test adds,
   a contract that names a sibling slot, and the places stage 1 refuses
   outright moved onto the chain where the flow has a step for them. With
   `SlotReads` in place, a slot read is typed by its domain, which closes
   MIST-1357.
4. The backend: `SlotRoutes`, the value route, `ArrayLowering`, the four
   refusals. The first three "wrong afterwards" rows of the proposal are
   closed here and each gets its test. `SlotSensitivity` and its refusal
   follow as their own delivery before stage 5, because stage 4 landed with
   the four wrong answers of *A closing as a type argument*. The deliveries of
   *Identity by value* follow in its order. Stage 5 routes every slot of the
   fourth kind by value and would meet each stand-in, so it follows items 1 to
   7.
5. Requirement 6: the fourth kind of domain.
6. `samples/RuntimeDependentValues`, the book chapter, and the sentences the
   requirements revise in dependent-types, adhoc-refinement-constraints,
   stack-only-types, deep-ownership-types and
   flow-sensitive-refinement-inference.
7. Requirement 8 items 7 to 10, revised on 2026-11-19: a name in the
   grammar's sized rank, the return type's length slot naming a `readonly`
   parameter, its refusals, the slot-contract site and the call's capture.
   It needs stages 1 to 3 and nothing after them. Its accepted programs join
   stage 6's sample.
8. The refinement meaning, revised requirements of 2026-11-21 and 2026-11-22.
   Each step shows the owner its before-and-after programs before it is
   dispatched, and steps a and e first show the reading they build (*Readings
   to confirm*).
   a. Stage 5's fit judgement: the refinement a capture keeps at `Declare`,
      `SlotIdentity` asking membership, `SlotReads` through the receiver's
      holder, a creation's slot fact, `Tested` adding membership, and an
      invocation's capture judged by equality. The binder's inference reads the
      argument's slot, and a lambda's result keeps the refinement
      (Requirement 1.6). Every later step needs it.
   b. The refinement slot argument: resolution with the value winning and a
      fully qualified type, `ClosingHasNoValue`, and the Stage 5 rule
      `StaticThroughRefinement`.
   c. A member reached through a value reads that value's slot
      (Requirement 7.6).
   d. Abstracts (Requirement 11): the classifier's three conversions and their
      diagnostics, a test that passes on exactly one closing, inference from
      an abstract, and in the backend the closing a value holds found again at
      each use. It retires `SlotReadThroughUnknown`.
   e. The backend's test emits the refinement's predicate, with Stage 5 handing
      each test's kept refinement to the backend.
   f. The backend's anchor: a struct closing's C# type per refinement, its
      slot stored relative to `TN.Anchor`, the closing's default with its own
      value, and `T | void` for a class closing coming back from CLR code. It
      needs step b.
   g. Stage 6's sample and the book chapter take the programs the revised
      requirements accept.

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

Two of the five defects found while probing. Stage 4 closes two more, the
`unknown` slot inferred into a method's dependent parameter and the unsized
array creation, and stage 8 the fifth: `Both.N`, read through a type that
implements two closings, answered the first, and Requirement 11 now decides
it. Not closed here: a type test against a type parameter closed with a sized
array ignores the length, and switch arms of two sized arrays merge to
`IEnumerable<int>`.
