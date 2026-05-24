# Generic Constraints

Generic constraints restrict what types can be used as type arguments. By adding a `where` clause to a generic declaration, you tell the compiler that the type parameter must satisfy certain requirements — being a reference type, implementing an interface, extending a base class, or having a specific constructor. This lets you safely call methods, access members, or construct instances of the type parameter within the generic body.

## The `where` Clause Syntax

Constraints are declared after the type parameter list using the `where T : constraint` syntax. The `where` clause appears after the class body's opening parameters but before the body braces.

```
class Name<T> where T : constraint {
    // T is guaranteed to satisfy the constraint
}
```

The constraint can be a type name (interface, trait, base class), a structural constraint (`class`, `struct`), or a constructor constraint (`new()`). Multiple constraints on the same type parameter are separated by commas.

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

## Interface Constraint

Constraining to an interface guarantees that the type parameter implements specific methods or properties. You can call any member declared on the interface through the constrained type parameter.

{{#tabs }}
{{#tab name="Mist" }}
```mist
{{#include ../../snippets/type-system/generic-constraints/interface-constraint.mist}}
```
{{#endtab }}
{{#tab name="Emitted C#" }}
```csharp
{{#include ../../snippets/type-system/generic-constraints/interface-constraint.cs}}
```
{{#endtab }}
{{#endtabs }}

Because `T` is constrained to `IOrderable`, the compiler allows calling `First.GetOrder()` inside the generic body. Any type passed as `T` must implement `IOrderable`.

## Trait Constraint

Traits can also be used as constraints. A type satisfies a trait constraint if it structurally matches the trait's members (or has an explicit `with` mapping). The compiler emits the trait as an interface constraint in C#.

{{#tabs }}
{{#tab name="Mist" }}
```mist
{{#include ../../snippets/type-system/generic-constraints/trait-constraint.mist}}
```
{{#endtab }}
{{#tab name="Emitted C#" }}
```csharp
{{#include ../../snippets/type-system/generic-constraints/trait-constraint.cs}}
```
{{#endtab }}
{{#endtabs }}

Any type with a public `string Describe()` method structurally satisfies the `Describable` trait and can be used as `T` in `Describer<T>`.

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

## Constructor Constraints

### Parameterless `new()`

The `new()` constraint guarantees that the type parameter has a parameterless constructor, allowing you to create instances with `new T()` inside the generic body.

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

### Parameterized `new(Type)`

Mist extends C#'s constructor constraints with parameterized forms like `new(int)`. This guarantees the type has a constructor accepting the specified parameter types, allowing `new T(value)` in the generic body.

The compiler emits parameterized constructor constraints as factory delegates (`Func<ParamType, T>`) that are passed at the call site.

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

Notice that the emitted C# uses a `Func<int, T>` delegate rather than a native `new()` constraint — C# doesn't support parameterized constructor constraints natively, so Mist lowers them to factory functions. At the call site, the compiler automatically generates the lambda: `new IntFactory<Widget>((arg0) => new Widget(arg0))`.

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

## Constraints on Interfaces and Structs

Constraints can also appear on generic interface and struct declarations, not just classes.

{{#tabs }}
{{#tab name="Mist" }}
```mist
{{#include ../../snippets/type-system/generic-constraints/interface-struct-constraints.mist}}
```
{{#endtab }}
{{#tab name="Emitted C#" }}
```csharp
{{#include ../../snippets/type-system/generic-constraints/interface-struct-constraints.cs}}
```
{{#endtab }}
{{#endtabs }}

The same `where` clause syntax works uniformly across classes, structs, interfaces, and traits.

## Summary

| Constraint | Meaning |
|---|---|
| `class` | Must be a reference type |
| `struct` | Must be a value type |
| `InterfaceName` | Must implement the interface |
| `TraitName` | Must structurally satisfy the trait |
| `BaseClassName` | Must extend the base class |
| `new()` | Must have a parameterless constructor |
| `new(Type)` | Must have a constructor accepting that parameter type |
| `new(), new(Type)` | Multiple constructor constraints can be combined |

Constraints compose freely — combine `class`, interface, trait, base class, and constructor constraints on the same type parameter to express precise requirements. Use multiple `where` clauses when constraining different type parameters independently.
