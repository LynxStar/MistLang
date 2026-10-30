# The Unknown Type

The `unknown` type is Mist's top type — every value of every type is assignable to a variable of type `unknown`. In exchange for this universal acceptance, `unknown` forbids all operations: you cannot call methods, access properties, perform arithmetic, or do anything else with an `unknown` value until you narrow it to a concrete type. This makes `unknown` the safe counterpart to unchecked casts — it forces you to prove what a value is before you use it.

In the emitted C#, a bare `unknown` variable lowers to `object`. The compiler rejects any member access on the `unknown` receiver at compile time, so no runtime dispatch is needed.

## Assigning to Unknown

Any value of any type can be assigned to an `unknown` variable. The variable can be reassigned to values of different types over its lifetime — the compiler accepts every assignment because `unknown` is the top type.

{{#tabs }}
{{#tab name="Mist" }}
```mist
{{#include ../../snippets/advanced/unknown-type/assigning.mist}}
```
{{#endtab }}
{{#tab name="Emitted C#" }}
```csharp
{{#include ../../snippets/advanced/unknown-type/assigning.cs}}
```
{{#endtab }}
{{#endtabs }}

No operations are permitted on `u` beyond assignment and passing it to methods that accept `unknown` or `object`. Attempting to call a method or access a property on an `unknown` value produces a compile-time error.

## Unknown as a Parameter Type

A method can accept `unknown` as a parameter type, allowing callers to pass any value. Inside the method body, the parameter is opaque until narrowed.

{{#tabs }}
{{#tab name="Mist" }}
```mist
{{#include ../../snippets/advanced/unknown-type/parameter.mist}}
```
{{#endtab }}
{{#tab name="Emitted C#" }}
```csharp
{{#include ../../snippets/advanced/unknown-type/parameter.cs}}
```
{{#endtab }}
{{#endtabs }}

The `unknown` parameter lowers to `object` in C#. The caller can pass any type without an explicit cast, and the callee must narrow before using the value.

## Narrowing via Pattern Matching

The `is` keyword narrows an `unknown` value to a concrete type. After a successful `is` check, the bound variable has the narrowed type within the guarded scope, and all operations on that type become available.

{{#tabs }}
{{#tab name="Mist" }}
```mist
{{#include ../../snippets/advanced/unknown-type/narrowing.mist}}
```
{{#endtab }}
{{#tab name="Emitted C#" }}
```csharp
{{#include ../../snippets/advanced/unknown-type/narrowing.cs}}
```
{{#endtab }}
{{#endtabs }}

Inside the `if` branch, `narrowed` is typed `int` — not `unknown` — so arithmetic and all `int` operations resolve normally. Outside the branch, `maybeInt` remains `unknown`.

You can chain multiple `is` checks to handle different possible types:

```mist
{{#include ../../snippets/advanced/unknown-type/chained-checks.mist}}
```

Each branch narrows independently — the compiler tracks the narrowed type within each guarded scope.

## Unknown as a Tuple Element Type

A tuple can contain `unknown` as an element type, creating a container where some positions hold values of indeterminate type. Each element can be narrowed independently via pattern matching.

{{#tabs }}
{{#tab name="Mist" }}
```mist
{{#include ../../snippets/advanced/unknown-type/tuple-element.mist}}
```
{{#endtab }}
{{#tab name="Emitted C#" }}
```csharp
{{#include ../../snippets/advanced/unknown-type/tuple-element.cs}}
```
{{#endtab }}
{{#endtabs }}

The `tag` element is `int` and can be used directly. The `payload` element is `unknown` (lowered to `object`) and must be narrowed before any operations are available. This pattern is useful for tagged-union-style envelopes where the payload type varies.

Deconstructing a tuple with an `unknown` element works the same way — the deconstructed binding receives the `unknown` type and must be narrowed:

{{#tabs }}
{{#tab name="Mist" }}
```mist
{{#include ../../snippets/advanced/unknown-type/deconstruction.mist}}
```
{{#endtab }}
{{#tab name="Emitted C#" }}
```csharp
{{#include ../../snippets/advanced/unknown-type/deconstruction.cs}}
```
{{#endtab }}
{{#endtabs }}

After deconstruction, `num` is `int` (usable immediately) and `data` is `unknown` (requires narrowing). Each element is independent — narrowing one does not affect the other.

## Widening a Generic Container

A class generic widens to the same generic closed with `unknown`: a `MyList<int>` is assignable to a `MyList<unknown>`, with no cast, because every element it holds is a value of `unknown`. The widened view is a *view* of the original object, so it can read but must not store — storing an `unknown` into storage the runtime object types as `int` would corrupt it.

The compiler keeps that sound by classifying every member of a generic type by the storage it writes, transitively through the members it calls. On a receiver closed with `unknown`, any member whose write reaches storage typed by a parameter bound to `unknown` is not a member at that receiver: calling it, assigning it, or reading a getter that writes is refused with `MIST0395`, which names the member, the receiver type and the type parameter. Members that only read stay available, and their results come back as `unknown`. Hiding removes a writer from the candidates rather than refusing the name, so an overload of the same name that writes nothing is still selected.

{{#tabs }}
{{#tab name="Mist" }}
```mist
{{#include ../../snippets/advanced/unknown-type/widening.mist}}
```
{{#endtab }}
{{#tab name="Emitted C#" }}
```csharp
{{#include ../../snippets/advanced/unknown-type/widening.cs}}
```
{{#endtab }}
{{#endtabs }}

Here `Size()` and `GetAt(0)` are available on `widened`, while `widened.Add(99)` or `widened.item0 = 99` would be refused: `Add` writes `item0`, whose type is the parameter `T`, and the receiver closes `T` with `unknown`. Narrowing back to `MyList<int>` with `is` restores the whole type, `Add` included. The view is lowered to C# `dynamic`, which dispatches each admitted member against the runtime object.

Two more things follow from a closed-with-`unknown` type being only a view. It cannot be constructed: `new MyList<unknown>()` is refused with `MIST0203`, because the storage it would create would be typed `unknown`. And it is not a meaningful narrowing target from a value that already widens to it: `ints is MyList<unknown> view` is refused with `MIST0195`, since the test is always true and the binding only hides members. Testing a bare `unknown` for `MyList<unknown>` remains a real question and is allowed.
