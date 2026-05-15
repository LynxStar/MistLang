# Ref Parameters

Mist provides three ways to pass parameters by reference, each with different guarantees about what the callee can do with the value. These range from a full mutable reference to a storage location (`ref`) to ownership-aware specifiers that enforce borrowing rules at compile time (`borrowed` and `readonly ref`).

| Specifier | Callee can read | Callee can mutate | Caller blocked during call | Emitted to C# |
|-----------|:-:|:-:|:-:|----------------|
| `readonly ref` | ✓ | ✗ | No | erased (compile-time only) |
| `borrowed` | ✓ | ✓ | Yes — no access until return | erased (compile-time only) |
| `ref` | ✓ | ✓ | No | C# `ref` |

## `readonly ref` Parameters

The `readonly ref` specifier declares that the callee receives a live, read-only view of the caller's variable. The callee can read the value and call readonly methods on it, but cannot mutate it. The view is non-storable — it cannot be assigned to a field or returned from the function.

Because `readonly ref` is an ownership-tier constraint, it is enforced entirely at compile time. The emitted C# contains no `ref` keyword — the parameter is passed by value at the runtime level.

{{#tabs }}
{{#tab name="Mist" }}
```mist
using System;

namespace RefParameters;

public class Sensor
{
    public int Reading;

    public Sensor(int reading)
    {
        this.Reading = reading;
    }

    public string Describe()
    {
        return $"Sensor({Reading})";
    }
}

public class Program
{
    public static void ReadSensor(readonly ref Sensor s)
    {
        // Can read fields and call methods
        Console.WriteLine($"reading={s.Describe()}");
        // Cannot mutate: s.Reading = 10; would be a compile error
    }

    public static void Main()
    {
        owned Sensor s = new Sensor(42);
        ReadSensor(s);
        // Owner retains full access after readonly ref returns
        s.Reading = 100;
        Console.WriteLine($"after={s.Describe()}");
    }
}
```
{{#endtab }}
{{#tab name="Emitted C#" }}
```csharp
using System;

namespace RefParameters;

public class Sensor
{
    public int Reading;

    public Sensor(int reading)
    {
        this.Reading = reading;
    }

    public string Describe()
    {
        return $"Sensor({Reading})";
    }
}

public class Program
{
    public static void ReadSensor(Sensor s)
    {
        // readonly ref is erased — passed by value at runtime
        Console.WriteLine($"reading={s.Describe()}");
    }

    public static void Main()
    {
        Sensor s = new Sensor(42);
        ReadSensor(s);
        s.Reading = 100;
        Console.WriteLine($"after={s.Describe()}");
    }
}
```
{{#endtab }}
{{#endtabs }}

While a `readonly ref` borrow is active, the owner enters the *LentReadonly* state. In this state the owner can still read and write its own variable, but cannot move it or lend it as `borrowed` until the readonly borrow ends. After the call returns, the owner regains full access.

## `borrowed` Parameters

The `borrowed` specifier grants the callee exclusive mutable access to the caller's variable for the duration of the call. While the borrow is active, the caller cannot read, write, or lend the variable — only one mutable borrow is permitted at a time. This prevents data races and aliasing bugs at compile time.

Like `readonly ref`, `borrowed` is erased in the emitted C#. The enforcement is purely compile-time.

{{#tabs }}
{{#tab name="Mist" }}
```mist
using System;

namespace RefParameters;

public class Pair
{
    public int X;
    public int Y;

    public Pair(int x, int y)
    {
        this.X = x;
        this.Y = y;
    }

    public void SetX(int x)
    {
        this.X = x;
    }

    public string Describe()
    {
        return $"Pair({X}, {Y})";
    }
}

public class Program
{
    public static void MutateBorrowed(borrowed Pair p)
    {
        // Borrowed holder has exclusive mutable access
        p.SetX(999);
        Console.WriteLine($"mutated={p.Describe()}");
    }

    public static void Main()
    {
        owned Pair p = new Pair(1, 2);
        // Lend as borrowed — owner loses all access until call returns
        MutateBorrowed(p);
        // Owner regains access after borrowed returns
        Console.WriteLine($"owner after={p.Describe()}");
    }
}
```
{{#endtab }}
{{#tab name="Emitted C#" }}
```csharp
using System;

namespace RefParameters;

public class Pair
{
    public int X;
    public int Y;

    public Pair(int x, int y)
    {
        this.X = x;
        this.Y = y;
    }

    public void SetX(int x)
    {
        this.X = x;
    }

    public string Describe()
    {
        return $"Pair({X}, {Y})";
    }
}

public class Program
{
    public static void MutateBorrowed(Pair p)
    {
        // borrowed is erased — passed by value at runtime
        p.SetX(999);
        Console.WriteLine($"mutated={p.Describe()}");
    }

    public static void Main()
    {
        Pair p = new Pair(1, 2);
        MutateBorrowed(p);
        Console.WriteLine($"owner after={p.Describe()}");
    }
}
```
{{#endtab }}
{{#endtabs }}

A `borrowed` recipient can re-lend the value to other functions — as `readonly ref`, `frozen`, or even `borrowed` again — but only one outstanding mutable borrow exists at any point in the call chain.

## `ref` Parameters

The `ref` specifier creates a true mutable reference to a storage location, mirroring C#'s `ref` parameter semantics. Unlike `readonly ref` and `borrowed`, `ref` is not erased — it emits directly as a C# `ref` parameter. The caller must explicitly pass the argument with the `ref` keyword.

Use `ref` when you need to mutate a value type in place (since value types are copied on assignment) or when you need to reassign the caller's variable to a different object entirely.

{{#tabs }}
{{#tab name="Mist" }}
```mist
using System;

namespace RefParameters;

public struct Point
{
    public int X;
    public int Y;

    public Point(int x, int y)
    {
        this.X = x;
        this.Y = y;
    }

    public string Describe()
    {
        return $"Point({X}, {Y})";
    }
}

public class Program
{
    private static void MutatePoint(ref Point p)
    {
        p.X = 100;
        p.Y = 200;
    }

    public static void Main()
    {
        Point pt = new Point(1, 2);
        Console.WriteLine($"before: {pt.Describe()}");
        MutatePoint(ref pt);
        Console.WriteLine($"after: {pt.Describe()}");
    }
}
```
{{#endtab }}
{{#tab name="Emitted C#" }}
```csharp
using System;

namespace RefParameters;

public struct Point
{
    public int X;
    public int Y;

    public Point(int x, int y)
    {
        this.X = x;
        this.Y = y;
    }

    public string Describe()
    {
        return $"Point({X}, {Y})";
    }
}

public class Program
{
    private static void MutatePoint(ref Point p)
    {
        p.X = 100;
        p.Y = 200;
    }

    public static void Main()
    {
        Point pt = new Point(1, 2);
        Console.WriteLine($"before: {pt.Describe()}");
        MutatePoint(ref pt);
        Console.WriteLine($"after: {pt.Describe()}");
    }
}
```
{{#endtab }}
{{#endtabs }}

With `ref`, the caller's storage location is directly aliased. Mutations through the reference are immediately visible to the caller — there is no copy. This is essential for value types like structs, where pass-by-value would create an independent copy that the callee's mutations wouldn't affect.

## `ref` with Reference Types

When `ref` is applied to a reference type parameter, the callee can reassign the caller's variable to point to a different object entirely. Without `ref`, the callee could mutate the object's fields but could not change which object the caller's variable refers to.

{{#tabs }}
{{#tab name="Mist" }}
```mist
using System;

namespace RefParameters;

public class Container
{
    public int Value;

    public Container(int value)
    {
        this.Value = value;
    }

    public string Describe()
    {
        return $"Container({Value})";
    }
}

public class Program
{
    private static void ReplaceContainer(ref Container c)
    {
        c = new Container(999);
    }

    public static void Main()
    {
        Container box = new Container(42);
        Console.WriteLine($"before: {box.Describe()}");
        ReplaceContainer(ref box);
        Console.WriteLine($"after: {box.Describe()}");
    }
}
```
{{#endtab }}
{{#tab name="Emitted C#" }}
```csharp
using System;

namespace RefParameters;

public class Container
{
    public int Value;

    public Container(int value)
    {
        this.Value = value;
    }

    public string Describe()
    {
        return $"Container({Value})";
    }
}

public class Program
{
    private static void ReplaceContainer(ref Container c)
    {
        c = new Container(999);
    }

    public static void Main()
    {
        Container box = new Container(42);
        Console.WriteLine($"before: {box.Describe()}");
        ReplaceContainer(ref box);
        Console.WriteLine($"after: {box.Describe()}");
    }
}
```
{{#endtab }}
{{#endtabs }}

## Choosing Between the Three

- Use **`readonly ref`** when a function needs to observe a value without modifying it, and you want the compiler to enforce that guarantee. The caller retains access during the call.
- Use **`borrowed`** when a function needs exclusive mutable access and you want the compiler to prevent the caller from accessing the value concurrently. This is the strongest ownership-based guarantee.
- Use **`ref`** when you need true pass-by-reference semantics at runtime — mutating value types in place, reassigning the caller's variable, or working with ref returns and ref locals.

The ownership specifiers (`readonly ref` and `borrowed`) are part of Mist's compile-time safety system and have zero runtime cost. The `ref` specifier maps directly to C#'s `ref` and carries the same runtime semantics.
