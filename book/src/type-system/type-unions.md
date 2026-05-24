# Type Unions

Type unions allow a variable, parameter, or return type to hold a value of one of several types. You declare a union type using the `|` operator between constituent types.

## Union Type Syntax

A union type is written as two or more types separated by `|`. The resulting type can hold a value of any of the listed types.

{{#tabs }}
{{#tab name="Mist" }}
```mist
{{#include ../../snippets/type-system/type-unions/syntax.mist}}
```
{{#endtab }}
{{#tab name="Emitted C#" }}
```csharp
{{#include ../../snippets/type-system/type-unions/syntax.cs}}
```
{{#endtab }}
{{#endtabs }}

## Union-Typed Parameters and Return Types

Union types work as parameter types and return types, allowing functions to accept or produce values of multiple types.

{{#tabs }}
{{#tab name="Mist" }}
```mist
{{#include ../../snippets/type-system/type-unions/params-returns.mist}}
```
{{#endtab }}
{{#tab name="Emitted C#" }}
```csharp
{{#include ../../snippets/type-system/type-unions/params-returns.cs}}
```
{{#endtab }}
{{#endtabs }}

Callers can pass any constituent type directly — no explicit wrapping or conversion is needed.

## Union Type Aliases

A union type is a type expression, so you can bind it to a name with a type alias. This avoids repeating the full union everywhere you use it:

{{#tabs }}
{{#tab name="Mist" }}
```mist
{{#include ../../snippets/type-system/type-unions/alias.mist}}
```
{{#endtab }}
{{#tab name="Emitted C#" }}
```csharp
{{#include ../../snippets/type-system/type-unions/alias.cs}}
```
{{#endtab }}
{{#endtabs }}

`Result` is just a name for `int | string` — the alias is erased at compile time. This is especially useful when the same union appears in multiple method signatures or across files. See [Type Aliases](./type-aliases.md) for more on how aliases work.

## Narrowing via Pattern Matching

To access the underlying value of a union-typed variable, use `is` pattern matching. This narrows the type and binds the value to a new variable.

{{#tabs }}
{{#tab name="Mist" }}
```mist
{{#include ../../snippets/type-system/type-unions/narrowing.mist}}
```
{{#endtab }}
{{#tab name="Emitted C#" }}
```csharp
{{#include ../../snippets/type-system/type-unions/narrowing.cs}}
```
{{#endtab }}
{{#endtabs }}

`value is int n` reads naturally — "if value is an int, bind it to n." Inside the `if` block, `n` is a plain `int` — the union has been narrowed away.

## Multi-Type Unions

Unions are not limited to two types. You can combine three or more constituent types with additional `|` separators.

{{#tabs }}
{{#tab name="Mist" }}
```mist
{{#include ../../snippets/type-system/type-unions/multi-type.mist}}
```
{{#endtab }}
{{#tab name="Emitted C#" }}
```csharp
{{#include ../../snippets/type-system/type-unions/multi-type.cs}}
```
{{#endtab }}
{{#endtabs }}

Pattern matching works identically regardless of how many types are in the union.

## Reassignment

A union-typed variable can be reassigned from one constituent type to another freely.

{{#tabs }}
{{#tab name="Mist" }}
```mist
{{#include ../../snippets/type-system/type-unions/reassignment.mist}}
```
{{#endtab }}
{{#tab name="Emitted C#" }}
```csharp
{{#include ../../snippets/type-system/type-unions/reassignment.cs}}
```
{{#endtab }}
{{#endtabs }}

After reassignment, pattern matching reflects the new type.
