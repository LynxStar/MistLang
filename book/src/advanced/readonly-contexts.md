# Readonly Contexts

Mist provides two mechanisms for enforcing immutability at specific points in your code: the `frozen` parameter modifier and the `readonly` block statement. Both are compile-time guarantees — the compiler verifies that no mutation occurs, and the enforcement is erased in the emitted C#. There is no runtime overhead.

These constructs complement the ownership system introduced in the previous chapter. Where ownership tiers control *who* can access a value and *how*, readonly contexts let you create scoped regions where mutation is structurally impossible.

## The `frozen` Parameter Modifier

The `frozen` modifier on a parameter declares that the callee receives an immutable, non-storable view of the argument. The callee can read fields and call readonly methods on the parameter, but cannot mutate it, store it, or pass it to a function expecting a mutable reference.

{{#tabs }}
{{#tab name="Mist" }}
```mist
using System;

namespace ReadonlyContexts;

public class Pair
{
    public int X;
    public int Y;

    public Pair(int x, int y)
    {
        this.X = x;
        this.Y = y;
    }

    public int Sum() { return X + Y; }
    public string Describe() { return $"Pair({X}, {Y})"; }
    public void SetX(int x) { this.X = x; }
}

public class FrozenDemo
{
    public static void FreezeAndRead(frozen Pair p)
    {
        // Can read through frozen — guaranteed stable snapshot
        var sum = p.Sum();
        Console.WriteLine($"frozen sum={sum}");
    }

    public static void Main(string[] args)
    {
        owned Pair p = new Pair(5, 10);
        FreezeAndRead(p);
        // After frozen borrow returns, owner can mutate again
        p.SetX(50);
        Console.WriteLine($"after thaw={p.Describe()}");
    }
}
```
{{#endtab }}
{{#tab name="Emitted C#" }}
```csharp
using System;

namespace ReadonlyContexts;

public class Pair
{
    public int X;
    public int Y;

    public Pair(int x, int y)
    {
        this.X = x;
        this.Y = y;
    }

    public int Sum() { return X + Y; }
    public string Describe() { return $"Pair({X}, {Y})"; }
    public void SetX(int x) { this.X = x; }
}

public class FrozenDemo
{
    // frozen modifier is erased — enforcement is compile-time only
    public static void FreezeAndRead(Pair p)
    {
        var sum = p.Sum();
        Console.WriteLine($"frozen sum={sum}");
    }

    public static void Main(string[] args)
    {
        Pair p = new Pair(5, 10);
        FreezeAndRead(p);
        p.SetX(50);
        Console.WriteLine($"after thaw={p.Describe()}");
    }
}
```
{{#endtab }}
{{#endtabs }}

In the emitted C#, the `frozen` keyword disappears entirely. The parameter becomes a plain `Pair p`. All immutability enforcement happens at compile time — if `FreezeAndRead` tried to call `p.SetX(10)` or assign to `p.X`, the Mist compiler would reject it with an error.

## The LentFrozen State

When you pass an owned variable to a `frozen` parameter, the owner transitions to the **LentFrozen** state for the duration of the call. While in this state, the owner cannot:

- **Mutate** the variable (assign to its fields or call mutating methods)
- **Move** the variable (transfer ownership elsewhere)
- **Lend as borrowed** (pass it to a `borrowed` parameter)

The owner *can* still lend the variable as `frozen` or `readonly ref` to other callees — these are compatible because they also guarantee no mutation.

{{#tabs }}
{{#tab name="Mist" }}
```mist
using System;

namespace ReadonlyContexts;

public class Sensor
{
    public int Id;
    public int Reading;

    public Sensor(int id, int reading)
    {
        this.Id = id;
        this.Reading = reading;
    }

    public int GetReading() { return Reading; }
    public void SetReading(int r) { this.Reading = r; }
    public string Describe() { return $"Sensor({Id}, {Reading})"; }
}

public class LentFrozenDemo
{
    public static void WithFrozenRef(frozen Sensor s)
    {
        // While this frozen ref exists, the owner is in LentFrozen state
        Console.WriteLine($"frozen view={s.GetReading()}");
    }

    public static void Main(string[] args)
    {
        owned Sensor s = new Sensor(1, 100);

        // Lend as frozen — owner enters LentFrozen during call
        WithFrozenRef(s);

        // After frozen returns, owner is back to Live
        s.SetReading(200);
        Console.WriteLine($"after thaw={s.Describe()}");
    }
}
```
{{#endtab }}
{{#tab name="Emitted C#" }}
```csharp
using System;

namespace ReadonlyContexts;

public class Sensor
{
    public int Id;
    public int Reading;

    public Sensor(int id, int reading)
    {
        this.Id = id;
        this.Reading = reading;
    }

    public int GetReading() { return Reading; }
    public void SetReading(int r) { this.Reading = r; }
    public string Describe() { return $"Sensor({Id}, {Reading})"; }
}

public class LentFrozenDemo
{
    // frozen erased — LentFrozen state is compile-time only
    public static void WithFrozenRef(Sensor s)
    {
        Console.WriteLine($"frozen view={s.GetReading()}");
    }

    public static void Main(string[] args)
    {
        Sensor s = new Sensor(1, 100);

        WithFrozenRef(s);

        s.SetReading(200);
        Console.WriteLine($"after thaw={s.Describe()}");
    }
}
```
{{#endtab }}
{{#endtabs }}

The LentFrozen state is the compiler's way of ensuring that while a frozen borrow is outstanding, the original owner cannot invalidate the callee's assumption of immutability. Once the call returns, the owner transitions back to the Live state and regains full access.

## The `readonly` Block Statement

The `readonly { }` block creates a lexical scope where all field access is treated as immutable. Inside the block, you cannot assign to fields, call mutating methods on `this`, or perform any operation that would modify state. You can still read fields, call readonly methods, and work with local variables.

{{#tabs }}
{{#tab name="Mist" }}
```mist
using System;

namespace ReadonlyContexts;

public class ReadonlyBlockDemo
{
    public int Value;

    public ReadonlyBlockDemo(int value)
    {
        this.Value = value;
    }

    public string SafeRead()
    {
        var result = "";
        readonly
        {
            // Inside readonly block: can read fields, call readonly methods
            var v = Value;
            result = $"SafeRead={v}";
        }
        return result;
    }

    public string MixedMethod()
    {
        var before = Value;
        this.Value = Value + 1;
        var after = 0;
        readonly
        {
            // Can only read here — mutation would be a compile error
            after = Value;
        }
        return $"before={before}, after={after}";
    }
}
```
{{#endtab }}
{{#tab name="Emitted C#" }}
```csharp
using System;

namespace ReadonlyContexts;

public class ReadonlyBlockDemo
{
    public int Value;

    public ReadonlyBlockDemo(int value)
    {
        this.Value = value;
    }

    public string SafeRead()
    {
        var result = "";
        // readonly block emits as a regular block
        {
            var v = Value;
            result = $"SafeRead={v}";
        }
        return result;
    }

    public string MixedMethod()
    {
        var before = Value;
        this.Value = Value + 1;
        var after = 0;
        {
            after = Value;
        }
        return $"before={before}, after={after}";
    }
}
```
{{#endtab }}
{{#endtabs }}

In the emitted C#, the `readonly` keyword is stripped and the block becomes a plain `{ }` scope. The immutability guarantee exists only at compile time — the Mist compiler rejects any mutation attempt inside the block before code generation.

## Readonly Blocks in Constructors

A particularly useful application of `readonly` blocks is inside constructors. Constructors normally have *constructor privilege* — they can assign to readonly fields and call setters. A `readonly` block inside a constructor **suspends** this privilege, creating a region where even the constructor cannot mutate fields.

{{#tabs }}
{{#tab name="Mist" }}
```mist
using System;

namespace ReadonlyContexts;

public readonly class PrivilegeRemovalDemo
{
    public int X;
    public int Y;

    public PrivilegeRemovalDemo(int x, int y)
    {
        // Constructor privilege: can assign fields
        this.X = x;
        this.Y = y;

        // Readonly block suspends constructor privilege
        readonly
        {
            // Can only read fields here, not assign
            var sum = X + Y;
        }
    }

    public string Describe()
    {
        return $"PrivilegeRemovalDemo(X={X}, Y={Y})";
    }
}
```
{{#endtab }}
{{#tab name="Emitted C#" }}
```csharp
using System;

namespace ReadonlyContexts;

public class PrivilegeRemovalDemo
{
    public int X;
    public int Y;

    public PrivilegeRemovalDemo(int x, int y)
    {
        this.X = x;
        this.Y = y;

        // Readonly block emits as a regular block
        {
            var sum = X + Y;
        }
    }

    public string Describe()
    {
        return $"PrivilegeRemovalDemo(X={X}, Y={Y})";
    }
}
```
{{#endtab }}
{{#endtabs }}

This pattern is useful when a constructor needs to perform validation or computation after field initialization but wants to guarantee it doesn't accidentally modify the fields it just set.

## When to Use Each Construct

Use `frozen` parameters when you want to accept a reference to an object and promise the caller you won't modify it. This is stronger than `readonly ref` — a frozen borrow also prevents the callee from storing the reference or passing it to something that might mutate it later.

Use `readonly` blocks when you want to create a safe region within a method where you can reason about immutability locally. This is especially valuable in methods that mix mutation and reading — you can isolate the reading portion in a `readonly` block to make the intent explicit and compiler-verified.

Both constructs have zero runtime cost. They exist purely to help you communicate and enforce immutability invariants at compile time.
