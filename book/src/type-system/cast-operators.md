# Cast Operators

> **⚠️ Unvetted Content:** From this chapter onward, the book content is auto-generated from spec documents and sample code using Opus and has not been manually reviewed for accuracy. Descriptions may be incorrect or misleading. All code snippets are at least valid compilable Mist, and the `samples/` directory in the repository contains working programs that fully demonstrate each feature end-to-end — trust the code over the prose when in doubt.

Cast operators let you define custom conversions between types. Mist supports two kinds: **implicit** operators that the compiler applies automatically, and **explicit** operators that require the programmer to write a cast expression. Both are declared as static methods on the source type and emit directly to C#'s `implicit operator` and `explicit operator` syntax.

## Implicit Operator

An implicit cast operator is declared with `public static implicit operator TargetType(SourceType param)`. Once declared, the compiler applies the conversion automatically wherever a value of the source type appears in a context that expects the target type — assignments, argument passing, and return statements all trigger the conversion without any cast syntax.

{{#tabs }}
{{#tab name="Mist" }}
```mist
{{#include ../../snippets/type-system/cast-operators/implicit-operator.mist}}
```
{{#endtab }}
{{#tab name="Emitted C#" }}
```csharp
{{#include ../../snippets/type-system/cast-operators/implicit-operator.cs}}
```
{{#endtab }}
{{#endtabs }}

The declaration syntax mirrors C# exactly. The implicit operator is a static method on `Celsius` that takes a `Celsius` parameter and returns a `Fahrenheit`. Because it's implicit, the compiler inserts the conversion call wherever needed.

### Automatic Conversion at Assignment

When you assign a `Celsius` value to a variable typed as `Fahrenheit`, the implicit operator fires automatically:

{{#tabs }}
{{#tab name="Mist" }}
```mist
{{#include ../../snippets/type-system/cast-operators/assignment-conversion.mist}}
```
{{#endtab }}
{{#tab name="Emitted C#" }}
```csharp
{{#include ../../snippets/type-system/cast-operators/assignment-conversion.cs}}
```
{{#endtab }}
{{#endtabs }}

In the Mist source, no cast syntax is needed — the assignment `Fahrenheit boilingF = boiling;` is valid because the implicit operator exists. In the emitted C#, the compiler inserts an explicit cast expression `((Fahrenheit)boiling)` to invoke the operator. The programmer never writes this cast; the compiler handles it.

### Automatic Conversion at Argument Sites

Implicit casts also apply when passing an argument to a method that expects the target type:

{{#tabs }}
{{#tab name="Mist" }}
```mist
{{#include ../../snippets/type-system/cast-operators/argument-conversion.mist}}
```
{{#endtab }}
{{#tab name="Emitted C#" }}
```csharp
{{#include ../../snippets/type-system/cast-operators/argument-conversion.cs}}
```
{{#endtab }}
{{#endtabs }}

Here `PrintTemperature` expects a `Fahrenheit`, but we pass a `Celsius`. The compiler sees the implicit operator and applies the conversion transparently.

## Explicit Operator

An explicit cast operator is declared with `public static explicit operator TargetType(SourceType param)`. Unlike implicit operators, explicit casts are never applied automatically — the programmer must write the `(TargetType)expression` cast syntax to invoke them. Use explicit operators for conversions that may lose information, change scale, or otherwise require the programmer's conscious intent.

{{#tabs }}
{{#tab name="Mist" }}
```mist
{{#include ../../snippets/type-system/cast-operators/explicit-operator.mist}}
```
{{#endtab }}
{{#tab name="Emitted C#" }}
```csharp
{{#include ../../snippets/type-system/cast-operators/explicit-operator.cs}}
```
{{#endtab }}
{{#endtabs }}

The explicit operator emits identically to C#'s `explicit operator` syntax. The difference from implicit is purely in how the compiler treats call sites.

### Cast Expression Syntax

To invoke an explicit operator, you write `(TargetType)expression`:

{{#tabs }}
{{#tab name="Mist" }}
```mist
{{#include ../../snippets/type-system/cast-operators/cast-expression.mist}}
```
{{#endtab }}
{{#tab name="Emitted C#" }}
```csharp
{{#include ../../snippets/type-system/cast-operators/cast-expression.cs}}
```
{{#endtab }}
{{#endtabs }}

Without the `(Kelvin)` cast, this code would not compile — the compiler does not apply explicit operators automatically. The emitted C# wraps the cast in an extra set of parentheses, but the semantics are identical.

## Compile-Time Type Resolution

Cast operators are resolved against the **compile-time (static) type** of the expression, not the runtime type. This means that even if a variable holds an instance of a derived type that declares a cast operator, the operator is only available when the variable's declared type is the one that defines it.

{{#tabs }}
{{#tab name="Mist" }}
```mist
{{#include ../../snippets/type-system/cast-operators/compile-time-resolution.mist}}
```
{{#endtab }}
{{#tab name="Emitted C#" }}
```csharp
{{#include ../../snippets/type-system/cast-operators/compile-time-resolution.cs}}
```
{{#endtab }}
{{#endtabs }}

With this hierarchy, the following code compiles because the variable's static type is `Weight`:

```mist
{{#include ../../snippets/type-system/cast-operators/cast-ok.mist}}
```

But if you assign the same value to a base-typed variable, the cast becomes unavailable:

```mist
{{#include ../../snippets/type-system/cast-operators/cast-error.mist}}
```

Even though `m` holds a `Weight` at runtime, the compiler only sees `Measurement` — which does not declare a cast to `Kelvin`. This static resolution eliminates any possibility of a runtime cast failure: if the code compiles, the cast will always succeed.

## When to Use Each Kind

| | Implicit | Explicit |
|---|---|---|
| **Syntax at call site** | None — applied automatically | `(TargetType)expression` |
| **When to use** | Safe, lossless conversions (widening) | Conversions that change scale, may lose precision, or require intent |
| **Compiler behavior** | Inserts conversion at assignment, argument, and return sites | Only applies when the programmer writes the cast |
| **Example** | Celsius → Fahrenheit (linear transform, no data loss) | Celsius → Kelvin (different scale, programmer should be aware) |

Both kinds emit as standard C# cast operators, so the .NET runtime handles them identically. The distinction is purely about what the Mist compiler allows without explicit syntax.
