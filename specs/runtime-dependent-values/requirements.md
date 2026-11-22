# Runtime dependent values

## Status and scope

Requirements are settled with the owner one decision at a time, developed from
[proposal.md](proposal.md). Settled so far: Requirements 1 to 11, on 2026-11-11.
Requirement 8 was revised on 2026-11-19, when
[runtime-length-facts](../runtime-length-facts/requirements.md) Requirement 3
moved here. On 2026-11-21 the owner set a new direction: a run-time closing is
typed by what is provable about its value, not by the value
([design.md](design.md), *Direction of 2026-11-21*). Requirements written for
the value meaning are revised one at a time; until then that section governs
where they differ.
The proposal lists the decisions still open, and a requirement is added here
when its decision is made. This is a language contract, not a claim that the
current compiler implements it.

In scope: what a dependent slot means when its argument is a value known only
when the program runs, how two such types are judged to be the same type, how
the relation survives a signature, and what a value carries when the program
runs.

This spec revises [dependent-types](../dependent-types/requirements.md) where
a requirement below says so.

Out of scope: the proof engine, which is
[flow-sensitive-refinement-inference](../flow-sensitive-refinement-inference/requirements.md)
Requirements 1 to 8 as delivered; Array's operations and allocators; and a
general `const` declaration.

A slot is the position of a dependent type parameter in an argument list. A
closing is a type with its slots filled.

## Requirement 1 A slot takes what is provable about a value

A dependent slot is one more generic argument. Its value may not be known until
the program runs. The compiler types it by what it can prove about that value,
and the object carries the value itself. Revised on 2026-11-21 under the
owner's direction ([design.md](design.md), *Direction of 2026-11-21*); item 4
was ruled "The second one, whatever is provable."

1. A dependent type argument SHALL be a value. Where the type is written, the
   type SHALL be closed with the refinement the facts in hand prove for that
   value, and an object created there SHALL be closed with the exact value. A
   literal is the refinement with one member, so `new int[3]` is an
   `Array<int, 3>` both ways.
2. The argument SHALL be evaluated once, where the type is written. The object
   holds the value it produced. A later write to a variable the argument read
   SHALL change neither the object nor the type already written, as
   flow-sensitive-refinement-inference Requirement 3 states of every fact.
3. An object's type SHALL be fixed when the object is created. Nothing changes
   the value a slot of its type was closed with.
4. The refinement SHALL be everything the facts in hand prove about that one
   value at that point: its bounds, a constant, and the values it is known not
   to be, including what a condition on the current path adds. After
   `var n = 7;`, `new int[n]` is an `Array<int, 7>`. A relation to another name,
   such as `n < m` or `a.Length == n`, SHALL stay a fact and SHALL NOT enter the
   type.
5. This revises dependent-types Requirements 2, 5, 11.4 and 15.1, which admit
   only a compile-time constant.
6. The body of a lambda or a local function runs many times, and a value
   captured inside it is a new value each time, but what is provable about it
   is the same each time. The result type the compiler infers for that body
   SHALL keep the refinement: with `n : Range`, `k => new int[n]` returns an
   `int[Range]`. This replaces the earlier rule that the slot is `unknown`
   there.

Example.

```mist
type Range = int > 10 and < 30;

void Foo(Range n)
{
    var a = new int[n];         // Array<int, Range>; the object is exactly Array<int, 15> when n is 15
    if(n > 20)
    {
        var b = new int[n];     // Array<int, int > 20 and < 30>
    }

    var seven = 7;
    var c = new int[seven];     // Array<int, 7>
}
```

## Requirement 2 A closing fits where what is provable about it fits

There is no weak typing. A value of one closing is used as another only where
what is provable about its slots lies inside the other's. Revised on 2026-11-21
under the owner's direction; item 2 was ruled "Yes, facts narrow".

1. A value of a closing SHALL be usable where another closing of the same
   declaration is expected when, for every slot, what is provable about its
   value lies inside the expected refinement. `Cell<15>` fits where `Cell<Range>`
   is expected, and two `Cell<Range>` values fit each other whatever their
   values. Two literals fit exactly when they are the same value, as
   dependent-types Requirement 8.1 states.
2. What is provable about a slot SHALL include the facts in hand at the use,
   not only the type written. An object created as `new Cell<n>()` records that
   its slot holds the value `n` had there, as `new int[n]` records
   `Length == n`, so where the facts prove `n == 15` that object is a
   `Cell<15>`.
3. Where the proof is not reached, at an initializer, an assignment, an
   argument or a return, the compiler SHALL refuse the use, and the diagnostic
   SHALL state the failing case in the program's own names. A program that
   needs the narrower closing tests for it with `is` (Requirement 9).
4. No check SHALL be made when the program runs other than a test the program
   writes.

Example.

```mist
type Range = int > 10 and < 30;

void Foo(Range n, Range m)
{
    var a = new int[n];         // Array<int, Range>
    a = new int[m];             // accepted: both are Array<int, Range>

    var cell = new Cell<n>();   // Cell<Range>
    Cell<15> x = cell;          // refused: fails when n <= 14 or n >= 16
    if(n == 15)
    {
        Cell<15> y = cell;      // proven: cell's slot holds n, and n == 15
    }
}
```

## Requirement 3 What a declaration takes

1. A `var` SHALL take the type of its initializer with the values of its slots,
   whether a value is a literal or not. `var a = new int[n];` declares an
   `Array<int, n>`, as `var a = new int[3];` declares an `Array<int, 3>`.
2. Every later write to the variable SHALL be held to that type under
   Requirement 2. The `n` it is held to is the value `n` had at the
   declaration.
3. A variable meant to hold values of different closings is declared with the
   type that says so, `int[] a = new int[n];`, as today.
4. This revises [stack-only-types](../stack-only-types/requirements.md) where it
   types a runtime-sized `stackalloc int[n]` as `Span<T, unknown>`. It is a
   `Span<int, n>`.

Programs that compile today change under this requirement:
`var a = new int[n]; a = new int[m];` is accepted today, because that `a` is an
`int[]`.

## Requirement 4 The slot's own contract

A slot states what it admits through its domain and its `where` clause:
`Array<TValue, Length:ZeroOrPositiveInt>`, `Ring<Capacity:PositiveInt>`,
`where N >= 1`. This requirement follows from Requirement 1 and from
[adhoc-refinement-constraints](../adhoc-refinement-constraints/requirements.md)
Requirement 5, which already refuses a forwarded parameter whose own constraint
does not prove the slot's. The owner confirmed it on 2026-11-12.

1. A runtime argument SHALL be proven against the slot's contract where the
   type is written, under flow-sensitive-refinement-inference Requirement 1, as
   an argument is proven against a parameter predicate.
2. `new T[n]` and `stackalloc T[n]` close `Length` with `n`, so `n` owes
   `Length`'s contract: `n >= 0`.
3. No guard SHALL be emitted. This revises adhoc-refinement-constraints
   Requirements 5.7 and 12.2, which rest on every dependent value being known
   when the program is compiled.

Example.

```mist
void Build(int n)
{
    var a = new int[n];         // refused: fails when n <= -1
    if(n >= 0)
    {
        var b = new int[n];     // proven
    }
}
```

Programs that compile today change under this requirement: a creation sized by
a plain `int` is accepted today, and a negative size fails when the program
runs.

Four samples held such creations. They are migrated with the owner's approval,
the one exception this spec takes to `samples/AGENTS.md` Rule 1, and their
output is the same.

- `samples/Attributes`: `LookupTable`'s constructor takes a
  `ZeroOrPositiveInt`. The code blocks that mirror it in
  [attribute-projection](../attribute-projection/requirements.md) and
  [attributes-and-test-command](../attributes-and-test-command/requirements.md)
  read the same.
- `samples/SizedSequencePatterns`: `Produce`, `DescribeSpan` and
  `DescribeReadOnly` take a `ZeroOrPositiveInt`.
- `samples/NamedAllocationElements` and `samples/StampableTypes`:
  `NextAllocationSize` and `NextSize` return a `ZeroOrPositiveInt` from behind
  `if(size >= 0)`.

## Requirement 5 What is written in a slot

1. A dependent type argument SHALL be written as a literal, as today, or as a
   name: a local, a parameter, a field, a dependent parameter in scope, or a
   member reached by a dotted path of names, such as `source.Length`.
2. No other expression SHALL be written in a slot. A program that needs one
   gives it to a variable and names the variable. The diagnostic of
   dependent-types Requirement 15.6 stays and says so.
3. Whether a name in an argument list is a type or a value SHALL be decided by
   the slot it fills. Any slot that is not dependent reads a type. A dependent
   slot reads a value, or a refinement of the slot's domain written as a type
   name, `Cell<Range>`, which closes the type with that refinement under
   Requirement 1.
4. Where a name in a dependent slot is both a value in scope and a type, the
   value SHALL win, as it does in an expression, where `var x = Size;` reads a
   variable `Size` before the type literal `Size`. The type is then written by
   its fully qualified name, `Cell<Probe.Size>`. Ruled on 2026-11-21 ("I'm
   pretty sure variable wins"; "you need the FQN to disambiguate for the
   type"), matching the compiler's existing expression rule.
5. `new T[e]` and `stackalloc T[e]` SHALL keep taking any expression for `e`,
   as today. The value `e` produced closes `Length` under Requirement 1 and
   owes its contract under Requirement 4.

Example.

```mist
var size = n + 1;
var a = new Array<string, size>(k => "");

var b = new Array<string, source.Length>(k => "");

var c = new Array<string, n + 1>(k => "");      // refused: name a variable
var d = new int[n + 1];                         // as today
```

## Requirement 6 What may close a type

An object's type can never mutate, so a type is closed only with something
immutable.

1. The domain of a dependent parameter SHALL be a type that is readonly and
   stampable. Its value and its identity are one and the same, and it is
   immutable. Readonly is the property
   [readonly-contexts](../readonly-contexts/requirements.md) gives a type, and
   stampable is the property [stampability](../stampability/requirements.md)
   proves.
2. This revises dependent-types Requirement 4, which admits the integral types,
   `bool` and `char` and nothing else. Those remain admitted.
3. A refinement or an alias of an admitted type SHALL remain admitted through
   the type it refines, as
   [dependent-type-refinement-constraints](../dependent-type-refinement-constraints/requirements.md)
   Requirement 2 states. `Length:ZeroOrPositiveInt` is unchanged.
4. A type whose identity is not its value SHALL NOT be a domain. A reference
   type is the plain case.

Kept as they are until ruled otherwise. `float`, `double` and `decimal` stay
out under dependent-types Requirement 4.3: `NaN` is not equal to itself, `0.0`
equals `-0.0`, and `1.0m` equals `1.00m`, so value and identity are not the
same. Enums and newtypes stay out because stampability Requirement 2.6
excludes them.

Requirement 2 proves two values identical with the facts the flow spec records
for their type. This spec adds none. Where the flow spec records nothing for a
type, two slots are identical only when they hold the same value.

## Requirement 7 Reading the slot through a value

Revised on 2026-11-21 to follow Requirements 1 and 2 under the owner's
direction.

1. Every type SHALL answer its dependent parameters through an instance.
   `ring.Capacity` is the exact value the object was created with, as
   `a.Length` is for an array. Requirement 11 states the one read that has no
   answer.
2. The read SHALL yield the same value for the life of the object and SHALL
   NOT be assignable.
3. The read's type SHALL be what the value's type proves about the slot. Where
   the compiler sees the object created, it SHALL also record the read as equal
   to the value the object was created with: `new int[n]` has `Length == n`,
   `new Ring<n>()` has `Capacity == n`, and a field `Array<int, Capacity> slots`
   has `slots.Length == Capacity`.
4. A slot written `unknown` SHALL mean the slot's whole domain. A value of such
   a closing narrows like any other under Requirement 2: after
   `if(ring.Capacity == n)`, the facts prove the slot holds `n`'s value.
5. The read through the type, `Ring<4>.Capacity`, stays. This revises
   dependent-types Requirement 7.3, which refuses the read through an
   instance.

Example.

```mist
void Show(Ring<unknown> ring, PositiveInt n)
{
    Ring<4> first = ring;       // refused: nothing proves ring.Capacity == 4
    if(ring.Capacity == 4)
    {
        Ring<4> same = ring;    // proven by the condition
    }
}
```

## Requirement 8 A relation through a signature

1. A relation between a value and the types of a signature SHALL be stated
   through a dependent parameter, of the method or of the enclosing type, or,
   for the length of an array the method returns, through a regular parameter
   under items 7 to 10. Nowhere else SHALL a type in a signature name one of
   the method's regular parameters.
2. A method's dependent parameter SHALL take a runtime value under
   Requirements 1 and 5, as `Zeroes<count>()`. It is evaluated once, before
   the regular arguments, and inside the method it is a value fixed for the
   call.
3. Where the argument list is omitted, the parameter SHALL be inferred from an
   argument as today, and the inferred value is the value that argument
   carries. For an `Array<int, n>` it is `n`. For an `int[]` it is the array's
   `Length`, under Requirement 7.
4. Where more than one argument binds one dependent parameter, the first
   SHALL bind it and each other SHALL be proven identical under Requirement 2.
5. The parameter's contract SHALL be proven at the call under Requirement 4.
6. At a call, the result has the return type with the parameter bound to the
   value passed. Inside the method the types of its parameters are facts at
   the first line, and each `return` SHALL be proven against the return type.
   No body is read at a call, as flow-sensitive-refinement-inference
   Requirement 6 states.
7. Where the return type is an array, its length slot MAY name one of the
   method's regular parameters: `int[n] Zeroes(readonly ZeroOrPositiveInt n)`,
   called as `Zeroes(count)`. A slot nested inside the return type, as in
   `List<int[n]>`, and the slot of any other type, as in `Ring<n>`, SHALL NOT.
   The bracket form of item 2 stays valid for a dependent parameter.
8. A parameter named under item 7 SHALL be a value parameter declared
   `readonly`. Naming one that is not is a compile-time error, not an implicit
   `readonly`. A `readonly` value parameter refuses every write
   (readonly-contexts Requirement 14.8), so in the body it is the value the
   caller passed, and each `return` is proven against the return type under
   item 6. A `readonly ref` parameter does not qualify: its value can change
   through its owner while the method runs.
9. The parameter's type and the method's `where` clause SHALL prove the
   slot's contract under Requirement 4. `int[n] Zeroes(readonly int n)` is
   refused: it fails when `n <= -1`.
10. At a call, the result SHALL be closed with the value passed for that
    parameter, evaluated once, as the argument, under Requirement 1. The
    argument is an argument and not a slot, so Requirement 5 does not limit
    it: `Zeroes(count)` is an `Array<int, count>`, and `Zeroes(count + 1)` is
    closed with the value `count + 1` produced, which is identical to another
    closing's value only as far as the facts recorded for it prove.

Example.

```mist
Array<int, N> Zeroes<N:ZeroOrPositiveInt>()
{
    return new Array<int, N>(k => 0);
}

void Blit<N:ZeroOrPositiveInt>(Array<int, N> source, Array<int, N> target)
{
    for(var i = 0; i < N; i++)
    {
        target[i] = source[i];
    }
}

void Caller(ZeroOrPositiveInt count)
{
    var a = Zeroes<count>();    // Array<int, count>
    var b = Zeroes<count>();
    Blit(a, b);                 // N is inferred from a, and b is proven to match
    Blit(a, new int[3]);        // refused: fails when count <= 2 or count >= 4
}
```

The same relation through a regular parameter, under items 7 to 10:

```mist
int[n] Sized(readonly ZeroOrPositiveInt n)
{
    return new int[n];
}

int[n] Wrong(readonly ZeroOrPositiveInt n)
{
    return new int[3];          // refused: fails when n <= 2 or n >= 4
}

int[n] Loose(ZeroOrPositiveInt n)       // refused: n is named in the return type and is not readonly
int[n] Signed(readonly int n)           // refused: fails when n <= -1
Ring<n> Make(readonly ZeroOrPositiveInt n)   // refused: only an array's length slot may name a parameter

void Caller(ZeroOrPositiveInt count)
{
    var a = Sized(count);       // Array<int, count>
    var b = Sized(count);
    Blit(a, b);                 // proven: both are closed with count's value
}
```

## Requirement 9 Testing a value against a closing

Revised on 2026-11-21 to follow Requirement 1 under the owner's direction; ruled
"`this is Ring<n>` should testing for the refinement of n".

1. A type test, in an `is`, a type pattern or an `as`, SHALL take a closing
   whose slots are written under Requirement 5: a literal, a name, or a
   refinement type name. A name in a test's slot stands for the refinement the
   facts prove for its value where the test is written, under Requirement 1.
2. The test SHALL pass when the value is of the declaration and the value it
   carries in each slot lies inside that slot's refinement. A literal is the
   refinement with one member, so `is Ring<5>` compares with 5. The check is
   made when the program runs, and it is the only check this spec adds to a
   running program. The programmer wrote it.
3. Where the test passes, the binding it introduces SHALL have the tested
   type, and its read SHALL be proven inside the slot's refinement. The test
   does not prove the read equal to the name's value: after
   `ring is Ring<n> same`, `same.Capacity` lies inside `n`'s refinement. A
   program that needs the read equal to the value compares them,
   `ring.Capacity == n`, under Requirement 7.
4. A slot in a test MAY be a dependent parameter in scope, and the value
   tested MAY be of a type closed with one. Inside
   `void Check<N:int>(Buffer<N> b)`, `b is Buffer<5>` compares `N` with 5, and
   the branch it selects gains `N == 5`. This revises dependent-types
   Requirements 9.3, 9.4 and 15.7, which refuse the test.
5. `as` SHALL answer `void` where the test would not pass, as it does for a
   literal today.
6. A cast is not a test. `(Ring<n>)value` reaches only a type the value is
   proven to be under Requirement 2, as a cast does today, and nothing is
   checked when the program runs.

Example.

```mist
type Range = int > 10 and < 30;

void Show(unknown thing, Ring<unknown> ring, Range n)
{
    if(ring is Ring<n> same)
    {
        // same is a Ring<Range>: its capacity is above 10 and below 30
    }

    if(ring.Capacity == n)
    {
        // ring's capacity is n's value
    }

    if(n == 15 && thing is Ring<n> found)
    {
        // thing is a Ring, and its capacity is 15
    }
}
```

## Requirement 10 Static storage

Static means of the type. An object's type is a closing with an exact value in
each slot, so each value has its own static storage, or the word static means
something else.
[generics-type-arguments](../generics-type-arguments/requirements.md)
Requirement 10 states this for type arguments and it stands. Revised on
2026-11-21 under the owner's direction; ruled "Ok, so by value", and for a
static reached through a refinement, "Ok sold".

1. Each closing with an exact value in every slot SHALL have its own static
   storage, whether a value is a literal or known only when the program runs.
   A closing is its declaration, its type arguments and the values of its
   slots. `Counter<1>.hits` and `Counter<2>.hits` are two storages, whether or
   not the static mentions a dependent parameter.
2. A static reached through a closing whose slot is a name SHALL reach the
   storage of the value the name holds where it is written, whatever the facts
   prove about it: `Counter<n>.hits` when `n` is 1 is `Counter<1>.hits`. Its
   type is what is provable under Requirement 1.
3. An instance member SHALL reach the static storage of the value its object
   carries, whatever type the object is held as. Through `y` held as a
   `Counter<ZeroOrPositiveInt>`, a method that reads a static field reads the
   storage of `y`'s own value, as the object always has one.
4. A closing written with a refinement type name, `Counter<ZeroOrPositiveInt>`,
   names no single value. A static field reached through it SHALL be refused,
   and the diagnostic SHALL say to name a value. A static method or computed
   property reached through it SHALL be accepted only where it reads neither a
   dependent parameter of the declaration nor one of its static fields,
   directly or through a member it calls.
5. State shared by every value of a slot belongs on a type that is not closed
   with that slot.
6. No dependent parameter is confined to literals on account of static
   storage.

A program that closes a type with many distinct values keeps static storage
for each of them.

Example.

```mist
public class Counter<N:int>
{
    public static int hits;
    public static int[N] slots = new int[N];
    public static int Twice(int x) { return x * 2; }
}

void Show(ZeroOrPositiveInt n)                              // called with 1
{
    Counter<1>.hits = 5;
    Console.WriteLine(Counter<2>.hits);                     // 0
    Console.WriteLine(Counter<n>.hits);                     // 5
    var a = Counter<n>.slots;                               // Array<int, ZeroOrPositiveInt>, and a.Length == n
    Console.WriteLine(Counter<ZeroOrPositiveInt>.Twice(3)); // 6
    var b = Counter<ZeroOrPositiveInt>.hits;                // refused: name a value
}
```

## Requirement 11 The read with no single answer

An object is exactly one closing of its own class or struct. It may implement
several closings of one abstract, as a class may implement `IConvert<int>` and
`IConvert<string>`.

1. A type MAY implement one abstract at more than one value of a slot, as
   today. `class Both : ISized<1>, ISized<2>` is valid. Through each named
   closing the slot has one value: 1 through `ISized<1>`, 2 through
   `ISized<2>`.
2. The slot of an abstract SHALL NOT be read through a value whose type closes
   that slot with `unknown`. `some.N` over an `ISized<unknown>` is refused,
   because the object may implement more than one closing and the read has no
   single answer.
3. The value SHALL be reached by a type test under Requirement 9, which an
   object passes for every closing it implements.
4. What is known of an abstract's slot through a named closing comes from that
   closing. It SHALL NOT be kept as a fact about the object under another
   closing.
5. A dependent parameter SHALL NOT be inferred under Requirement 8 from an
   argument whose type closes an abstract's slot with `unknown`. The call
   states it.
6. Every other read is Requirement 7 unchanged: through a class or a struct,
   whatever its closing, and through any named closing of an abstract.

Example.

```mist
public class Both : ISized<1>, ISized<2>
{
    public string Describe()
    {
        return "both";
    }
}

ISized<1> one = new Both();
one.N                           // 1

ISized<unknown> some = new Both();
some.N                          // refused: Both is an ISized<1> and an ISized<2>

if(some is ISized<n> sized)
{
    sized.N                     // n
}
```
