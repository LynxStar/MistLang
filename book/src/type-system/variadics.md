# Variadics

> **Note:** The code examples in this chapter use inline code blocks because the full variadics emission pipeline is still maturing. They will be converted to compiled snippets (following the Book Snippet Policy) once the complete emission pipeline is stable.

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

```mist
class Foo<params TArgs> { }      // bracket-free construction sugar
class Foo<Type[]:TArgs> { }      // canonical explicit dependent form
```

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

```mist
int CountThenCall<params TArgs>(params TArgs args) {
    var n = TArgs.Length;      // .Length in expression position
    return Sink(args...);      // spread into argument slots
}
```

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

```mist
TResult Apply<params TArgs, TResult>(Func<TArgs..., TResult> f, params TArgs args) {
    return f(args...);   // forwards into f's parameter list
}
```

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

```mist
var pair = (10, 20);
var result = Add2(pair...);   // well-typed if Add2 takes (int, int)
```

## Spread-Generated Members

A spread in a member-declaring position generates an indexed family of members:

```mist
public class Bundle<params TArgs> {
    public TArgs... Item;   // aggregate Item : (TArgs...) + projections Item1..ItemN
}
```

This surfaces:

- `bundle.Item` -- the whole `(TArgs...)` tuple (the aggregate).
- `bundle.Item1` through `bundle.ItemN` -- individual elements (1-based projections).

The declaration's accessibility and member kind (field or property) apply to both the aggregate and its projections:

```mist
public class ReadOnlyRow<params TArgs> {
    public TArgs... Slot { get; }   // property projections Slot1..SlotN
}
```

A zip constructor populates spread members in parallel:

```mist
public Bundle(params TArgs values) {
    Item... = values...;   // zip: each Item{k} = values element k
}
```

Re-spreading the generated members feeds them into a function:

```mist
public TResult Map<TResult>(Func<TArgs..., TResult> f) {
    return f(Item...);   // re-spread the aggregate
}
```

## Whole-Pack Constraints

A whole-pack constraint applies elementwise to every element:

```mist
public struct ShapePair<Type[2]:TShapes> where TShapes : IShape {
    public TShapes... Shape;

    public int TotalArea() {
        return Shape1.Area() + Shape2.Area();   // IShape.Area() is callable
    }
}
```

Every element type must satisfy the constraint. An empty pack vacuously satisfies it.

## Value-Level `params`

The homogeneous sibling of a type pack is C#-style `params`:

```mist
void Log(params string[] parts) {
    // parts is a real string[]
}

Log("a", "b");   // flat call site
```

This is the same `params` keyword, but targeting a homogeneous array (`T[]`) rather than a heterogeneous tuple. The callee receives a real array; the call site is flat.

At most one `params` is allowed per value-parameter list, whether it targets a tuple or an array.

## Call-Site Inference

The compiler infers a type pack from value arguments:

```mist
int Describe<params TArgs>(params TArgs args) {
    return TArgs.Length;
}

Describe(1, "x", true);         // infers TArgs=[int, string, bool]
Describe<[int, string, bool]>(); // explicit form always available
```

Inference solves fixed scalar type parameters alongside the pack using the same binding rule.

## The `Func` / `Action` Rewrite

The motivating payoff: 34 hand-written declarations collapse to two:

```mist
// One declaration covers all arities
intrinsic public class Func<params TArgs, TResult> { }

// Action spreads the pack into Func, fixing the result to void
type Action<params TArgs> = Func<TArgs..., void>;
```

Usage reads exactly as before:

```mist
Func<int, int, bool> isLess;      // TArgs=[int, int], TResult=bool
Func<string> getName;             // TArgs=[], TResult=string
Action<int, string> log;          // resolves to Func<int, string, void>
```

Invocation uses a single generalized rule: a `Func<params TArgs, TResult>` value is well-typed to invoke when the argument list matches `TArgs` elementwise, and the result type is `TResult`. No per-arity recognition is retained.

## Declaration Kinds

A pack type-parameter list is permitted on every generic-capable declaration kind:

```mist
class Bundle<params TArgs> { }                            // variadic class
struct Pair<Type[2]:TArgs> where TArgs : IShape { }       // variadic struct
abstract IApplier<params TArgs, TResult> { }              // variadic abstract type
type Action<params TArgs> = Func<TArgs..., void>;         // variadic alias
type Row<params TArgs> = new (TArgs...);                  // variadic newtype
int Method<params TArgs>(params TArgs args) { }           // method-level pack
```

The same rules apply uniformly: binding, arity-in-slots, and one `params` per list.

### Variadic Abstract Types

A variadic abstract type can be implemented at a concrete arity or generically:

```mist
abstract ICountable<params TArgs> {
    int Count();
}

// Concrete arity: ICountable<int, string> requires Count() returning int
class Fixed : ICountable<int, string> {
    public int Count() { return 2; }
}

// Generic: spreads the implementer's own pack into the base-type list
class Tup<params TArgs> : ICountable<TArgs...> {
    public int Count() { return TArgs.Length; }
}
```

## Backend Arity Ceiling

The Mist language imposes no limit on pack length. The front end (grammar, model builder, type checker) accepts packs of any size. Only the C# backend has target-imposed limits:

- `System.Func` / `System.Action` cap at 16 input parameters.
- `System.ValueTuple` caps at 7 elements before nesting (the backend auto-nests beyond 7).

When a construction exceeds the target capacity, the backend emits `MIST_VAR_0010`. This is a backend concern, not a language limit.
