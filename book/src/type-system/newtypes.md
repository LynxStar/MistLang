# Newtypes

A newtype creates a distinct type that wraps an existing type. Unlike type aliases, which are transparent and interchangeable with their source type, a newtype is a separate type at compile time — you cannot accidentally pass a `UserId` where a plain `int` is expected, or vice versa. This gives you type safety for domain concepts without runtime overhead.

## Newtype Declaration

A newtype is declared with the `type` keyword, a name, and `= new SourceType;`. The source type can be any primitive, struct, or class.

{{#tabs }}
{{#tab name="Mist" }}
```mist
{{#include ../../snippets/type-system/newtypes/declaration.mist}}
```
{{#endtab }}
{{#tab name="Emitted C#" }}
```csharp
{{#include ../../snippets/type-system/newtypes/declaration.cs}}
```
{{#endtab }}
{{#endtabs }}

Each newtype wrapping a primitive emits as a `readonly struct` with a `Value` field holding the wrapped value. The compiler generates explicit cast operators in both directions — from the source type to the newtype and back — so conversions are always intentional.

## Wrapping Structs

When a newtype wraps a struct, the emitted type reproduces the struct's constructor and public members directly. This means you construct the newtype the same way you'd construct the underlying struct.

{{#tabs }}
{{#tab name="Mist" }}
```mist
{{#include ../../snippets/type-system/newtypes/wrapping-structs.mist}}
```
{{#endtab }}
{{#tab name="Emitted C#" }}
```csharp
{{#include ../../snippets/type-system/newtypes/wrapping-structs.cs}}
```
{{#endtab }}
{{#endtabs }}

The `Position` newtype gets its own struct with the same fields and methods as `Point`. Even though they have identical shapes, `Position` and `Point` are distinct types — you cannot assign one to the other without an explicit cast.

## Wrapping Classes

Newtypes can also wrap classes. The emitted type is a class that mirrors the wrapped class's constructor and public members.

{{#tabs }}
{{#tab name="Mist" }}
```mist
{{#include ../../snippets/type-system/newtypes/wrapping-classes.mist}}
```
{{#endtab }}
{{#tab name="Emitted C#" }}
```csharp
{{#include ../../snippets/type-system/newtypes/wrapping-classes.cs}}
```
{{#endtab }}
{{#endtabs }}

The `Actor` newtype is a completely separate class from `Entity`. It has the same constructor signature and public methods, but the type system treats them as unrelated types.

## Newtypes vs Type Aliases

The key difference between newtypes and type aliases is **interchangeability**:

| | Type Alias | Newtype |
|---|---|---|
| **Declaration** | `type Name = SourceType;` | `type Name = new SourceType;` |
| **Type identity** | Same type as the source — fully interchangeable | Distinct type — not interchangeable |
| **Assignment** | Can assign source type values directly | Requires explicit cast |
| **Purpose** | Readability and convenience | Type safety and domain modeling |

A type alias is transparent: `type Seconds = int;` means `Seconds` and `int` are the same type everywhere. A newtype is opaque: `type Seconds = new int;` means `Seconds` is its own type, and you must cast explicitly to convert between `Seconds` and `int`.

Use type aliases when you want a shorter or more descriptive name. Use newtypes when you want the compiler to prevent accidental mixing of values that happen to share the same underlying representation (e.g., `UserId` vs `OrderId`, both wrapping `int`).

## Member Access

Newtype instances expose the wrapped type's public members directly. For primitive newtypes, the wrapped value is accessible via the `.Value` property. For struct and class newtypes, the source type's fields and methods are available directly on the newtype instance.

{{#tabs }}
{{#tab name="Mist" }}
```mist
{{#include ../../snippets/type-system/newtypes/member-access.mist}}
```
{{#endtab }}
{{#tab name="Emitted C#" }}
```csharp
{{#include ../../snippets/type-system/newtypes/member-access.cs}}
```
{{#endtab }}
{{#endtabs }}

For primitive newtypes (`UserId`, `Label`, `Flag`), the underlying value lives in the `Value` field on the emitted struct. For struct and class newtypes (`Position`, `Actor`), the members are reproduced directly on the newtype, so you access them the same way you would on the original type.

## Explicit Casting

Converting between a newtype and its source type requires an explicit cast. This is intentional — the whole point of a newtype is to prevent accidental mixing of types that share the same representation.

{{#tabs }}
{{#tab name="Mist" }}
```mist
{{#include ../../snippets/type-system/newtypes/explicit-casting.mist}}
```
{{#endtab }}
{{#tab name="Emitted C#" }}
```csharp
{{#include ../../snippets/type-system/newtypes/explicit-casting.cs}}
```
{{#endtab }}
{{#endtabs }}

The cast syntax is `(NewType)value` to wrap and `(SourceType)newtypeValue` to unwrap. In the emitted C#, these use the explicit cast operators that the compiler generates on the newtype struct. Without the cast, the compiler reports a type error — there is no implicit conversion between a newtype and its source type.
