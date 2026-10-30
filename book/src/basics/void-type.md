# Void and Unit Type

## What Void Means in Mist

In most languages, `void` means "nothing" — a method returns no value, and you can't do anything with it. Mist takes a different approach: `void` is a real type with exactly one value, the **Void singleton**. In type theory this is called a **unit type** — a type inhabited by a single value that carries no information.

This makes `void` a first-class citizen. You can store it in variables, pass it as a parameter, use it as a generic type argument, and return it from methods. There is no special case — `void` behaves like any other type, it just happens to have only one possible value.

## Why This Matters

The traditional split between "void methods" and "value-returning methods" forces duplication in generic code. If you want a `Result<T>` type, you need a separate `Result` (no type parameter) for operations that don't produce a value. With Mist's approach, `Result<void>` works naturally — no special-casing required.

## The Void Value

To obtain the Void singleton, write the `void` keyword in expression position. The same keyword serves double duty: depending on the surrounding context it resolves either to the Void singleton **value** or to a **type literal**. The rule is driven by the *target type* — the type the expression is expected to produce:

- **Target type is `void`** → `void` resolves to the Void singleton value.
- **Target type is `Type`** → `void` resolves to a type literal (a `Type` instance describing the Void type).
- **No target type** (such as `var` inference) → `void` resolves to a type literal.

This mirrors how `true` and `false` are both keywords and value literals for `bool`. When the target type is `void`, the keyword behaves as a value literal:

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

With `var` inference there is no target type, so `void` resolves as a type literal — the variable holds a `Type` instance describing the Void type rather than the Void singleton value:

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

When the return value is explicitly used, the method returns the Void singleton via `return void;`:

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

A `return` in a body whose result is `void` can carry only a void value: `return;`, `return void;`, or `return` of another void call, which runs the call once and then returns. Any other value is refused at the value with `MIST0157`, before any C# is generated. The rule covers every body whose result is void: methods, local functions, lambdas whose function type returns `void`, setters, and constructors.

## Equality Semantics

Since there is only one possible void value, all Void singleton values are equal. The `==` operator always returns `true` and `!=` always returns `false`:

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
