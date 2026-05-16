# Void and Unit Type

In most languages, `void` means "nothing" — a method returns no value, and that's the end of it. Mist takes a different approach: `void` is a first-class value type with a single instance, `Void.Void`. This means you can store it in variables, pass it as a parameter, and use it as a generic type argument. The compiler emits a `MistVoid` struct in the generated C# to represent this value.

This design eliminates the need for separate "void" and "non-void" code paths in generic code. A `Result<void>` works just as naturally as a `Result<int>`.

## Void as a Variable Type

You can declare a variable with type `void` and assign it the value `Void.Void`:

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

You can also use `var` for type inference:

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

In both cases, the variable holds the singleton `MistVoid.Instance` value.

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

The parameter type `void` becomes `MistVoid` in the emitted C#, and `Void.Void` becomes `MistVoid.Instance`.

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

When `void` is used as a type argument, the compiler substitutes `MistVoid` in the emitted C#. This means `Result<void>` becomes `Result<MistVoid>` — a perfectly valid generic instantiation that requires no special-casing.

## Fire-and-Forget Void Methods

When a void method is called as a standalone statement and its return value is not used, the compiler emits a standard C# `void` method:

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

This is the familiar pattern from C# — the method performs a side effect and returns nothing.

## Value-Returning Void Methods

When a void method explicitly returns `Void.Void` and its return value is captured by the caller, the compiler emits a `MistVoid` return type instead of C# `void`:

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

The distinction is automatic: if the compiler detects that a void method's return value is consumed (assigned to a variable, passed as an argument, etc.), it emits `MistVoid` as the return type with `return MistVoid.Instance`. If the return value is never used, it emits standard C# `void`.

## Void Equality Semantics

Since there is only one possible void value, all `Void.Void` instances are equal. The `==` operator always returns `true`, `!=` always returns `false`, and `GetHashCode()` returns a constant value (0):

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

The emitted `MistVoid` struct implements `IEquatable<MistVoid>` with these semantics:

```csharp
{{#include ../../snippets/basics/void-type/mistvoid-struct.cs}}
```

This makes void safe to use in collections, dictionaries, and equality comparisons without surprises.
