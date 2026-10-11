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

A generic slot is answered by a member with as many type parameters, matched by position whatever they are called. The answer may require of them only what the slot promises, since a call through the abstract type may pass any type the slot admits: it may repeat the slot's constraints or ask for less, but not more.

{{#tabs }}
{{#tab name="Mist" }}
```mist
{{#include ../../snippets/object-model/abstract-types/generic-slots.mist}}
```
{{#endtab }}
{{#tab name="Emitted C#" }}
```csharp
{{#include ../../snippets/object-model/abstract-types/generic-slots.cs}}
```
{{#endtab }}
{{#endtabs }}

`Pick<U>` restates the slot's constraint and `Name<V>` asks for none, so both answer. C# requires an implementing member's constraints to match exactly, so `Name` is reached from the interface through an explicit implementation that forwards to it. An answer adding a constraint the slot does not declare is refused: had `IPicker` declared `string Plain<T>(T value);` with no constraint, `Plain<V>(V value) where V : IShow` would not answer it.

```text
error MIST_PA_SLOT_CONSTRAINT_STRENGTHENED: 'Picker' does not fill slot 'string Plain<T>(T)' of 'IPicker': its 'Plain' requires 'IShow' of type parameter 'V', which the slot does not promise.
```

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

## Late Binding with `with` Blocks

Sometimes a type already exists and you want it to conform to an abstract type after the fact — without modifying the type's original declaration. A `with` block establishes **retroactive conformance**: it tells the compiler that a type satisfies an abstract type's contract by mapping its existing members to the abstract type's slots.

This is useful when you don't control the type's source, or when the abstract type was defined after the concrete type and you want to connect them without refactoring.

### Basic Syntax

A `with` block names the concrete type, the abstract type it should conform to, and one or more member mappings using the `as` keyword:

```
with ConcreteType : AbstractType
{
    sourceMember as targetSlot;
}
```

Each mapping declares that `sourceMember` on the concrete type fulfills `targetSlot` on the abstract type. The source member must have the same return type and parameter types as the target slot.

{{#tabs }}
{{#tab name="Mist" }}
```mist
{{#include ../../snippets/object-model/abstract-types/with-block-basic.mist}}
```
{{#endtab }}
{{#tab name="Emitted C#" }}
```csharp
{{#include ../../snippets/object-model/abstract-types/with-block-basic.cs}}
```
{{#endtab }}
{{#endtabs }}

`Logger` doesn't declare `: INamed` in its class header, but the `with` block establishes that `Logger` conforms to `INamed` by mapping `GetLabel` to the `Name` slot. After the `with` block, a `Logger` value can be assigned to an `INamed`-typed variable and used polymorphically.

### Interaction with Defaults

When the abstract type has default methods, only the required slots (those without bodies) need mappings in the `with` block. Default methods are inherited automatically — just as they would be for a type that directly implements the abstract type.

{{#tabs }}
{{#tab name="Mist" }}
```mist
{{#include ../../snippets/object-model/abstract-types/with-block-defaults.mist}}
```
{{#endtab }}
{{#tab name="Emitted C#" }}
```csharp
{{#include ../../snippets/object-model/abstract-types/with-block-defaults.cs}}
```
{{#endtab }}
{{#endtabs }}

`ITaggable` has two members: `Tag()` (a required slot) and `DisplayTag()` (a default). The `with` block only maps `GetWidgetTag as Tag` — the default `DisplayTag()` is inherited without an explicit mapping. If you want to override a default, you can provide an additional mapping for it.

### Polymorphic Usage

After a `with` block establishes conformance, values of the adapted type can be used anywhere the abstract type is expected — passed to functions, stored in abstract-typed variables, or placed in collections. Multiple types can be adapted to the same abstract type with separate `with` blocks:

{{#tabs }}
{{#tab name="Mist" }}
```mist
{{#include ../../snippets/object-model/abstract-types/with-block-polymorphism.mist}}
```
{{#endtab }}
{{#tab name="Emitted C#" }}
```csharp
{{#include ../../snippets/object-model/abstract-types/with-block-polymorphism.cs}}
```
{{#endtab }}
{{#endtabs }}

Both `Sensor` and `Actuator` are adapted to `IDescribable` via separate `with` blocks. The `PrintInfo` function accepts any `IDescribable`, so both types can be passed to it — the compiler dispatches to the correct mapped member at runtime.

### Multiple `with` Blocks on the Same Type

A single type can have multiple `with` blocks, each establishing conformance with a different abstract type. This lets you retroactively make a type satisfy several contracts without touching its declaration:

```
with MyType : IFirstAbstract
{
    MethodA as SlotA;
}

with MyType : ISecondAbstract
{
    MethodB as SlotB;
}
```

Each `with` block is independent — the type accumulates all the conformances declared across its `with` blocks.
