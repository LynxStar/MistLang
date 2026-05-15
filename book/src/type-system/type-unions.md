# Type Unions

Type unions in Mist allow a variable, parameter, or return type to hold a value of one of several types. You declare a union type using the `|` operator between constituent types. The compiler emits a discriminated union helper struct that wraps the value with a tag indicating which type it currently holds, along with implicit conversion operators for seamless assignment.

## Union Type Syntax

A union type is written as two or more types separated by `|`. The resulting type can hold a value of any of the listed types.

{{#tabs }}
{{#tab name="Mist" }}
```mist
using System;

namespace UnionDemo;

public class Program
{
    public static void Main(string[] args)
    {
        int | string x = 42;
        int | string y = "hello";

        Console.WriteLine($"x holds an int");
        Console.WriteLine($"y holds a string");
    }
}
```
{{#endtab }}
{{#tab name="Emitted C#" }}
```csharp
using System;

namespace UnionDemo;

public class Program
{
    public static void Main(string[] args)
    {
        Union_Int32_String x = 42;
        Union_Int32_String y = "hello";

        Console.WriteLine($"x holds an int");
        Console.WriteLine($"y holds a string");
    }
}
```
{{#endtab }}
{{#endtabs }}

The compiler generates a `Union_Int32_String` struct with a tag field, `IsInt32`/`IsString` properties, `AsInt32`/`AsString` accessors, and implicit conversion operators from both `int` and `string`. The constituent names in the helper type are sorted alphabetically.

## Union-Typed Parameters and Return Types

Union types work as parameter types and return types, allowing functions to accept or produce values of multiple types.

{{#tabs }}
{{#tab name="Mist" }}
```mist
using System;

namespace UnionDemo;

public class Program
{
    private static void Describe(int | string value)
    {
        if (value is int n)
        {
            Console.WriteLine($"  int: {n}");
        }
        if (value is string s)
        {
            Console.WriteLine($"  string: {s}");
        }
    }

    private static int | string GetValue(bool useInt)
    {
        if (useInt)
        {
            return 100;
        }
        return "hello";
    }

    public static void Main(string[] args)
    {
        Program.Describe(7);
        Program.Describe("Mist");

        int | string result = Program.GetValue(true);
        Console.WriteLine($"GetValue(true) returned a value");
    }
}
```
{{#endtab }}
{{#tab name="Emitted C#" }}
```csharp
using System;

namespace UnionDemo;

public class Program
{
    private static void Describe(Union_Int32_String value)
    {
        if (value.IsInt32)
        {
            int n = value.AsInt32;
            Console.WriteLine($"  int: {n}");
        }
        if (value.IsString)
        {
            string s = value.AsString;
            Console.WriteLine($"  string: {s}");
        }
    }

    private static Union_Int32_String GetValue(bool useInt)
    {
        if (useInt)
        {
            return 100;
        }
        return "hello";
    }

    public static void Main(string[] args)
    {
        Program.Describe(7);
        Program.Describe("Mist");

        Union_Int32_String result = Program.GetValue(true);
        Console.WriteLine($"GetValue(true) returned a value");
    }
}
```
{{#endtab }}
{{#endtabs }}

Parameters and return types both emit as the union helper struct. Callers can pass any constituent type directly thanks to the implicit conversion operators on the struct.

## Narrowing via Pattern Matching

To access the underlying value of a union-typed variable, use `is` pattern matching. This narrows the type and binds the value to a new variable.

{{#tabs }}
{{#tab name="Mist" }}
```mist
using System;

namespace UnionDemo;

public class Program
{
    public static void Main(string[] args)
    {
        int | string value = 42;

        if (value is int n)
        {
            Console.WriteLine($"It's an int: {n}");
        }
        if (value is string s)
        {
            Console.WriteLine($"It's a string: {s}");
        }
    }
}
```
{{#endtab }}
{{#tab name="Emitted C#" }}
```csharp
using System;

namespace UnionDemo;

public class Program
{
    public static void Main(string[] args)
    {
        Union_Int32_String value = 42;

        if (value.IsInt32)
        {
            int n = value.AsInt32;
            Console.WriteLine($"It's an int: {n}");
        }
        if (value.IsString)
        {
            string s = value.AsString;
            Console.WriteLine($"It's a string: {s}");
        }
    }
}
```
{{#endtab }}
{{#endtabs }}

In Mist, `value is int n` reads naturally — "if value is an int, bind it to n." The compiler emits this as a check on the union's `IsInt32` property followed by extracting the value via `AsInt32`. Inside the `if` block, `n` is a plain `int` — the union has been narrowed away.

## Multi-Type Unions

Unions are not limited to two types. You can combine three or more constituent types with additional `|` separators.

{{#tabs }}
{{#tab name="Mist" }}
```mist
using System;

namespace UnionDemo;

public class Program
{
    private static void DescribeTriple(int | string | bool value)
    {
        if (value is int n)
        {
            Console.WriteLine($"  int: {n}");
        }
        if (value is string s)
        {
            Console.WriteLine($"  string: {s}");
        }
        if (value is bool b)
        {
            Console.WriteLine($"  bool: {b}");
        }
    }

    public static void Main(string[] args)
    {
        int | string | bool tri1 = 99;
        int | string | bool tri2 = "triple";
        int | string | bool tri3 = true;

        Program.DescribeTriple(tri1);
        Program.DescribeTriple(tri2);
        Program.DescribeTriple(tri3);
    }
}
```
{{#endtab }}
{{#tab name="Emitted C#" }}
```csharp
using System;

namespace UnionDemo;

public class Program
{
    private static void DescribeTriple(Union_Boolean_Int32_String value)
    {
        if (value.IsInt32)
        {
            int n = value.AsInt32;
            Console.WriteLine($"  int: {n}");
        }
        if (value.IsString)
        {
            string s = value.AsString;
            Console.WriteLine($"  string: {s}");
        }
        if (value.IsBoolean)
        {
            bool b = value.AsBoolean;
            Console.WriteLine($"  bool: {b}");
        }
    }

    public static void Main(string[] args)
    {
        Union_Boolean_Int32_String tri1 = 99;
        Union_Boolean_Int32_String tri2 = "triple";
        Union_Boolean_Int32_String tri3 = true;

        Program.DescribeTriple(tri1);
        Program.DescribeTriple(tri2);
        Program.DescribeTriple(tri3);
    }
}
```
{{#endtab }}
{{#endtabs }}

The helper struct name includes all constituent types sorted alphabetically — `int | string | bool` becomes `Union_Boolean_Int32_String`. The struct gains `Is` and `As` properties for each constituent type, and pattern matching works identically regardless of how many types are in the union.

## Reassignment

A union-typed variable can be reassigned from one constituent type to another. The implicit conversion operators on the helper struct handle the type transition.

{{#tabs }}
{{#tab name="Mist" }}
```mist
using System;

namespace UnionDemo;

public class Program
{
    public static void Main(string[] args)
    {
        int | string value = 1;
        Console.WriteLine($"before: assigned int");

        value = "switched";
        if (value is string s)
        {
            Console.WriteLine($"after: {s}");
        }
    }
}
```
{{#endtab }}
{{#tab name="Emitted C#" }}
```csharp
using System;

namespace UnionDemo;

public class Program
{
    public static void Main(string[] args)
    {
        Union_Int32_String value = 1;
        Console.WriteLine($"before: assigned int");

        value = "switched";
        if (value.IsString)
        {
            string s = value.AsString;
            Console.WriteLine($"after: {s}");
        }
    }
}
```
{{#endtab }}
{{#endtabs }}

Reassigning `value = "switched"` invokes the implicit conversion from `string` to `Union_Int32_String`, updating the internal tag and stored value. After reassignment, pattern matching confirms the new type.
