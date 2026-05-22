# Classes vs Structs

Mist has two kinds of concrete types: classes and structs. They share the same syntax for fields, methods, and constructors (covered in [Concrete Types](./concrete-types.md)), but differ in how they behave at runtime.

## The Key Difference

- **Classes** are reference types — variables hold a reference to a heap-allocated object. Assignment copies the reference, so two variables can point to the same object.
- **Structs** are value types — variables hold the data directly (on the stack or inline). Assignment copies the entire value, so each variable is independent.

## Declaring a Struct

Declare a struct with the `struct` keyword. A struct can contain fields, constructors, and methods — the same members as a class.

{{#tabs }}
{{#tab name="Mist" }}
```mist
{{#include ../../snippets/object-model/structs/declaration.mist}}
```
{{#endtab }}
{{#tab name="Emitted C#" }}
```csharp
{{#include ../../snippets/object-model/structs/declaration.cs}}
```
{{#endtab }}
{{#endtabs }}

Structs support the same access modifiers (`public`, `private`), constructors, and instance methods as classes.

## Copy Semantics

{{#tabs }}
{{#tab name="Mist" }}
```mist
{{#include ../../snippets/object-model/structs/value-semantics.mist}}
```
{{#endtab }}
{{#tab name="Emitted C#" }}
```csharp
{{#include ../../snippets/object-model/structs/value-semantics.cs}}
```
{{#endtab }}
{{#endtabs }}

This makes structs ideal for small, immutable data. Because each variable holds its own copy, there's no risk of unintended aliasing — a common source of bugs with reference types.

## When to Use Structs vs Classes

| | Class | Struct |
|---|---|---|
| **Semantics** | Reference type (heap-allocated, shared by reference) | Value type (stack-allocated, copied on assignment) |
| **Assignment** | Copies the reference — both variables point to the same object | Copies the entire value — each variable is independent |
| **Identity** | Two variables can alias the same object | No aliasing — each copy is distinct |
| **Inheritance** | Single inheritance (runtime subtype, reference) | Single inheritance (field composition, no virtual dispatch) |
| **Best for** | Large objects, shared state, polymorphism via interfaces | Small data carriers, coordinates, measurements, colors |

Use structs for small, self-contained values where copy semantics are desirable. Use classes for larger objects or when you need shared references.

Structs also support single inheritance — see the [Inheritance](./inheritance.md#struct-inheritance) chapter for details on how this works as a compile-time composition mechanism.
