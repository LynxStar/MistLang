# Inheritance

Inheritance lets you create a new class that reuses, extends, and specializes the behavior of an existing class. The derived class (child) inherits all public and private members from the base class (parent), and can add its own fields and methods or provide specialized implementations.

Mist uses the same colon syntax as C# for declaring inheritance relationships. A class can extend at most one base class.

## Extending a Base Class

To derive from a base class, place a colon (`:`) followed by the base class name after the class name in the declaration. The derived class inherits all members from the base class and can access public members directly.

{{#tabs }}
{{#tab name="Mist" }}
```mist
{{#include ../../snippets/object-model/inheritance/extending.mist}}
```
{{#endtab }}
{{#tab name="Emitted C#" }}
```csharp
{{#include ../../snippets/object-model/inheritance/extending.cs}}
```
{{#endtab }}
{{#endtabs }}

`Dog` extends `Animal`, inheriting `GetName()`, `GetAge()`, and `Describe()`. You can call any inherited public method on a `Dog` instance just as you would on an `Animal`. A derived type is also assignable to a variable or parameter of the base type — passing a `Dog` where an `Animal` is expected works without any cast.

## Base Constructor Chaining

When a derived class has a constructor, it must forward the required arguments to the base class constructor using `: base(...)`. This ensures the base class fields are properly initialized before the derived class adds its own state.

{{#tabs }}
{{#tab name="Mist" }}
```mist
{{#include ../../snippets/object-model/inheritance/base-constructor.mist}}
```
{{#endtab }}
{{#tab name="Emitted C#" }}
```csharp
{{#include ../../snippets/object-model/inheritance/base-constructor.cs}}
```
{{#endtab }}
{{#endtabs }}

`GuideDog` extends `Dog`, which itself extends `Animal` — forming a three-level hierarchy. The `GuideDog` constructor chains to `Dog`'s constructor via `: base(name, age, breed)`, which in turn chains to `Animal`'s constructor. Each level initializes its own fields while delegating the rest upward.

## Methods in Derived Classes

A derived class can define its own methods that call inherited methods to build on base class behavior. Each level in the hierarchy can compose richer functionality by reusing logic from its ancestors.

{{#tabs }}
{{#tab name="Mist" }}
```mist
{{#include ../../snippets/object-model/inheritance/method-overriding.mist}}
```
{{#endtab }}
{{#tab name="Emitted C#" }}
```csharp
{{#include ../../snippets/object-model/inheritance/method-overriding.cs}}
```
{{#endtab }}
{{#endtabs }}

Each level in the hierarchy defines its own describe method that calls inherited methods (`GetName()`, `GetAge()`, `GetBreed()`) to compose a richer description. Note that Mist does not support method overriding — derived classes cannot replace a base class method with the same signature. Instead, each class defines distinctly-named methods that build on inherited behavior.
