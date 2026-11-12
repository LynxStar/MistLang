# Runtime dependent values

## Status and scope

Draft proposal, 2026-11-10, measured against main at `7047fd4ed`. The decisions
that are the owner's are listed at the end, in the order they block each other.
Each one that is made becomes a requirement in [requirements.md](requirements.md),
which is the contract; what this proposal says beyond it is not settled.
`design.md` follows the requirements.

[dependent-types](../dependent-types/requirements.md) Requirement 5 makes every
dependent argument a compile-time constant.
[flow-sensitive-refinement-inference](../flow-sensitive-refinement-inference/requirements.md)
kept that rule and named the runtime slot a stretch goal, to be revisited once
the rest of that spec was delivered. It landed on main at `7047fd4ed`. This
proposal is the revisit.

In scope: what a dependent slot means when its argument is a value known only at
run time, how two such types are judged to be the same type, how the relation
survives a signature, and what a value carries at run time.

Out of scope:

- The domains were out of scope when this was drafted. The owner ruled on them
  on 2026-11-11: Requirement 6 admits any type that is readonly and stampable.
  Every such value copies on assignment, which is what makes capturing a value
  simple.
- The proof engine. Every proof below is flow-sensitive-refinement-inference
  Requirements 1 to 8 as delivered. This spec gives the engine new things to
  prove and no new way to prove them.
- Array's operations, its growth operation among them, and allocators.
- A general `const` declaration (MIST-145). A `static readonly` member named in
  a slot becomes an ordinary runtime value here, which narrows what that
  decision still has to cover.

## What the compiler does today

Every row was run with `mist run` built from `7047fd4ed`. The rows share:

```mist
public class Buffer<Size:int>
{
    public int Cap()
    {
        return Size;
    }
}
```

`n`, `m` and `i` are `ZeroOrPositiveInt` parameters unless a row says otherwise.

### A runtime value in a slot is read as a type name

| Program | Result |
|---|---|
| `var a = new Array<int, n>(k => k);` | MIST0298, the type 'n' could not be found |
| `Array<int, n> a = new int[n];` | MIST0298 |
| `var b = new Buffer<n>();` | MIST0298 |
| `Twice<n>()` over `int Twice<N:int>()` | MIST0298 |
| `Array<int, n> Make(ZeroOrPositiveInt n)` | MIST0298 |
| `new Array<int, source.Length>(k => k)` | MIST0298, the type 'source.Length' |
| `new Buffer<Shared>()` over `static readonly int Shared = 3;` | MIST0298 |
| `new Array<int, (n + 1)>(k => k)` | MIST0123, does not parse |

One consequence: an array of a reference type cannot be created with a runtime
size at all. `new string[n]` is MIST0225, which tells the programmer to write
`new Array<string, Size>(i => ...)`, and that form is the first row.

### A creation sized at run time knows nothing of its length

| Program | Result |
|---|---|
| `var a = new int[n]; if(i < n) { a[i] = 7; }` | MIST0275, fails when `a.Length <= 2147483646` |
| `Span<int> a = stackalloc int[n]; if(i < n) { a[i] = 7; }` | MIST0275 |
| `var a = new int[n]; var b = new int[n]; if(i < a.Length) { b[i] = 8; }` | MIST0275 on `b[i]` |
| `var copy = new int[source.Length]; if(i < source.Length) { copy[i] = source[i]; }` | MIST0275 on `copy[i]` |
| `var four = 4; var a = new int[four]; a[3] = 1;` | MIST0275 |
| `new int[n]` where `n` is a plain `int` | Accepted. A negative `n` fails when the program runs. |

The same gap exists with no runtime value at all. Inside a type, a field typed
by the type's own dependent parameter does not know its length is that parameter:

```mist
public class Ring<Capacity:PositiveInt>
{
    public type Slot = ZeroOrPositiveInt and < Capacity;

    private Array<int, Capacity> slots;

    public int Get(Slot at)
    {
        return slots[at];       // MIST0275, fails when slots.Length <= 2147483646
    }
}
```

### Accepted by the frontend and wrong afterwards

No Carbonek task matches any of these. The first three are answered by this
spec. The fourth stands on its own.

| Program | Result |
|---|---|
| `int Len<N:ZeroOrPositiveInt>(Array<int, N> a)` called as `Len(new int[n])` | Passes analysis. The C# compile fails: MIST0003, 'int' cannot be used as type parameter 'TN', no conversion to 'IConstIntegral'. |
| `int Cap<N:int>(Buffer<N> b)` called with a `Buffer<unknown>` | The same MIST0003. |
| `Array<int, N> Make<N:ZeroOrPositiveInt>() { return new Array<int, N>(k => k); }` | Passes analysis. MIST0003, array creation must have array size. |
| `bool IsA<T>(unknown x) { return x is T; }` called as `IsA<int[3]>(new int[5])` | Prints `True`. `IsA<Buffer<4>>(new Buffer<5>())` prints `False`. |

### What holds today and must still hold

- `var a = new int[3]; a = new int[4];` is MIST0157, cannot convert 'int[4]' to
  'int[3]'. A `var` takes the literal length. `var a = new int[n]; a = new int[m];`
  runs, because that `a` is `int[]`.
- `Len(new int[3])` infers `N` as 3.
- `int[]` and `Array<int, unknown>` read `Length` when the program runs.
  `Buffer<unknown> b = new Buffer<4>();` answers 4 from `b.Cap()`.
- `b is Buffer<4>` from a `Buffer<unknown>` and `a is Array<int, 3>` from an
  `int[]` both test and narrow.
- `(Array<int, 3>)a` from an `int[]` is MIST0149: a cast reaches only a type the
  value already is.
- `new Ring<0>()` under `where N >= 1` is MIST0278.
- `Buffer<4>.Size` is 4. `buffer.Size` on an instance is MIST0301. `a.Length` on
  an array is the one dependent parameter that is read through an instance.
- Inside `void Check<N:int>(Buffer<N> b)`, `b is Buffer<5>` is MIST0193, "can
  never be 'Buffer<5>'". It can: `Check(new Buffer<5>())` is such a call.
- `true switch { true => new int[3], _ => new int[4] }` has the type
  `IEnumerable<int>`, not `int[]`, so `.Length` on it is MIST0301.
- Three behaviours that rest on how literal closings reach C# today, as one C#
  type per value: static storage is per closing (`Counter<1>.hits = 5` leaves
  `Counter<2>.hits` at 0); `Buffer<5> == Buffer<6>` as type values is `False`;
  one class may implement `ISized<1>` and `ISized<2>`.

## The model

### A slot takes a value, captured once

Today a slot takes a literal, and the literal is part of the type's name. The
proposal: a slot takes a value. Where the argument is a literal nothing changes.
Where it is a name, of a local, a parameter or a member, or the size expression
of `new T[e]`, the type is closed with the value it produced at that moment. The compiler does
not know the number. It knows which value it is and the facts in hand about it.

```mist
void Demo(ZeroOrPositiveInt n, ZeroOrPositiveInt m, ZeroOrPositiveInt i)
{
    var a = new Array<int, n>(k => 0);
    n = m;
    if(i < n)
    {
        a[i] = 1;       // refused
    }
}
```

In the words of flow-sensitive-refinement-inference Requirement 3: line 1 closes
the type of `a` with `n@1`. Line 2 begins `n@2`, which equals `m`. `i < n@2`
says nothing about `n@1`, so the index is refused, failing when `a.Length <= i`.
Nothing is recomputed at the write. The type never held the name `n`, only the
value it had.

The argument is evaluated once, where the type is written, in source order and
before the constructor's or the method's ordinary arguments. `new T[Next()]`
already behaves this way for its size.

### The same type when the facts prove the values equal

`Array<int, x>` and `Array<int, y>` are one type exactly when the facts in hand
prove `x == y`. Two literals are the case that needs no facts.

```mist
void Copy(ZeroOrPositiveInt n, ZeroOrPositiveInt m)
{
    var a = new Array<int, n>(k => 0);
    Array<int, n> b = a;        // proven: both slots read the same value of n
    Array<int, m> c = a;        // refused: fails when m <= n - 1 or m >= n + 1
    if(m == n)
    {
        Array<int, m> d = a;    // proven by the condition
    }

    var three = 3;
    Array<int, three> e = new int[3];   // proven: three == 3
}
```

The proof is Requirement 1 of the flow spec: assume the two values differ, chain
the facts, and the conversion is proven when no value satisfies the result. A
refusal reports the leftover statement in the program's own names.

The rule applies wherever a type meets a type: an assignment, an argument, a
return, and each slot of a type nested in another (`List<Array<int, n>>`
against `List<Array<int, m>>` needs `n == m`). Choosing between overloads never
waits on a proof. Where the compiler must pick one type from several, such as
the arms of a switch expression, closings that differ only in a slot's value
become the type closed with `unknown`.

### Reading the slot through the value

A value carries the value its type was closed with, and reading the dependent
parameter through it yields exactly that value. For Array this is `a.Length`.
It is true of every value of the type, however it was obtained, so it is not a
fact recorded at creation sites. It is what the type says.

This one rule proves every row of "knows nothing of its length" above:

- `new int[n]` is an `Array<int, n>`, so `a.Length == n`.
- Two arrays created with the same `n` have the same length.
- A parameter `Array<int, N> a` has `a.Length == N` inside the body.
- A field `Array<int, Capacity> slots` has `slots.Length == Capacity`.
- The result of `Array<int, N> Make<N:ZeroOrPositiveInt>()` has that length
  at the caller.

### unknown

`int[]` is `Array<int, unknown>`: closed with a value the type does not name.
The value still carries it, and `x.Length` still reads it. Every closing converts
to the `unknown` one, as today. The `unknown` one converts to a named closing
when the facts prove it:

```mist
void Take(int[] x, ZeroOrPositiveInt n)
{
    Array<int, n> a = x;        // refused: nothing says x.Length == n
    if(x.Length == n)
    {
        Array<int, n> b = x;    // proven
    }
}
```

A type that leaves the reach of the value it is closed with becomes the
`unknown` closing: a lambda's inferred return type that names a value created
inside the lambda is one case.

### Signatures

Decided 2026-11-11, Requirement 8: a relation crosses a signature through a
dependent parameter, never through a regular one.

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
    var a = Zeroes<count>();    // a is Array<int, count>
    var b = Zeroes<count>();
    Blit(a, b);                 // proven
    Blit(a, new int[3]);        // refused: fails when count <= 2 or count >= 4
}
```

At a call the dependent parameter is bound to the value passed or inferred,
each argument's type is proven against its parameter's, and the result has the
return type with the same binding. Inside the callee the parameter types are
facts at the first line, and every `return` is proven against the return type.
No body is read at a call, as Requirement 6 of the flow spec already states.

### The slot's own contract

`Array<TValue, Length:ZeroOrPositiveInt>`, `Ring<Capacity:PositiveInt>` and a
`where N >= 1` clause state what a slot admits. A runtime argument is proven
against it where the type is written, as an argument against a parameter
predicate is. No guard is emitted.

```mist
void Build(int n)
{
    var a = new Array<int, n>(k => 0);      // refused: fails when n <= -1
    if(n >= 0)
    {
        var b = new Array<int, n>(k => 0);  // proven
    }
}
```

### A method's dependent parameter

`Twice<n>()` closes `N` with the value of `n` for that call. Where the argument
list is omitted, `N` is inferred from an argument as today, and the inferred
value is whatever that argument carries: `Len(a)` over an `Array<int, n>`
answers `n`, and over an `int[]` it answers `a.Length`. The first two rows of
"wrong afterwards" become valid programs with a defined answer.

### What a value carries at run time

For the language: a value of a type with a dependent parameter carries that
parameter's value for its life, and an invocation of a method with one carries
it for the call. A literal closing and a runtime closing of a proven-equal value
are one type, and a value passes between them with no conversion and no copy.
An object does not record which of the two created it.

For the C# backend this means one representation per declaration. Today a
literal closing is a C# type of its own (`Buffer<Const_4>`), which makes two
types of what the language calls one. Array, Span and ReadOnlySpan already
drop the slot from their C# spelling and keep the value in the instance, and
their type tests write the comparison (`int[] { Length: 3 }`). The direction
for `design.md`: a declaration that some closing in the program closes at run
time takes that same route, with the value in the instance or passed with the
call, and every other declaration keeps today's carriers, so no existing
program changes. Whole-program compilation is what lets the compiler know
which is which. This needs a spike before it is a decision.

## Acceptance programs

Array:

| Program | Expected |
|---|---|
| `var a = new Array<int, n>(k => 0); if(i < n) { a[i] = 1; }` | Accepted. |
| `var a = new int[n]; var b = new int[n]; if(i < a.Length) { b[i] = 8; }` | Accepted. |
| `var a = new Array<int, n>(k => 0); n = m; if(i < n) { a[i] = 1; }` | Refused at the index. |
| `var a = new Array<int, n>(k => 0);` where `n` is a plain `int` | Refused, fails when `n <= -1`. |
| `Array<int, m> c = a;` with `a` an `Array<int, n>` | Refused. Accepted inside `if(m == n)`. |
| `var z = Zeroes<count>(); if(i < count) { z[i] = 1; }` | Accepted. |
| `return new int[N + 1];` from `Array<int, N> Make<N:ZeroOrPositiveInt>()` | Refused at the return. |

An ordinary user-defined type:

```mist
public type PositiveInt = int >= 1;

public class Ring<Capacity:PositiveInt>
{
    public type Slot = ZeroOrPositiveInt and < Capacity;

    private Array<int, Capacity> slots;

    public Ring()
    {
        slots = new Array<int, Capacity>(k => 0);
    }

    public int Get(Slot at)
    {
        return slots[at];
    }
}

Ring<N> Fresh<N:PositiveInt>()
{
    return new Ring<N>();
}
```

`n` and `m` are `PositiveInt` parameters here unless a row says otherwise.

| Program | Expected |
|---|---|
| `var r = new Ring<n>();` | Accepted, and `slots[at]` inside `Ring` is proven. |
| `var r = new Ring<n>();` with `ZeroOrPositiveInt n` | Refused, fails when `n <= 0`. |
| `var r = Fresh<n>(); if(i < n) { r.Get(i); }` | Accepted. |
| `var r = new Ring<n>(); n = m; Ring<n> s = r;` | Refused at the declaration of `s`. |
| `var r = new Ring<n>(); if(m == n) { Ring<m> s = r; }` | Accepted. |

## What this revises

- dependent-types: Requirements 2 and 5 (an argument is a constant), 4 (the
  domains), 7.3 (no read through an instance), 8.1 (equal means identical
  literals), 11.4 (method arguments are constants), 15.1 (its diagnostic), and
  13 and 14 (one C# type per value) for the declarations the backend moves off
  carriers. 15.6 keeps refusing an expression in a slot. 9.3, 9.4 and 15.7
  (no test against an open closing) are revised by Requirement 9.
- adhoc-refinement-constraints Requirement 12.2, "dependent values are always
  known at compile time". No guard is emitted still; the reason changes.
- flow-sensitive-refinement-inference "Status and scope": the first stretch
  goal is this spec. Requirement 2.4.6 is restated by "Reading the slot through
  the value".
- stack-only-types: a runtime-sized `stackalloc` is no longer an
  unknown-length span.
- deep-ownership-types, introduction: it expects a runtime dependent argument
  to need an owned binding moved in. Requirement 6 closes a type only with an
  immutable value that copies, so the captured value cannot change after the
  capture, and no ownership rule is needed.

## Decisions for the owner

One per message when they are put to the owner. Each line here is only the
question and the recommendation.

1. **Does `var` take a runtime length as it takes a literal one?** Decided
   2026-11-11: yes. Requirements 1 to 3.
2. **Does `new T[n]` owe the slot's contract?** Not put as a decision: it
   follows from Decision 1 and adhoc-refinement-constraints Requirement 5, and
   is Requirement 4. All eight runtime-sized creations in the samples are
   sized by a plain `int` and change with it.
3. **How is an argument that is not a name written?** Decided 2026-11-11: it
   is not. A slot takes a literal or a name, and any other expression goes to
   a variable first. Requirement 5.
4. **May a dependent parameter be read through an instance of any type?**
   Decided 2026-11-11: yes, Requirement 7. The same answer set what may close
   a type at all, Requirement 6: anything readonly and stampable, and nothing
   mutable.
5. **May a signature's types name the method's regular parameters?** Decided
   2026-11-11: no. A relation crosses a signature through a dependent
   parameter only. Requirement 8.
6. **Is a type test against a runtime closing a comparison at run time?**
   Decided 2026-11-11: yes, and a test may name a dependent parameter in scope.
   Requirement 9.
7. **What is static storage on a type closed at run time?** Decided
   2026-11-11, after the owner re-examined generics-type-arguments Requirement
   10 and kept it: static means of the type, a closing is a type, so storage is
   per closing, runtime closings included. Requirement 10.

   Decided after it, 2026-11-11: a type may implement one abstract at two
   values of a slot, `class Both : ISized<1>, ISized<2>`, and only the slot
   read through the `unknown` closing of an abstract is refused. Requirement
   11. A draft that refused the declaration was withdrawn.
8. **The C# representation.** Not put to the owner, who holds the C# backend
   to be a crutch the language is not designed around. [design.md](design.md)
   settles it: a declaration no closing reaches at run time keeps today's
   carriers, and the rest carry the value in the instance or the call.

## Shape of the implementation

Written before the decisions and kept as the first sketch.
[design.md](design.md) is the design.

- A runtime argument is a hidden readonly local, declared where the type is
  written and initialized from the expression. The type holds that local. Its
  facts, its single evaluation and its capture by a lambda are then what a
  local already has.
- The binder admits a conversion between two closings of one declaration whose
  slot arguments are not the same literal and records what has to be proven.
  Stage 5 proves it or refuses, in the existing fact flow.
- A slot argument resolves by the slot's kind: a name in a dependent slot is a
  value, a name in a type slot is a type.
- The Length holder in the fact flow becomes the slot's argument: a literal,
  the hidden local, a dependent parameter of the enclosing declaration, or a
  fresh value for `unknown`.
- The four programs under "wrong afterwards" each get a test at the tier their
  rule owns.
