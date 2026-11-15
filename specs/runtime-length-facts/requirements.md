# Run-time length facts

## Status and scope

Proposed 2026-11-15 from the runtime-dependent-values exploration, for the
refinement work to settle with the owner. It extends
[flow-sensitive-refinement-inference](../flow-sensitive-refinement-inference/requirements.md):
three facts that let a program size an array at run time and prove its
indexes and its calls without closing any type at run time.

The owner's direction: the dependent-type design for run-time values stands,
and these refinement improvements give a second way that needs no dependent
typing for the cases below. Nothing here changes what a dependent slot means,
and nothing here closes a type with a run-time value.

Why arrays fit a fact: the CLR array object keeps its own length, so a fact
about `a.Length` stays true wherever the array goes, CLR code included. A
user type's dependent slot has no such keeper, which is why a run-time closing
of one stays with [runtime-dependent-values](../runtime-dependent-values/design.md).

Out of scope: a type that names a run-time value, a relation stored inside
data (a field, an element, a collection), and identity by value.

Every "today" below was run on main at e2656913, with `n`, `m` and `i`
parameters of type `ZeroOrPositiveInt`.

## Requirement 1 Creating a sequence with a run-time length

Flow-sensitive Requirement 2 item 6 records `a.Length == 7` for
`new Array<int, 7>`. A length that is a run-time value records the same fact.

1. `new int[e]`, `new Array<int, e>(fn)` and a stack allocation of length `e`
   SHALL record `a.Length == e` when `e` is a value Requirement 2 of that spec
   gives a fact to: a local, a parameter, a stable member read, or an
   expression it records.
2. The array's type SHALL NOT change: it is the sized-by-unknown sequence it is
   today. The relation is a fact in the bag, not a type argument.
3. The fact SHALL follow the value as every other fact does, under
   flow-sensitive Requirement 3: a write to `e` or to `a` ends it.

Example. Today the index is refused; after this requirement it is proven.

```mist
var a = new int[n];
if(i < n)
{
    a[i] = 7;    // today: MIST0275, 'i' is not proven within the bounds of 'a'
}

if(m == n)
{
    if(i < m)
    {
        a[i] = 3;    // today: MIST0275; proven from i < m, m == n, a.Length == n
    }
}
```

And at a call, where a `where` clause relates two lengths through a bound:

```mist
public static int Copy(int[] a, int[] b, int n) where n <= a.Length and <= b.Length

var z1 = new int[n];
var z2 = new int[n];
Copy(z1, z2, n);    // today: MIST0277 (fails when z2.Length >= 0 and n >= 1)
Copy(z1, new int[3], n);    // refused before and after: fails when n >= 4
```

## Requirement 2 A relation between two members

1. A `where` clause SHALL accept a member read on its left side, so that
   `where a.Length <= b.Length` states the relation directly. Today it does not
   parse (MIST0123), and the relation needs an extra bound parameter, as in
   Requirement 1's `Copy`.
2. It SHALL be proven at the call under flow-sensitive Requirement 6 item 2,
   with each parameter bound to the value passed for it.

## Requirement 3 A relation between a result and the arguments

This is flow-sensitive's second stretch goal, needed here because a length
made inside a method is lost at its return.

1. A return type SHALL be able to state a relation between the result and the
   parameters, and the caller SHALL record it under flow-sensitive
   Requirement 6 item 1, with each parameter bound to the argument.
2. The spelling is open. Three candidates:
   - The owner's: `int[n] Zeroes(int n) where n >= 5`. The result is an array
     whose length is the argument, and the caller also learns what the
     parameter's refinement says (`>= 5` here, more than an array's own
     `>= 0`). It reads the array's dependent slot with an ordinary parameter,
     so it touches the dependent-types grammar, though no run-time closing.
   - Flow-sensitive's: a dependent alias over an expression,
     `type Doubled<int:x> = x * 2;` with `Doubled<x> Double(int x)`.
   - A clause on the result, which stays within refinements' own spelling.

Example. Refused today on main and on the runtime-dependent-values branch:

```mist
public static int[] Zeroes(ZeroOrPositiveInt n)
{
    return new int[n];
}

Copy(Zeroes(n), Zeroes(n), n);    // MIST0277: the caller does not learn Length == n
```

After this requirement, with the result's length stated in `Zeroes`'s
signature, the call is proven.

## What stays with dependent types

These were measured on the same two compilers and are not this spec's:

- A user type closed with a run-time value, `Cell<n>`, in any slot domain,
  integers included. It becomes `Cell<n> | void` when it comes back from CLR
  code, because nothing outside Mist keeps its slot honest.
- A relation stored inside data, such as a grid whose rows all have the
  run-time column count.
- Identity by value: `x is Ring<5>` for a ring made as `new Ring<n>()` with
  `n` equal to 5.
- A slot whose domain is a struct value, which no interval or constant fact
  describes.
