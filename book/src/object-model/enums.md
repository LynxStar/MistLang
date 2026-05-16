# Enums

Enums in Mist define named constant sets with an underlying type. Unlike C# enums which are limited to integral types, Mist enums can have any readonly type as their underlying type — including strings, structs, classes, and even type unions. The compiler emits each enum as a `record class` with `public static readonly` fields for each member and an implicit conversion operator to the underlying type.

## Typed Enums

An enum with an explicit underlying type annotation declares the type after a colon. Each member specifies its value after a colon separator.

{{#tabs }}
{{#tab name="Mist" }}
```mist
{{#include ../../snippets/object-model/enums/typed.mist}}
```
{{#endtab }}
{{#tab name="Emitted C#" }}
```csharp
{{#include ../../snippets/object-model/enums/typed.cs}}
```
{{#endtab }}
{{#endtabs }}

The enum emits as a `record class` with a single `Value` property of the underlying type. Each member becomes a `public static readonly` field initialized with the specified value. The implicit operator allows assigning an enum value directly to a variable of the underlying type.

## Name-Only Enums

When no underlying type is specified and members have no explicit values, the enum uses `byte` as its underlying type and auto-assigns sequential values starting from 0.

{{#tabs }}
{{#tab name="Mist" }}
```mist
{{#include ../../snippets/object-model/enums/name-only.mist}}
```
{{#endtab }}
{{#tab name="Emitted C#" }}
```csharp
{{#include ../../snippets/object-model/enums/name-only.cs}}
```
{{#endtab }}
{{#endtabs }}

Name-only enums are the simplest form — you list the member names and the compiler handles the rest. The auto-assigned values are sequential bytes starting at zero.

## Mixed Explicit and Implicit Values

An enum can mix members with explicit values and members without. Members without a value increment from the last explicit value.

{{#tabs }}
{{#tab name="Mist" }}
```mist
{{#include ../../snippets/object-model/enums/mixed-values.mist}}
```
{{#endtab }}
{{#tab name="Emitted C#" }}
```csharp
{{#include ../../snippets/object-model/enums/mixed-values.cs}}
```
{{#endtab }}
{{#endtabs }}

`NotFound` has no explicit value, so it receives 401 — one more than the preceding `BadRequest` (400). This mirrors the auto-increment behavior familiar from C-style enums.

## Union Underlying Types

The underlying type can be a type union, allowing members to hold values of different types within the same enum.

{{#tabs }}
{{#tab name="Mist" }}
```mist
{{#include ../../snippets/object-model/enums/union-underlying.mist}}
```
{{#endtab }}
{{#tab name="Emitted C#" }}
```csharp
{{#include ../../snippets/object-model/enums/union-underlying.cs}}
```
{{#endtab }}
{{#endtabs }}

When the underlying type is a union like `int | string`, the emitted `Value` property uses the compiler's discriminated union struct (`Union_Int32_String`). Each member's value is implicitly converted into the union type. We'll cover type unions in detail in the [Type Unions](../type-system/type-unions.md) chapter.

## User-Defined Underlying Types

Enums can use user-defined structs or classes as their underlying type, provided the type is `readonly`. Each member's value is an instance of that type.

### Struct Underlying Type

{{#tabs }}
{{#tab name="Mist" }}
```mist
{{#include ../../snippets/object-model/enums/struct-underlying.mist}}
```
{{#endtab }}
{{#tab name="Emitted C#" }}
```csharp
{{#include ../../snippets/object-model/enums/struct-underlying.cs}}
```
{{#endtab }}
{{#endtabs }}

### Class Underlying Type

{{#tabs }}
{{#tab name="Mist" }}
```mist
{{#include ../../snippets/object-model/enums/class-underlying.mist}}
```
{{#endtab }}
{{#tab name="Emitted C#" }}
```csharp
{{#include ../../snippets/object-model/enums/class-underlying.cs}}
```
{{#endtab }}
{{#endtabs }}

The underlying type must be `readonly` — this ensures enum values are immutable after construction. Primitive types (`int`, `string`, `byte`, etc.) are inherently readonly and always accepted. User-defined types must be explicitly marked `readonly`.

## Implicit Conversion

Enum values implicitly convert to their underlying type. You can assign an enum member directly to a variable of the underlying type without a cast.

{{#tabs }}
{{#tab name="Mist" }}
```mist
{{#include ../../snippets/object-model/enums/implicit-conversion.mist}}
```
{{#endtab }}
{{#tab name="Emitted C#" }}
```csharp
{{#include ../../snippets/object-model/enums/implicit-conversion.cs}}
```
{{#endtab }}
{{#endtabs }}

The implicit conversion operator emitted on the record class (`public static implicit operator T(EnumName e) => e.Value;`) makes this seamless. The enum value's `Value` property is extracted automatically wherever the underlying type is expected.

## Pattern Matching

Enum values support pattern matching with the `is` keyword. The compiler emits an equality comparison against the enum member.

{{#tabs }}
{{#tab name="Mist" }}
```mist
{{#include ../../snippets/object-model/enums/pattern-matching.mist}}
```
{{#endtab }}
{{#tab name="Emitted C#" }}
```csharp
{{#include ../../snippets/object-model/enums/pattern-matching.cs}}
```
{{#endtab }}
{{#endtabs }}

In Mist, you write `d is Direction.North` to test if an enum variable matches a specific member. The compiler emits this as a value equality check (`d == Direction.North`) rather than C#'s `is` pattern, since enums are record classes with built-in equality semantics.
