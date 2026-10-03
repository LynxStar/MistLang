# Generic Constraints

Generic constraints restrict what types can be used as type arguments. By adding a `where` clause to a generic declaration, you tell the compiler that the type parameter must satisfy certain requirements — being a reference type, implementing an abstract type, extending a base class, or having a specific constructor. This lets you safely call methods, access members, or construct instances of the type parameter within the generic body.

## The `where` Clause Syntax

Constraints are declared after the type parameter list using the `where T : constraint` syntax. The `where` clause appears after the class body's opening parameters but before the body braces.

```
class Name<T> where T : constraint {
    // T is guaranteed to satisfy the constraint
}
```

The constraint can be a type name (abstract type, base class), a structural constraint (`class`, `struct`, `stampable`), a default constraint (`default`), or a constructor constraint (`new()`). Multiple constraints on the same type parameter are separated by commas.

## Class Constraint (Reference Type)

The `class` constraint restricts a type parameter to reference types only. Value types like `int`, `bool`, or user-defined structs cannot be used as the type argument.

{{#tabs }}
{{#tab name="Mist" }}
```mist
{{#include ../../snippets/type-system/generic-constraints/class-constraint.mist}}
```
{{#endtab }}
{{#tab name="Emitted C#" }}
```csharp
{{#include ../../snippets/type-system/generic-constraints/class-constraint.cs}}
```
{{#endtab }}
{{#endtabs }}

With this constraint, `RefOnly<string>` is valid (strings are reference types) but `RefOnly<int>` would be a compile error.

## Struct Constraint (Value Type)

The `struct` constraint restricts a type parameter to value types only. Reference types like classes and strings cannot be used as the type argument.

{{#tabs }}
{{#tab name="Mist" }}
```mist
{{#include ../../snippets/type-system/generic-constraints/struct-constraint.mist}}
```
{{#endtab }}
{{#tab name="Emitted C#" }}
```csharp
{{#include ../../snippets/type-system/generic-constraints/struct-constraint.cs}}
```
{{#endtab }}
{{#endtabs }}

With this constraint, `ValOnly<int>` and `ValOnly<Point>` (where `Point` is a struct) are valid, but `ValOnly<string>` would be rejected.

## Stampable Constraint

`where T : stampable` requires a type whose default can be replicated safely into contiguous storage. This is stronger than `struct`: a struct containing a `string` field is a value type, but is not stampable. The constraint enables bare array creation and `stackalloc` of an open type parameter, and must be satisfied when a generic caller forwards that parameter.

The compiler accepts stampability inferred from concrete and closed generic instance storage; the type argument does not need an explicit declaration modifier. See [Stampable Types](./stampability.md#a-generic-constraint) for a compiling generic allocation example and the distinction between inference and a declaration guarantee.

`stampable` can be combined with `struct`, but conflicts with `class`. It does not supply the separate `default` or constructor capabilities.

## Abstract Type Constraint

Constraining to an abstract type guarantees that the type parameter implements specific methods or properties. You can call any member declared on the abstract type through the constrained type parameter.

{{#tabs }}
{{#tab name="Mist" }}
```mist
{{#include ../../snippets/type-system/generic-constraints/abstract-constraint.mist}}
```
{{#endtab }}
{{#tab name="Emitted C#" }}
```csharp
{{#include ../../snippets/type-system/generic-constraints/abstract-constraint.cs}}
```
{{#endtab }}
{{#endtabs }}

Because `T` is constrained to `IOrderable`, the compiler allows calling `First.GetOrder()` inside the generic body. Any type passed as `T` must implement `IOrderable`.

## Base Class Constraint

Constraining to a base class guarantees that the type parameter extends that class. You can access any public or protected members of the base class through the constrained type parameter.

{{#tabs }}
{{#tab name="Mist" }}
```mist
{{#include ../../snippets/type-system/generic-constraints/base-class-constraint.mist}}
```
{{#endtab }}
{{#tab name="Emitted C#" }}
```csharp
{{#include ../../snippets/type-system/generic-constraints/base-class-constraint.cs}}
```
{{#endtab }}
{{#endtabs }}

With `where T : Animal`, only `Animal` or its subclasses (`Dog`, `Cat`, etc.) can be used as `T`. The generic body can call `GetSpecies()` because it's guaranteed to exist on any `Animal` subtype.

## Default Constraint

`where T : default` requires a type argument with a Mist default value. A declaration-level constraint makes `default(T)` available throughout the class body, including instance methods.

{{#tabs }}
{{#tab name="Mist" }}
```mist
{{#include ../../snippets/type-system/generic-constraints/default-constraint.mist}}
```
{{#endtab }}
{{#tab name="Emitted C#" }}
```csharp
{{#include ../../snippets/type-system/generic-constraints/default-constraint.cs}}
```
{{#endtab }}
{{#endtabs }}

Here `Get()` returns `0` for `int`, `""` for `string`, the designated `North` variant for `Direction`, and a fresh `Widget` whose `Value` is `7`. The generated class stores a `Func<T>` default factory supplied when the box is created, so its instance method can use the concrete argument's default.

The constraint can also appear on a generic method. Without a `default` constraint on the type parameter, `default(T)` is an analysis error. Neither `class`, `struct` nor `new()` supplies the separate default capability. A type with constructors and no default block cannot satisfy `default`, even if it satisfies `new()`. See [The Default Keyword](../advanced/default-keyword.md#the-where-t--default-generic-constraint) for method constraints and the rules for defaultable types.

## Constructor Constraints

### Parameterless `new()`

The `new()` constraint guarantees that the type parameter has a parameterless constructor, allowing you to create instances with `new T()` inside the generic body. The constructor may be implicit: a constructor-less type is initialized from its semantic default.

{{#tabs }}
{{#tab name="Mist" }}
```mist
{{#include ../../snippets/type-system/generic-constraints/new-parameterless.mist}}
```
{{#endtab }}
{{#tab name="Emitted C#" }}
```csharp
{{#include ../../snippets/type-system/generic-constraints/new-parameterless.cs}}
```
{{#endtab }}
{{#endtabs }}

Here `Creator<Box<Widget>>.Create()` constructs a box containing a fresh widget whose `Value` is `7`. The emitted C# retains the native `new()` constraint and receives a construction factory that the Mist caller supplies. Invoking that factory preserves any concrete default factories needed by the constructed type. With a user-declared constructor, the factory calls that constructor and preserves its behavior.

### Parameterized `new(Type)`

Mist extends C#'s constructor constraints with parameterized forms like `new(int)`. This guarantees the type has a constructor accepting the specified parameter types, allowing `new T(value)` in the generic body.

The compiler emits parameterized constructor constraints as factory delegates (`Func<ParamType, T>`) that are passed at the call site, alongside the generated constructor-contract metadata.

{{#tabs }}
{{#tab name="Mist" }}
```mist
{{#include ../../snippets/type-system/generic-constraints/new-parameterized.mist}}
```
{{#endtab }}
{{#tab name="Emitted C#" }}
```csharp
{{#include ../../snippets/type-system/generic-constraints/new-parameterized.cs}}
```
{{#endtab }}
{{#endtabs }}

The emitted C# invokes a `Func<int, T>` delegate to construct the value because C# does not support parameterized constructor constraints natively. At the call site, Mist generates a lambda that calls the required concrete constructor and supplies any default factories its generic class needs.

### Multiple Constructor Constraints

You can require multiple constructors on the same type parameter. This guarantees the type has several construction paths available in the generic body:

{{#tabs }}
{{#tab name="Mist" }}
```mist
{{#include ../../snippets/type-system/generic-constraints/multiple-constructors.mist}}
```
{{#endtab }}
{{#tab name="Emitted C#" }}
```csharp
{{#include ../../snippets/type-system/generic-constraints/multiple-constructors.cs}}
```
{{#endtab }}
{{#endtabs }}

Here `T` must have a parameterless constructor, a constructor taking `int`, and a constructor taking `bool`. The generic body can use all three forms of `new T(...)`.

Each constructor signature has its own construction factory. These factories are separate from `where T : default`: a constructor factory runs the chosen constructor, while a default factory produces the type's declared default.

## Multiple Constraints on a Single Type Parameter

You can combine multiple constraints on the same type parameter by separating them with commas. The type argument must satisfy all listed constraints simultaneously.

{{#tabs }}
{{#tab name="Mist" }}
```mist
{{#include ../../snippets/type-system/generic-constraints/multiple-constraints.mist}}
```
{{#endtab }}
{{#tab name="Emitted C#" }}
```csharp
{{#include ../../snippets/type-system/generic-constraints/multiple-constraints.cs}}
```
{{#endtab }}
{{#endtabs }}

Here `T` must be a reference type (`class`), implement `IPrintable`, and have a parameterless constructor (`new()`). Only types satisfying all three constraints can be used as `T`.

## Multiple `where` Clauses

When a generic type has multiple type parameters, each parameter can have its own `where` clause. Multiple `where` clauses are written one after another.

{{#tabs }}
{{#tab name="Mist" }}
```mist
{{#include ../../snippets/type-system/generic-constraints/multiple-where.mist}}
```
{{#endtab }}
{{#tab name="Emitted C#" }}
```csharp
{{#include ../../snippets/type-system/generic-constraints/multiple-where.cs}}
```
{{#endtab }}
{{#endtabs }}

`K` must implement `IOrderable` and `V` must have a parameterless constructor. Each `where` clause constrains exactly one type parameter.

## Constraints on Generic Methods

Constraints aren't limited to type declarations — generic methods can also have `where` clauses. The constraint appears after the parameter list and before the method body.

{{#tabs }}
{{#tab name="Mist" }}
```mist
{{#include ../../snippets/type-system/generic-constraints/method-constraints.mist}}
```
{{#endtab }}
{{#tab name="Emitted C#" }}
```csharp
{{#include ../../snippets/type-system/generic-constraints/method-constraints.cs}}
```
{{#endtab }}
{{#endtabs }}

The `where T : new()` clause on the method means callers must provide a type argument that has a parameterless constructor: `MethodConstraints.CreateItem<Widget>()`.

## Constraints on Abstract Types and Structs

Constraints can also appear on generic abstract type and struct declarations, not just classes.

{{#tabs }}
{{#tab name="Mist" }}
```mist
{{#include ../../snippets/type-system/generic-constraints/abstract-struct-constraints.mist}}
```
{{#endtab }}
{{#tab name="Emitted C#" }}
```csharp
{{#include ../../snippets/type-system/generic-constraints/abstract-struct-constraints.cs}}
```
{{#endtab }}
{{#endtabs }}

The same `where` clause syntax works uniformly across classes, structs, and abstract types.

## Summary

| Constraint | Meaning |
|---|---|
| `class` | Must be a reference type |
| `struct` | Must be a value type |
| `stampable` | Must have recursively stampable instance storage |
| `default` | Must have a Mist default value; enables `default(T)` |
| `AbstractTypeName` | Must implement the abstract type |
| `BaseClassName` | Must extend the base class |
| `new()` | Must have a parameterless constructor |
| `new(Type)` | Must have a constructor accepting that parameter type |
| `new(), new(Type)` | Multiple constructor constraints can be combined |

Constraints compose freely — combine `class`, abstract type, base class, and constructor constraints on the same type parameter to express precise requirements. Use multiple `where` clauses when constraining different type parameters independently.
