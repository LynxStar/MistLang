# Dependent Types

Dependent types let you parameterize a type by a compile-time constant value rather than another type. Where generics say "this works for any type T," dependent types say "this is specifically sized to 5" or "this has exactly 3 rows." The value becomes part of the type's identity, giving you stronger compile-time guarantees about dimensions, capacities, and configurations without runtime overhead.

Mist dependent types are related to but distinct from refinement types. Refinement types constrain the *range* of values an existing type can hold (e.g., "an int that is positive"). Dependent types parameterize the *type itself* by a constant value, creating a family of distinct types indexed by that value.

## Dependent Parameter Syntax

A dependent type parameter uses the `Name:PrimitiveType` syntax inside the type parameter list. The colon separates the parameter name from its value type — this distinguishes it from a regular generic type parameter.

{{#tabs }}
{{#tab name="Mist" }}
```mist
{{#include ../../snippets/type-system/dependent-types/syntax.mist}}
```
{{#endtab }}
{{#tab name="Emitted C#" }}
```csharp
{{#include ../../snippets/type-system/dependent-types/syntax.cs}}
```
{{#endtab }}
{{#endtabs }}

In the emitted C#, the dependent parameter `Size:int` becomes a generic type parameter `TSize` constrained to an `IConstInt` interface. The compiler generates a static property `Size` that retrieves the constant value through the carrier type. This encoding preserves type safety at the .NET level while giving Mist the ability to treat the value as a compile-time constant.

## Allowed Value Types

Dependent parameters support all integral types, `bool`, and `char`. These are the types that can serve as compile-time constants:

- `int`, `uint`, `long`, `ulong`
- `short`, `ushort`, `byte`, `sbyte`
- `bool`
- `char`

{{#tabs }}
{{#tab name="Mist" }}
```mist
{{#include ../../snippets/type-system/dependent-types/allowed-types.mist}}
```
{{#endtab }}
{{#tab name="Emitted C#" }}
```csharp
{{#include ../../snippets/type-system/dependent-types/allowed-types.cs}}
```
{{#endtab }}
{{#endtabs }}

Each value type gets its own const-carrier interface (`IConstInt`, `IConstBool`, `IConstChar`, etc.). The compiler only emits interfaces for the value types actually used in the program.

## Using Dependent Parameters as Values

Inside the type body, a dependent parameter name acts as a static value. You can use it in expressions, return it from methods, embed it in string interpolation, or pass it to other methods — anywhere a value of that type is expected.

{{#tabs }}
{{#tab name="Mist" }}
```mist
{{#include ../../snippets/type-system/dependent-types/using-values.mist}}
```
{{#endtab }}
{{#tab name="Emitted C#" }}
```csharp
{{#include ../../snippets/type-system/dependent-types/using-values.cs}}
```
{{#endtab }}
{{#endtabs }}

Multiple dependent parameters each get their own carrier type parameter and static property. The values are available as static members, so they don't occupy instance storage.

## Dependent Types on Classes

Classes are the most common host for dependent type parameters. You can mix regular type parameters with dependent parameters in the same parameter list.

{{#tabs }}
{{#tab name="Mist" }}
```mist
{{#include ../../snippets/type-system/dependent-types/classes.mist}}
```
{{#endtab }}
{{#tab name="Emitted C#" }}
```csharp
{{#include ../../snippets/type-system/dependent-types/classes.cs}}
```
{{#endtab }}
{{#endtabs }}

Regular type parameters (`T`) pass through unchanged. Dependent parameters (`Size:int`) are transformed into constrained carrier parameters (`TSize : IConstInt`). At the call site, `new TypedBuffer<string, 16>("hello")` supplies `string` for `T` and a carrier struct `Const_Int_16` for `TSize`.

## Dependent Types on Structs

Structs support dependent type parameters with the same syntax and semantics as classes.

{{#tabs }}
{{#tab name="Mist" }}
```mist
{{#include ../../snippets/type-system/dependent-types/structs.mist}}
```
{{#endtab }}
{{#tab name="Emitted C#" }}
```csharp
{{#include ../../snippets/type-system/dependent-types/structs.cs}}
```
{{#endtab }}
{{#endtabs }}

Since structs are value types, a `FixedSpan<3>` carries no heap allocation. The dependent value is accessed through the static property, adding zero per-instance overhead.

## Dependent Types on Interfaces

Interfaces can declare dependent type parameters to define contracts that depend on a constant value.

{{#tabs }}
{{#tab name="Mist" }}
```mist
{{#include ../../snippets/type-system/dependent-types/interfaces.mist}}
```
{{#endtab }}
{{#tab name="Emitted C#" }}
```csharp
{{#include ../../snippets/type-system/dependent-types/interfaces.cs}}
```
{{#endtab }}
{{#endtabs }}

When a class implements an interface with a dependent parameter, it supplies a concrete value (here `8`). The compiler emits the corresponding carrier struct (`Const_Int_8`) as the type argument.

## Dependent Types on Traits

Traits can also declare dependent type parameters. A type satisfies a dependent trait structurally — it must have matching members regardless of the dependent value.

{{#tabs }}
{{#tab name="Mist" }}
```mist
{{#include ../../snippets/type-system/dependent-types/traits.mist}}
```
{{#endtab }}
{{#tab name="Emitted C#" }}
```csharp
{{#include ../../snippets/type-system/dependent-types/traits.cs}}
```
{{#endtab }}
{{#endtabs }}

The trait `ISized<N:int>` declares a contract parameterized by a constant. `SizedBox<N:int>` satisfies it structurally because it provides a `GetCapacity()` method with the correct signature.

## Dependent Types on Methods

Methods can declare their own dependent type parameters, independent of the enclosing class. This is useful for operations that need a compile-time constant without requiring the entire class to be parameterized.

{{#tabs }}
{{#tab name="Mist" }}
```mist
{{#include ../../snippets/type-system/dependent-types/methods.mist}}
```
{{#endtab }}
{{#tab name="Emitted C#" }}
```csharp
{{#include ../../snippets/type-system/dependent-types/methods.cs}}
```
{{#endtab }}
{{#endtabs }}

At the call site, you supply the constant value as a type argument: `DependentMethods.Repeat<3>("hello")`. The compiler resolves `3` to the carrier struct `Const_Int_3`. The `Process` method demonstrates forwarding a dependent parameter — it accepts any `FixedBuffer<N>` and the `N` flows through as a type parameter.

## Relationship to Refinement Types

Dependent types and refinement types serve different purposes:

- **Refinement types** constrain the *values* a variable can hold at runtime. A `int where > 0` is still an `int`, but the compiler and runtime enforce that it's always positive. Arithmetic on a refined type produces the base type.

- **Dependent types** parameterize the *type itself* by a compile-time constant. A `FixedBuffer<5>` is a fundamentally different type from `FixedBuffer<10>` — they cannot be assigned to each other. The constant is baked into the type's identity.

Use refinement types when you want to restrict what values flow through a variable. Use dependent types when the constant should be part of the type's structure — dimensions, capacities, protocol versions, or any value that distinguishes one instantiation from another at the type level.

## Type Identity Semantics

Different dependent values produce distinct types. `FixedBuffer<5>` and `FixedBuffer<10>` are not interchangeable — you cannot assign one to the other, and a function expecting `FixedBuffer<5>` will not accept a `FixedBuffer<10>`.

{{#tabs }}
{{#tab name="Mist" }}
```mist
{{#include ../../snippets/type-system/dependent-types/type-identity.mist}}
```
{{#endtab }}
{{#tab name="Emitted C#" }}
```csharp
{{#include ../../snippets/type-system/dependent-types/type-identity.cs}}
```
{{#endtab }}
{{#endtabs }}

The function `AcceptFive` only accepts `FixedBuffer<5>`. Passing a `FixedBuffer<10>` would be a compile-time error because `Const_Int_5` and `Const_Int_10` are different types — the .NET type system enforces the distinction. This gives you compile-time guarantees about dimensions and capacities that would otherwise require runtime checks.

You can also use pattern matching to check dependent type identity at runtime:

```mist
{{#include ../../snippets/type-system/dependent-types/pattern-matching.mist}}
```
