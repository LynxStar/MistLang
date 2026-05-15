# Control Flow

Mist provides the standard control flow constructs you'd expect from a C-family language: conditionals with `if`/`else`, counting loops with `for`, condition-based loops with `while`, collection iteration with `foreach`, and early exit with `break`. The syntax maps directly to C#, so if you've written control flow in C# or Java, you'll feel right at home.

## If / Else If / Else

The `if` statement evaluates a boolean condition and executes the corresponding block. You can chain multiple conditions with `else if` and provide a fallback with `else`.

{{#tabs }}
{{#tab name="Mist" }}
```mist
using System;

namespace Demo;

public class Program
{
    public static void Main(string[] args)
    {
        int score = 85;

        if (score >= 90)
        {
            Console.WriteLine("Grade: A");
        }
        else if (score >= 80)
        {
            Console.WriteLine("Grade: B");
        }
        else
        {
            Console.WriteLine("Grade: C or below");
        }
    }
}
```
{{#endtab }}
{{#tab name="Emitted C#" }}
```csharp
using System;

namespace Demo;

public class Program
{
    public static void Main(string[] args)
    {
        int score = 85;

        if (score >= 90)
        {
            Console.WriteLine("Grade: A");
        }
        else if (score >= 80)
        {
            Console.WriteLine("Grade: B");
        }
        else
        {
            Console.WriteLine("Grade: C or below");
        }
    }
}
```
{{#endtab }}
{{#endtabs }}

The condition must be enclosed in parentheses and the body must use braces. Each branch is a block — there is no braceless single-statement form.

## While Loops

A `while` loop repeatedly executes its body as long as the condition evaluates to `true`. The condition is checked before each iteration.

{{#tabs }}
{{#tab name="Mist" }}
```mist
using System;

namespace Demo;

public class Program
{
    public static void Main(string[] args)
    {
        int count = 0;
        while (count < 3)
        {
            Console.WriteLine($"Count: {count}");
            count = count + 1;
        }
    }
}
```
{{#endtab }}
{{#tab name="Emitted C#" }}
```csharp
using System;

namespace Demo;

public class Program
{
    public static void Main(string[] args)
    {
        int count = 0;
        while (count < 3)
        {
            Console.WriteLine($"Count: {count}");
            count = count + 1;
        }
    }
}
```
{{#endtab }}
{{#endtabs }}

## For Loops

The `for` loop provides a compact syntax for counted iteration. It takes an initializer, a condition, and an iterator expression, separated by semicolons.

{{#tabs }}
{{#tab name="Mist" }}
```mist
using System;

namespace Demo;

public class Program
{
    public static void Main(string[] args)
    {
        for (int i = 0; i < 5; i = i + 1)
        {
            Console.WriteLine($"i = {i}");
        }
    }
}
```
{{#endtab }}
{{#tab name="Emitted C#" }}
```csharp
using System;

namespace Demo;

public class Program
{
    public static void Main(string[] args)
    {
        for (int i = 0; i < 5; i = i + 1)
        {
            Console.WriteLine($"i = {i}");
        }
    }
}
```
{{#endtab }}
{{#endtabs }}

The initializer declares a loop variable scoped to the `for` block. The condition is evaluated before each iteration, and the iterator runs after each iteration completes.

## Foreach Loops

The `foreach` loop iterates over each element in a collection. You declare the element type and name, followed by `in` and the collection expression.

{{#tabs }}
{{#tab name="Mist" }}
```mist
using System;

namespace Demo;

public class Program
{
    public static void Main(string[] args)
    {
        int[] numbers = new int[3];
        numbers[0] = 10;
        numbers[1] = 20;
        numbers[2] = 30;

        foreach (int n in numbers)
        {
            Console.WriteLine($"Element: {n}");
        }
    }
}
```
{{#endtab }}
{{#tab name="Emitted C#" }}
```csharp
using System;

namespace Demo;

public class Program
{
    public static void Main(string[] args)
    {
        int[] numbers = new int[3];
        numbers[0] = 10;
        numbers[1] = 20;
        numbers[2] = 30;

        foreach (int n in numbers)
        {
            Console.WriteLine($"Element: {n}");
        }
    }
}
```
{{#endtab }}
{{#endtabs }}

The loop variable is scoped to the `foreach` block and takes on the value of each element in sequence.

## Break

The `break` statement exits the innermost enclosing loop immediately. Execution continues with the first statement after the loop.

{{#tabs }}
{{#tab name="Mist" }}
```mist
using System;

namespace Demo;

public class Program
{
    public static void Main(string[] args)
    {
        for (int i = 0; i < 10; i = i + 1)
        {
            if (i == 3)
            {
                break;
            }
            Console.WriteLine($"i = {i}");
        }
    }
}
```
{{#endtab }}
{{#tab name="Emitted C#" }}
```csharp
using System;

namespace Demo;

public class Program
{
    public static void Main(string[] args)
    {
        for (int i = 0; i < 10; i = i + 1)
        {
            if (i == 3)
            {
                break;
            }
            Console.WriteLine($"i = {i}");
        }
    }
}
```
{{#endtab }}
{{#endtabs }}

This loop prints `i = 0`, `i = 1`, and `i = 2`, then exits when `i` reaches 3.

## Nested Control Flow

Control flow constructs can be freely nested. Here's a common pattern — a loop containing a conditional that filters which iterations produce output:

{{#tabs }}
{{#tab name="Mist" }}
```mist
using System;

namespace Demo;

public class Program
{
    public static void Main(string[] args)
    {
        int[] values = new int[5];
        values[0] = 3;
        values[1] = 7;
        values[2] = 2;
        values[3] = 9;
        values[4] = 4;

        var sum = 0;
        foreach (int v in values)
        {
            if (v > 5)
            {
                sum = sum + v;
            }
        }
        Console.WriteLine($"Sum of values > 5: {sum}");
    }
}
```
{{#endtab }}
{{#tab name="Emitted C#" }}
```csharp
using System;

namespace Demo;

public class Program
{
    public static void Main(string[] args)
    {
        int[] values = new int[5];
        values[0] = 3;
        values[1] = 7;
        values[2] = 2;
        values[3] = 9;
        values[4] = 4;

        var sum = 0;
        foreach (int v in values)
        {
            if (v > 5)
            {
                sum = sum + v;
            }
        }
        Console.WriteLine($"Sum of values > 5: {sum}");
    }
}
```
{{#endtab }}
{{#endtabs }}

This iterates over an array and accumulates only the elements greater than 5. Since Mist's control flow maps directly to C#, the emitted code is identical — the compiler passes these constructs through without transformation.
