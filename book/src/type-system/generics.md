# Generics

Generics let you write type-safe code that works with any type without sacrificing compile-time checking. Instead of duplicating a class or method for every concrete type, you declare one or more type parameters that callers supply when they use the type. Mist generics map directly to C# generics — the emitted code preserves the same type parameter structure.

## Generic Classes

A generic class declares one or more type parameters in angle brackets after the class name. Inside the class body, the type parameter can be used as a field type, parameter type, or return type.

{{#tabs }}
{{#tab name="Mist" }}
```mist
{{#include ../../snippets/type-system/generics/classes.mist}}
```
{{#endtab }}
{{#tab name="Emitted C#" }}
```csharp
{{#include ../../snippets/type-system/generics/classes.cs}}
```
{{#endtab }}
{{#endtabs }}

The type parameter `T` acts as a placeholder. When you create a `GenericBox<int>`, the compiler treats `item` as an `int`; when you create a `GenericBox<string>`, it's a `string`. The emitted C# is structurally identical — Mist generics are a direct pass-through to the .NET generics system.

## Generic Structs

Structs can also be generic. A generic struct follows the same syntax as a generic class but uses the `struct` keyword. Since structs are value types, a `Pair<int, int>` is stored inline without heap allocation.

{{#tabs }}
{{#tab name="Mist" }}
```mist
{{#include ../../snippets/type-system/generics/structs.mist}}
```
{{#endtab }}
{{#tab name="Emitted C#" }}
```csharp
{{#include ../../snippets/type-system/generics/structs.cs}}
```
{{#endtab }}
{{#endtabs }}

Generic structs are useful for lightweight containers like pairs, coordinates, or result wrappers where heap allocation is undesirable.

## Generic Interfaces

Interfaces can declare type parameters, allowing implementors to provide concrete types when they satisfy the contract.

{{#tabs }}
{{#tab name="Mist" }}
```mist
{{#include ../../snippets/type-system/generics/interfaces.mist}}
```
{{#endtab }}
{{#tab name="Emitted C#" }}
```csharp
{{#include ../../snippets/type-system/generics/interfaces.cs}}
```
{{#endtab }}
{{#endtabs }}

A class implements a generic interface by specifying the concrete type argument (or forwarding its own type parameter). Here, `GenericBox<T>` implements `IGenericContainer<T>` by forwarding `T`:

{{#tabs }}
{{#tab name="Mist" }}
```mist
{{#include ../../snippets/type-system/generics/implementing.mist}}
```
{{#endtab }}
{{#tab name="Emitted C#" }}
```csharp
{{#include ../../snippets/type-system/generics/implementing.cs}}
```
{{#endtab }}
{{#endtabs }}

Because `GenericBox<T>` implements `IGenericContainer<T>`, you can assign a `GenericBox<int>` to a variable typed `IGenericContainer<int>` and call `GetItem()` through the interface.

## Generic Traits

Traits can declare type parameters with the same syntax. A type satisfies a generic trait when its members match the trait's signatures with the type parameter substituted.

{{#tabs }}
{{#tab name="Mist" }}
```mist
{{#include ../../snippets/type-system/generics/traits.mist}}
```
{{#endtab }}
{{#tab name="Emitted C#" }}
```csharp
{{#include ../../snippets/type-system/generics/traits.cs}}
```
{{#endtab }}
{{#endtabs }}

A class with `int CompareTo(int other)` structurally satisfies `Comparable<int>`. Multiple type parameters work the same as with classes and interfaces — `Converter<TIn, TOut>` requires both to be supplied at the satisfaction site.

## Generic Methods

Methods can declare their own type parameters independently of the enclosing class. This is useful for utility methods that operate on arbitrary types without requiring the entire class to be generic.

{{#tabs }}
{{#tab name="Mist" }}
```mist
{{#include ../../snippets/type-system/generics/methods.mist}}
```
{{#endtab }}
{{#tab name="Emitted C#" }}
```csharp
{{#include ../../snippets/type-system/generics/methods.cs}}
```
{{#endtab }}
{{#endtabs }}

The type parameter `<T>` appears after the method name and before the parameter list. The compiler infers `T` from the argument type at the call site in most cases, but you can also specify it explicitly (see below).

## Type Arguments at Call Sites

When constructing a generic type or calling a generic method, you supply type arguments in angle brackets. The compiler uses these to produce a concrete instantiation.

{{#tabs }}
{{#tab name="Mist" }}
```mist
{{#include ../../snippets/type-system/generics/type-arguments.mist}}
```
{{#endtab }}
{{#tab name="Emitted C#" }}
```csharp
{{#include ../../snippets/type-system/generics/type-arguments.cs}}
```
{{#endtab }}
{{#endtabs }}

Type arguments appear inside `<` and `>` after the type or method name. For generic type construction (e.g., `new GenericBox<int>(42)`), you must always supply type arguments explicitly. For generic method calls, the compiler can often infer type arguments from the arguments you pass — see [Type Argument Inference](#type-argument-inference) below.

## Multiple Type Parameters

A generic type or method can declare multiple type parameters separated by commas. Each parameter is independent and can be supplied with a different concrete type.

{{#tabs }}
{{#tab name="Mist" }}
```mist
{{#include ../../snippets/type-system/generics/multiple-params.mist}}
```
{{#endtab }}
{{#tab name="Emitted C#" }}
```csharp
{{#include ../../snippets/type-system/generics/multiple-params.cs}}
```
{{#endtab }}
{{#endtabs }}

At the call site, you supply each type argument in order:

{{#tabs }}
{{#tab name="Mist" }}
```mist
{{#include ../../snippets/type-system/generics/multi-param-demo.mist}}
```
{{#endtab }}
{{#tab name="Emitted C#" }}
```csharp
{{#include ../../snippets/type-system/generics/multi-param-demo.cs}}
```
{{#endtab }}
{{#endtabs }}

Multiple type parameters are common for collection-like types (dictionaries, result types) and mapping functions. The type parameters are positional — `KeyValueStore<string, int>` means `K` is `string` and `V` is `int`.

## Type Argument Inference

When you call a generic method, the compiler can often deduce the type arguments from the types of the arguments you pass. This means you can omit the angle brackets entirely and let the compiler figure out the concrete types.

{{#tabs }}
{{#tab name="Mist" }}
```mist
{{#include ../../snippets/type-system/generics/inference.mist}}
```
{{#endtab }}
{{#tab name="Emitted C#" }}
```csharp
{{#include ../../snippets/type-system/generics/inference.cs}}
```
{{#endtab }}
{{#endtabs }}

In the example above, `Identity(42)` infers `T = int` from the argument, and `Unwrap(box)` infers `T = string` by examining the constructed type `Container<string>` passed as the argument.

### When inference applies

Type argument inference applies only to **method calls** — both instance methods and static methods. It does not apply to generic type construction. You must always write `new Container<int>(42)` with explicit type arguments; the compiler will not infer them from constructor arguments.

### When explicit type arguments are required

You still need to supply explicit type arguments when:

- **Inference fails** — the compiler cannot determine a unique type for every type parameter from the arguments alone. This happens when a type parameter appears only in the return type and not in any parameter position.
- **Conflicting arguments** — two arguments imply different concrete types for the same type parameter. The compiler reports an error rather than guessing.

When inference fails, the compiler emits a diagnostic telling you which type parameters could not be inferred, so you know exactly which type arguments to add.

Generic constraints (restricting what types can be used as type arguments) are covered in the [Generic Constraints](generic-constraints.md) chapter.
