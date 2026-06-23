# Abstract Types

Abstract types are Mist's unified contract mechanism. They replace both interfaces and traits from earlier designs with a single concept built on **slot semantics**: members without bodies are *slots* that implementers must fill, and members with bodies are *default implementations* that implementers may optionally override.

An abstract type declares what a family of concrete types must provide. Any concrete type that lists the abstract type in its base clause commits to filling every required slot. The abstract type can then be used as a variable type, parameter type, or return type — enabling polymorphic code that works with any conforming type.

Abstract types emit as C# interfaces. Default methods emit as C# default interface methods (DIM), with forwarding stubs generated on concrete types to ensure consistent dispatch regardless of reference type.

## Declaration

An abstract type is declared with the `abstract` keyword followed by a name and a braced body. Members without bodies are **slots** — required holes that every implementer must fill:

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

Any type declaring `: IDescribable` must provide implementations for both `Describe()` and `Priority()`. These are required slots — there is no body, so the implementer must supply one.

## Property Slots

Abstract types can declare property signatures as slots. Property slots use the `{ get; }` or `{ get; set; }` accessor syntax and cannot have default implementations — they must always be filled by the implementer:

{{#tabs }}
{{#tab name="Mist" }}
```mist
{{#include ../../snippets/object-model/abstract-types/property-slots.mist}}
```
{{#endtab }}
{{#tab name="Emitted C#" }}
```csharp
{{#include ../../snippets/object-model/abstract-types/property-slots.cs}}
```
{{#endtab }}
{{#endtabs }}

A type implementing `ILabeled` must provide both the `Label` and `Id` properties along with the `Describe()` method. Attempting to give a property slot a body is a compile error — only methods can have defaults.

## Default Implementations

A method declared with a body in an abstract type is a **default implementation**. Implementers are not required to provide their own version — if they don't, the default is used:

{{#tabs }}
{{#tab name="Mist" }}
```mist
{{#include ../../snippets/object-model/abstract-types/defaults.mist}}
```
{{#endtab }}
{{#tab name="Emitted C#" }}
```csharp
{{#include ../../snippets/object-model/abstract-types/defaults.cs}}
```
{{#endtab }}
{{#endtabs }}

`IGreeting` declares `Name()` as a required slot and `Greet()` as a default. Any implementer must provide `Name()`, but `Greet()` is optional — if omitted, the default body is used.

Default implementations in Mist use **Rust-style semantics**: the default IS the implementation, period. There is no "lens" behavior — calling the method through the concrete type and through the abstract type reference produces identical results. The compiler ensures this by generating a forwarding stub on concrete types that don't override the default.

**Important:** Only methods can have default implementations. Property slots must always be filled by the implementer. Attempting to give a property a body in an abstract type is a compile error:

```
Property slots cannot have default implementations. Only methods can have defaults.
```

### Taking a Default

A concrete type can rely on a default method without providing its own implementation:

{{#tabs }}
{{#tab name="Mist" }}
```mist
{{#include ../../snippets/object-model/abstract-types/taking-defaults.mist}}
```
{{#endtab }}
{{#tab name="Emitted C#" }}
```csharp
{{#include ../../snippets/object-model/abstract-types/taking-defaults.cs}}
```
{{#endtab }}
{{#endtabs }}

`Person` fills the required `Name()` slot but does not provide `Greet()`. The default implementation is used — calling `person.Greet()` returns `"Hello, I am Alice"`. This works identically whether called on the concrete type or through an `IGreeting` reference.

### Overriding a Default

A concrete type can provide its own implementation for a default method, which takes precedence over the default:

{{#tabs }}
{{#tab name="Mist" }}
```mist
{{#include ../../snippets/object-model/abstract-types/overriding-defaults.mist}}
```
{{#endtab }}
{{#tab name="Emitted C#" }}
```csharp
{{#include ../../snippets/object-model/abstract-types/overriding-defaults.cs}}
```
{{#endtab }}
{{#endtabs }}

`Robot` provides its own `Greet()` — calling `robot.Greet()` returns `"Beep boop, I am T-800"` regardless of whether the value is accessed through the concrete type or through the abstract type reference.

## Abstract Type Inheritance

Abstract types can inherit from other abstract types using the colon syntax. The child inherits all slots and defaults from its parents:

{{#tabs }}
{{#tab name="Mist" }}
```mist
{{#include ../../snippets/object-model/abstract-types/inheritance.mist}}
```
{{#endtab }}
{{#tab name="Emitted C#" }}
```csharp
{{#include ../../snippets/object-model/abstract-types/inheritance.cs}}
```
{{#endtab }}
{{#endtabs }}

A type implementing `IFullyDescribable` must fill both `DetailedDescribe()` (declared directly) and `Describe()` (inherited from `IDescribable`). Inheritance is transitive — an implementer of `IFullyDescribable` is also assignable to a variable typed as `IDescribable`.

## Implementation

A concrete type implements an abstract type by listing it after a colon in its declaration. The type must fill every required slot — all method slots and all property slots:

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

`Rectangle` fills the `Name` property slot, the `Area()` method slot, and the `Describe()` method slot. If any were missing, the compiler would report an error listing the unfilled slots.

## Polymorphic Usage

Abstract types can be used as variable types and parameter types, accepting any value whose type implements the abstract type:

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

Both `Person` and `Robot` implement `IDescribable`, so they can be assigned to an `IDescribable`-typed variable or passed to a method expecting `IDescribable`. The compiler dispatches `Describe()` to the correct implementation at runtime.

Conformance is strictly nominal — a type must explicitly declare `: IDescribable` or have conformance established via a `with` block. Having matching method names and signatures is not sufficient (no structural typing).

## Late Binding via With Blocks

A `with` block establishes retroactive conformance between an existing type and an abstract type. This is useful when you can't (or don't want to) modify the original type to add the abstract type to its base clause:

{{#tabs }}
{{#tab name="Mist" }}
```mist
{{#include ../../snippets/object-model/abstract-types/with-block.mist}}
```
{{#endtab }}
{{#tab name="Emitted C#" }}
```csharp
{{#include ../../snippets/object-model/abstract-types/with-block.cs}}
```
{{#endtab }}
{{#endtabs }}

The `with` block maps `Logger.GetLabel` to `INamed.Name` using the `as` keyword. After this declaration, `Logger` can be assigned to `INamed`-typed variables and parameters.

The `with` block syntax is:

```
with TypeName : AbstractType
{
    sourceMember as slotMember;
}
```

Each mapping declares that the type's `sourceMember` fulfills the abstract type's `slotMember`. The key property of `with` blocks is retroactive conformance — you can adapt existing types to new abstract types without touching the original source code.

When an abstract type has default methods, a `with` block only requires mappings for the required slots. Default methods don't need mappings — they're used automatically unless explicitly mapped to an override.

## Diamond Inheritance

When a concrete type implements multiple abstract types that share a common parent, it only needs to provide a single implementation for the shared slot:

```mist
abstract IBase
{
    string Id();
}

abstract ILeft : IBase
{
    string LeftInfo();
}

abstract IRight : IBase
{
    string RightInfo();
}

public class Combined : ILeft, IRight
{
    // Single Id() fills the slot from both ILeft and IRight
    public string Id()
    {
        return "combined-1";
    }

    public string LeftInfo()
    {
        return "left";
    }

    public string RightInfo()
    {
        return "right";
    }
}
```

`Combined` implements both `ILeft` and `IRight`, which both inherit from `IBase`. The single `Id()` implementation satisfies the `IBase.Id()` requirement coming from both paths of the diamond. The compiler does not require separate implementations for each path.

## Comparison Table

| | Mist `abstract` | C# `interface` | Rust `trait` |
|---|---|---|---|
| **Keyword** | `abstract` | `interface` | `trait` |
| **Conformance** | Nominal (explicit `: AbstractType` or `with` block) | Nominal (explicit `: IInterface`) | Nominal (`impl Trait for Type`) |
| **Retroactive conformance** | Yes — via `with` blocks | No — must modify the type's source | Yes — `impl Trait for Type` in any crate |
| **Default methods** | Yes — method bodies in abstract type | Yes — default interface methods (C# 8+) | Yes — method bodies in trait |
| **Property defaults** | No — property slots must always be filled | Yes — properties can have default implementations | N/A (no properties, only methods) |
| **Structural typing** | No — always requires explicit declaration | No | No |
| **Diamond inheritance** | Single implementation satisfies all paths | Single implementation satisfies all paths | No diamond — traits use explicit disambiguation |
| **Generic parameters** | Yes | Yes | Yes |
| **Associated types** | Yes | No (simulated via generics) | Yes |
| **Dispatch semantics** | Reference-type-independent (Rust-style) | Reference-type-dependent (DIM only on interface reference) | Static dispatch by default, `dyn Trait` for dynamic |
| **Emitted form** | C# interface + forwarding stubs | C# interface (direct) | Monomorphized code / vtable |

The key distinction from C# interfaces: in C#, a default interface method is only callable through an interface reference — you must cast to the interface to call it. In Mist, the compiler generates forwarding stubs so default methods are callable directly on the concrete type with no casting needed. The behavior is always the same regardless of reference type.

The key distinction from Rust traits: Mist does not support structural typing or auto-trait derivation. Conformance is always explicit. However, `with` blocks provide a similar retroactive conformance story to Rust's `impl Trait for Type`.
