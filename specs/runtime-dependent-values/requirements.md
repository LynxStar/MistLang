# Runtime dependent values

## Status and scope

Requirements are settled with the owner one decision at a time, developed from
[proposal.md](proposal.md). Settled so far: Requirements 1 to 11, on 2026-11-11.
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

## Requirement 1 A slot takes a value

A dependent slot is one more generic argument. Its value may not be known until
the program runs, and the compiler reasons about it all the same.

1. A dependent type argument SHALL be a value. A literal is a value known when
   the program is compiled. Any other argument is a value known when the
   program runs. Both close the type: `new int[n]` is an `Array<int, n>` as
   `new int[3]` is an `Array<int, 3>`.
2. The argument SHALL be evaluated once, where the type is written, and the
   type SHALL be closed with the value it produced. A later write to a variable
   the argument read SHALL NOT change the type. The type holds the value, as
   flow-sensitive-refinement-inference Requirement 3 states of every fact.
3. An object's type SHALL be fixed when the object is created. Nothing changes
   the value a slot of its type was closed with.
4. Where the facts in hand know the value as a literal, the closing SHALL be the
   closing with that literal. After `var n = 7;`, `new int[n]` is an
   `Array<int, 7>`.
5. This revises dependent-types Requirements 2, 5, 11.4 and 15.1, which admit
   only a compile-time constant.
6. The body of a lambda or a local function runs many times, and a value
   captured inside it is a new value each time. A type closed with such a value
   SHALL NOT appear in the result type the compiler infers for that body. The
   slot is `unknown` there. This follows from item 2 and was not decided
   separately. It refuses nothing that compiles today: `k => new int[n]`
   returns an `int[]` today and still does.

Example.

```mist
var n = 7;                  // int, known to be 7
var a = new int[n];         // Array<int, 7>

var p = GetPositiveInt();   // int > 0
var b = new int[p];         // Array<int, p>, and p > 0 is known
```

## Requirement 2 One type when the values are proven identical

There is no weak typing. A value is of one closing, and it is used as another
only when the two are proven to be the same.

1. Two closings of one declaration SHALL be the same type exactly when, for
   every slot, the facts in hand prove the two values identical. Two literals
   are identical when they are the same value, as dependent-types Requirement
   8.1 states. That is the case that needs no facts.
2. Where a value meets a type that differs from its own only in the values of
   slots, at an initializer, an assignment, an argument or a return, the
   compiler SHALL prove the values identical under
   flow-sensitive-refinement-inference Requirement 1. It SHALL refuse the use
   when the proof is not reached, and the diagnostic SHALL state the failing
   case in the program's own names.
3. No check SHALL be made when the program runs.
4. What `unknown` in a slot means is not changed by this requirement.

Example.

```mist
var n = GetPositiveInt();
var m = GetPositiveInt();
var a = new int[n];         // Array<int, n>
a = new int[m];             // refused: fails when m <= n - 1 or m >= n + 1

if(n == m)
{
    a = new int[m];         // proven by the condition
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
   the slot it fills. A dependent slot reads a value. Any other slot reads a
   type.
4. `new T[e]` and `stackalloc T[e]` SHALL keep taking any expression for `e`,
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

1. Every type SHALL answer its dependent parameters through an instance.
   `ring.Capacity` is the value `ring`'s type was closed with, as `a.Length`
   is for an array. Requirement 11 states the one read that has no answer.
2. The read SHALL yield the same value for the life of the object and SHALL
   NOT be assignable.
3. The compiler SHALL know the read to be identical to the value the type was
   closed with, whether that is a literal, a runtime value or a dependent
   parameter of the enclosing declaration. `new int[n]` has `Length == n`, and
   a field `Array<int, Capacity> slots` has `slots.Length == Capacity`.
4. A value whose type is a class or a struct closed with `unknown` carries a
   value all the same, because an object is exactly one closing of its own
   class or struct. It SHALL be usable as a named closing where the facts
   prove its read identical to that closing's value, under Requirement 2.
5. The read through the type, `Ring<4>.Capacity`, stays. This revises
   dependent-types Requirement 7.3, which refuses the read through an
   instance.

Example.

```mist
void Show(Ring<unknown> ring, PositiveInt n)
{
    Ring<n> first = ring;       // refused: nothing says ring.Capacity == n
    if(ring.Capacity == n)
    {
        Ring<n> same = ring;    // proven by the condition
    }
}
```

## Requirement 8 A relation through a signature

1. A relation between a value and the types of a signature SHALL be stated
   through a dependent parameter, of the method or of the enclosing type. A
   type in a signature SHALL NOT name one of the method's regular parameters.
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

## Requirement 9 Testing a value against a closing

1. A type test, in an `is`, a type pattern or an `as`, SHALL take a closing
   whose slot is a literal or a name, under Requirement 5. A name is read
   once, where the test is written.
2. The test SHALL pass when the value is of the declaration and the value it
   carries in each slot is identical to the slot's value. That comparison is
   made when the program runs, and it is the only check this spec adds to a
   running program. The programmer wrote it.
3. Where the test passes, the binding it introduces SHALL have the tested
   type, and the facts SHALL gain that its read is identical to the slot's
   value: after `ring is Ring<n> same`, `same.Capacity == n`.
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
void Show(unknown thing, Ring<unknown> ring, PositiveInt n)
{
    if(ring is Ring<n> same)
    {
        // same is a Ring<n>
    }

    if(thing is Ring<n> found)
    {
        // thing is a Ring, and its capacity is n
    }
}
```

## Requirement 10 Static storage

Static means of the type. A closing is a type, so each closing has its own
static storage, or the word static means something else.
[generics-type-arguments](../generics-type-arguments/requirements.md)
Requirement 10 states this for type arguments and it stands.

1. Each closing SHALL have its own static storage, whether its slots hold
   literals or runtime values. A closing is its declaration, its type
   arguments and the values of its slots.
2. Two closings that are one type under Requirement 2 SHALL reach the same
   storage. `Counter<n>.hits` when `n` is 1 is `Counter<1>.hits`.
3. No dependent parameter is confined to literals on account of static
   storage.

A program that closes a type with many distinct values keeps static storage
for each of them.

Example.

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
