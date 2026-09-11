# Methods

In Mist, functions are called **methods**, just like in C#. They are declared inside a class body and can be either static (called on the class itself) or instance methods (called on an object). The syntax closely mirrors C# — you specify a return type, a name, parameters in parentheses, and a body in braces.

## Method Declarations

A method declaration specifies the return type, name, and parameters. Parameters are typed and comma-separated. The method body contains the logic and uses `return` to produce a value.

{{#tabs }}
{{#tab name="Mist" }}
```mist
{{#include ../../snippets/basics/functions/declarations.mist}}
```
{{#endtab }}
{{#tab name="Emitted C#" }}
```csharp
{{#include ../../snippets/basics/functions/declarations.cs}}
```
{{#endtab }}
{{#endtabs }}

Each method declares its return type before the name (`int`, `string`), and parameters are declared with their type followed by the parameter name. The compiler emits identical C# method signatures.

## Static Methods

A static method belongs to the class itself rather than to any particular instance. You call it using the class name (or directly within the same class). Use the `static` modifier in the declaration.

{{#tabs }}
{{#tab name="Mist" }}
```mist
{{#include ../../snippets/basics/functions/static-methods.mist}}
```
{{#endtab }}
{{#tab name="Emitted C#" }}
```csharp
{{#include ../../snippets/basics/functions/static-methods.cs}}
```
{{#endtab }}
{{#endtabs }}

Static methods can only access static fields — they have no `this` reference. They're useful for utility methods and factory methods.

## Instance Methods

An instance method operates on a specific object. It has access to the object's fields via `this` and is called using dot-notation on an instance.

{{#tabs }}
{{#tab name="Mist" }}
```mist
{{#include ../../snippets/basics/functions/instance-methods.mist}}
```
{{#endtab }}
{{#tab name="Emitted C#" }}
```csharp
{{#include ../../snippets/basics/functions/instance-methods.cs}}
```
{{#endtab }}
{{#endtabs }}

Instance methods don't use the `static` modifier. They can read and write instance fields, create new objects, and call other methods on `this`.

## Access Modifiers

Mist supports `public` and `private` access modifiers on methods. A `public` method is accessible from any code that has a reference to the object. A `private` method is only callable from within the same class — it's an internal implementation detail.

{{#tabs }}
{{#tab name="Mist" }}
```mist
{{#include ../../snippets/basics/functions/access-modifiers.mist}}
```
{{#endtab }}
{{#tab name="Emitted C#" }}
```csharp
{{#include ../../snippets/basics/functions/access-modifiers.cs}}
```
{{#endtab }}
{{#endtabs }}

Here `ApplyFee` is `private` — it's a helper that only `Deposit` uses internally. External code can call `Deposit` and `GetBalance` but cannot call `ApplyFee` directly.

## Void Return Type

Remember that `void` is a real type in Mist — it has a single value, the Void singleton. A method with a `void` return type can use any of these forms: no return statement at all, `return;`, or `return void;`. Omitting the return statement is syntactic sugar; the compiler inserts the implicit return for you.

See [Void Type](./void-type.md) for the full details on using `void` as a first-class value.

{{#tabs }}
{{#tab name="Mist" }}
```mist
{{#include ../../snippets/basics/functions/void-return.mist}}
```
{{#endtab }}
{{#tab name="Emitted C#" }}
```csharp
{{#include ../../snippets/basics/functions/void-return.cs}}
```
{{#endtab }}
{{#endtabs }}

Void methods are called as standalone statements. The compiler emits standard C# `void` methods regardless of which return form you use.

## Recursion

A method can call itself to solve problems that decompose into smaller subproblems. Here's the classic factorial example — `Factorial` calls itself with a decreasing argument until it reaches the base case.

{{#tabs }}
{{#tab name="Mist" }}
```mist
{{#include ../../snippets/basics/functions/recursion.mist}}
```
{{#endtab }}
{{#tab name="Emitted C#" }}
```csharp
{{#include ../../snippets/basics/functions/recursion.cs}}
```
{{#endtab }}
{{#endtabs }}

Mist doesn't impose any special syntax for recursion — a method simply references its own name in the body. The compiler emits a standard recursive C# method call.

## Local Functions

A function can be declared as a statement, inside a block or at the top of a file beside
top-level statements. Its name belongs to the block that declares it, so it can be called before
its declaration and can call itself. It sees the locals declared before it and captures them by
reference, as a lambda does; `static` refuses capture, as it does on a lambda. An accessibility
modifier is accepted and means nothing: a local function is visible exactly where it is declared.

{{#tabs }}
{{#tab name="Mist" }}
```mist
{{#include ../../snippets/basics/functions/local-functions.mist}}
```
{{#endtab }}
{{#tab name="Emitted C#" }}
```csharp
{{#include ../../snippets/basics/functions/local-functions.cs}}
```
{{#endtab }}
{{#endtabs }}

A local function may be `async`, with the same meaning the modifier has on a method. It cannot
declare type parameters, and it takes no modifier but an accessibility one, `static` or `async`.
