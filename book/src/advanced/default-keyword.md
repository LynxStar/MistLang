# The Default Keyword

The `default` keyword in Mist produces the default value for a given type. It comes in two expression forms — `default(T)` with an explicit type, and bare `default` where the type is inferred from context — plus a `default` block declaration that specifies custom field-level defaults inside class and struct bodies.

## What `default` Produces

The value produced by `default` depends on the type category:

| Type category | Default value |
|---------------|---------------|
| Numeric types (`int`, `float`, `decimal`, etc.) | `0` |
| `bool` | `false` |
| `char` | `'\0'` |
| `string` | `""` (empty string, not null) |
| Classes | A new instance with all fields set to their defaults |
| Structs | A new instance with all fields set to their defaults |

For classes and structs, the compiler generates a `__CreateDefault()` factory method that constructs an instance and initializes each field to its type-appropriate default (or to the value specified in a `default` block, if one exists).

## The `default(T)` Form

The explicit form `default(T)` produces the default value for the named type. Use this when the target type isn't obvious from context, or when you want to be explicit about which type you're defaulting.

{{#tabs }}
{{#tab name="Mist" }}
```mist
{{#include ../../snippets/advanced/default-keyword/default-t.mist}}
```
{{#endtab }}
{{#tab name="Emitted C#" }}
```csharp
{{#include ../../snippets/advanced/default-keyword/default-t.cs}}
```
{{#endtab }}
{{#endtabs }}

For primitive types, the emitted C# uses the native `default(T)` expression. For `string`, Mist emits `""` rather than `null` — Mist treats string defaults as empty strings to avoid null-reference issues.

### `default(T)` with Classes

When you call `default` on a class type, the compiler emits a call to the generated `__CreateDefault()` factory method. This method creates a new instance and initializes all fields to their type-appropriate defaults.

{{#tabs }}
{{#tab name="Mist" }}
```mist
{{#include ../../snippets/advanced/default-keyword/with-classes.mist}}
```
{{#endtab }}
{{#tab name="Emitted C#" }}
```csharp
{{#include ../../snippets/advanced/default-keyword/with-classes.cs}}
```
{{#endtab }}
{{#endtabs }}

The generated `__CreateDefault()` method handles the initialization — each field gets its zero-value (`0` for int, `false` for bool, `""` for string). The compiler also emits a `protected` parameterless constructor so the factory method can instantiate the class.

## The Bare `default` Form

When the target type can be inferred from context (such as a variable declaration with an explicit type annotation), you can use bare `default` without specifying the type.

{{#tabs }}
{{#tab name="Mist" }}
```mist
{{#include ../../snippets/advanced/default-keyword/bare-default.mist}}
```
{{#endtab }}
{{#tab name="Emitted C#" }}
```csharp
{{#include ../../snippets/advanced/default-keyword/bare-default.cs}}
```
{{#endtab }}
{{#endtabs }}

The compiler infers the type from the left-hand side of the assignment (`Config`) and emits the same `__CreateDefault()` call as the explicit form. Bare `default` is purely syntactic sugar — the emitted code is identical.

## The `default` Block Declaration

A `default` block inside a class or struct body specifies custom default values for fields. When `default(T)` or bare `default` is used on a type with a `default` block, the generated `__CreateDefault()` method uses those values instead of zero-initialization.

{{#tabs }}
{{#tab name="Mist" }}
```mist
{{#include ../../snippets/advanced/default-keyword/default-block.mist}}
```
{{#endtab }}
{{#tab name="Emitted C#" }}
```csharp
{{#include ../../snippets/advanced/default-keyword/default-block.cs}}
```
{{#endtab }}
{{#endtabs }}

The `default` block is not emitted as a C# member. Instead, the compiler incorporates its values into the generated parameterless constructor and `__CreateDefault()` factory method. The `default` block uses assignment syntax without semicolons — each line is `fieldName = value`.

### `default` Block in Structs

The `default` block also works in struct bodies. For `readonly` structs, the compiler emits a private sentinel constructor to set the default values (since readonly struct fields cannot be assigned outside a constructor).

{{#tabs }}
{{#tab name="Mist" }}
```mist
{{#include ../../snippets/advanced/default-keyword/struct-default.mist}}
```
{{#endtab }}
{{#tab name="Emitted C#" }}
```csharp
{{#include ../../snippets/advanced/default-keyword/struct-default.cs}}
```
{{#endtab }}
{{#endtabs }}

This example highlights the distinction between `new` and `default` for types that have both a user-defined constructor and a `default` block. Calling `new Container()` invokes the user's constructor (producing `Code=99, Label="constructed"`), while `default(Container)` invokes the `__CreateDefault()` factory which uses the sentinel constructor to apply the `default` block values (`Code=42, Label="defaulted"`).

For readonly structs, the sentinel constructor pattern (`private Container(bool __defaultSentinel)`) is necessary because readonly fields can only be assigned inside a constructor. Non-readonly structs use direct field assignment in `__CreateDefault()` instead.

## Field Initializers vs. `default` Block

Classes with field initializers (e.g., `public int Timeout = 30;`) also participate in the default system. When you call `default` on such a type, the field initializers provide the default values — no explicit `default` block is needed.

```mist
{{#include ../../snippets/advanced/default-keyword/field-initializers.mist}}
```

The `default` block is useful when you want default values that differ from field initializers, or when you want to centralize default logic separately from the field declarations.
