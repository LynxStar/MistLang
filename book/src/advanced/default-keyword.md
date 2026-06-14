# The Default Keyword

The `default` keyword in Mist produces the default value for a given type. It comes in two expression forms — `default(T)` with an explicit type, and bare `default` where the type is inferred from context — plus a `default` block declaration that specifies custom field-level defaults inside class and struct bodies.

Not all types support `default`. Mist separates two distinct concepts:

- **Default (data)**: A precomputed, deterministic zero-value template. No constructor runs — it is pure data stamping. Side-effect-free and instantaneous.
- **Constructor / `new()` (behavior)**: Explicit initialization logic that may perform IO, fail, or depend on external state.

A type that supports the `default` operator is called **defaultable**. A type that does not is **non-defaultable** — and that is a perfectly valid state. The compiler does not force every type to have a meaningful zero value.

## Design Rationale

Many types have no sensible default. A `DatabaseConnection` with empty fields is not a usable connection — it is a bug waiting to happen. Requiring every type to be defaultable forces developers to invent meaningless zero values or suppress diagnostics for types that genuinely need constructor arguments.

Mist's approach: if a type has constructors, those constructors handle initialization. The type does not need a default value unless the developer explicitly provides one via a `default {}` block. This keeps the type system honest — `default(T)` always produces a value that the developer declared meaningful, never a synthetic placeholder.

The tradeoff is that generic code which needs `default(T)` must declare that requirement explicitly via `where T : default`. This makes the constraint visible in the API surface instead of silently assuming all types are defaultable.

## What `default` Produces

The value produced by `default` depends on the type category:

| Type category | Default value |
|---------------|---------------|
| Numeric types (`int`, `float`, `decimal`, etc.) | `0` |
| `bool` | `false` |
| `char` | `'\0'` |
| `string` | `""` (empty string, not null) |
| Defaultable classes/structs | A new instance with all fields set to their defaults |
| Enums with a `default` variant | The designated default variant |
| Union types containing `void` | `void` |

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

For numeric and boolean types, the emitted C# uses the native `default(T)` expression. For `string`, Mist emits `""` rather than `null` — Mist treats string defaults as empty strings to avoid null-reference issues.

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

## Defaultable vs. Non-Defaultable Types

A type is classified as **defaultable** or **non-defaultable** based on its structure:

| Condition | Classification |
|-----------|---------------|
| Primitive types (`int`, `bool`, `string`, etc.) | Always defaultable |
| Class/struct with no constructor, all fields defaultable | Defaultable (inferred) |
| Class/struct with an explicit `default {}` block | Defaultable (explicit) |
| Class/struct with constructor(s), no `default {}` block | Non-defaultable |
| Enum with a `default` variant | Defaultable |
| Enum without a `default` variant | Non-defaultable |
| Union type containing `void` (e.g., `T?`) | Defaultable |
| Union type without `void` | Non-defaultable |
| Interfaces and traits | Always non-defaultable |

Using `default(T)` on a non-defaultable type is a compile error. The compiler emits `MIST_DEFAULT_NOT_SUPPORTED` and the type must be constructed explicitly via `new`.

## Constructor-Less Types Are Implicitly Defaultable

A type with no user-defined constructor *must* be defaultable. Since there is no constructor to call, the only way to create an instance is via `default` (or the implicit parameterless constructor, which returns the default value). If all fields are defaultable types, the compiler infers defaultability automatically.

{{#tabs }}
{{#tab name="Mist" }}
```mist
{{#include ../../snippets/advanced/default-keyword/implicit-defaultable.mist}}
```
{{#endtab }}
{{#tab name="Emitted C#" }}
```csharp
{{#include ../../snippets/advanced/default-keyword/implicit-defaultable.cs}}
```
{{#endtab }}
{{#endtabs }}

The compiler generates an implicit parameterless constructor that returns the default value. Both `new Point()` and `default(Point)` produce the same zero-initialized instance.

If a constructor-less type has a field whose type is non-defaultable, the compiler emits an error — the type cannot be initialized without a constructor to assign that field.

## Types with Constructors Opt Out of Defaultability

When a type has at least one user-defined constructor, the compiler no longer requires it to be defaultable. The constructor handles initialization, and `default(T)` is not available unless the developer explicitly provides a `default {}` block.

{{#tabs }}
{{#tab name="Mist" }}
```mist
{{#include ../../snippets/advanced/default-keyword/non-defaultable.mist}}
```
{{#endtab }}
{{#tab name="Emitted C#" }}
```csharp
{{#include ../../snippets/advanced/default-keyword/non-defaultable.cs}}
```
{{#endtab }}
{{#endtabs }}

`DatabaseConnection` has a constructor, so it is non-defaultable. Attempting `default(DatabaseConnection)` is a compile error. Instances must be created through the constructor: `new DatabaseConnection("Server=localhost", 30)`.

This is the key behavioral change from earlier versions of Mist. Previously, the compiler would emit an error if a type could not produce a default value. Now, having a constructor is sufficient — the type simply opts out of defaultability.

### Opting Back In with a `default` Block

A type can have both constructors and a `default {}` block. This makes the type both constructible (via `new()`) and defaultable (via `default(T)`). Both paths produce fully initialized instances, but through different mechanisms.

{{#tabs }}
{{#tab name="Mist" }}
```mist
{{#include ../../snippets/advanced/default-keyword/constructor-and-default.mist}}
```
{{#endtab }}
{{#tab name="Emitted C#" }}
```csharp
{{#include ../../snippets/advanced/default-keyword/constructor-and-default.cs}}
```
{{#endtab }}
{{#endtabs }}

Here `Config` is defaultable because of the explicit `default` block. `new Config("production")` runs the constructor logic, while `default(Config)` produces an instance with the block's values. The two paths are independent — the constructor does not call into the default logic.

## The Definite Assignment Guarantee

When a type has constructors, the compiler verifies that every field is assigned a value by the end of each constructor body. This is the **definite assignment analysis** — it ensures no instance can exist with uninitialized fields, even for non-defaultable types.

The rules are:

1. **Fields with initializers** (e.g., `public int X = 5;`) are considered assigned at constructor entry.
2. **Fields with defaultable types** and no initializer are considered assigned at constructor entry — the runtime injects the default value as if the field were declared `type x = default;`.
3. **Fields with non-defaultable types** and no initializer must be explicitly assigned in every constructor body, on every control-flow path.

{{#tabs }}
{{#tab name="Mist" }}
```mist
{{#include ../../snippets/advanced/default-keyword/definite-assignment.mist}}
```
{{#endtab }}
{{#tab name="Emitted C#" }}
```csharp
{{#include ../../snippets/advanced/default-keyword/definite-assignment.cs}}
```
{{#endtab }}
{{#endtabs }}

If a constructor fails to assign a non-defaultable field on any code path, the compiler emits `MIST_FIELD_NOT_DEFINITELY_ASSIGNED` naming the unassigned field.

The analysis is control-flow sensitive. A field assigned inside an `if` block is only considered assigned after the `if` if it is also assigned in the `else` branch (or the code unconditionally continues past the branch).

## The `where T : default` Generic Constraint

Generic code that needs to call `default(T)` must declare the requirement using the `default` constraint. Without this constraint, `default(T)` on a type parameter is a compile error — the compiler cannot know at the generic definition site whether the type argument will be defaultable.

{{#tabs }}
{{#tab name="Mist" }}
```mist
{{#include ../../snippets/advanced/default-keyword/default-constraint.mist}}
```
{{#endtab }}
{{#tab name="Emitted C#" }}
```csharp
{{#include ../../snippets/advanced/default-keyword/default-constraint.cs}}
```
{{#endtab }}
{{#endtabs }}

The constraint is enforced at instantiation: if you try to call `Factory.Create<DatabaseConnection>()` where `DatabaseConnection` is non-defaultable, the compiler emits `MIST_DEFAULT_CONSTRAINT_NOT_SATISFIED`.

The `default` constraint is a Mist-only concept — it has no direct equivalent in C#. The emitter outputs a comment documenting the constraint, but the actual enforcement happens entirely at compile time in the Mist type checker.

### Arrays Require `where TValue : default`

The standard library `Array<TValue, Length>` type declares `where TValue : default` on its element type parameter. This ensures that array slot initialization is well-defined — every element gets a default value when the array is created.

If you need an array of a non-defaultable type, use the optional form: `DatabaseConnection?[]` works because `DatabaseConnection?` desugars to `DatabaseConnection | void`, which is defaultable (see [Union Type Defaultability](#union-type-defaultability) below).

## Enum Default Variant Designation

Enum types in Mist do not have an implicit default. The compiler does not guess from the first variant, the backing type's zero value, or positional ordering. Instead, you explicitly designate a default variant using the `default` modifier.

{{#tabs }}
{{#tab name="Mist" }}
```mist
{{#include ../../snippets/advanced/default-keyword/enum-default.mist}}
```
{{#endtab }}
{{#tab name="Emitted C#" }}
```csharp
{{#include ../../snippets/advanced/default-keyword/enum-default.cs}}
```
{{#endtab }}
{{#endtabs }}

The `default` modifier appears before the variant name in the enum member list. Only one variant may carry this modifier — multiple `default` variants is a compile error (`MIST_ENUM_MULTIPLE_DEFAULTS`).

An enum without any `default` variant is non-defaultable. Using `default(Priority)` on such an enum produces `MIST_DEFAULT_NOT_SUPPORTED`. The compiler's error message suggests the `default VariantName` syntax.

## Union Type Defaultability

A union type is defaultable if and only if it contains `void` as one of its constituents. The default value of such a union is `void` — the "nothing" value representing the absence of a real value.

This rule is why optional types (`T?`, which desugars to `T | void`) are always defaultable: they contain `void`. The default of `int?` is `void` (meaning "no int present"), not `0`.

{{#tabs }}
{{#tab name="Mist" }}
```mist
{{#include ../../snippets/advanced/default-keyword/union-default.mist}}
```
{{#endtab }}
{{#tab name="Emitted C#" }}
```csharp
{{#include ../../snippets/advanced/default-keyword/union-default.cs}}
```
{{#endtab }}
{{#endtabs }}

A union that does not include `void` (e.g., `int | string`) is non-defaultable. The compiler cannot pick one constituent over another as the "default" — that would be an arbitrary choice with no semantic basis.

## Interfaces and Traits

Interfaces and traits are always non-defaultable. They represent abstract contracts, not concrete values. You cannot call `default(ISerializable)` or `default(Printable)` — the compiler emits `MIST_DEFAULT_ON_INTERFACE` or `MIST_DEFAULT_ON_TRAIT` respectively.

A `where T : default` constraint will also reject interface and trait types as type arguments, since they cannot satisfy the defaultability requirement.

## Summary of Rules

The defaultability system follows a small set of principles:

1. **Primitive types are always defaultable** — they have well-known zero values.
2. **Constructor-less types must be defaultable** — the implicit constructor returns the default, so a default must exist.
3. **Constructors opt the type out** — if you provide constructors, you handle initialization. No default required.
4. **`default {}` opts back in** — a type with constructors can still be defaultable by declaring an explicit default block.
5. **Enums require explicit designation** — no guessing which variant is the default.
6. **Unions default to `void`** — only unions containing `void` are defaultable.
7. **Generics must declare the need** — use `where T : default` if your generic code calls `default(T)`.
8. **Definite assignment guarantees safety** — constructors must assign every field, so no instance exists in a half-initialized state.
