# Abstract Types

Abstract types define contracts that concrete types must fulfill. An abstract type declares method signatures and property signatures without providing implementations — any type that lists the abstract type in its base clause must supply concrete implementations for every member. Abstract types enable polymorphism: code can accept any type that implements a given abstract type without knowing the concrete type at compile time.

An abstract type is its own type. When a concrete type declares that it implements an abstract type, it establishes a nominal relationship — the type *is-a* instance of that abstract type. This isn't structural: two types with identical methods are not interchangeable unless they both explicitly declare the same abstract type. The relationship is opt-in and checked at compile time. A value of a concrete type can be assigned to a variable of an abstract type it implements, passed to a function expecting that abstract type, or stored in a collection typed by the abstract type — all because the type named the abstract type in its declaration.

Classes and structs can both implement abstract types.

## Abstract Type Declaration

An abstract type is declared with the `abstract` keyword followed by a name and a body containing method and property signatures. Members have no access modifiers (they are implicitly public) and no method bodies.

{{#tabs }}
{{#tab name="Mist" }}
```mist
{{#include ../../snippets/object-model/abstract-types/declaration.mist}}
```
{{#endtab }}
{{#tab name="Emitted C#" }}
```csharp
{{#include ../../snippets/object-model/abstract-types/declaration.cs}}
```
{{#endtab }}
{{#endtabs }}

An abstract type can contain method signatures (like `Describe()` and `Area()`) and property signatures (like `Name { get; }`).

## Abstract Type Inheritance

Abstract types can extend other abstract types using the colon syntax, inheriting all members from the parent:

{{#tabs }}
{{#tab name="Mist" }}
```mist
{{#include ../../snippets/object-model/abstract-types/extending.mist}}
```
{{#endtab }}
{{#tab name="Emitted C#" }}
```csharp
{{#include ../../snippets/object-model/abstract-types/extending.cs}}
```
{{#endtab }}
{{#endtabs }}

`IFullyDescribable` extends `IDescribable`, so it inherits the `Describe()` method. Any type implementing `IFullyDescribable` must provide implementations for both `DetailedDescribe()` (declared directly) and `Describe()` (inherited from the parent).

Abstract type inheritance is transitive — a type implementing `IFullyDescribable` is also assignable to an `IDescribable`-typed variable or parameter, since `IFullyDescribable` is a subtype of `IDescribable`.

## Implementation

A concrete type implements an abstract type by listing the abstract type name after a colon in its declaration. The type must provide concrete implementations for every member declared in the abstract type.

{{#tabs }}
{{#tab name="Mist" }}
```mist
{{#include ../../snippets/object-model/abstract-types/implementation.mist}}
```
{{#endtab }}
{{#tab name="Emitted C#" }}
```csharp
{{#include ../../snippets/object-model/abstract-types/implementation.cs}}
```
{{#endtab }}
{{#endtabs }}

The `Person` class declares `: IDescribable` in its header and provides a `Describe()` method that satisfies the abstract type contract.

When a type implements a child abstract type, it must satisfy all members from the entire abstract type hierarchy:

{{#tabs }}
{{#tab name="Mist" }}
```mist
{{#include ../../snippets/object-model/abstract-types/child-implementation.mist}}
```
{{#endtab }}
{{#tab name="Emitted C#" }}
```csharp
{{#include ../../snippets/object-model/abstract-types/child-implementation.cs}}
```
{{#endtab }}
{{#endtabs }}

`Robot` implements `IFullyDescribable`, which extends `IDescribable`. The class provides both `Describe()` (from `IDescribable`) and `DetailedDescribe()` (from `IFullyDescribable`).

## Polymorphic Usage

Abstract types can be used as variable types and parameter types, allowing code to work with any implementing type without knowing the concrete type.

{{#tabs }}
{{#tab name="Mist" }}
```mist
{{#include ../../snippets/object-model/abstract-types/polymorphic-usage.mist}}
```
{{#endtab }}
{{#tab name="Emitted C#" }}
```csharp
{{#include ../../snippets/object-model/abstract-types/polymorphic-usage.cs}}
```
{{#endtab }}
{{#endtabs }}

The `PrintDescription` method accepts any `IDescribable` — both `Person` and `Robot` can be passed because they both implement the abstract type. Similarly, a variable typed as `IDescribable` can hold a reference to any implementing type. The compiler dispatches the `Describe()` call to the correct implementation at runtime based on the actual object type.

## Default Implementations

Abstract types can provide default method implementations — methods with bodies that concrete types inherit automatically without needing to supply their own version. Only methods can have defaults; properties cannot.

A default method is written just like a regular method declaration, but inside an abstract type body:

{{#tabs }}
{{#tab name="Mist" }}
```mist
{{#include ../../snippets/object-model/abstract-types/default-method.mist}}
```
{{#endtab }}
{{#tab name="Emitted C#" }}
```csharp
{{#include ../../snippets/object-model/abstract-types/default-method.cs}}
```
{{#endtab }}
{{#endtabs }}

`IGreetable` declares `Name()` as a slot (no body — must be filled by implementers) and `Greet()` as a default method (has a body). `FriendlyDog` implements `IGreetable` and provides only `Name()` — it inherits the `Greet()` default automatically.

### Overriding Defaults

A concrete type can override a default method by providing its own implementation with the same signature:

{{#tabs }}
{{#tab name="Mist" }}
```mist
{{#include ../../snippets/object-model/abstract-types/default-override.mist}}
```
{{#endtab }}
{{#tab name="Emitted C#" }}
```csharp
{{#include ../../snippets/object-model/abstract-types/default-override.cs}}
```
{{#endtab }}
{{#endtabs }}

`FormalCat` provides its own `Greet()` method, which replaces the default from `IGreetable`. The override is used regardless of how the value is accessed.

### Dispatch Semantics

Default methods follow Rust-style semantics: calling through a concrete type reference or through an abstract type reference produces identical behavior. There is no "lens" or split dispatch — the same implementation runs either way.

{{#tabs }}
{{#tab name="Mist" }}
```mist
{{#include ../../snippets/object-model/abstract-types/default-dispatch.mist}}
```
{{#endtab }}
{{#tab name="Emitted C#" }}
```csharp
{{#include ../../snippets/object-model/abstract-types/default-dispatch.cs}}
```
{{#endtab }}
{{#endtabs }}

Both `dog.Greet()` and `greeter.Greet()` (where `greeter` is typed as `IGreetable`) invoke the same default implementation. If the concrete type overrides the default, the override is used through both paths.

### Restrictions

- **Only methods can have defaults** — property slots cannot have bodies inside abstract types. Attempting to add a body to a property declaration produces a compiler error.
- **Defaults propagate through inheritance** — if an abstract type inherits from another abstract type that has defaults, implementers of the child type inherit those defaults as well without needing to re-declare them.
