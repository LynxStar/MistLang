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

## Static Extension Members

Extension blocks can declare `static` methods. These belong to the type itself and are called via the type name, not on an instance. They cannot access the receiver parameter. Primitive keywords also reach supported static members: a static extension declared on `int` can be called through either `int` or `Int32`. Bare keyword member lookup considers the represented type's static members first, then reflected `Type` properties such as `string.Name`. A keyword used as a value still produces a `Type`; `var t = int` keeps reflection through `t.Name`, and does not turn `t` into a static receiver for `Int32`.

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

## Ambient Extension Methods

Mist also supports extension methods defined in .NET assemblies. When you import a namespace with a `using` directive, any C# extension methods declared in that namespace become available as instance methods on their target types — just like Mist-native extensions.

This is how LINQ works in Mist. The `System.Linq` namespace contains extension methods like `Count()`, `First()`, and `Skip()` that target `IEnumerable<T>`. Once you add `using System.Linq;`, those methods appear on arrays and other collections:

{{#tabs }}
{{#tab name="Mist" }}
```mist
{{#include ../../snippets/object-model/extension-methods/ambient-extensions.mist}}
```
{{#endtab }}
{{#tab name="Emitted C#" }}
```csharp
{{#include ../../snippets/object-model/extension-methods/ambient-extensions.cs}}
```
{{#endtab }}
{{#endtabs }}

From the caller's perspective, `numbers.Count()` looks like a regular method call. Under the hood, the compiler emits it as a static method invocation on the original .NET class — `System.Linq.Enumerable.Count(numbers)` — passing the receiver as the first argument.

### Visibility Rules

Ambient extension methods follow the same visibility rules as Mist-native extensions: they are only available when their containing namespace is imported. If you remove `using System.Linq;`, the LINQ methods disappear from the available members.

This gives you explicit control over which extensions are in scope. A method like `Count()` won't silently appear on your collections unless you've opted into the namespace that provides it.

### Ambiguity with Mist-Native Extensions

If a Mist-native extension block and an ambient extension method both provide a member with the same name for the same receiver type, the compiler reports an ambiguity error. You'll need to resolve the conflict — typically by removing one of the `using` directives or renaming the Mist-native extension member.
