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

## Ref Structs {#ref-structs}

A **ref struct** is a struct that is restricted to the stack. It can never be boxed, stored on the heap, or escape the method that created it. This makes ref structs ideal for high-performance scenarios where you need zero-allocation access to contiguous memory — types like `Span<T>` and `ReadOnlySpan<T>` in .NET are ref structs.

### Declaration Syntax

Declare a ref struct by placing the `ref` modifier before `struct`:

{{#tabs }}
{{#tab name="Mist" }}
```mist
{{#include ../../snippets/object-model/structs/ref-struct.mist}}
```
{{#endtab }}
{{#tab name="Emitted C#" }}
```csharp
{{#include ../../snippets/object-model/structs/ref-struct.cs}}
```
{{#endtab }}
{{#endtabs }}

A ref struct can have fields, constructors, methods, properties, and indexers — the same members as a regular struct. You can also combine `ref` with `readonly` (e.g., `readonly ref struct`).

### Stack-Only Lifetime

The defining characteristic of a ref struct is that its value **cannot escape to the heap**. The compiler enforces this at every usage site:

- A ref struct value can only exist as a local variable, a method parameter, or a method return value.
- A ref struct can contain fields of other ref struct types (since the containing type is also stack-only).
- When a method returns a ref struct, the caller receives a copy on its own stack frame.

### Permitted Operations

| Operation | Example |
|-----------|---------|
| Local variable | `var pair = new IntPair(1, 2);` |
| Method parameter | `fun Add(p: IntPair): int` |
| Method return type | `fun Make(): IntPair` |
| Field in another ref struct | `ref struct Wrapper { pair: IntPair; }` |
| Member access (methods, properties, indexers) | `pair.Sum()`, `span[0]`, `span.Length` |

### Prohibited Operations

The compiler rejects any attempt to store a ref struct value in a location that could outlive the stack frame:

| Operation | Why it's rejected |
|-----------|-------------------|
| Field in a non-ref-struct class or struct | The containing object lives on the heap |
| Property type in a non-ref-struct | Same reason — the backing storage is heap-allocated |
| Generic type argument (e.g., `List<Span<int>>`) | Generic containers are heap-allocated |
| Union type constituent (e.g., `Span<int> \| string`) | Unions are boxed |
| Optional type (e.g., `Span<int>?`) | Optionals map to `Nullable<T>`, which requires boxing |
| Assignment to an interface variable | Interface assignment boxes the value |

Note that a ref struct **can** declare that it implements an interface (e.g., `ref struct Foo : IBar`). The restriction applies at the usage site — you cannot assign a ref struct value to an interface-typed variable or pass it where an interface type is expected.

### Span Types and `stackalloc`

The most common ref structs in practice are `Span<T>` and `ReadOnlySpan<T>` — lightweight views over contiguous memory. Combined with `stackalloc`, they enable zero-allocation stack buffers with full type safety and frame-escape enforcement. See [Stack-Only Spans](../advanced/stack-only-spans.md) for the full syntax, API, bounds-checking rules, and frame-escape restrictions.
