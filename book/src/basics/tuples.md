# Tuples

Tuples let you group multiple values into a single lightweight value without defining a class or struct. They are useful for returning multiple results from a function, passing related values together, or any situation where a full type definition would be overkill.

Mist tuples are value types that lower to C# `ValueTuple`. This means they are stack-allocated, copied on assignment, and compared by value — the same semantics as `int` or `bool`.

## Positional Tuples

The simplest tuple form groups values by position. Declare the type as `(Type1, Type2)` and access elements via `Item1`, `Item2`, and so on.

{{#tabs }}
{{#tab name="Mist" }}
```mist
{{#include ../../snippets/basics/tuples/positional.mist}}
```
{{#endtab }}
{{#tab name="Emitted C#" }}
```csharp
{{#include ../../snippets/basics/tuples/positional.cs}}
```
{{#endtab }}
{{#endtabs }}

Positional access works for tuples of any arity — a three-element tuple has `Item1`, `Item2`, and `Item3`.

## Type Inference

You can use `var` and the compiler infers the full tuple type from the elements:

```mist
var foo = (8, -93.2934, "banana", false);
// inferred type: (int, double, string, bool)

Console.WriteLine(foo.Item1);  // 8
Console.WriteLine(foo.Item3);  // banana
```

This works with any combination of element types — the compiler determines each element's type independently.

## Named Tuples

You can give each element a name in the type declaration. Named elements are accessible by their declared name and by their positional name (`Item1`, `Item2`, etc.).

{{#tabs }}
{{#tab name="Mist" }}
```mist
{{#include ../../snippets/basics/tuples/named.mist}}
```
{{#endtab }}
{{#tab name="Emitted C#" }}
```csharp
{{#include ../../snippets/basics/tuples/named.cs}}
```
{{#endtab }}
{{#endtabs }}

Named tuples improve readability when the meaning of each element isn't obvious from context. The names exist at compile time only — at runtime, the tuple is still a `ValueTuple` with positional fields.

## Tuple Deconstruction

Deconstruction unpacks a tuple's elements into individual variables in a single statement. Mist supports two forms.

### The `var` Form

Use `var (name1, name2) = tupleExpr;` to deconstruct with inferred types:

{{#tabs }}
{{#tab name="Mist" }}
```mist
{{#include ../../snippets/basics/tuples/deconstruction-var.mist}}
```
{{#endtab }}
{{#tab name="Emitted C#" }}
```csharp
{{#include ../../snippets/basics/tuples/deconstruction-var.cs}}
```
{{#endtab }}
{{#endtabs }}

### The Typed Form

Use `(Type1 name1, Type2 name2) = tupleExpr;` to deconstruct with explicit types:

{{#tabs }}
{{#tab name="Mist" }}
```mist
{{#include ../../snippets/basics/tuples/deconstruction-typed.mist}}
```
{{#endtab }}
{{#tab name="Emitted C#" }}
```csharp
{{#include ../../snippets/basics/tuples/deconstruction-typed.cs}}
```
{{#endtab }}
{{#endtabs }}

Both forms create individual variables rather than a single tuple variable. The typed form is useful when you want to document the expected types explicitly or when the compiler can't infer them from context.

## Nested Tuples

A tuple element can itself be a tuple, allowing you to represent structured data at arbitrary depth. Access nested elements by chaining positional names.

{{#tabs }}
{{#tab name="Mist" }}
```mist
{{#include ../../snippets/basics/tuples/nested.mist}}
```
{{#endtab }}
{{#tab name="Emitted C#" }}
```csharp
{{#include ../../snippets/basics/tuples/nested.cs}}
```
{{#endtab }}
{{#endtabs }}

Nested tuples are useful for grouping related coordinates, key-value pairs with composite keys, or any situation where a flat tuple would lose structural meaning.
