# Interfaces

Interfaces define contracts that classes must fulfill. An interface declares method signatures and property signatures without providing implementations — any class that lists the interface in its base clause must supply concrete implementations for every member. Interfaces enable polymorphism: code can accept any type that implements a given interface without knowing the concrete class at compile time.

An interface is its own type. When a class declares that it implements an interface, it establishes a nominal relationship — the class *is-a* instance of that interface type. This isn't structural: two classes with identical methods are not interchangeable unless they both explicitly declare the same interface. The relationship is opt-in and checked at compile time. A value of a class type can be assigned to a variable of an interface type it implements, passed to a function expecting that interface, or stored in a collection typed by the interface — all because the class named the interface in its declaration.

## Interface Declaration

An interface is declared with the `interface` keyword followed by a name and a body containing method and property signatures. Members have no access modifiers (they are implicitly public) and no method bodies.

{{#tabs }}
{{#tab name="Mist" }}
```mist
{{#include ../../snippets/object-model/interfaces/declaration.mist}}
```
{{#endtab }}
{{#tab name="Emitted C#" }}
```csharp
{{#include ../../snippets/object-model/interfaces/declaration.cs}}
```
{{#endtab }}
{{#endtabs }}

An interface can contain method signatures (like `Describe()` and `Area()`) and property signatures (like `Name { get; }`).

Interfaces can also extend other interfaces using the colon syntax, inheriting all members from the parent interface:

{{#tabs }}
{{#tab name="Mist" }}
```mist
{{#include ../../snippets/object-model/interfaces/extending.mist}}
```
{{#endtab }}
{{#tab name="Emitted C#" }}
```csharp
{{#include ../../snippets/object-model/interfaces/extending.cs}}
```
{{#endtab }}
{{#endtabs }}

A class implementing `IFullyDescribable` must provide implementations for both `DetailedDescribe()` (declared directly) and `Describe()` (inherited from `IDescribable`).

## Class Implementation

A class implements an interface by listing the interface name after a colon in the class declaration. The class must provide concrete implementations for every member declared in the interface.

{{#tabs }}
{{#tab name="Mist" }}
```mist
{{#include ../../snippets/object-model/interfaces/implementation.mist}}
```
{{#endtab }}
{{#tab name="Emitted C#" }}
```csharp
{{#include ../../snippets/object-model/interfaces/implementation.cs}}
```
{{#endtab }}
{{#endtabs }}

The `Person` class declares `: IDescribable` in its header and provides a `Describe()` method that satisfies the interface contract.

When a class implements a child interface, it must satisfy all members from the entire interface hierarchy:

{{#tabs }}
{{#tab name="Mist" }}
```mist
{{#include ../../snippets/object-model/interfaces/child-implementation.mist}}
```
{{#endtab }}
{{#tab name="Emitted C#" }}
```csharp
{{#include ../../snippets/object-model/interfaces/child-implementation.cs}}
```
{{#endtab }}
{{#endtabs }}

`Robot` implements `IFullyDescribable`, which extends `IDescribable`. The class provides both `Describe()` (from `IDescribable`) and `DetailedDescribe()` (from `IFullyDescribable`).

## Polymorphic Usage

Interface types can be used as variable types and parameter types, allowing code to work with any implementing class without knowing the concrete type.

{{#tabs }}
{{#tab name="Mist" }}
```mist
{{#include ../../snippets/object-model/interfaces/polymorphic-usage.mist}}
```
{{#endtab }}
{{#tab name="Emitted C#" }}
```csharp
{{#include ../../snippets/object-model/interfaces/polymorphic-usage.cs}}
```
{{#endtab }}
{{#endtabs }}

The `PrintDescription` method accepts any `IDescribable` — both `Person` and `Robot` can be passed because they both implement the interface. Similarly, a variable typed as `IDescribable` can hold a reference to any implementing class. The compiler dispatches the `Describe()` call to the correct implementation at runtime based on the actual object type.

Because `IFullyDescribable` extends `IDescribable`, a `Robot` (which implements `IFullyDescribable`) is also assignable to an `IDescribable`-typed variable or parameter — interface inheritance is transitive.
