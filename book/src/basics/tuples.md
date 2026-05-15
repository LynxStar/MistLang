# Tuples

Tuples let you group multiple values into a single lightweight value without defining a class or struct. They are useful for returning multiple results from a function, passing related values together, or any situation where a full type definition would be overkill.

Mist tuples are value types that lower to C# `ValueTuple`. This means they are stack-allocated, copied on assignment, and compared by value — the same semantics as `int` or `bool`.

## Positional Tuples

The simplest tuple form groups values by position. Declare the type as `(Type1, Type2)` and access elements via `Item1`, `Item2`, and so on.

{{#tabs }}
{{#tab name="Mist" }}
```mist
(int, string) pair = (42, "forty-two");
Console.WriteLine($"pair.Item1={pair.Item1} pair.Item2={pair.Item2}");
```
{{#endtab }}
{{#tab name="Emitted C#" }}
```csharp
(int, string) pair = (42, "forty-two");
Console.WriteLine($"pair.Item1={pair.Item1} pair.Item2={pair.Item2}");
```
{{#endtab }}
{{#endtabs }}

Positional access works for tuples of any arity — a three-element tuple has `Item1`, `Item2`, and `Item3`.

## Named Tuples

You can give each element a name in the type declaration. Named elements are accessible by their declared name and by their positional name (`Item1`, `Item2`, etc.).

{{#tabs }}
{{#tab name="Mist" }}
```mist
(int x, int y) point = (x: 3, y: 4);
Console.WriteLine($"point.x={point.x} point.y={point.y}");
Console.WriteLine($"positional: Item1={point.Item1} Item2={point.Item2}");
```
{{#endtab }}
{{#tab name="Emitted C#" }}
```csharp
(int x, int y) point = (x: 3, y: 4);
Console.WriteLine($"point.x={point.x} point.y={point.y}");
Console.WriteLine($"positional: Item1={point.Item1} Item2={point.Item2}");
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
var (a, b) = (10, 20);
Console.WriteLine($"a={a} b={b}");
```
{{#endtab }}
{{#tab name="Emitted C#" }}
```csharp
var (a, b) = (10, 20);
Console.WriteLine($"a={a} b={b}");
```
{{#endtab }}
{{#endtabs }}

### The Typed Form

Use `(Type1 name1, Type2 name2) = tupleExpr;` to deconstruct with explicit types:

{{#tabs }}
{{#tab name="Mist" }}
```mist
(int u, int v) = (100, 200);
Console.WriteLine($"u={u} v={v}");
```
{{#endtab }}
{{#tab name="Emitted C#" }}
```csharp
(int u, int v) = (100, 200);
Console.WriteLine($"u={u} v={v}");
```
{{#endtab }}
{{#endtabs }}

Both forms create individual variables rather than a single tuple variable. The typed form is useful when you want to document the expected types explicitly or when the compiler can't infer them from context.

## Nested Tuples

A tuple element can itself be a tuple, allowing you to represent structured data at arbitrary depth. Access nested elements by chaining positional names.

{{#tabs }}
{{#tab name="Mist" }}
```mist
((int, int), string) nested = ((1, 2), "label");
Console.WriteLine(
    $"inner=({nested.Item1.Item1}, {nested.Item1.Item2}) outer={nested.Item2}");
```
{{#endtab }}
{{#tab name="Emitted C#" }}
```csharp
((int, int), string) nested = ((1, 2), "label");
Console.WriteLine(
    $"inner=({nested.Item1.Item1}, {nested.Item1.Item2}) outer={nested.Item2}");
```
{{#endtab }}
{{#endtabs }}

Nested tuples are useful for grouping related coordinates, key-value pairs with composite keys, or any situation where a flat tuple would lose structural meaning.
