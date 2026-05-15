# Switch Expressions

Switch expressions provide a concise way to produce a value from multi-branch logic. Unlike traditional switch statements, a switch expression is an expression — it evaluates to a value and can be used anywhere an expression is expected, such as variable assignments or method arguments. The syntax uses a postfix `switch` keyword followed by a set of pattern arms.

## Postfix Switch Syntax

A switch expression is written as `value switch { arms }`, where each arm maps a pattern to a result expression separated by `=>`. Arms are separated by commas.

{{#tabs }}
{{#tab name="Mist" }}
```mist
using System;

namespace SwitchDemo;

public class Program
{
    public static void Main(string[] args)
    {
        int num = 2;
        var parity = num switch {
            1 => "odd",
            2 => "even",
            3 => "odd",
            var _ => "unknown",
        };
        Console.WriteLine($"num={num} parity={parity}");
    }
}
```
{{#endtab }}
{{#tab name="Emitted C#" }}
```csharp
using System;

namespace SwitchDemo;

public class Program
{
    public static void Main(string[] args)
    {
        int num = 2;
        var parity = num switch
        {
            1 => "odd",
            2 => "even",
            3 => "odd",
            var _ => "unknown",
        };
        Console.WriteLine($"num={num} parity={parity}");
    }
}
```
{{#endtab }}
{{#endtabs }}

The switch expression evaluates `num` against each arm in order. The first arm whose pattern matches determines the result. The emitted C# uses the same switch expression syntax since C# natively supports this feature.

## Constant Pattern Arms

Constant patterns match a specific literal value — integers, strings, or booleans. Each arm compares the scrutinee against the constant and produces the corresponding result.

{{#tabs }}
{{#tab name="Mist" }}
```mist
using System;

namespace SwitchDemo;

public class Program
{
    public static void Main(string[] args)
    {
        int day = 3;
        var dayName = day switch {
            1 => "Monday",
            2 => "Tuesday",
            3 => "Wednesday",
            4 => "Thursday",
            5 => "Friday",
            var _ => "Weekend",
        };
        Console.WriteLine($"Day {day} is {dayName}");

        string command = "quit";
        var response = command switch {
            "start" => "Starting...",
            "stop" => "Stopping...",
            "quit" => "Goodbye!",
            var _ => "Unknown command",
        };
        Console.WriteLine($"Command '{command}' => {response}");

        bool isActive = true;
        var status = isActive switch {
            true => "Active",
            false => "Inactive",
        };
        Console.WriteLine($"isActive={isActive} => {status}");
    }
}
```
{{#endtab }}
{{#tab name="Emitted C#" }}
```csharp
using System;

namespace SwitchDemo;

public class Program
{
    public static void Main(string[] args)
    {
        int day = 3;
        var dayName = day switch
        {
            1 => "Monday",
            2 => "Tuesday",
            3 => "Wednesday",
            4 => "Thursday",
            5 => "Friday",
            var _ => "Weekend",
        };
        Console.WriteLine($"Day {day} is {dayName}");

        string command = "quit";
        var response = command switch
        {
            "start" => "Starting...",
            "stop" => "Stopping...",
            "quit" => "Goodbye!",
            var _ => "Unknown command",
        };
        Console.WriteLine($"Command '{command}' => {response}");

        bool isActive = true;
        var status = isActive switch
        {
            true => "Active",
            false => "Inactive",
        };
        Console.WriteLine($"isActive={isActive} => {status}");
    }
}
```
{{#endtab }}
{{#endtabs }}

Integer and string constants require a catch-all arm (like `var _`) to ensure exhaustiveness. Boolean switches with both `true` and `false` arms are exhaustive without a catch-all.

## Type Pattern Arms

When the scrutinee is a type union, you can use type patterns to narrow the value and bind it to a variable. Each arm matches one constituent type of the union.

{{#tabs }}
{{#tab name="Mist" }}
```mist
using System;

namespace SwitchDemo;

public class Program
{
    public static void Main(string[] args)
    {
        int | string mixed = 123;
        var result = mixed switch {
            int n => $"integer: {n}",
            string s => $"string: {s}",
        };
        Console.WriteLine(result);

        int | string mixed2 = "hello";
        var result2 = mixed2 switch {
            int n => $"integer: {n}",
            string s => $"string: {s}",
        };
        Console.WriteLine(result2);
    }
}
```
{{#endtab }}
{{#tab name="Emitted C#" }}
```csharp
using System;

namespace SwitchDemo;

public class Program
{
    public static void Main(string[] args)
    {
        Union_Int32_String mixed = 123;
        var result = mixed switch
        {
            { IsInt32: true, AsInt32: var n } => $"integer: {n}",
            { IsString: true, AsString: var s } => $"string: {s}",
        };
        Console.WriteLine(result);

        Union_Int32_String mixed2 = "hello";
        var result2 = mixed2 switch
        {
            { IsInt32: true, AsInt32: var n } => $"integer: {n}",
            { IsString: true, AsString: var s } => $"string: {s}",
        };
        Console.WriteLine(result2);
    }
}
```
{{#endtab }}
{{#endtabs }}

In Mist, `int n` in a switch arm reads naturally — "if the value is an int, bind it to n." The compiler emits property patterns that check the union's discriminator (`IsInt32`) and extract the value (`AsInt32`). When all constituent types are covered, the switch is exhaustive without a catch-all.

## Discard Pattern

The discard pattern serves as a catch-all arm that matches any value not handled by preceding arms. Mist supports two forms: `var _` (var-discard) and bare `_` (underscore-discard).

{{#tabs }}
{{#tab name="Mist" }}
```mist
using System;

namespace SwitchDemo;

public class Program
{
    public static void Main(string[] args)
    {
        int code = 99;
        var meaning = code switch {
            0 => "Zero",
            1 => "One",
            var _ => "Other",
        };
        Console.WriteLine($"Code {code} => {meaning}");

        int code2 = 50;
        var meaning2 = code2 switch {
            0 => "Zero",
            1 => "One",
            _ => "Other",
        };
        Console.WriteLine($"Code {code2} => {meaning2}");
    }
}
```
{{#endtab }}
{{#tab name="Emitted C#" }}
```csharp
using System;

namespace SwitchDemo;

public class Program
{
    public static void Main(string[] args)
    {
        int code = 99;
        var meaning = code switch
        {
            0 => "Zero",
            1 => "One",
            var _ => "Other",
        };
        Console.WriteLine($"Code {code} => {meaning}");

        int code2 = 50;
        var meaning2 = code2 switch
        {
            0 => "Zero",
            1 => "One",
            _ => "Other",
        };
        Console.WriteLine($"Code {code2} => {meaning2}");
    }
}
```
{{#endtab }}
{{#endtabs }}

Both `var _` and `_` match any value without binding it. Use the `var` form when you want to capture the value (e.g., `var x` binds the matched value to `x`), or either discard form when you only need the catch-all behavior.

## When Guards

A `when` guard adds an additional boolean condition to a switch arm. The arm only matches if both the pattern and the guard condition are satisfied.

{{#tabs }}
{{#tab name="Mist" }}
```mist
using System;

namespace SwitchDemo;

public class Program
{
    public static void Main(string[] args)
    {
        int number = -5;
        var sign = number switch {
            int n when n > 0 => "positive",
            int n when n < 0 => "negative",
            var _ => "zero",
        };
        Console.WriteLine($"Number {number} => {sign}");

        int amount = 150;
        var tier = amount switch {
            int n when n >= 100 => $"premium (value={n})",
            int n when n >= 50 => $"standard (value={n})",
            var _ => "basic",
        };
        Console.WriteLine($"Amount {amount} => {tier}");
    }
}
```
{{#endtab }}
{{#tab name="Emitted C#" }}
```csharp
using System;

namespace SwitchDemo;

public class Program
{
    public static void Main(string[] args)
    {
        int number = -5;
        var sign = number switch
        {
            int n when n > 0 => "positive",
            int n when n < 0 => "negative",
            var _ => "zero",
        };
        Console.WriteLine($"Number {number} => {sign}");

        int amount = 150;
        var tier = amount switch
        {
            int n when n >= 100 => $"premium (value={n})",
            int n when n >= 50 => $"standard (value={n})",
            var _ => "basic",
        };
        Console.WriteLine($"Amount {amount} => {tier}");
    }
}
```
{{#endtab }}
{{#endtabs }}

The `when` keyword follows the pattern and precedes `=>`. The guard expression can reference any variable bound by the pattern (like `n` above). Guards are evaluated in order, so `n >= 100` is checked before `n >= 50`.

## Relational and Logical Patterns

Relational patterns (`<`, `<=`, `>`, `>=`) compare the scrutinee against a constant value directly in the pattern position. Logical patterns (`and`, `or`) combine multiple patterns into compound conditions.

{{#tabs }}
{{#tab name="Mist" }}
```mist
using System;

namespace SwitchDemo;

public class Program
{
    public static void Main(string[] args)
    {
        int temp = 75;
        var comfort = temp switch {
            < 32 => "Freezing",
            < 60 => "Cold",
            <= 80 => "Comfortable",
            var _ => "Hot",
        };
        Console.WriteLine($"Temperature {temp} => {comfort}");

        int score = 85;
        var grade = score switch {
            >= 90 and <= 100 => "A",
            >= 80 and < 90 => "B",
            >= 70 and < 80 => "C",
            >= 60 and < 70 => "D",
            var _ => "F",
        };
        Console.WriteLine($"Score {score} => Grade {grade}");
    }
}
```
{{#endtab }}
{{#tab name="Emitted C#" }}
```csharp
using System;

namespace SwitchDemo;

public class Program
{
    public static void Main(string[] args)
    {
        int temp = 75;
        var comfort = temp switch
        {
            < 32 => "Freezing",
            < 60 => "Cold",
            <= 80 => "Comfortable",
            var _ => "Hot",
        };
        Console.WriteLine($"Temperature {temp} => {comfort}");

        int score = 85;
        var grade = score switch
        {
            >= 90 and <= 100 => "A",
            >= 80 and < 90 => "B",
            >= 70 and < 80 => "C",
            >= 60 and < 70 => "D",
            var _ => "F",
        };
        Console.WriteLine($"Score {score} => Grade {grade}");
    }
}
```
{{#endtab }}
{{#endtabs }}

Relational patterns eliminate the need for `when` guards in many numeric scenarios. The `and` combinator creates range checks — `>= 80 and < 90` matches values in the half-open interval [80, 90). Arms are evaluated top-to-bottom, so order matters when ranges overlap.
