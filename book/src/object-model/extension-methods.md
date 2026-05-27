# Extensions

Extensions let you add new methods and properties to existing types without modifying their source code. In Mist, you declare extensions inside an `extension(ReceiverType)` block.

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

The `extension(Greeting self)` block declares members that operate on `Greeting` instances. The parameter name `self` is how you reference the receiver inside the block.

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

## Extension Properties

Extension blocks can also declare read-only properties. These are accessed with dot-notation just like regular properties.

```mist
extension(Greeting self)
{
    public bool IsEmpty => self.Message.Length == 0;
    public int CharCount => self.Message.Length;
}
```

Extension properties cannot have `set` or `init` accessors — they are always read-only. This ensures extensions cannot mutate the receiver's state through a property assignment.

## Calling Extensions

Extensions are called using dot-notation on an instance of the receiver type, exactly like regular instance members.

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

From the caller's perspective, `greeting.WordCount()` looks like a regular method call. Extension methods and properties integrate seamlessly — callers don't need to know whether a member is defined on the type itself or in an extension block.

## Extending Built-in Types

Extension blocks work on built-in types too. Here, `string` is extended with utility methods that become available on any string value:

{{#tabs }}
{{#tab name="Mist" }}
```mist
{{#include ../../snippets/object-model/extension-methods/builtin-extensions.mist}}
```
{{#endtab }}
{{#tab name="Emitted C#" }}
```csharp
{{#include ../../snippets/object-model/extension-methods/builtin-extensions.cs}}
```
{{#endtab }}
{{#endtabs }}

## Static Extension Members

Extension blocks can declare `static` methods. These belong to the type itself and are called via the type name, not on an instance. They cannot access the receiver parameter.

```mist
extension(Greeting self)
{
    public static Greeting Create()
    {
        return new Greeting("Hello, World!");
    }
}
```

Call it as `Greeting.Create()` — no instance needed.
