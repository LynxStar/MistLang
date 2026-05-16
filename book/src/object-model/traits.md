# Traits

Traits define structural typing contracts. Unlike interfaces, which require a class to explicitly declare conformance, a trait is satisfied by any type whose public members match the trait's declared signatures. This gives you the flexibility of duck typing with the safety of compile-time verification — if a type has the right shape, it fits the trait automatically.

## Trait Declaration

A trait is declared with the `trait` keyword followed by a name and a body containing field signatures, method signatures, and property signatures. Traits can also extend other traits using the colon syntax.

{{#tabs }}
{{#tab name="Mist" }}
```mist
{{#include ../../snippets/object-model/traits/declaration.mist}}
```
{{#endtab }}
{{#tab name="Emitted C#" }}
```csharp
{{#include ../../snippets/object-model/traits/declaration.cs}}
```
{{#endtab }}
{{#endtabs }}

A trait emits as a C# interface. Field signatures in the trait become read-only properties (`{ get; }`) on the emitted interface. Method signatures emit as interface method declarations.

Traits can extend other traits, inheriting all member requirements from their parents:

{{#tabs }}
{{#tab name="Mist" }}
```mist
{{#include ../../snippets/object-model/traits/extending.mist}}
```
{{#endtab }}
{{#tab name="Emitted C#" }}
```csharp
{{#include ../../snippets/object-model/traits/extending.cs}}
```
{{#endtab }}
{{#endtabs }}

A type satisfying `ReadWriteStream` must provide `Read()`, `Write(string)`, and `Flush()` — all members from the trait and its parents.

## Structural Satisfaction

A type satisfies a trait when its public members match the trait's declared signatures by name and type. No explicit `: TraitName` clause is needed — the compiler verifies structural compatibility automatically.

{{#tabs }}
{{#tab name="Mist" }}
```mist
{{#include ../../snippets/object-model/traits/structural-satisfaction.mist}}
```
{{#endtab }}
{{#tab name="Emitted C#" }}
```csharp
{{#include ../../snippets/object-model/traits/structural-satisfaction.cs}}
```
{{#endtab }}
{{#endtabs }}

The `User` class never mentions `Printable` in its declaration, yet the compiler determines it satisfies the trait and emits `: Printable` in the C# output. The class's `Name` and `Id` properties match the trait's field signatures, so the structural contract is fulfilled.

If a type is missing a required member, the compiler reports an error at the point where the type is used as the trait type — you'll see a diagnostic like `Type 'X' does not satisfy trait 'Y'. Missing members: Z.`

## The `with` Block

When a type has the right member types but different names, a `with` block provides explicit member mapping. This is a top-level declaration separate from the class body.

{{#tabs }}
{{#tab name="Mist" }}
```mist
{{#include ../../snippets/object-model/traits/with-block-fields.mist}}
```
{{#endtab }}
{{#tab name="Emitted C#" }}
```csharp
{{#include ../../snippets/object-model/traits/with-block-fields.cs}}
```
{{#endtab }}
{{#endtabs }}

The `with` block syntax is `with TypeName : TraitName { sourceMember as traitMember; }`. Each mapping line declares that the type's `sourceMember` fulfills the trait's `traitMember`. In the emitted C#, these become explicit interface implementations that delegate to the actual members on the class.

The `with` block also works for method mappings:

{{#tabs }}
{{#tab name="Mist" }}
```mist
{{#include ../../snippets/object-model/traits/with-block-methods.mist}}
```
{{#endtab }}
{{#tab name="Emitted C#" }}
```csharp
{{#include ../../snippets/object-model/traits/with-block-methods.cs}}
```
{{#endtab }}
{{#endtabs }}

Here `Logger.Log` is mapped to `Writable.Write`. The emitted C# generates an explicit interface implementation that forwards the call to the actual `Log` method.

## Polymorphic Usage

Trait types can be used as variable types and parameter types, accepting any value whose type satisfies the trait — whether through structural matching or a `with` block.

{{#tabs }}
{{#tab name="Mist" }}
```mist
{{#include ../../snippets/object-model/traits/polymorphic-usage.mist}}
```
{{#endtab }}
{{#tab name="Emitted C#" }}
```csharp
{{#include ../../snippets/object-model/traits/polymorphic-usage.cs}}
```
{{#endtab }}
{{#endtabs }}

Both `User` (structurally matched) and `Product` (mapped via `with`) can be assigned to a `Printable`-typed variable or passed to a function expecting `Printable`. The compiler dispatches member access through the interface at runtime.

## Traits vs Interfaces

Traits and interfaces both define contracts, but they differ in how conformance is established:

| | Traits | Interfaces |
|---|---|---|
| **Typing** | Structural — a type satisfies a trait if its members match | Nominal — a class must explicitly list the interface |
| **Declaration** | No `: TraitName` needed on the class | Requires `: InterfaceName` in the class header |
| **Member mapping** | `with` block for name mismatches | Not applicable — names must match exactly |
| **Emitted C#** | Interface + compiler-inserted `: TraitName` on satisfying types | Interface + developer-declared `: InterfaceName` |
| **Retroactive conformance** | Yes — existing types can satisfy new traits without modification | No — the class source must be changed to add the interface |

The key advantage of traits is **retroactive conformance**: you can define a new trait and existing types will satisfy it automatically if they already have the right members. With interfaces, you'd need to modify each class to add the interface to its base clause. The `with` block extends this further — even types with differently-named members can be adapted to a trait without touching the original class.

Both traits and interfaces emit as C# interfaces in the generated code. The difference is purely in how the Mist compiler determines and enforces conformance at compile time.
