# Definite Assignment

Mist enforces a simple rule: every storage location must hold a real value before it can be read. There is no `null`, no uninitialized memory, no zero-filled slot that silently pretends to be valid. The compiler proves at compile time that every field, local variable, and array element has been definitely assigned before any code can observe it.

This guarantee eliminates an entire class of bugs — null reference exceptions, uninitialized field reads, and arrays with "empty" slots — by making them compilation errors instead of runtime surprises.

## Field Assignment in Constructors

A constructor must assign every instance field on all control-flow paths before the constructor completes, and before any code reads the field. Reading a field that hasn't been assigned yet is a compile-time error:

```
error MIST_FIELD_READ_BEFORE_ASSIGNMENT: Field 'Name' is read before it is
      definitely assigned in constructor 'Connection(string, int)'
```

Fields with explicit initializers or whose types are defaultable (and thus receive a synthetic default) are considered pre-assigned at the start of the constructor body.

The following example shows a constructor that assigns all fields before they are read — this compiles cleanly:

{{#tabs }}
{{#tab name="Mist" }}
```mist
{{#include ../../snippets/type-system/definite-assignment/field-assignment.mist}}
```
{{#endtab }}
{{#tab name="Emitted C#" }}
```csharp
{{#include ../../snippets/type-system/definite-assignment/field-assignment.cs}}
```
{{#endtab }}
{{#endtabs }}

The compiler tracks assignments through branches: if a field is assigned on every branch of a conditional, it is considered definitely assigned after the merge point. If assigned on only some branches, reading it after the merge is an error.

## Local Variable Read-Before-Assignment

The same rule applies to local variables. A local declared without an initializer must be assigned before it can be read:

```
error MIST_LOCAL_READ_BEFORE_ASSIGNMENT: Local variable 'result' is read
      before it is definitely assigned
```

Locals declared with an initializer (`var x = expr`) are immediately considered assigned. For locals without initializers, the compiler tracks assignments through branches and loops, using the same conservative analysis as fields — a variable assigned only inside a loop body is not considered assigned after the loop (the body may not execute).

Compound assignment (`x += 1`) counts as a read followed by a write, so using it on an unassigned local is also an error.

## Stampable Types and Bare Array Creation

When you write `new T[size]`, the question is: what value goes in each slot? For types whose default value is a flat, copyable bit pattern — primitives like `int` and pure structs whose fields are all primitives or other pure structs — the answer is straightforward: the CLR's zero-fill produces a valid default. These types are called **stampable**.

A stampable type is one where replicating the default into N slots is a zero-cost bitwise operation with no hidden constructor calls, no heap allocation, and no aliasing concerns. Bare array creation `new T[size]` is restricted to stampable element types:

{{#tabs }}
{{#tab name="Mist" }}
```mist
{{#include ../../snippets/type-system/definite-assignment/bare-array-stampable.mist}}
```
{{#endtab }}
{{#tab name="Emitted C#" }}
```csharp
{{#include ../../snippets/type-system/definite-assignment/bare-array-stampable.cs}}
```
{{#endtab }}
{{#endtabs }}

Attempting `new T[size]` with a non-stampable type (any class, or a struct containing a reference-typed or type-parameter field) produces a compile-time error:

```
error MIST_ARRAY_REQUIRES_INITIALIZER: Cannot create a bare array of 'Widget'
      because it is a reference type and is not stampable. Use initializer-based
      construction: new Array<Widget, N>(i => ...)
```

## Initializer-Based Array Construction

For non-stampable types — classes, structs with reference fields, or any type where zero-filling would produce an invalid value — Mist provides an explicit initializer constructor. You supply an index→value function that is called once per slot:

{{#tabs }}
{{#tab name="Mist" }}
```mist
{{#include ../../snippets/type-system/definite-assignment/initializer-construction.mist}}
```
{{#endtab }}
{{#tab name="Emitted C#" }}
```csharp
{{#include ../../snippets/type-system/definite-assignment/initializer-construction.cs}}
```
{{#endtab }}
{{#endtabs }}

The function receives the index `i` and returns the value for that slot. The compiler emits a loop that invokes the function for every index in `[0, size)`, ensuring every element holds a real value before the array is observable. The per-element cost is visible in your source — no hidden factory calls.

This form accepts any element type, whether stampable or not.

## Array Literals

Array literals (`[a, b, c]`) are always valid for any element type, since every slot is explicitly provided by the programmer:

{{#tabs }}
{{#tab name="Mist" }}
```mist
{{#include ../../snippets/type-system/definite-assignment/array-literal-reference.mist}}
```
{{#endtab }}
{{#tab name="Emitted C#" }}
```csharp
{{#include ../../snippets/type-system/definite-assignment/array-literal-reference.cs}}
```
{{#endtab }}
{{#endtabs }}

When the number of elements is known upfront, array literals are the simplest way to construct an array of any type.

## The Soundness Guarantee

Together, these rules form a closed system: no array element, field, or local variable can ever be observed in an uninitialized state. The construction paths are:

| Construction Form | Element Restriction | Guarantee |
|---|---|---|
| `new T[size]` | Stampable types only | Every slot holds the valid zero/stamp default |
| `new Array<T, Size>(fn)` | Any type | Every slot holds `fn(i)` — a real value |
| `[a, b, c]` | Any type | Every slot is explicitly provided |

There is no path that yields a `null` reference, a zeroed-but-invalid struct, or an uninitialized slot. If you can read it, it has been assigned.
