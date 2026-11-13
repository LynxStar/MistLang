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

Assignability judges a conversion in more places than `Proving.Judge` visits: a
switch arm, the right of `??`, an array literal element, an expression-bodied
lambda's result, a `foreach` element, a deconstruction target, a `with` value,
a field or property initializer, and a getter's expression body. The
classifier's third test would admit a pair there that nothing proves. At each
of those places the assignability rule refuses any slot pair that is not the
same capture, with `ClosingNotProven`. Stage 3 moves a place onto the chain
where the flow graph has a step for it, and a place with none keeps refusing.

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

The type of a slot read is the slot's domain, the type written after the
parameter's colon, as dependent-types Requirement 7.1 gives the property read
through the type. `a.Length` is a `ZeroOrPositiveInt` whatever the array is
closed with, and `ring.Capacity` over `Ring<Capacity:int>` is an `int`. A
`where` on the parameter is no part of that type; what it states is known as a
fact about the value. What the read holds is known through `SlotReads`,
from the receiver's type, never through the type of the read. Today the read
is typed by the slot's argument, a literal or `unknown`, and the index prover
and `SpanLengthDecisionTests` take the length from there. Typing it by a
capture as well put a value where a type belongs: `[a.Length]` became an array
of `n`, and `Id(a.Length)` bound `T` to a value (MIST-1357). Stage 3 moves the
prover onto `SlotReads` and types the read by its domain.

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

### Identity by value

The owner ruled on 2026-11-13 that the backend makes these programs work. A
closing is identified by its value, however it was written:

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
`Ring<N> : ISized<N>`, compares with its hidden field. Nothing but a test
reads through the object: a read through a named closing is the closing's
value, and a read or an inference through `unknown` is refused by
Requirement 11.

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
through a literal closing, a capture, a dependent parameter and `unknown`.

### Where the C# backend falls short

These were this design's first answer, written before the ruling above. Until
the delivery that replaces it lands, each is refused by the backend with its
own diagnostic and never miscompiled:

- Static storage in a value-routed type. Requirement 10 needs a table keyed by
  slot value. Nothing in the samples, Standard or the book snippets declares
  static storage in a type with a dependent parameter.
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
- A constructor constraint closed by a value-routed type, MIST0495. Neither
  C#'s `new()` nor the synthesized factory supplies the hidden constructor
  argument.
- An array creation or a stack allocation whose element is a closing of a
  value-routed struct, MIST0496. The elements start from the CLR zero, the
  hidden field with them.
- Overloads that differ only in a slot the backend does not carry in the type,
  MIST0497. `Name(int[3])` beside `Name(int[4])` is one of them.
- A method's slot inferred from a closing nested in another type's argument,
  MIST0498. No member reads the value back.
- A read of a value-routed parameter where no value reaches, MIST0499: a
  conversion into the type, a nested type, an attribute argument, and a copy
  that union specialization makes with a closing of the parameter's owner in a
  type argument.
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
  Test it with an 'is' pattern instead."
- `SlotNotInferable`, Stage 4: "'{parameter}' cannot be inferred from an
  argument of type '{type}'. State it between angle brackets."
- The backend refusals of *Where the C# backend falls short*, MIST0491 to
  MIST0501, and one more for *A closing as a type argument*.

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

Three of the five defects found while probing. Stage 4 closes the other two,
the `unknown` slot inferred into a method's dependent parameter and the unsized
array creation. Not closed here: a type test against a type parameter closed
with a sized array ignores the length; switch arms of two sized arrays merge to
`IEnumerable<int>`; and `Both.N`, read through a type that implements two
closings, answers the first.
