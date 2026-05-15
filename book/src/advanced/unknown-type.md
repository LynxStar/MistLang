# The Unknown Type

The `unknown` type is Mist's top type — every value of every type is assignable to a variable of type `unknown`. In exchange for this universal acceptance, `unknown` forbids all operations: you cannot call methods, access properties, perform arithmetic, or do anything else with an `unknown` value until you narrow it to a concrete type. This makes `unknown` the safe counterpart to unchecked casts — it forces you to prove what a value is before you use it.

In the emitted C#, a bare `unknown` variable lowers to `object`. The compiler rejects any member access on the `unknown` receiver at compile time, so no runtime dispatch is needed.

## Assigning to Unknown

Any value of any type can be assigned to an `unknown` variable. The variable can be reassigned to values of different types over its lifetime — the compiler accepts every assignment because `unknown` is the top type.

{{#tabs }}
{{#tab name="Mist" }}
```mist
using System;

namespace UnknownDemo;

public class Program
{
    public static void Main(string[] args)
    {
        unknown u = 42;
        Console.WriteLine(u);
        u = "hello";
        Console.WriteLine(u);
        u = true;
        Console.WriteLine(u);
    }
}
```
{{#endtab }}
{{#tab name="Emitted C#" }}
```csharp
using System;

namespace UnknownDemo;

public class Program
{
    public static void Main(string[] args)
    {
        object u = 42;
        Console.WriteLine(u);
        u = "hello";
        Console.WriteLine(u);
        u = true;
        Console.WriteLine(u);
    }
}
```
{{#endtab }}
{{#endtabs }}

No operations are permitted on `u` beyond assignment and passing it to methods that accept `unknown` or `object`. Attempting to call a method or access a property on an `unknown` value produces a compile-time error.

## Unknown as a Parameter Type

A method can accept `unknown` as a parameter type, allowing callers to pass any value. Inside the method body, the parameter is opaque until narrowed.

{{#tabs }}
{{#tab name="Mist" }}
```mist
using System;

namespace UnknownDemo;

public class Program
{
    private static void Describe(unknown value)
    {
        if (value is int i)
        {
            Console.WriteLine($"int: {i}");
        }
        if (value is string s)
        {
            Console.WriteLine($"string: {s}");
        }
    }

    public static void Main(string[] args)
    {
        Describe(42);
        Describe("mist");
    }
}
```
{{#endtab }}
{{#tab name="Emitted C#" }}
```csharp
using System;

namespace UnknownDemo;

public class Program
{
    private static void Describe(object value)
    {
        if (value is int i)
        {
            Console.WriteLine($"int: {i}");
        }
        if (value is string s)
        {
            Console.WriteLine($"string: {s}");
        }
    }

    public static void Main(string[] args)
    {
        Describe(42);
        Describe("mist");
    }
}
```
{{#endtab }}
{{#endtabs }}

The `unknown` parameter lowers to `object` in C#. The caller can pass any type without an explicit cast, and the callee must narrow before using the value.

## Narrowing via Pattern Matching

The `is` keyword narrows an `unknown` value to a concrete type. After a successful `is` check, the bound variable has the narrowed type within the guarded scope, and all operations on that type become available.

{{#tabs }}
{{#tab name="Mist" }}
```mist
using System;

namespace UnknownDemo;

public class Program
{
    public static void Main(string[] args)
    {
        unknown maybeInt = 17;
        if (maybeInt is int narrowed)
        {
            int doubled = narrowed + narrowed;
            Console.WriteLine($"narrowed int doubled: {doubled}");
        }
    }
}
```
{{#endtab }}
{{#tab name="Emitted C#" }}
```csharp
using System;

namespace UnknownDemo;

public class Program
{
    public static void Main(string[] args)
    {
        object maybeInt = 17;
        if (maybeInt is int narrowed)
        {
            int doubled = narrowed + narrowed;
            Console.WriteLine($"narrowed int doubled: {doubled}");
        }
    }
}
```
{{#endtab }}
{{#endtabs }}

Inside the `if` branch, `narrowed` is typed `int` — not `unknown` — so arithmetic and all `int` operations resolve normally. Outside the branch, `maybeInt` remains `unknown`.

You can chain multiple `is` checks to handle different possible types:

```mist
if (value is int i)
{
    Console.WriteLine($"int: {i}");
}
if (value is string s)
{
    Console.WriteLine($"string: {s}");
}
if (value is bool b)
{
    Console.WriteLine($"bool: {b}");
}
```

Each branch narrows independently — the compiler tracks the narrowed type within each guarded scope.

## Unknown as a Tuple Element Type

A tuple can contain `unknown` as an element type, creating a container where some positions hold values of indeterminate type. Each element can be narrowed independently via pattern matching.

{{#tabs }}
{{#tab name="Mist" }}
```mist
using System;

namespace UnknownDemo;

public class Greeter
{
    public string Name;

    public Greeter(string name)
    {
        this.Name = name;
    }

    public string Greet()
    {
        return $"Hello from {this.Name}";
    }
}

public class Program
{
    public static void Main(string[] args)
    {
        (int tag, unknown payload) envelope = (tag: 1, payload: new Greeter("Mist"));
        Console.WriteLine($"tag={envelope.tag}");
        if (envelope.payload is Greeter g)
        {
            Console.WriteLine($"payload: {g.Greet()}");
        }
    }
}
```
{{#endtab }}
{{#tab name="Emitted C#" }}
```csharp
using System;

namespace UnknownDemo;

public class Greeter
{
    public string Name;

    public Greeter(string name)
    {
        this.Name = name;
    }

    public string Greet()
    {
        return $"Hello from {this.Name}";
    }
}

public class Program
{
    public static void Main(string[] args)
    {
        (int tag, object payload) envelope = (tag: 1, payload: new Greeter("Mist"));
        Console.WriteLine($"tag={envelope.tag}");
        if (envelope.payload is Greeter g)
        {
            Console.WriteLine($"payload: {g.Greet()}");
        }
    }
}
```
{{#endtab }}
{{#endtabs }}

The `tag` element is `int` and can be used directly. The `payload` element is `unknown` (lowered to `object`) and must be narrowed before any operations are available. This pattern is useful for tagged-union-style envelopes where the payload type varies.

Deconstructing a tuple with an `unknown` element works the same way — the deconstructed binding receives the `unknown` type and must be narrowed:

{{#tabs }}
{{#tab name="Mist" }}
```mist
using System;

namespace UnknownDemo;

public class Program
{
    public static void Main(string[] args)
    {
        (int, unknown) source = (7, "seven");
        var (num, data) = source;
        Console.WriteLine($"num={num}");
        if (data is string ds)
        {
            Console.WriteLine($"data narrowed to string: {ds}");
        }
    }
}
```
{{#endtab }}
{{#tab name="Emitted C#" }}
```csharp
using System;

namespace UnknownDemo;

public class Program
{
    public static void Main(string[] args)
    {
        (int, object) source = (7, "seven");
        var (num, data) = source;
        Console.WriteLine($"num={num}");
        if (data is string ds)
        {
            Console.WriteLine($"data narrowed to string: {ds}");
        }
    }
}
```
{{#endtab }}
{{#endtabs }}

After deconstruction, `num` is `int` (usable immediately) and `data` is `unknown` (requires narrowing). Each element is independent — narrowing one does not affect the other.
