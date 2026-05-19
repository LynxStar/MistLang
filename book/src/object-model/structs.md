# Structs

Structs are value types — they live on the stack (or inline within their containing type) and are copied on assignment. Use a struct when you need a small, self-contained piece of data with value semantics, such as a color, a coordinate, or a measurement. Unlike classes, two struct variables are independent copies: modifying one never affects the other.

## Struct Declaration

Declare a struct with the `struct` keyword followed by a name and a brace-delimited body. A struct can contain fields, constructors, and methods — the same members as a class.

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

Structs support the same access modifiers (`public`, `private`), constructors, and instance methods as classes. The key difference is in how they behave at runtime.

## Value-Type vs Reference-Type Semantics

The fundamental distinction between structs and classes is their assignment behavior. When you assign a struct to a new variable, the entire value is copied. When you assign a class instance, only the reference is copied — both variables point to the same object.

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
| **Inheritance** | Supports single inheritance | No inheritance |
| **Best for** | Large objects, shared state, polymorphism via interfaces | Small data carriers, coordinates, measurements, colors |

Use structs for small, self-contained values where copy semantics are desirable. Use classes for larger objects or when you need shared references.
