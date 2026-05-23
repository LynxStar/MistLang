# Concrete Types

Classes and structs are Mist's concrete types — they define objects with fields (data) and methods (behavior). This chapter covers the anatomy shared by both: how to declare fields, write constructors, define methods, and control access. The differences between classes and structs are covered in [Classes vs Structs](./structs.md).

## Fields

A class is declared with the `class` keyword followed by a name and a brace-delimited body. Fields are declared inside the body with a type, name, and optional initializer.

{{#tabs }}
{{#tab name="Mist" }}
```mist
{{#include ../../snippets/object-model/classes/fields.mist}}
```
{{#endtab }}
{{#tab name="Emitted C#" }}
```csharp
{{#include ../../snippets/object-model/classes/fields.cs}}
```
{{#endtab }}
{{#endtabs }}

Fields hold the per-instance state of the class. Here `x` and `y` are `private`, meaning only code inside `Point` can access them directly.

## Constructors

A constructor initializes a new instance of the class. It has the same name as the class, takes parameters, and assigns values to fields. Use the `this` keyword to disambiguate between constructor parameters and instance fields when they share the same name.

{{#tabs }}
{{#tab name="Mist" }}
```mist
{{#include ../../snippets/object-model/classes/constructors.mist}}
```
{{#endtab }}
{{#tab name="Emitted C#" }}
```csharp
{{#include ../../snippets/object-model/classes/constructors.cs}}
```
{{#endtab }}
{{#endtabs }}

The constructor body runs when you write `new Point(3, 4)`. The `this.x = x` assignment stores the parameter value into the instance field.

## Constructor Chaining

A class can have multiple constructors. One constructor can delegate to another using `: this(...)` after the parameter list. This avoids duplicating initialization logic.

{{#tabs }}
{{#tab name="Mist" }}
```mist
{{#include ../../snippets/object-model/classes/constructor-chaining.mist}}
```
{{#endtab }}
{{#tab name="Emitted C#" }}
```csharp
{{#include ../../snippets/object-model/classes/constructor-chaining.cs}}
```
{{#endtab }}
{{#endtabs }}

The parameterless constructor `Point()` chains to `Point(int, int)` with default values. The chained constructor runs first, then the body of the calling constructor executes (which is empty here).

## The `this` Keyword

Inside an instance method or constructor, `this` refers to the current object. Use it to access fields, call other instance methods, or pass the current instance to another method.

{{#tabs }}
{{#tab name="Mist" }}
```mist
{{#include ../../snippets/object-model/classes/this-keyword.mist}}
```
{{#endtab }}
{{#tab name="Emitted C#" }}
```csharp
{{#include ../../snippets/object-model/classes/this-keyword.cs}}
```
{{#endtab }}
{{#endtabs }}

In `Translate`, `this.x` reads the current instance's `x` field. The method creates and returns a new `Point` without modifying the original.

## Instance Methods

Instance methods operate on a specific object. They can read and write the object's fields and call other instance methods. You invoke them with dot-notation on an instance.

{{#tabs }}
{{#tab name="Mist" }}
```mist
{{#include ../../snippets/object-model/classes/instance-methods.mist}}
```
{{#endtab }}
{{#tab name="Emitted C#" }}
```csharp
{{#include ../../snippets/object-model/classes/instance-methods.cs}}
```
{{#endtab }}
{{#endtabs }}

`DistanceFromOrigin` is an instance method — it has no `static` modifier and accesses `this.x` and `this.y`. You call it on a specific `Point` instance via `p.DistanceFromOrigin()`.

## Static Methods

A static method belongs to the class itself, not to any instance. It cannot access instance fields or use `this`. Declare it with the `static` modifier and call it using the class name.

{{#tabs }}
{{#tab name="Mist" }}
```mist
{{#include ../../snippets/object-model/classes/static-methods.mist}}
```
{{#endtab }}
{{#tab name="Emitted C#" }}
```csharp
{{#include ../../snippets/object-model/classes/static-methods.cs}}
```
{{#endtab }}
{{#endtabs }}

`GetTotalCreated` is static — it reads the static field `totalCreated` and is called via `Counter.GetTotalCreated()`. The `Main` method is also static, which is required for program entry points.

## Static Fields

A static field is shared across all instances of a class. It's declared with the `static` modifier and exists once per class rather than once per object. Static fields are useful for tracking class-wide state like counters or configuration.

{{#tabs }}
{{#tab name="Mist" }}
```mist
{{#include ../../snippets/object-model/classes/static-fields.mist}}
```
{{#endtab }}
{{#tab name="Emitted C#" }}
```csharp
{{#include ../../snippets/object-model/classes/static-fields.cs}}
```
{{#endtab }}
{{#endtabs }}

Here `totalCreated` is a static field incremented by each constructor call. Both `a` and `b` share the same `totalCreated` — after creating two instances, `GetTotalCreated()` returns `2`.

## Method Overloading

Method overloading lets you define multiple methods with the same name in a single class, as long as their parameter lists differ. The compiler distinguishes overloads by the number of parameters (arity) or by their types.

### Overloading by Parameter Count

Two or more methods can share a name if they accept different numbers of parameters:

{{#tabs }}
{{#tab name="Mist" }}
```mist
{{#include ../../snippets/object-model/method-overloading/by-count.mist}}
```
{{#endtab }}
{{#tab name="Emitted C#" }}
```csharp
{{#include ../../snippets/object-model/method-overloading/by-count.cs}}
```
{{#endtab }}
{{#endtabs }}

Constructor overloading works the same way:

{{#tabs }}
{{#tab name="Mist" }}
```mist
{{#include ../../snippets/object-model/method-overloading/constructor-overloading.mist}}
```
{{#endtab }}
{{#tab name="Emitted C#" }}
```csharp
{{#include ../../snippets/object-model/method-overloading/constructor-overloading.cs}}
```
{{#endtab }}
{{#endtabs }}

### Overloading by Parameter Type

Methods can share a name and the same number of parameters if the parameter types differ:

{{#tabs }}
{{#tab name="Mist" }}
```mist
{{#include ../../snippets/object-model/method-overloading/by-type.mist}}
```
{{#endtab }}
{{#tab name="Emitted C#" }}
```csharp
{{#include ../../snippets/object-model/method-overloading/by-type.cs}}
```
{{#endtab }}
{{#endtabs }}

Both methods are named `Add` and both take two parameters, but one accepts `int` and the other `double`. The compiler resolves the call based on the argument types.

{{#tabs }}
{{#tab name="Mist" }}
```mist
{{#include ../../snippets/object-model/method-overloading/instance-type-overloads.mist}}
```
{{#endtab }}
{{#tab name="Emitted C#" }}
```csharp
{{#include ../../snippets/object-model/method-overloading/instance-type-overloads.cs}}
```
{{#endtab }}
{{#endtabs }}

## Access Modifiers

Mist supports `public` and `private` access modifiers on class members. A `public` member is accessible from any code that has a reference to the object. A `private` member is only accessible within the declaring class.

{{#tabs }}
{{#tab name="Mist" }}
```mist
{{#include ../../snippets/object-model/classes/access-modifiers.mist}}
```
{{#endtab }}
{{#tab name="Emitted C#" }}
```csharp
{{#include ../../snippets/object-model/classes/access-modifiers.cs}}
```
{{#endtab }}
{{#endtabs }}

The fields `name`, `value`, and `totalCreated` are `private` — external code cannot read or write them directly. The methods `Increment` and `Describe` are `public`, providing controlled access to the internal state. This encapsulation pattern keeps the class's invariants safe from outside interference.
