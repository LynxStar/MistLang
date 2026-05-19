# Arrays

Arrays in Mist are fixed-size, bounds-checked collections. Unlike many languages where arrays are simple contiguous buffers with unchecked access, Mist arrays carry their size in the type system and the compiler statically proves that every index access is within bounds.

## The Array Type

The underlying array type is `Array<T, Length>` from the Standard Library, where `Length` is a dependent type parameter constrained to be a non-negative integer. In practice you'll use shorthand syntax:

| Syntax | Meaning |
|--------|---------|
| `int[5]` | `Array<int, 5>` — an array of exactly 5 ints |
| `int[]` | `Array<int, unknown>` — an array of ints whose length is not statically known |
| `var arr = new int[5]` | Compiler infers `int[5]` — the size is known |
| `var arr = [1, 2, 3]` | Compiler infers `int[3]` from the literal |

The `T[]` form is shorthand for `Array<T, unknown>` — the length exists but isn't statically known in this context. The `T[N]` form tracks the exact size at compile time.

## Bounds Checking

Array indexing uses a refinement type called `ArrayBounds` defined as `>= 0 and < Length`. The compiler must prove that any index expression satisfies this constraint at compile time. If it can't, the code won't compile.

This means:
- Literal indices on known-size arrays are checked at compile time
- Loop induction variables bounded by `.Length` are proven safe automatically
- Arbitrary expressions that can't be proven safe are rejected

The details of how this works are covered in the [Refinement Types](../type-system/refinement-types.md) and [Dependent Types](../type-system/dependent-types.md) chapters. For practical purposes: use standard `for` loop patterns with `.Length` and the compiler handles the rest.

## Creating Arrays

### With `new`

Create an array of a specific size using `new T[size]`:

```mist
var numbers = new int[5];    // int[5] — five zeros
var names = new string[3];   // string[3] — three nulls
```

### With Array Literals

Array literals use bracket syntax. The compiler infers the element type and size:

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

### Empty Arrays

An empty array literal `[]` requires an explicit type annotation since the compiler can't infer the element type from zero elements. Use `T[0]` to express that the size is known to be zero:

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

### Nested Arrays

Array literals can be nested to create jagged arrays:

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

### Explicit Type Annotation

When you declare the variable with an explicit sized array type, the compiler uses that type information in the emitted code. Use `T[N]` when the size is known:

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

### Trailing Comma

A trailing comma after the last element is permitted for formatting convenience:

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

## Passing Arrays

### As Method Arguments

Array literals can be passed directly at call sites:

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

### As Return Values

Methods can return array literals directly:

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

## Indexing and Iteration

Access elements by index. The compiler verifies bounds statically:

```mist
var items = [10, 20, 30];
int first = items[0];   // proven safe: 0 >= 0 and < 3
int last = items[2];    // proven safe: 2 >= 0 and < 3
```

Iterate with a standard `for` loop — the compiler recognizes `i < arr.Length` as proving bounds:

```mist
var data = new int[4];
for (int i = 0; i < data.Length; i = i + 1)
{
    data[i] = i * 10;  // proven safe: i >= 0 and < Length
}
```

When an array is passed as a parameter with unknown length (`int[]`), the same pattern works — the compiler uses symbolic reasoning to prove `i < items.Length` satisfies the bounds constraint for `items[i]`:

```mist
public static int Sum(int[] items)
{
    int total = 0;
    for (int i = 0; i < items.Length; i = i + 1)
    {
        total = total + items[i];  // symbolic tautology: i < Length proves bounds
    }
    return total;
}
```
