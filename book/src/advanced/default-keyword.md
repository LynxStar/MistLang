# The Default Keyword

The `default` keyword in Mist produces the default value for a given type. It comes in two expression forms — `default(T)` with an explicit type, and bare `default` where the type is inferred from context — plus a `default` block declaration that specifies custom field-level defaults inside class and struct bodies.

Not all types support `default`. Mist separates two distinct concepts:

- **Default (data)**: A deterministic template. The type's own constructors never run: a default is built from its field initializers and its `default` block, which must be [pure](./purity.md#where-mist-requires-purity), so taking a default has no side effects and every default of a type starts in the same state. A property's setter does not run either: a default writes the property's backing storage directly, so a setter that validates or logs sees nothing until the program assigns the property itself.
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

A field initializer of a defaultable type, and each entry of a `default` block, must be a pure expression. It may allocate, and call a pure constructor such as `new List<int>()`, so each default gets its own list. An initializer that calls a constructor which prints, or reads state that can change, is refused with `MIST0143`, naming the field and the operation that is not pure. A pure expression may still fail, the same way each time the default is taken. [Where Mist Requires Purity](./purity.md#where-mist-requires-purity) has an example.

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
| Abstract types | Always non-defaultable |

Using `default(T)` on a non-defaultable class, struct or type parameter is refused during analysis with `MIST0180`. A concrete type without a default must be constructed explicitly via `new`.

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

### Constructor-Less Generic Types

For a constructor-less generic type, `new` also initializes fields with the semantic defaults of the closed type arguments. A class field receives a fresh instance of its declared default, and a string field receives `""`.

{{#tabs }}
{{#tab name="Mist" }}
```mist
{{#include ../../snippets/advanced/default-keyword/constructorless-generic.mist}}
```
{{#endtab }}
{{#tab name="Emitted C#" }}
```csharp
{{#include ../../snippets/advanced/default-keyword/constructorless-generic.cs}}
```
{{#endtab }}
{{#endtabs }}

Both `new Box<Widget>()` and `default(Box<Widget>)` start with `Value.Value == 7`. Changing the widget inside one box leaves the other at `7`. The compiler passes the concrete default factory into the generic initialization path. `Box<T>` does not need a declaration constraint merely to hold `T`; its closed type arguments must make its fields defaultable. Calling `default(T)` inside a generic body requires the explicit constraint described below.

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
2. **Fields with defaultable types** and no initializer receive their type's semantic default before the constructor body runs. A class field receives a fresh default instance, a string receives `""`, an enum receives its designated default variant, and an optional receives absence. Auto-properties with backing storage follow the same rule, including get-only auto-properties.
3. **Fields with non-defaultable types** and no initializer must be explicitly assigned in every constructor body, on every control-flow path. An entry in the containing type's `default` block does not satisfy this obligation, because user construction does not run that block.

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

If a constructor fails to assign a non-defaultable field on any code path, the compiler emits `MIST0223` naming the unassigned field.

The analysis is control-flow sensitive. An assignment in only one branch does not discharge a field's constructor obligation; every path that reaches the end must assign it. A `this(...)` chain relies on its target constructor's assignments and does not repeat storage initialization. A base constructor initializes its own storage portion once.

Constructor entry uses the stored member's type default, not the containing type's whole default. For example, a scalar field `Count` starts at `0` in an otherwise empty user constructor even if the containing type's `default` block overrides `Count` to `7`. Taking the containing type's default applies that override without running its user constructors. Static storage and computed properties do not receive per-instance initialization.

## The `where T : default` Generic Constraint

Generic code that needs to call `default(T)` must declare the requirement using the `default` constraint. Without this constraint, `default(T)` on a type parameter is refused during analysis — the compiler cannot know at the generic definition site whether the type argument will be defaultable. A `new()` constraint alone does not supply this requirement: constructors and defaults are separate capabilities.

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

The constraint is enforced at instantiation: if you try to call `Factory.Create<DatabaseConnection>()` where `DatabaseConnection` is non-defaultable, the compiler emits `MIST0178`.

The `default` constraint has no direct equivalent in C#. Mist checks it at compile time and passes a generated `Func<T>` default factory to the emitted generic method. The method invokes that factory to obtain the type argument's semantic default. For a class-level constraint, the emitted class retains the factory so instance methods can use it; see the [class-level example](../type-system/generic-constraints.md#default-constraint). Each factory call produces a fresh class default.

C# callers can use the emitted constructor overload that accepts a `Func<T>` to supply the semantic default. Compatibility overloads without that factory use the CLR default only when it is non-null; they throw `InvalidOperationException` when a null CLR default would be used. Supplying the factory is required for reference defaults and for nullable value defaults that represent absence. These overloads do not infer Mist defaults or run a type argument's constructor.

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

The `default` modifier appears before the variant name in the enum member list. Only one variant may carry this modifier.

An enum without any `default` variant is non-defaultable. Designate a variant with the `default VariantName` syntax to give the enum a default.

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

## Abstract Types

Abstract types are always non-defaultable. They represent abstract contracts, not concrete values. They cannot supply a concrete default instance.

A `where T : default` constraint will also reject abstract types as type arguments, since they cannot satisfy the defaultability requirement.

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
