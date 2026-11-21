# Run-time length facts

## Status and scope

Proposed 2026-11-15 from the runtime-dependent-values exploration, for the
refinement work to settle with the owner. It extends
[flow-sensitive-refinement-inference](../flow-sensitive-refinement-inference/requirements.md):
facts that let a program size an array at run time and prove its indexes
and its calls without closing any type at run time. Three were proposed:
Requirement 1 is this spec's, Requirement 2 is withdrawn, and Requirement 3
moved to runtime-dependent-values.

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

1. `new int[e]` and a stack allocation of length `e` SHALL record
   `a.Length == e`, and `a.Length >= 0`, when `e` is a value Requirement 2 of
   that spec gives a fact to: a local, a parameter, a stable member read, or an
   expression it records. `new Array<int, e>(fn)` with a run-time `e` is not
   one: it closes `Array`'s length slot with a run-time value, which stays with
   [runtime-dependent-values](../runtime-dependent-values/design.md).
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
Copy(z1, new int[3], n);    // refused before and after; after: fails when new int[3].Length == 3 and n >= 4
```

## Requirement 2 A relation between two members (withdrawn)

Withdrawn by the owner on 2026-11-19. A `where` clause stays a target and a
pattern, and the relation this requirement wanted is already written with a
count whose bound names both members:

```mist
public static void CopyInto(int[] source, int[] target, int n)
    where n <= source.Length and <= target.Length
```

With Requirement 1, a caller that creates its arrays from a run-time size
proves the call, and a caller copying a whole array passes `source.Length` for
`n` after creating `target` as `new int[source.Length]`. Neither other
spelling is adopted: `where a.Length <= b.Length` is not a target and a
pattern, and `where a { Length: <= b.Length }` could only be met by an
argument already typed with that refinement under refinement-types
Requirement 13.7, which stays as it is.

## Requirement 3 A relation between a result and the arguments (moved)

Moved to [runtime-dependent-values](../runtime-dependent-values/design.md) on
2026-11-19, with the owner's ruling on its spelling. A return type may name an
ordinary parameter in an array's length slot:

```mist
public static int[n] Zeroes(readonly ZeroOrPositiveInt n)
{
    return new int[n];
}

var a = Zeroes(count);    // the caller learns a.Length == count

public static int[n] Ones(ZeroOrPositiveInt n)    // refused: n is named in the return type and is not readonly
```

This replaces that spec's rule of 2026-11-11 that a type in a signature names
only a dependent parameter in brackets, `int[N] Zeroes<N:ZeroOrPositiveInt>()`,
which stays valid for a dependent parameter. A parameter named in the return
type SHALL be declared `readonly`; naming one that is not is a compile-time
error, not an implicit `readonly`. A `readonly` parameter refuses every write
(readonly-contexts Requirement 14.8), so `n` is the value the caller passed for
the whole body and the body must return an array of that length. Because
`int[n]` with a run-time `n` closes `Array`'s length slot at run time, it is
built under runtime-dependent-values, not here.

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
