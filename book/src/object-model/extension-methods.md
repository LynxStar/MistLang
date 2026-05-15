# Extension Methods

Extension methods let you add new methods to existing types without modifying their source code. In Mist, you declare extension methods inside an `extension(ReceiverType)` block. The compiler emits them as static methods in a generated static class, with a `this`-annotated first parameter that enables dot-notation calls on instances of the receiver type.

## Declaring Extension Methods

An extension block is declared with the `extension` keyword followed by the receiver type and a parameter name in parentheses. Methods declared inside the block can access the receiver instance through the named parameter.

{{#tabs }}
{{#tab name="Mist" }}
```mist
using System;

namespace ExtensionMethods;

public class Greeting
{
    public string Message;

    public Greeting(string message)
    {
        Message = message;
    }
}

extension(Greeting self)
{
    public int WordCount()
    {
        return self.Message.Length;
    }

    public string Uppercase()
    {
        return self.Message;
    }
}
```
{{#endtab }}
{{#tab name="Emitted C#" }}
```csharp
using System;

namespace ExtensionMethods;

public class Greeting
{
    public string Message;

    public Greeting(string message)
    {
        Message = message;
    }
}

public static class GreetingExtensions_GreetingExtensions_L14
{
    public static int WordCount(this Greeting self)
    {
        return self.Message.Length;
    }

    public static string Uppercase(this Greeting self)
    {
        return self.Message;
    }
}
```
{{#endtab }}
{{#endtabs }}

The `extension(Greeting self)` block declares methods that operate on `Greeting` instances. The parameter name `self` is how you reference the receiver inside the method body. The compiler emits a static class with each method receiving `this Greeting self` as its first parameter — this is the standard C# extension method pattern.

Extension methods can also accept additional parameters beyond the implicit receiver:

{{#tabs }}
{{#tab name="Mist" }}
```mist
using System;

namespace ExtensionMethods;

extension(Greeting self)
{
    public Greeting Repeat(int times)
    {
        var repeated = "";
        for (var i = 0; i < times; i = i + 1)
        {
            if (i > 0) { repeated = repeated + " "; }
            repeated = repeated + self.Message;
        }
        return new Greeting(repeated);
    }
}
```
{{#endtab }}
{{#tab name="Emitted C#" }}
```csharp
using System;

namespace ExtensionMethods;

public static class GreetingExtensions_GreetingExtensions_L5
{
    public static Greeting Repeat(this Greeting self, int times)
    {
        var repeated = "";
        for (var i = 0; i < times; i = i + 1)
        {
            if (i > 0) { repeated = repeated + " "; }
            repeated = repeated + self.Message;
        }
        return new Greeting(repeated);
    }
}
```
{{#endtab }}
{{#endtabs }}

The `Repeat` method takes an `int times` parameter in addition to the implicit `self` receiver. In the emitted C#, this becomes the second parameter after `this Greeting self`.

## Calling Extension Methods

Extension methods are called using dot-notation on an instance of the receiver type, exactly like regular instance methods. The compiler resolves the call to the corresponding static method.

{{#tabs }}
{{#tab name="Mist" }}
```mist
using System;

namespace ExtensionMethods;

public class Program
{
    public static void Main(string[] args)
    {
        var greeting = new Greeting("Hello beautiful world");

        // Call extension methods via dot-notation
        var wc = greeting.WordCount();
        var upper = greeting.Uppercase();
        var repeated = greeting.Repeat(2);

        Console.WriteLine(wc);
        Console.WriteLine(upper);
    }
}
```
{{#endtab }}
{{#tab name="Emitted C#" }}
```csharp
using System;

namespace ExtensionMethods;

public class Program
{
    public static void Main(string[] args)
    {
        var greeting = new Greeting("Hello beautiful world");

        // Compiler rewrites dot-notation to static method calls
        var wc = GreetingExtensions_GreetingExtensions_L14.WordCount(greeting);
        var upper = GreetingExtensions_GreetingExtensions_L14.Uppercase(greeting);
        var repeated = GreetingExtensions_GreetingExtensions_L5.Repeat(greeting, 2);

        Console.WriteLine(wc);
        Console.WriteLine(upper);
    }
}
```
{{#endtab }}
{{#endtabs }}

From the caller's perspective, `greeting.WordCount()` looks like a regular method call. The compiler rewrites it to `GreetingExtensions_..._L14.WordCount(greeting)`, passing the instance as the first argument to the static method. This means extension methods integrate seamlessly with existing types — callers don't need to know whether a method is defined on the type itself or in an extension block.

Extension methods on primitive types work the same way. Here, `string` is extended with utility methods that become available on any string value:

{{#tabs }}
{{#tab name="Mist" }}
```mist
using System;

namespace StringUtils;

extension(string self)
{
    public bool IsNullOrEmpty()
    {
        return self.Length == 0;
    }

    public string Reverse()
    {
        return self;
    }
}
```
{{#endtab }}
{{#tab name="Emitted C#" }}
```csharp
using System;

namespace StringUtils;

public static class stringExtensions_StringUtils_L5
{
    public static bool IsNullOrEmpty(this string self)
    {
        return self.Length == 0;
    }

    public static string Reverse(this string self)
    {
        return self;
    }
}
```
{{#endtab }}
{{#endtabs }}

Extension methods declared in a different namespace require a `using` directive at the call site to become visible. For example, calling `text.IsNullOrEmpty()` requires `using StringUtils;` at the top of the file.
