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

## Readonly Structs

Adding the `readonly` modifier to a struct declaration prevents mutation of its fields after construction. The compiler enforces that no method or property setter modifies the struct's state.

{{#tabs }}
{{#tab name="Mist" }}
```mist
{{#include ../../snippets/object-model/structs/readonly-struct.mist}}
```
{{#endtab }}
{{#tab name="Emitted C#" }}
```csharp
{{#include ../../snippets/object-model/structs/readonly-struct.cs}}
```
{{#endtab }}
{{#endtabs }}

Notice that in the emitted C#, methods on a `readonly struct` receive the `readonly` modifier. This tells the C# compiler that the method does not mutate the struct, enabling optimizations like avoiding defensive copies when the struct is passed by reference.

Readonly structs are a good default for value types that represent immutable data — coordinates, colors, measurements, or any "data carrier" where mutation after construction is not needed.
