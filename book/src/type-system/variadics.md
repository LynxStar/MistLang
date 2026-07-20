# Variadics

Variadics let you write a single generic declaration that works across any number of type arguments. Where ordinary generics fix the arity at the declaration site ("this takes exactly two type parameters"), a variadic type parameter stands for an ordered list of types whose length varies per use site. The canonical payoff is expressing the entire `Func` / `Action` callable family as one declaration rather than 17.

## The Keystone: A Pack Is a Dependent Type Parameter over `Type[]`

Mist does not invent a new language primitive for packs. Instead, it composes three features that already exist:

1. A type name in expression position is a `Type` value (type literals).
2. An array literal `[int, string, bool]` is a first-class value (array literals).
3. A compile-time value can index type identity (dependent types).

Put them together: `[int, string, bool]` is a compile-time `Type[]` value, and a dependent type parameter whose domain is `Type[]` is exactly a type-parameter pack. Writing `params TArgs` is sugar for writing `Type[]:TArgs` -- a dependent type parameter over the metatype array.

A variadic family is therefore a type-level function from `Type[]` values to types. `Func<int, string>` identifies itself by the pair (Func, `[int, string]`) in the same way that `FixedBuffer<5>` identifies itself by the pair (FixedBuffer, 5).

## Declaration Syntax

Both spellings declare the same parameter:

{{#tabs }}
{{#tab name="Mist" }}
```mist
{{#include ../../snippets/type-system/variadics/declaration-syntax.mist}}
```
{{#endtab }}
{{#tab name="Emitted C#" }}
```csharp
{{#include ../../snippets/type-system/variadics/declaration-syntax.cs}}
```
{{#endtab }}
{{#endtabs }}

The `params` spelling grants bracket-free construction at the use site. The explicit `Type[]:` form requires the caller to pass arguments in brackets.

### Fixed-Size Packs

A fixed-size domain annotation is also supported:

```mist
class Pair<Type[2]:TArgs> { }    // exactly 2 element types
```

This gives the `Type[N]` rung of the capability ladder: the compiler knows the exact length at compile time, enabling indexing.

### Multiple Packs

Multiple `Type[]:` packs are allowed in one list. They are disambiguated at the use site by brackets. At most one pack per list may be `params` (the single bracket-free slot):

```mist
class Multi<TVal, Type[]:TArgs, params CtorArgs, Type[]:Args3, TRes> { }

// Usage: brackets disambiguate the non-params packs
new Multi<int, [a, b], x, y, z, [c], float>()
// TVal=int, TArgs=[a,b], CtorArgs=[x,y,z], Args3=[c], TRes=float
```

If a list has only one `Type[]:` pack, inverse sugar makes it act as `params` automatically.

## The Binding Rule

The binding rule resolves a flat argument list to declared slots:

1. Fixed scalar slots and bracketed `Type[]:` slots bind inward from both ends.
2. The single unbracketed `params` pack absorbs whatever remains in the middle.
3. An empty middle is valid -- the pack binds to `[]`.

The pack can appear anywhere -- leading, middle, or trailing:

```mist
// Trailing pack (most common, like Func)
class Func<params TArgs, TResult> { }
Func<int, string, bool>   // TArgs=[int, string], TResult=bool
Func<string>              // TArgs=[], TResult=string

// Leading pack
class Leading<params TArgs, TLast> { }
Leading<int, string, bool>   // TArgs=[int, string], TLast=bool

// Middle pack
class Between<TFirst, params TMid, TLast> { }
Between<int, string, bool, double>
// TFirst=int, TLast=double, TMid=[string, bool]
```

The flat form and the bracketed form denote the same type: `Foo<int, string>` and `Foo<[int, string]>` are identical.

## Type Identity

A constructed variadic type is identified by the pair (family, the `Type[]` value). Identity is both order-sensitive and arity-sensitive:

- `Foo<[int, string]>` and `Foo<[string, int]>` are distinct (different order).
- `Foo<[int, string]>` and `Foo<[int, string, bool]>` are distinct (different arity).
- `Foo<[int, string]>` and `Foo<[int, string]>` are the same type.

This follows directly from elementwise array equality -- the same principle that makes `FixedBuffer<5>` differ from `FixedBuffer<6>`.

## Position Semantics and the Capability Ladder

Inside a body, the meaning of a pack identifier depends on its position:

| Position | Meaning |
|----------|---------|
| Expression (`TArgs`) | The compile-time `Type[]` value |
| Bare type (`public TArgs Item;`) | ERROR -- a pack is a value, not a type |
| Indexed (`TArgs[i]`) | Legal only when length is proven |
| Spread (`TArgs...`) | Expands in place |

What a pack can do depends on what is proven about its length:

| Proven length | `.Length` | iteration | spread | indexing |
|---------------|-----------|-----------|--------|----------|
| Unknown (`Type[]`) | yes | yes | yes | no |
| Fixed (`Type[N]`) | yes | yes | yes | yes, 0..N-1 |

Narrowing restores indexing for an unknown-length pack:

```mist
if (TArgs is Type[3] fixed) {
    // fixed[0], fixed[1], fixed[2] are legal here
}
```

## The Value Side

The value counterpart of a type pack is a tuple. Declaring `params TArgs args` gives `args` the type `(TArgs...)` -- a `ValueTuple` whose elements match the pack.

{{#tabs }}
{{#tab name="Mist" }}
```mist
{{#include ../../snippets/type-system/variadics/value-side.mist}}
```
{{#endtab }}
{{#tab name="Emitted C#" }}
```csharp
{{#include ../../snippets/type-system/variadics/value-side.cs}}
```
{{#endtab }}
{{#endtabs }}

Key distinctions:

- `params TArgs args` -- the flattened-call form. The caller writes a flat argument list; the slurp collects the middle.
- `(TArgs...) args` -- an ordinary tuple parameter. The caller passes one tuple.
- `args...` -- the spread operator, decomposing the tuple into argument slots.

Without `...`, `args` is simply a first-class tuple value that can be stored, returned, or passed whole.

### Value-Parameter Binding

The same binding rule applies on the value side. Fixed parameters bind inward from both ends; the pack absorbs the middle:

```mist
void Foo(int number, params TArgs args, bool enabled) { }
Foo(5, a, b, c, true)
// number=5, enabled=true, args=(a, b, c)
```

### Forwarding with `args...`

Spreading a value pack forwards it into argument slots:

{{#tabs }}
{{#tab name="Mist" }}
```mist
{{#include ../../snippets/type-system/variadics/forwarding.mist}}
```
{{#endtab }}
{{#tab name="Emitted C#" }}
```csharp
{{#include ../../snippets/type-system/variadics/forwarding.cs}}
```
{{#endtab }}
{{#endtabs }}

This is well-typed because `f`'s parameter list IS the pack `TArgs`, so the spread provably matches for every instantiation.

A spread into a fixed-arity destination that is not provably compatible is rejected:

```mist
int Bad<params TArgs>(params TArgs args) {
    return Sum3(args...);   // MIST_VAR_0011: Sum3 wants (int, int, int)
}
```

Value-pack narrowing recovers fixed-arity forwarding within a guarded branch:

```mist
int Categorize<params TArgs>(params TArgs args) {
    if (args is (int, int, int) triple) {
        return Sum3(triple...);        // proven shape here
    } else if (args is (string, string) pair) {
        return Join(pair...).Length;
    } else {
        return TArgs.Length;
    }
}
```

### General Tuple Decomposition

The `...` operator is not pack-specific. It decomposes any tuple:

{{#tabs }}
{{#tab name="Mist" }}
```mist
{{#include ../../snippets/type-system/variadics/tuple-decomposition.mist}}
```
{{#endtab }}
{{#tab name="Emitted C#" }}
```csharp
{{#include ../../snippets/type-system/variadics/tuple-decomposition.cs}}
```
{{#endtab }}
{{#endtabs }}

## Spread-Generated Members

A spread in a member-declaring position generates an indexed family of members:

{{#tabs }}
{{#tab name="Mist" }}
```mist
{{#include ../../snippets/type-system/variadics/spread-members.mist}}
```
{{#endtab }}
{{#tab name="Emitted C#" }}
```csharp
{{#include ../../snippets/type-system/variadics/spread-members.cs}}
```
{{#endtab }}
{{#endtabs }}

This surfaces:

- `bundle.Item` -- the whole `(TArgs...)` tuple (the aggregate).
- `bundle.Item1` through `bundle.ItemN` -- individual elements (1-based projections).

The declaration's accessibility and member kind (field or property) apply to both the aggregate and its projections. A zip constructor (`Item... = values...`) populates spread members in parallel. Re-spreading the generated members (`Item...`) feeds them into a function.

## Whole-Pack Constraints

A whole-pack constraint applies elementwise to every element:

{{#tabs }}
{{#tab name="Mist" }}
```mist
{{#include ../../snippets/type-system/variadics/whole-pack-constraint.mist}}
```
{{#endtab }}
{{#tab name="Emitted C#" }}
```csharp
{{#include ../../snippets/type-system/variadics/whole-pack-constraint.cs}}
```
{{#endtab }}
{{#endtabs }}

Every element type must satisfy the constraint. An empty pack vacuously satisfies it.

## Value-Level `params`

The homogeneous sibling of a type pack is C#-style `params`:

{{#tabs }}
{{#tab name="Mist" }}
```mist
{{#include ../../snippets/type-system/variadics/value-level-params.mist}}
```
{{#endtab }}
{{#tab name="Emitted C#" }}
```csharp
{{#include ../../snippets/type-system/variadics/value-level-params.cs}}
```
{{#endtab }}
{{#endtabs }}

This is the same `params` keyword, but targeting a homogeneous array (`T[]`) rather than a heterogeneous tuple. The callee receives a real array; the call site is flat.

At most one `params` is allowed per value-parameter list, whether it targets a tuple or an array.

## Call-Site Inference

The compiler infers a type pack from value arguments:

{{#tabs }}
{{#tab name="Mist" }}
```mist
{{#include ../../snippets/type-system/variadics/inference.mist}}
```
{{#endtab }}
{{#tab name="Emitted C#" }}
```csharp
{{#include ../../snippets/type-system/variadics/inference.cs}}
```
{{#endtab }}
{{#endtabs }}

Inference solves fixed scalar type parameters alongside the pack using the same binding rule.

## Notable Variadic Types

The standard library uses variadics for types that naturally span multiple arities.

### `Func` and `Action`

`Func` is variadic over its input parameters, with a fixed return type in the trailing slot:

```mist
intrinsic public class Func<params TArgs, TResult> { }
```

`Action` is a type alias that spreads its pack into `Func` with a `void` return:

```mist
type Action<params TArgs> = Func<TArgs..., void>;
```

Usage:

```mist
Func<int, int, bool> isLess;      // TArgs=[int, int], TResult=bool
Func<string> getName;             // TArgs=[], TResult=string
Action<int, string> log;          // resolves to Func<int, string, void>
```

A `Func<params TArgs, TResult>` value is well-typed to invoke when the argument list matches `TArgs` elementwise and the result type is `TResult`.

## Declaration Kinds

A pack type-parameter list is permitted on every generic-capable declaration kind:

{{#tabs }}
{{#tab name="Mist" }}
```mist
{{#include ../../snippets/type-system/variadics/declaration-kinds.mist}}
```
{{#endtab }}
{{#tab name="Emitted C#" }}
```csharp
{{#include ../../snippets/type-system/variadics/declaration-kinds.cs}}
```
{{#endtab }}
{{#endtabs }}

The same rules apply uniformly: binding, arity-in-slots, and one `params` per list.

## Backend Arity Ceiling

The Mist language imposes no limit on pack length. The front end (grammar, model builder, type checker) accepts packs of any size. Only the C# backend has target-imposed limits:

- `System.Func` / `System.Action` cap at 16 input parameters.
- `System.ValueTuple` caps at 7 elements before nesting (the backend auto-nests beyond 7).

When a construction exceeds the target capacity, the backend emits `MIST_VAR_0010`. This is a backend concern, not a language limit.
