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

## Struct Inheritance

Structs support single inheritance using the same `:` syntax as classes. A derived struct contains all fields from its base struct plus its own — the compiler flattens the inheritance at emit time by copying base fields into the derived struct and synthesizing implicit conversion operators.

{{#tabs }}
{{#tab name="Mist" }}
```mist
{{#include ../../snippets/object-model/structs/inheritance.mist}}
```
{{#endtab }}
{{#tab name="Emitted C#" }}
```csharp
{{#include ../../snippets/object-model/structs/inheritance.cs}}
```
{{#endtab }}
{{#endtabs }}

Notice that the emitted C# has no inheritance relationship — `Point3D` does not extend `Point`. Instead, the compiler copies `X` and `Y` directly into `Point3D` and generates an implicit conversion operator so you can assign a `Point3D` to a `Point` variable without a cast.

### Static Dispatch Only

Both class and struct inheritance use static dispatch — there are no virtual methods on either concrete type. A derived class or struct can define new methods that call inherited ones, but cannot override them.

### Class vs Struct Inheritance

While the syntax is the same, the runtime behavior differs:

- **Class inheritance** preserves a runtime subtype relationship. A `Dog` reference stored in an `Animal` variable still points to the original `Dog` object on the heap. No data is lost.
- **Struct inheritance** is a compile-time composition mechanism. Assigning a `Point3D` to a `Point` variable copies only the base fields (`X`, `Y`) into a new independent value. The `Z` field is not carried along — this is safe narrowing, not slicing a reference.
