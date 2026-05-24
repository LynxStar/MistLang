# Type Aliases

Type aliases let you create readable, meaningful names for existing types. An alias introduces no new type — it's simply an alternative name that the compiler erases during transpilation. Everywhere you use the alias, the compiler substitutes the underlying type in the emitted C#. This makes aliases ideal for improving readability without introducing runtime overhead or type incompatibility.

## Simple Aliases

A simple type alias gives a new name to an existing type using the `type Name = ExistingType;` syntax. The alias is fully transparent — values of the alias type and the underlying type are interchangeable without casts.

{{#tabs }}
{{#tab name="Mist" }}
```mist
{{#include ../../snippets/type-system/type-aliases/simple.mist}}
```
{{#endtab }}
{{#tab name="Emitted C#" }}
```csharp
{{#include ../../snippets/type-system/type-aliases/simple.cs}}
```
{{#endtab }}
{{#endtabs }}

Notice that the `type` declarations themselves produce no C# output — they are erased entirely. Every usage of `UserId` becomes `int`, `UserName` becomes `string`, and so on.

## Closed Generic Aliases

An alias can name a specific instantiation of a generic type. This is useful when you frequently use a generic type with the same type argument and want a shorter name.

{{#tabs }}
{{#tab name="Mist" }}
```mist
{{#include ../../snippets/type-system/type-aliases/closed-generic.mist}}
```
{{#endtab }}
{{#tab name="Emitted C#" }}
```csharp
{{#include ../../snippets/type-system/type-aliases/closed-generic.cs}}
```
{{#endtab }}
{{#endtabs }}

The alias `IntBox` is erased to `GenericBox<int>` everywhere — both in variable declarations and in `new` expressions. No `IntBox` type exists in the emitted C#.

## Generic Aliases

A type alias can itself have type parameters. The type parameter is substituted into the underlying type expression, creating a parameterized shorthand.

{{#tabs }}
{{#tab name="Mist" }}
```mist
{{#include ../../snippets/type-system/type-aliases/generic-aliases.mist}}
```
{{#endtab }}
{{#tab name="Emitted C#" }}
```csharp
{{#include ../../snippets/type-system/type-aliases/generic-aliases.cs}}
```
{{#endtab }}
{{#endtabs }}

When you write `Box<int>`, the compiler substitutes `T = int` into the alias definition `GenericBox<T>`, producing `GenericBox<int>` in the emitted C#. The generic alias itself is erased — it never appears in the output.

## Transparency and Interchangeability

Type aliases are fully transparent. A value declared with an alias type can be assigned to a variable of the underlying type, and vice versa, with no cast or conversion required.

{{#tabs }}
{{#tab name="Mist" }}
```mist
{{#include ../../snippets/type-system/type-aliases/transparency.mist}}
```
{{#endtab }}
{{#tab name="Emitted C#" }}
```csharp
{{#include ../../snippets/type-system/type-aliases/transparency.cs}}
```
{{#endtab }}
{{#endtabs }}

Because aliases are erased, there is no distinction between `UserId` and `int` at runtime. The parameter type `UserId` becomes `int` in the emitted method signature, so passing a plain `int` works without any conversion. This transparency applies to all alias forms — simple, closed generic, and generic aliases are all interchangeable with their underlying types.

If you need a type that is *not* interchangeable with its underlying type — one that creates a distinct type identity — use a [newtype](./newtypes.md) instead.
