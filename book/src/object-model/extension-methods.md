# Extension Methods

Extension methods let you add new methods to existing types without modifying their source code. In Mist, you declare extension methods inside an `extension(ReceiverType)` block. The compiler emits them as static methods in a generated static class, with a `this`-annotated first parameter that enables dot-notation calls on instances of the receiver type.

## Declaring Extension Methods

An extension block is declared with the `extension` keyword followed by the receiver type and a parameter name in parentheses. Methods declared inside the block can access the receiver instance through the named parameter.

{{#tabs }}
{{#tab name="Mist" }}
```mist
{{#include ../../snippets/object-model/extension-methods/declaring.mist}}
```
{{#endtab }}
{{#tab name="Emitted C#" }}
```csharp
{{#include ../../snippets/object-model/extension-methods/declaring.cs}}
```
{{#endtab }}
{{#endtabs }}

The `extension(Greeting self)` block declares methods that operate on `Greeting` instances. The parameter name `self` is how you reference the receiver inside the method body. The compiler emits a static class with each method receiving `this Greeting self` as its first parameter — this is the standard C# extension method pattern.

Extension methods can also accept additional parameters beyond the implicit receiver:

{{#tabs }}
{{#tab name="Mist" }}
```mist
{{#include ../../snippets/object-model/extension-methods/with-params.mist}}
```
{{#endtab }}
{{#tab name="Emitted C#" }}
```csharp
{{#include ../../snippets/object-model/extension-methods/with-params.cs}}
```
{{#endtab }}
{{#endtabs }}

The `Repeat` method takes an `int times` parameter in addition to the implicit `self` receiver. In the emitted C#, this becomes the second parameter after `this Greeting self`.

## Calling Extension Methods

Extension methods are called using dot-notation on an instance of the receiver type, exactly like regular instance methods. The compiler resolves the call to the corresponding static method.

{{#tabs }}
{{#tab name="Mist" }}
```mist
{{#include ../../snippets/object-model/extension-methods/calling.mist}}
```
{{#endtab }}
{{#tab name="Emitted C#" }}
```csharp
{{#include ../../snippets/object-model/extension-methods/calling.cs}}
```
{{#endtab }}
{{#endtabs }}

From the caller's perspective, `greeting.WordCount()` looks like a regular method call. The compiler rewrites it to `GreetingExtensions_..._L14.WordCount(greeting)`, passing the instance as the first argument to the static method. This means extension methods integrate seamlessly with existing types — callers don't need to know whether a method is defined on the type itself or in an extension block.

Extension methods on primitive types work the same way. Here, `string` is extended with utility methods that become available on any string value:

{{#tabs }}
{{#tab name="Mist" }}
```mist
{{#include ../../snippets/object-model/extension-methods/primitive-extensions.mist}}
```
{{#endtab }}
{{#tab name="Emitted C#" }}
```csharp
{{#include ../../snippets/object-model/extension-methods/primitive-extensions.cs}}
```
{{#endtab }}
{{#endtabs }}

Extension methods declared in a different namespace require a `using` directive at the call site to become visible. For example, calling `text.IsNullOrEmpty()` requires `using StringUtils;` at the top of the file.
