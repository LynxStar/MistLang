# Array Literals

Mist provides a concise bracket syntax for creating arrays inline. Instead of writing `new int[] { 1, 2, 3 }`, you write `[1, 2, 3]` and let the compiler handle the allocation. The syntax works with type inference, explicit type annotations, nested arrays, and anywhere an array value is expected — arguments, return values, and assignments.

## Bracket Syntax with `var`

The simplest way to create an array is with bracket syntax and `var` inference. The compiler determines the element type from the values you provide.

{{#tabs }}
{{#tab name="Mist" }}
```mist
{{#include ../../snippets/basics/array-literals/bracket-syntax.mist}}
```
{{#endtab }}
{{#tab name="Emitted C#" }}
```csharp
{{#include ../../snippets/basics/array-literals/bracket-syntax.cs}}
```
{{#endtab }}
{{#endtabs }}

When you use `var`, the compiler infers the array type from the elements. `[1, 2, 3]` becomes an `int[]`, `["Alice", "Bob", "Charlie"]` becomes a `string[]`, and so on. The emitted C# uses `new[]` with an initializer list.

## Empty Arrays with Explicit Type

An empty array literal `[]` requires an explicit type annotation on the variable — the compiler can't infer the element type from zero elements.

{{#tabs }}
{{#tab name="Mist" }}
```mist
{{#include ../../snippets/basics/array-literals/empty-arrays.mist}}
```
{{#endtab }}
{{#tab name="Emitted C#" }}
```csharp
{{#include ../../snippets/basics/array-literals/empty-arrays.cs}}
```
{{#endtab }}
{{#endtabs }}

The compiler emits `Array.Empty<T>()` for empty array literals, which returns a cached empty array instance rather than allocating a new one each time.

## Nested Arrays

Array literals can be nested to create multi-dimensional jagged arrays. Each inner bracket creates a sub-array.

{{#tabs }}
{{#tab name="Mist" }}
```mist
{{#include ../../snippets/basics/array-literals/nested.mist}}
```
{{#endtab }}
{{#tab name="Emitted C#" }}
```csharp
{{#include ../../snippets/basics/array-literals/nested.cs}}
```
{{#endtab }}
{{#endtabs }}

The type of `matrix` is `int[][]` — an array of arrays. Each inner `[1, 2]` becomes its own `new[] { 1, 2 }` in the emitted C#.

## Arrays as Method Arguments

You can pass an array literal directly at a call site without assigning it to a variable first.

{{#tabs }}
{{#tab name="Mist" }}
```mist
{{#include ../../snippets/basics/array-literals/method-arguments.mist}}
```
{{#endtab }}
{{#tab name="Emitted C#" }}
```csharp
{{#include ../../snippets/basics/array-literals/method-arguments.cs}}
```
{{#endtab }}
{{#endtabs }}

The array literal is constructed at the call site and passed directly to the parameter.

## Arrays as Return Values

A method with an array return type can return an array literal directly.

{{#tabs }}
{{#tab name="Mist" }}
```mist
{{#include ../../snippets/basics/array-literals/return-values.mist}}
```
{{#endtab }}
{{#tab name="Emitted C#" }}
```csharp
{{#include ../../snippets/basics/array-literals/return-values.cs}}
```
{{#endtab }}
{{#endtabs }}

The compiler emits `new[]` for the return expression, letting C# infer the array type from the method's declared return type.

## Trailing Comma

Mist permits a trailing comma after the last element in an array literal. This is purely a convenience for formatting — it has no effect on the resulting array.

{{#tabs }}
{{#tab name="Mist" }}
```mist
{{#include ../../snippets/basics/array-literals/trailing-comma.mist}}
```
{{#endtab }}
{{#tab name="Emitted C#" }}
```csharp
{{#include ../../snippets/basics/array-literals/trailing-comma.cs}}
```
{{#endtab }}
{{#endtabs }}

Trailing commas make it easier to add or remove elements without touching adjacent lines — useful in version control diffs.

## Explicit Type Annotation

When you declare the variable with an explicit array type, the compiler uses that type information in the emitted code.

{{#tabs }}
{{#tab name="Mist" }}
```mist
{{#include ../../snippets/basics/array-literals/explicit-type.mist}}
```
{{#endtab }}
{{#tab name="Emitted C#" }}
```csharp
{{#include ../../snippets/basics/array-literals/explicit-type.cs}}
```
{{#endtab }}
{{#endtabs }}

With an explicit type annotation, the emitted C# uses `new T[] { ... }` instead of `new[] { ... }`. Both forms produce the same runtime result, but the explicit form makes the element type visible in the generated code.
