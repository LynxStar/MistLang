# Newtypes

A newtype creates a distinct type from an existing type. The compiler creates a new type of the same kind (struct for struct, class for class) and clones all of the source's public members into it. Unlike type aliases, which are transparent and interchangeable with their source type, a newtype is a separate type at compile time — you cannot accidentally pass a `UserId` where a plain `int` is expected, or vice versa. This gives you type safety for domain concepts without runtime overhead.

## Newtype Declaration

A newtype is declared with the `type` keyword, a name, and `= new SourceType;`. The source type can be any struct or class.

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

The compiler clones all public members from the source type into the newtype and generates explicit cast operators in both directions so conversions are always intentional.

## Newtypes from User-Defined Types

Newtypes work the same way regardless of whether the source is a struct or a class. The compiler produces a new type of the same kind with the source's constructor and public members cloned in.

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

Even though `Position` has the same shape as `Point` and `Actor` has the same shape as `Entity`, they are distinct types — you cannot assign one to the other without an explicit cast.

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

Newtype instances expose the source type's public members directly — they're cloned into the new type. You access fields and methods on a newtype the same way you would on the source type.

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

The source type's public fields, properties, and methods are all reproduced on the newtype. The only addition is the explicit cast operators that let you convert between the newtype and its source.

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

The cast syntax is `(NewType)value` to create the newtype and `(SourceType)newtypeValue` to extract the source value. Without the cast, the compiler reports a type error — there is no implicit conversion between a newtype and its source type.

## Type Identity and Pattern Matching

Because a newtype is a nominally distinct type, it does not match its source type in pattern matching. A `UserId` is not an `int` — even though it wraps one. This means `is int` will fail on a `UserId` value, and `is UserId` will fail on a plain `int`.

{{#tabs }}
{{#tab name="Mist" }}
```mist
{{#include ../../snippets/type-system/newtypes/type-identity.mist}}
```
{{#endtab }}
{{#tab name="Emitted C#" }}
```csharp
{{#include ../../snippets/type-system/newtypes/type-identity.cs}}
```
{{#endtab }}
{{#endtabs }}

Passing a `UserId` to an `unknown` parameter and checking `is int` produces no match — the runtime type is `UserId`, not `int`. Only `is UserId` succeeds. This is the core guarantee of newtypes: they are separate types at every level, including runtime type identity.
