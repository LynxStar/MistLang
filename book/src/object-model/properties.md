# Properties

Properties in Mist provide controlled access to an object's data through getter and setter accessors. They look like fields from the caller's perspective but execute logic when read or written. Mist supports block-bodied accessors, expression-bodied accessors, init-only accessors, auto-implemented properties, property initializers, and accessor-level access modifiers.

## Block-Bodied Accessors

Block-bodied accessors use `get { }` and `set { }` blocks to define custom logic for reading and writing a property. The `set` accessor receives the assigned value through the implicit `value` parameter, and the `field` keyword refers to the compiler-generated backing field.

{{#tabs }}
{{#tab name="Mist" }}
```mist
{{#include ../../snippets/object-model/properties/block-bodied.mist}}
```
{{#endtab }}
{{#tab name="Emitted C#" }}
```csharp
{{#include ../../snippets/object-model/properties/block-bodied.cs}}
```
{{#endtab }}
{{#endtabs }}

The `get` block returns `field` (the backing field), and the `set` block validates the incoming `value` before storing it in `field`. This pattern gives you full control over how a property behaves on read and write without needing to declare a separate backing field.

## Expression-Bodied Accessors

When an accessor contains a single expression, you can use the `=>` syntax for a more compact declaration. This works for both `get` and `set` accessors, as well as for read-only computed properties. Use `field` to refer to the backing field in expression-bodied accessors.

{{#tabs }}
{{#tab name="Mist" }}
```mist
{{#include ../../snippets/object-model/properties/expression-bodied.mist}}
```
{{#endtab }}
{{#tab name="Emitted C#" }}
```csharp
{{#include ../../snippets/object-model/properties/expression-bodied.cs}}
```
{{#endtab }}
{{#endtabs }}

A top-level `=>` after the property name creates a read-only computed property — it has no setter and recalculates its value each time it's accessed. Within an accessor block, `get =>` and `set =>` are shorthand for single-expression bodies.

## The `init` Accessor

The `init` accessor allows a property to be set during object construction (including property initializer blocks) but prevents modification afterward. This is useful for immutable data where you want the convenience of property syntax without allowing later mutation.

{{#tabs }}
{{#tab name="Mist" }}
```mist
{{#include ../../snippets/object-model/properties/init-accessor.mist}}
```
{{#endtab }}
{{#tab name="Emitted C#" }}
```csharp
{{#include ../../snippets/object-model/properties/init-accessor.cs}}
```
{{#endtab }}
{{#endtabs }}

The `init` keyword replaces `set` in the accessor declaration. A simple `init;` creates an auto-implemented init-only property. A block-bodied `init { }` lets you add validation logic — the `field` keyword refers to the compiler-generated backing field. After construction completes, any attempt to assign to an init-only property is a compile-time error.

## Auto-Implemented Properties

Auto-implemented properties declare `get` and `set` (or `get` alone) with semicolons and no body. The compiler generates the backing field automatically.

{{#tabs }}
{{#tab name="Mist" }}
```mist
{{#include ../../snippets/object-model/properties/auto-implemented.mist}}
```
{{#endtab }}
{{#tab name="Emitted C#" }}
```csharp
{{#include ../../snippets/object-model/properties/auto-implemented.cs}}
```
{{#endtab }}
{{#endtabs }}

A `{ get; set; }` property is read-write — callers can both read and assign it. A `{ get; }` property is read-only — it can only be assigned in the constructor. Auto-properties eliminate the boilerplate of declaring a private field and writing trivial get/set logic.

## Property Initializers

A property initializer assigns a default value using `= expression;` after the accessor block. This value is used when no explicit assignment occurs in the constructor.

{{#tabs }}
{{#tab name="Mist" }}
```mist
{{#include ../../snippets/object-model/properties/initializers.mist}}
```
{{#endtab }}
{{#tab name="Emitted C#" }}
```csharp
{{#include ../../snippets/object-model/properties/initializers.cs}}
```
{{#endtab }}
{{#endtabs }}

Property initializers run before the constructor body executes. They're useful for providing sensible defaults without requiring every constructor to repeat the same assignments.

You can also use property initializer blocks when constructing objects with `new`. This sets property values immediately after construction:

{{#tabs }}
{{#tab name="Mist" }}
```mist
{{#include ../../snippets/object-model/properties/initializer-blocks.mist}}
```
{{#endtab }}
{{#tab name="Emitted C#" }}
```csharp
{{#include ../../snippets/object-model/properties/initializer-blocks.cs}}
```
{{#endtab }}
{{#endtabs }}

The `new Config { Timeout = 60, Verbose = true }` syntax creates an instance and then assigns the listed properties. This works with both `set` and `init` accessors. A block whose elements name no member, as in `new Bag { 1, 2 }`, is a collection initializer: each element is passed to an `Add` method, and the compiler selects the overload and judges the element against its parameters.

## Accessor Modifiers

You can restrict the visibility of individual accessors by placing an access modifier before `get`, `set`, or `init`. The most common pattern is a public getter with a private setter — the property is readable from anywhere but only writable within the class.

{{#tabs }}
{{#tab name="Mist" }}
```mist
{{#include ../../snippets/object-model/properties/accessor-modifiers.mist}}
```
{{#endtab }}
{{#tab name="Emitted C#" }}
```csharp
{{#include ../../snippets/object-model/properties/accessor-modifiers.cs}}
```
{{#endtab }}
{{#endtabs }}

The `private set` modifier means external code can read `account.Name` but cannot assign to it — only methods inside the `Account` class can modify the value. This pattern provides encapsulation while keeping the property syntax clean for consumers.
