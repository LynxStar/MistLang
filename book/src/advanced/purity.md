# Pure and Local Members

A call can reach well beyond the member it names. It can write a field of an object you passed it, read a static that other code changes, or print. Mist lets a member state how far it reaches with three modifiers that compose: `readonly`, `local` and `pure`. A caller that is itself restricted can then decide which calls it may make from the declarations alone, without reading any body.

You have already met [`readonly`](../object-model/readonly.md), with its blocks and `frozen` parameters in [Readonly Contexts](./readonly-contexts.md). This chapter adds `local` and `pure`, and shows how the three fit together.

Each modifier is a promise a member makes about itself, not a demand on its callers. Marking `List<T>.Add` as `local` changes nothing about how you call it, and none of the modifiers changes what the emitted program does. (A `pure` type is also a readonly type, so its fields are emitted `readonly`, as a readonly type's are.) Only a scope that is itself `readonly`, `local` or `pure`, or a place where [Mist requires purity](#where-mist-requires-purity), reads these promises, and only to decide which calls it may make.

## Every Restriction Is About What Comes from Outside

A scope may do anything with what it **owns**: what it created, what was moved into it, and its own copies of structs. Each modifier restricts only what the scope does with what comes from **outside** it.

| Modifier | May read from outside its own scope | May write outside its own scope |
|---|---|---|
| `readonly` | anything | nothing |
| `local` | only its own instance | only its own instance |
| `local readonly` | only its own instance | nothing |
| `pure` | only what cannot change, and inputs it holds frozen | nothing |

For a local function or lambda, "its own instance" is what it captured from the scope around it.

The promises nest. Every `pure` member is `readonly`, and every `local readonly` member is both `local` and `readonly`. So wherever Mist asks for a readonly member (a readonly context calling it, a readonly abstract member it implements, a `where T : readonly` bound), a pure or local readonly one will do.

Leaving a member unmarked does not make a restricted caller assume the worst of it. If the member has a Mist body, the compiler [infers](#inference) the strongest promise that body keeps, and a restricted caller may rely on that. A member that keeps no promise, declared or inferred, may do anything, and a restricted caller assumes it does. That includes every member with no Mist body and no declared promise, since the compiler cannot see what it touches.

Order among the modifiers does not matter. Writing `pure readonly` is accepted with an informational `MIST_PM_READONLY_IMPLIED_BY_PURE`, since the `readonly` adds nothing. Writing `pure local` is an error, `MIST_PM_PURE_AND_LOCAL`: `local` lets a member change its own instance, which `pure` forbids. `local` needs an instance, so it is refused on static members and constructors with `MIST_PM_LOCAL_REQUIRES_INSTANCE`. `pure` is admitted on both.

## What `pure` Means

`pure` means what it means in computer science generally, and in Haskell. A pure computation:

1. has **no side effects**: it changes no state that exists outside it and performs no input or output; and
2. is **deterministic**: its result depends only on its inputs, so the same inputs always give the same result within one run of the program. [Mist Is Culture-Invariant](#mist-is-culture-invariant) says what a run fixes.

Mist takes the two standard refinements of that definition:

- **Allocation is not a side effect.** A pure member may create objects and change them, as Haskell's `ST` lets a pure function mutate state that exists only inside it. Building a list, adding to it and returning it is pure.
- **Pure is not total.** A pure member may still fail or never finish. Dividing by zero is pure, because it fails the same way every time. Purity promises the same outcome for the same inputs, not that the outcome is a value.

Other languages use the word differently. If you know one of them, this table maps it onto Mist:

| System | Its word | What it promises | In Mist |
|---|---|---|---|
| Haskell, general CS | pure | no side effects, deterministic | `pure` |
| Solidity | `view` / `pure` | reads but writes no state / reads and writes no state | `readonly` / `pure` |
| D | weakly / strongly pure | no global state; weakly pure may read and change its arguments | `pure` does neither to what it was handed; `local` is the closer match to weakly pure |
| C# `[Pure]` attribute | pure | makes no visible state changes | `readonly`, not `pure` |
| GCC `__attribute__((pure))` | pure | no side effects, may read global memory | weaker than `pure` |

## Where an Input Comes From

Purity is judged against the reference a scope holds, not against the object behind it. A pure member may read what it owns, what cannot change, and what it holds `frozen`. It may write only what it owns.

| Input | Where it comes from | May a pure scope read its state? | May it write it? |
|---|---|---|---|
| A struct, by value | the scope's own copy | yes | yes (the copy) |
| A reference-typed field inside that struct | outside: the copy still points at another object | stable fields only | no |
| A struct by `ref` or `in` | outside: the caller's storage | stable fields only | no |
| A class, unrestricted or `readonly ref` | outside, and may change between reads | stable fields only | no |
| A class, `frozen` | outside, but fixed for its lifetime | yes | no |
| A class, `owned` | moved in, so the scope's own | yes | yes |

A **stable field** is one that cannot change once its object is constructed: a `readonly` field, the storage of a property with no setter or an `init`-only one, a field of a readonly type, or a `readonly` static.

A scope owns a local or parameter declared `owned` (see [Ownership](./ownership.md)), a value it has just allocated, the result of a call that returns `owned`, and a local whose every assignment is one of those and which is never passed by `ref`. An allocation is not the scope's when its constructor lets `this` escape, since something else may then hold the new object.

A `frozen` reference is a value for its lifetime. While a caller lends an object as `frozen`, nothing can change it (see [The LentFrozen State](./readonly-contexts.md#the-lentfrozen-state)), so every read through the reference gives the same answer for as long as the reference exists. The object may change once the loan ends, but the next call receives a new loan, and that is a different input. A pure member that reads a frozen input therefore still gives the same result for the same inputs. An unrestricted reference has no such guarantee: other code holding the same object can change it between two reads, even within a single call.

{{#tabs }}
{{#tab name="Mist" }}
```mist
{{#include ../../snippets/advanced/purity/owned-and-frozen.mist}}
```
{{#endtab }}
{{#tab name="Emitted C#" }}
```csharp
{{#include ../../snippets/advanced/purity/owned-and-frozen.cs}}
```
{{#endtab }}
{{#endtabs }}

`Seeded` holds its list in an owned binding, so it may call `Add`, which is `local`, and `Count`, which is `local readonly`: everything they touch is inside `Seeded`. `Size` reads `Count` on a list it holds frozen. Between the two calls to `Size`, `Main` adds to the list; the second loan is a different input and gives a different answer, which determinism allows. `Shifted` writes `p.X`, but `p` is its own copy of the caller's struct, so the caller's `p.X` is still 1 afterwards. The program prints:

```text
Seeded()=3
Size(xs)=2
Size(xs) after Add=3
Shifted(p)=13 p.X=1
```

Take the `frozen` away and the same body is refused:

```mist
public static pure int Size(List<int> xs)
{
    return xs.Count;
}
```

The compiler reports `MIST_PA_PURE_SCOPE_CALLS_IMPURE` at `xs.Count`: `Count` is local readonly, and calling it needs an owned or frozen receiver. Reading a mutable field of an unrestricted input is refused too, as `MIST_PA_PURE_SCOPE_READS_MUTABLE`, and writing one is `MIST_PA_PURE_SCOPE_WRITES`. A pure scope also may not store a frozen input inside something it owns, since that may outlive the loan (`MIST_PA_PURE_SCOPE_STORES_UNOWNED`).

## What a Pure Scope May Call

| Calling... | Allowed on... | Because |
|---|---|---|
| a `pure` member | anything | it touches nothing outside its own scope |
| a `local readonly` member | an instance the scope owns, or a `frozen` input | it reads only that instance, which cannot change under it |
| a `local` member | an instance the scope owns | it may change that instance, and only the scope can observe it |
| a `readonly` member | nothing | it may read outside state that can change between calls |
| a member that keeps no promise, declared or inferred | nothing | it may do anything |

Operators and conversions on the built-in types are pure, and so is the default of a known type, such as `default(int)` or `default(Holder)`. The default of a type parameter is refused, with `MIST_PA_PURE_SCOPE_CALLS_IMPURE`: the type argument chooses what taking it does, and a `default` constraint promises only that a default exists, not that it is pure. The default of a type whose base constructor is not pure is refused too, since taking the default runs that constructor. `new` needs a pure constructor. A function value may be called when its type is a [pure function type](#pure-function-types), or when it is a local holding a lambda whose own promise, declared or inferred, the scope may call: a pure lambda on anything, and a local one only when the scope owns what it captured, as `record` shows [below](#local-functions-and-lambdas). Any other function value is refused. A pure scope performs no input or output, directly or through anything it calls, and it may not `await`.

A `local` scope follows the same table with one difference: it cannot read a frozen input, only its own instance and what it creates. It may use its arguments as values (store them, pass them on, compare them, read the fields of a struct passed by value), but it may not read or write through a reference-typed argument. Reading a field through one is `MIST_PA_LOCAL_SCOPE_READS_ARGUMENT`.

A `readonly` scope may call `readonly`, `pure` and `local readonly` members on anything. On something it owns, it may also write a field or an index, and call a `local` member, a `local` setter included. Those rules let a readonly member build a value before returning it: a readonly method may create a list, `Add` to it and return it, because `readonly` restricts what it does to what it was given, not to what it made. The [Readonly](../object-model/readonly.md#readonly-methods) chapter lists them with the rest of what a readonly method may do.

## Local Functions and Lambdas

A local function or lambda declared `local` treats what it captured as its own instance. What it writes through a captured reference is its own only if the enclosing scope owns what that reference points at. The compiler decides that where the function is called, against the enclosing scope's ownership.

{{#tabs }}
{{#tab name="Mist" }}
```mist
{{#include ../../snippets/advanced/purity/local-captures.mist}}
```
{{#endtab }}
{{#tab name="Emitted C#" }}
```csharp
{{#include ../../snippets/advanced/purity/local-captures.cs}}
```
{{#endtab }}
{{#endtabs }}

`record` is a `local` lambda whose instance is `readings`, and `Widest` is a `local readonly` local function that only reads it. `Spread` created `readings`, so both calls are calls to a local member on something `Spread` owns, and `Spread` stays pure. It prints `Spread()=6`. Had `readings` been a parameter of `Spread`, `record(3)` would be refused with `MIST_PA_PURE_SCOPE_CALLS_IMPURE`: the lambda is local, and its receiver is not owned.

A local function also accepts `pure`. A bare `readonly` stays refused on a local function; write `local readonly` or `pure`. A pure lambda or local function is judged differently from a local one: its captured variables are inputs from outside, as [Pure Function Types](#pure-function-types) describes.

## Promises on a Type

You can write `local`, `local readonly` or `pure` on a class, a struct or an [abstract type](../object-model/abstract-types.md), and every member of the type then makes the promise. An enum or a newtype takes `readonly` but not these: writing one there is `MIST_PM_PURITY_MODIFIER_PLACEMENT`.

- On a `pure` type, every member is pure, constructors and static members included. Every field is stable, and no member writes an instance once it is constructed, so a pure type cannot change after construction. A pure scope may read its state through any reference, not only a frozen one, and may call any of its members.
- On a `local` type, every instance method, property and indexer is `local`. Constructors and static members, which have no instance, keep what they declare or infer. A member that also writes `readonly` is `local readonly`. A member that writes `pure` is pure, which replaces `local` rather than conflicting with it.

{{#tabs }}
{{#tab name="Mist" }}
```mist
{{#include ../../snippets/advanced/purity/promises-on-types.mist}}
```
{{#endtab }}
{{#tab name="Emitted C#" }}
```csharp
{{#include ../../snippets/advanced/purity/promises-on-types.cs}}
```
{{#endtab }}
{{#endtabs }}

`Tally` is local, so `Add` is local and `Total`, which also says `readonly`, is local readonly. `local` does not reach the constructor, which states its own `pure`, so the compiler checks that promise; had it stated nothing, inference would have found the same one, as it does for `Vec` [below](#inference), and a pure scope could still call `new Tally(10)`. `Counted` owns its tally through an owned binding, so it may call both, and returns 17. `Money` is a pure type: `Combined` reads `a` and `b` through ordinary references it neither owns nor holds frozen, which the stable fields of a pure type allow, and returns 270.

A member of a local type may call a `local` or stronger member on a field the instance owns (see [Owned Fields](./ownership.md#owned-fields)), and only a pure member on a field it does not own, whose object other references may share.

## Promises on Accessors

On a property or indexer, a modifier applies to each accessor it declares. An accessor can also state its own promise at its start, ahead of any accessibility, and that promise replaces the declaration's for that accessor. The common case is a getter that only reads its instance and a setter that only writes it: `{ local readonly get; local set; }`. Standard's `List<T>` declares its indexer that way.

{{#tabs }}
{{#tab name="Mist" }}
```mist
{{#include ../../snippets/advanced/purity/accessor-promises.mist}}
```
{{#endtab }}
{{#tab name="Emitted C#" }}
```csharp
{{#include ../../snippets/advanced/purity/accessor-promises.cs}}
```
{{#endtab }}
{{#endtabs }}

`Turned` owns its dial, so it may call the local setter and the local readonly getter on it. It prints `Turned(3)=7`. `Dial` states what inference would find anyway: an auto getter over mutable storage is inferred local readonly, and an auto setter local. Stating the promises makes the compiler hold the accessors to them. `List<T>`'s indexer has no Mist body, so for it the stated promises are the only source.

## Pure Function Types

A pure member that calls a function it was given can only be pure if the function is. Writing `pure` before a [function type](../functions/first-class-functions.md) makes the promise part of the type: `pure func<int, int>`, `pure Func<int, int>`, `pure Action<int>`, or an alias of one. Calling a value of a pure function type counts as calling a pure member.

{{#tabs }}
{{#tab name="Mist" }}
```mist
{{#include ../../snippets/advanced/purity/pure-function-types.mist}}
```
{{#endtab }}
{{#tab name="Emitted C#" }}
```csharp
{{#include ../../snippets/advanced/purity/pure-function-types.cs}}
```
{{#endtab }}
{{#endtabs }}

The lambda `n => n + 1` converts to `pure func<int, int>` because its body keeps the pure rules. A lambda is judged once, where it converts, as a pure scope whose captured variables are inputs from outside. It may read a captured variable only when nothing can assign that variable after the capture: `offset` is `readonly`, and a local assigned again after the capture would be refused with `MIST_PA_PURE_SCOPE_READS_MUTABLE`. The method group `AddTen` converts because `AddTen` is a pure member. In the emitted C#, the promise is gone: `Twice` takes a plain `System.Func<int, int>`. The program prints:

```text
Twice(n => n + 1, 3)=5
Twice(AddTen, 1)=21
Twice(shift, 1)=201
plain(1)=101
```

A lambda that does something impure does not convert:

```mist
Twice(n => { Console.WriteLine(n); return n; }, 3);
```

The compiler reports `MIST_PA_PURE_SCOPE_CALLS_IMPURE` at the `Console.WriteLine` inside the lambda, the operation that breaks purity, rather than at the call to `Twice`.

A pure function type converts implicitly to the same function type without `pure`, as `plain` shows, and to a CLR delegate of the same shape, such as a `System.Predicate<int>` from a `pure func<int, bool>`. The reverse is refused, and a cast does not help: the assignment below is `MIST_PA_NO_IMPLICIT_CONVERSION`, and the same conversion written as a cast is `MIST_PA_CAST_HAS_NO_CONVERSION`.

```mist
func<int, int> plain = n => n + 1;
pure func<int, int> p = plain;
```

A plain function value carries no evidence of what it does. `plain` could hold a lambda that prints, or one that reads a variable someone reassigns, and nothing about its type says which. Mist judges a lambda where it converts to a pure function type, while its body is still in view. Once a function is a plain `func` value, its body is out of reach, and only a type that states `pure` can carry the promise further. A CLR delegate is refused for the same reason.

Where `pure` could belong to the enclosing declaration (before a member's return type, or the type of a property or indexer), it belongs to the declaration. To return a pure function or keep one in a property, name the type through an alias, as `type Step = pure func<int, int>;` does. As a type argument, the promise is part of the type: `List<pure func<int, int>>` and `List<func<int, int>>` are different types, and neither converts to the other. The promise is erased in the emitted C#, so two overloads that differ only in whether a function type is pure would emit the same signature, and the compiler refuses them.

## Inference

You do not have to write a promise on every member. Declare one where you want it checked, and the compiler infers the rest.

- A member that **declares** a promise is checked against it, with an error at each operation that breaks it.
- A member that **declares nothing** is inferred to keep the strongest promise its body keeps. Inference runs over the whole program: every member starts with every promise and loses each one its body breaks or a callee has lost, until nothing changes. Recursive members keep a promise when nothing else takes it away.

The compiler consults an inferred promise only when the member is a callee, to decide whether a caller that needs the promise may call it. It never reports a member for failing a promise the member did not declare.

{{#tabs }}
{{#tab name="Mist" }}
```mist
{{#include ../../snippets/advanced/purity/inferred-promises.mist}}
```
{{#endtab }}
{{#tab name="Emitted C#" }}
```csharp
{{#include ../../snippets/advanced/purity/inferred-promises.cs}}
```
{{#endtab }}
{{#endtabs }}

`Square` declares nothing, but its body is pure, so the declared pure `SumOfSquares` may call it. The same inference serves the places where [Mist requires purity](#where-mist-requires-purity). `Vec`'s constructor declares nothing and is inferred pure, so `Holder`'s field initializer may call it, and each `default(Holder)` gets its own `Vec`: changing `first.Origin` leaves `second.Origin` at 7. `Interval.Length` declares nothing either, and reads only readonly fields, so the `NonEmpty` refinement may read it. The program prints:

```text
SumOfSquares(3, 4)=25
first.Origin.X=70 second.Origin.X=7
proven.Length=5
```

As with [readonly inference](../object-model/readonly.md#readonly-inference), a declared promise is the sturdier one. An inferred promise disappears silently when the body changes, and the error then lands on the caller that relied on it. Declare the promise on members other code depends on.

An abstract member that declares a promise requires it of every implementation; one that declares nothing keeps exactly the promises every implementation in the program keeps. A member with no Mist body (intrinsic, CLR-projected or extern) keeps only what its Mist declaration states, since the compiler cannot see what it touches. A CLR member with no Mist declaration keeps no promise at all.

## Standard's Promises

Standard declares the promise of each member it supplies that keeps one:

| Declaration | Promise |
|---|---|
| The primitive types (`int`, `double`, `bool`, `char` and the rest), `string` and `Regex` | every member `pure` |
| `Math`, `Convert`, `Index`, `Range` | `pure` types |
| `List<T>` constructor | `pure` |
| `List<T>`: `Count`, `Contains`, `IndexOf`, `ToArray`, `GetEnumerator`, the indexer's getter | `local readonly` |
| `List<T>`: `Add`, `Insert`, `Remove`, `RemoveAt`, `Clear`, `Sort`, `Reverse`, the indexer's setter | `local` |
| `List<T>`: `AddRange`, `ForEach`, `FindAll` | none |
| `Console`, `Task`, `Type`, `Span` | none |

`AddRange` makes no promise because it reads through a reference-typed argument, which a local member may not do, and `ForEach` and `FindAll` run a function value they were handed. Array elements need no declaration: an element reads and writes like a field of its array.

"Every member" means every member the type declares. `ToString` and `GetHashCode` come to every value from Standard's universal extension instead, which states no promise, so a pure scope may not call `n.ToString()` even on an `int`. To turn a value into text in a pure scope, interpolate it or concatenate it, as [Mist Is Culture-Invariant](#mist-is-culture-invariant) describes.

Standard declares `List<T>` as an intrinsic over `System.Collections.Generic.List<T>`. It is never an `owned` class. `owned` on a class is the [cooperation contract](./ownership.md#the-cooperation-contract): every field assignment inside the type must come from an owned source. A container stores whatever its callers hand it, so it cannot promise to own its elements. A pure scope that changes a list owns the list itself, through an owned binding or by creating it, as `Seeded` and `Spread` do. The pure constructor is what lets a constructorless type hold a list in a field initializer.

## Where Mist Requires Purity

Two places in the language need a computation that gives the same answer every time, and both are defined as pure.

**Defaults.** A field initializer of a defaultable type, and each entry of a `default` block, is a pure expression evaluated with no inputs, so every default of a type starts in the same state (see [The Default Keyword](./default-keyword.md)). `Holder` above is admitted because `new Vec(7)` is pure. A constructor that prints is not:

```mist
public class Clock
{
    public int Ticks;

    public Clock()
    {
        Console.WriteLine("tick");
        Ticks = 1;
    }
}

public class Holder
{
    public Clock C = new Clock();
}
```

The compiler reports `MIST_PA_DEFAULT_NOT_CONSISTENT` at the initializer, naming the field, its type and the operation that is not pure. A default may still fail, since pure is not total: an initializer that divides by zero is pure, and fails the same way each time the default is taken.

**Refinement predicates.** A member that a [refinement predicate](../type-system/refinement-types.md#property-pattern-refinements) reads must be a pure member or a stable field, since the predicate's answer has to hold for as long as the value exists. `local readonly` is not enough: it reads its instance's current state, which may change after the value was proven.

```mist
type NonEmpty = List<int> { Count: > 0 };
```

`List<T>.Count` is local readonly, so this is `MIST_PA_REFINEMENT_OVER_MUTABLE_MEMBER`. The `NonEmpty` refinement in the inference snippet is admitted because `Interval.Length` is pure over readonly fields.

## Mist Is Culture-Invariant

.NET formats numbers, maps case and parses text according to the current culture. Under a German culture, `1.5` prints as `1,5`. Under a Turkish one, upper-casing `istanbul` gives a dotted capital I.

A pure member's result depends only on its inputs, so the same inputs give the same result for as long as the program runs. What the process fixes when it starts (the runtime and its version, its globalization mode and ICU data, the operating system, the machine) is the program's setting rather than an input, and a pure member may depend on it. The current culture is different: any code can change it while the program runs, so it is outside state, like the clock or a file. If these operations followed the culture, none of them could be pure.

Mist is culture-invariant instead. Every conversion between a value and text, every ordering of text, every case mapping and every regular expression that Mist performs on your program's behalf is independent of the current culture, because the emitted C# names the invariant culture at each call, or runs under it for that one call. That covers interpolation, concatenation, `Console.Write` and `WriteLine`, an explicit `ToString()` and its method group, and the text of a tuple, an optional, an anonymous object or a .NET value such as a `KeyValuePair` or a `Lazy<double>`. Text equality is ordinal, which no culture affects. That is what lets Standard declare `ToUpper` and `Convert.ToInt32` pure.

Sorting a `List<string>` uses the invariant culture's order. That order comes from the ICU data the runtime loads when it starts, so it holds for the whole run, though another .NET version or globalization mode may order some text differently. By the definition above, that is part of the program's setting. Another ordering, such as ordinal, would be a member of its own.

Interpolation and concatenation have no declaration in Standard to state a promise. The compiler rules on each hole instead. A hole whose text its type fixes is pure: a primitive, a string, an enum, a newtype, a type declared in Mist (unless it implements `System.IFormattable`), or a tuple or optional of these. A hole whose text is decided at run time is refused in a pure scope with `MIST_PA_PURE_SCOPE_CALLS_IMPURE`: a type parameter, `unknown`, a CLR type, or a container such as `List<T>`, whose `ToString` the program cannot see.

{{#tabs }}
{{#tab name="Mist" }}
```mist
{{#include ../../snippets/advanced/purity/invariant-culture.mist}}
```
{{#endtab }}
{{#tab name="Emitted C#" }}
```csharp
{{#include ../../snippets/advanced/purity/invariant-culture.cs}}
```
{{#endtab }}
{{#endtabs }}

The emitted C# shows the rule: `ToUpper` becomes `ToUpperInvariant`, the interpolated string in `Price`, whose hole is a `double`, is wrapped in `System.FormattableString.Invariant`, and `Convert.ToInt32` passes `CultureInfo.InvariantCulture`. The rule applies everywhere, not only in pure scopes, which is why the `Parsed` line in `Main`, whose hole is an `int`, is wrapped too. The other two lines in `Main` need nothing, since their holes are strings.

The compiler reads no culture either. It parses the literal `1.5` in `Main`, and writes it into the C#, the same way on every machine, and the build's own messages quote values the same way everywhere. So the program prints the same text wherever it is compiled and whatever culture it runs under:

```text
Shout(istanbul)=ISTANBUL
Price(1.5)=1.5 EUR
Parsed(41)=42
```

Mist names the culture at each call rather than setting a process-wide default, which the program could change. A culture-aware form, if you need one, belongs in a separate member that does not promise purity.

A few things are outside the rule, and no pure member can reach any of them:

- A .NET member you name directly is the call you name. A qualified `System.Console.WriteLine(3.5)` calls .NET's own `Console` rather than Standard's, and prints with the current culture. It states no promise, so a pure scope cannot call it.
- A list sorted through a type parameter or `unknown` element orders two strings invariantly, but a tuple it holds orders its text by the current culture. So does a list of optional tuples. A pure scope refuses both sorts, since the ordering runs code the program cannot see.
