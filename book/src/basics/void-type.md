# Void and Unit Type

## What Void Means in Mist

In most languages, `void` means "nothing" — a method returns no value, and you can't do anything with it. Mist takes a different approach: `void` is a real type with exactly one value, `Void.Void`. In type theory this is called a **unit type** — a type inhabited by a single value that carries no information.

This makes `void` a first-class citizen. You can store it in variables, pass it as a parameter, use it as a generic type argument, and return it from methods. There is no special case — `void` behaves like any other type, it just happens to have only one possible value.

## Why This Matters

The traditional split between "void methods" and "value-returning methods" forces duplication in generic code. If you want a `Result<T>` type, you need a separate `Result` (no type parameter) for operations that don't produce a value. With Mist's approach, `Result<void>` works naturally — no special-casing required.

## The Void Value

The single instance of the `void` type is written as `Void.Void`:

{{#tabs }}
{{#tab name="Mist" }}
```mist
{{#include ../../snippets/basics/void-type/variable-type.mist}}
```
{{#endtab }}
{{#tab name="Emitted C#" }}
```csharp
{{#include ../../snippets/basics/void-type/variable-type.cs}}
```
{{#endtab }}
{{#endtabs }}

Type inference works as expected:

{{#tabs }}
{{#tab name="Mist" }}
```mist
{{#include ../../snippets/basics/void-type/var-inference.mist}}
```
{{#endtab }}
{{#tab name="Emitted C#" }}
```csharp
{{#include ../../snippets/basics/void-type/var-inference.cs}}
```
{{#endtab }}
{{#endtabs }}

## Void as a Parameter Type

Methods can accept `void` as a parameter type. This is useful in generic contexts where a type parameter might be instantiated with `void`:

{{#tabs }}
{{#tab name="Mist" }}
```mist
{{#include ../../snippets/basics/void-type/parameter-type.mist}}
```
{{#endtab }}
{{#tab name="Emitted C#" }}
```csharp
{{#include ../../snippets/basics/void-type/parameter-type.cs}}
```
{{#endtab }}
{{#endtabs }}

## Void as a Generic Type Argument

The real power of first-class void shows up in generic code. You can instantiate any generic type with `void`:

{{#tabs }}
{{#tab name="Mist" }}
```mist
{{#include ../../snippets/basics/void-type/generic-argument.mist}}
```
{{#endtab }}
{{#tab name="Emitted C#" }}
```csharp
{{#include ../../snippets/basics/void-type/generic-argument.cs}}
```
{{#endtab }}
{{#endtabs }}

A `Result<void>` is just as valid as a `Result<int>` — no separate non-generic overload needed.

## Void Methods

A method with return type `void` can either be used in fire-and-forget style (the familiar pattern) or have its return value captured:

{{#tabs }}
{{#tab name="Mist" }}
```mist
{{#include ../../snippets/basics/void-type/fire-and-forget.mist}}
```
{{#endtab }}
{{#tab name="Emitted C#" }}
```csharp
{{#include ../../snippets/basics/void-type/fire-and-forget.cs}}
```
{{#endtab }}
{{#endtabs }}

When the return value is explicitly used, the method returns `Void.Void`:

{{#tabs }}
{{#tab name="Mist" }}
```mist
{{#include ../../snippets/basics/void-type/value-returning.mist}}
```
{{#endtab }}
{{#tab name="Emitted C#" }}
```csharp
{{#include ../../snippets/basics/void-type/value-returning.cs}}
```
{{#endtab }}
{{#endtabs }}

## Equality Semantics

Since there is only one possible void value, all `Void.Void` instances are equal. The `==` operator always returns `true` and `!=` always returns `false`:

{{#tabs }}
{{#tab name="Mist" }}
```mist
{{#include ../../snippets/basics/void-type/equality.mist}}
```
{{#endtab }}
{{#tab name="Emitted C#" }}
```csharp
{{#include ../../snippets/basics/void-type/equality.cs}}
```
{{#endtab }}
{{#endtabs }}

This makes void safe to use in collections, dictionaries, and equality comparisons without surprises.

## C# Emission

The C# backend represents Mist's `void` type using a `MistVoid` struct — a zero-size value type with a singleton `Instance` field:

```csharp
{{#include ../../snippets/basics/void-type/mistvoid-struct.cs}}
```

The compiler chooses between two emission strategies depending on how the void method's return value is used:

- **Fire-and-forget** — When a void method's return value is never consumed (not assigned, not passed as an argument), the compiler emits a standard C# `void` method. This produces idiomatic C# for the common case.
- **Value-returning** — When a void method's return value is captured (assigned to a variable, passed as an argument, used in a generic context), the compiler emits `MistVoid` as the return type with `return MistVoid.Instance`.

This distinction is automatic. You write the same Mist code either way — the compiler inspects usage at each call site to decide which emission strategy to use.
