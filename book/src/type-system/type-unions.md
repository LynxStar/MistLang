# Type Unions

Type unions in Mist allow a variable, parameter, or return type to hold a value of one of several types. You declare a union type using the `|` operator between constituent types. The compiler emits a discriminated union helper struct that wraps the value with a tag indicating which type it currently holds, along with implicit conversion operators for seamless assignment.

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

The compiler generates a `Union_Int32_String` struct with a tag field, `IsInt32`/`IsString` properties, `AsInt32`/`AsString` accessors, and implicit conversion operators from both `int` and `string`. The constituent names in the helper type are sorted alphabetically.

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

Parameters and return types both emit as the union helper struct. Callers can pass any constituent type directly thanks to the implicit conversion operators on the struct.

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

In Mist, `value is int n` reads naturally — "if value is an int, bind it to n." The compiler emits this as a check on the union's `IsInt32` property followed by extracting the value via `AsInt32`. Inside the `if` block, `n` is a plain `int` — the union has been narrowed away.

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

The helper struct name includes all constituent types sorted alphabetically — `int | string | bool` becomes `Union_Boolean_Int32_String`. The struct gains `Is` and `As` properties for each constituent type, and pattern matching works identically regardless of how many types are in the union.

## Reassignment

A union-typed variable can be reassigned from one constituent type to another. The implicit conversion operators on the helper struct handle the type transition.

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

Reassigning `value = "switched"` invokes the implicit conversion from `string` to `Union_Int32_String`, updating the internal tag and stored value. After reassignment, pattern matching confirms the new type.
