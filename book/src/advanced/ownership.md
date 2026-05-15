# Ownership

Mist's ownership system controls how references are shared and mutated. It provides compile-time guarantees about aliasing, mutation, and lifetime — preventing data races and use-after-free errors without a garbage collector or runtime borrow checker. Ownership annotations are enforced entirely at compile time and erased in the emitted C#, so there is zero runtime cost.

The system defines five ownership tiers, ordered from least restrictive to most restrictive. Each tier specifies what operations are permitted on a binding and whether the binding can be stored (e.g., assigned to a field or captured in a closure).

## Unrestricted (Default)

The default tier when no ownership annotation is present. Unrestricted bindings have standard reference semantics — multiple variables can alias the same object, and any of them can read or mutate it. This is the familiar behavior from C# and most garbage-collected languages.

{{#tabs }}
{{#tab name="Mist" }}
```mist
using System;

namespace OwnershipSystem;

public class Counter
{
    public int Value;
    public Counter(int value) { this.Value = value; }
    public void Increment() { this.Value = Value + 1; }
    public string Describe() { return $"Counter({Value})"; }
}

public class UnrestrictedDemo
{
    public static void Run()
    {
        var c1 = new Counter(10);
        var c2 = c1;  // Both c1 and c2 reference the same object
        c2.Increment();
        // Mutation through c2 is visible through c1
        Console.WriteLine($"c1={c1.Describe()}, c2={c2.Describe()}");
    }
}
```
{{#endtab }}
{{#tab name="Emitted C#" }}
```csharp
using System;

namespace OwnershipSystem;

public class Counter
{
    public int Value;
    public Counter(int value) { this.Value = value; }
    public void Increment() { this.Value = Value + 1; }
    public string Describe() { return $"Counter({Value})"; }
}

public class UnrestrictedDemo
{
    public static void Run()
    {
        var c1 = new Counter(10);
        var c2 = c1;
        c2.Increment();
        Console.WriteLine($"c1={c1.Describe()}, c2={c2.Describe()}");
    }
}
```
{{#endtab }}
{{#endtabs }}

Since unrestricted is the default, the emitted C# is identical to the Mist source (minus any ownership annotations elsewhere). Unrestricted bindings are storable — you can assign them to fields, return them from functions, and capture them in closures without restriction.

## Owned

An `owned` binding represents sole ownership of a value. The compiler guarantees that exactly one binding owns the object at any time. Owned bindings have full control: they can read, write, lend to other tiers, or transfer ownership via a move.

You declare owned variables with the `owned` keyword before the type:

{{#tabs }}
{{#tab name="Mist" }}
```mist
using System;

namespace OwnershipSystem;

public class Pair
{
    public int X;
    public int Y;
    public Pair(int x, int y) { this.X = x; this.Y = y; }
    public void SetX(int x) { this.X = x; }
    public string Describe() { return $"Pair({X}, {Y})"; }
}

public class OwnedDemo
{
    public static void Run()
    {
        owned Pair p = new Pair(3, 7);
        p.SetX(100);
        Console.WriteLine($"owned pair={p.Describe()}");
    }
}
```
{{#endtab }}
{{#tab name="Emitted C#" }}
```csharp
using System;

namespace OwnershipSystem;

public class Pair
{
    public int X;
    public int Y;
    public Pair(int x, int y) { this.X = x; this.Y = y; }
    public void SetX(int x) { this.X = x; }
    public string Describe() { return $"Pair({X}, {Y})"; }
}

public class OwnedDemo
{
    public static void Run()
    {
        Pair p = new Pair(3, 7);
        p.SetX(100);
        Console.WriteLine($"owned pair={p.Describe()}");
    }
}
```
{{#endtab }}
{{#endtabs }}

Notice that the `owned` keyword disappears in the emitted C#. Ownership is enforced at compile time — the runtime code is plain C# with no overhead.

### Owned Type Declarations

You can declare a type as `owned class` to indicate that instances of this type must always be held in an owned binding. All fields of an owned type must themselves be owned types or value types.

{{#tabs }}
{{#tab name="Mist" }}
```mist
using System;

namespace OwnershipSystem;

public owned class UniqueHandle
{
    int Id;
    int RefCount;

    public UniqueHandle(int id)
    {
        this.Id = id;
        this.RefCount = 1;
    }

    public int GetId() { return Id; }

    public void Touch() { this.RefCount = RefCount + 1; }

    public string Describe() { return $"UniqueHandle(Id={Id}, RefCount={RefCount})"; }
}

public class OwnedTypeDemo
{
    public static void Run()
    {
        owned UniqueHandle h = new UniqueHandle(42);
        h.Touch();
        Console.WriteLine($"handle={h.Describe()}");
    }
}
```
{{#endtab }}
{{#tab name="Emitted C#" }}
```csharp
using System;

namespace OwnershipSystem;

public class UniqueHandle
{
    int Id;
    int RefCount;

    public UniqueHandle(int id)
    {
        this.Id = id;
        this.RefCount = 1;
    }

    public int GetId() { return Id; }

    public void Touch() { this.RefCount = RefCount + 1; }

    public string Describe() { return $"UniqueHandle(Id={Id}, RefCount={RefCount})"; }
}

public class OwnedTypeDemo
{
    public static void Run()
    {
        UniqueHandle h = new UniqueHandle(42);
        h.Touch();
        Console.WriteLine($"handle={h.Describe()}");
    }
}
```
{{#endtab }}
{{#endtabs }}

The `owned` modifier on the class declaration is erased in C# — it's a compile-time contract that the Mist compiler enforces. An owned binding must receive its value from a constructor call, an owned-returning function, or a move from another owned binding.

## Move Semantics

When you assign an owned value to another owned binding, ownership *transfers* — the source variable enters a "moved" state and can no longer be accessed. The compiler emits a diagnostic error on any use-after-move.

{{#tabs }}
{{#tab name="Mist" }}
```mist
using System;

namespace OwnershipSystem;

public class MoveDemo
{
    public static void ConsumeHandle(owned UniqueHandle h)
    {
        h.Touch();
        Console.WriteLine($"consumed={h.Describe()}");
    }

    public static void Run()
    {
        owned UniqueHandle h1 = new UniqueHandle(10);
        Console.WriteLine($"before move={h1.Describe()}");

        // Move ownership to h2 — h1 is now inaccessible
        owned UniqueHandle h2 = h1;
        h2.Touch();
        Console.WriteLine($"after move to h2={h2.Describe()}");

        // Move ownership into a function parameter — h2 is now inaccessible
        ConsumeHandle(h2);
        Console.WriteLine($"transfer complete");
    }
}
```
{{#endtab }}
{{#tab name="Emitted C#" }}
```csharp
using System;

namespace OwnershipSystem;

public class MoveDemo
{
    public static void ConsumeHandle(UniqueHandle h)
    {
        h.Touch();
        Console.WriteLine($"consumed={h.Describe()}");
    }

    public static void Run()
    {
        UniqueHandle h1 = new UniqueHandle(10);
        Console.WriteLine($"before move={h1.Describe()}");

        UniqueHandle h2 = h1;
        h2.Touch();
        Console.WriteLine($"after move to h2={h2.Describe()}");

        ConsumeHandle(h2);
        Console.WriteLine($"transfer complete");
    }
}
```
{{#endtab }}
{{#endtabs }}

In the emitted C#, moves look like ordinary assignments. The safety guarantee is purely compile-time: if you try to use `h1` after the move to `h2`, the Mist compiler rejects the program with a use-after-move error. The C# runtime never sees invalid access because the compiler prevents it from being emitted.

Attempting to use a moved variable produces a compile-time error:

```mist
owned UniqueHandle h1 = new UniqueHandle(1);
owned UniqueHandle h2 = h1;
// Error: use of moved value 'h1'
Console.WriteLine(h1.Describe());
```

## Borrowed

A `borrowed` parameter receives exclusive mutable access to a value for the duration of the call. While the borrow is active, the owner cannot access the variable at all — it transitions to the "LentRef" state. Only one mutable borrow is permitted at a time.

{{#tabs }}
{{#tab name="Mist" }}
```mist
using System;

namespace OwnershipSystem;

public class BorrowedDemo
{
    public static void MutatePair(borrowed Pair p)
    {
        // Borrowed holder has exclusive mutable access
        p.SetX(999);
        Console.WriteLine($"borrowed mutated={p.Describe()}");
    }

    public static void Run()
    {
        owned Pair p = new Pair(1, 2);
        // Lend as borrowed — owner loses access until borrow returns
        MutatePair(p);
        // Owner regains access after the borrow ends
        Console.WriteLine($"owner after return={p.Describe()}");
    }
}
```
{{#endtab }}
{{#tab name="Emitted C#" }}
```csharp
using System;

namespace OwnershipSystem;

public class BorrowedDemo
{
    public static void MutatePair(Pair p)
    {
        p.SetX(999);
        Console.WriteLine($"borrowed mutated={p.Describe()}");
    }

    public static void Run()
    {
        Pair p = new Pair(1, 2);
        MutatePair(p);
        Console.WriteLine($"owner after return={p.Describe()}");
    }
}
```
{{#endtab }}
{{#endtabs }}

The `borrowed` keyword is erased in C# — at runtime it's a normal parameter pass. The compile-time guarantee is that no other code can access the lent variable while the borrower holds it, preventing concurrent mutation bugs.

A borrowed recipient can re-lend the value further — as `readonly ref`, `frozen`, or even `borrowed` to another function:

```mist
public static void ReLend(borrowed Pair p)
{
    ReadPair(p);       // re-lend as readonly ref
    FreezePair(p);     // re-lend as frozen
    p.SetX(42);        // borrower can still mutate after re-lends return
}
```

## Frozen

A `frozen` parameter provides an immutable view of a value. While the frozen borrow is active, the owner transitions to the "LentFrozen" state — it can still read the value and take additional readonly refs or frozen borrows, but it cannot mutate, move, or lend as borrowed until the frozen borrow ends.

{{#tabs }}
{{#tab name="Mist" }}
```mist
using System;

namespace OwnershipSystem;

public class FrozenDemo
{
    public static void FreezeAndRead(frozen Pair p)
    {
        // Can read through frozen — guaranteed stable snapshot
        var sum = p.X + p.Y;
        Console.WriteLine($"frozen sum={sum}");
    }

    public static void Run()
    {
        owned Pair p = new Pair(5, 10);
        // Lend as frozen — owner cannot mutate while frozen borrow is active
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

namespace OwnershipSystem;

public class FrozenDemo
{
    public static void FreezeAndRead(Pair p)
    {
        var sum = p.X + p.Y;
        Console.WriteLine($"frozen sum={sum}");
    }

    public static void Run()
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

Frozen borrows are non-storable — you cannot assign a frozen reference to a field or return it from a function. This ensures the immutability guarantee is scoped to the call duration.

## Readonly Ref

A `readonly ref` parameter provides a live readonly view of a value. The callee can read but not mutate. Unlike `frozen`, a readonly ref does not block the owner from mutating — the owner transitions to the "LentReadonly" state where it retains read and write access.

{{#tabs }}
{{#tab name="Mist" }}
```mist
using System;

namespace OwnershipSystem;

public class ReadonlyRefDemo
{
    public static void ReadPair(readonly ref Pair p)
    {
        // Can read fields and call readonly methods
        var sum = p.X + p.Y;
        Console.WriteLine($"readonly ref sum={sum}");
    }

    public static void Run()
    {
        owned Pair p = new Pair(3, 7);
        // Lend as readonly ref — callee can only read
        ReadPair(p);
        // Owner retains full access (including mutation)
        p.SetX(100);
        Console.WriteLine($"after readonly ref={p.Describe()}");
    }
}
```
{{#endtab }}
{{#tab name="Emitted C#" }}
```csharp
using System;

namespace OwnershipSystem;

public class ReadonlyRefDemo
{
    public static void ReadPair(Pair p)
    {
        var sum = p.X + p.Y;
        Console.WriteLine($"readonly ref sum={sum}");
    }

    public static void Run()
    {
        Pair p = new Pair(3, 7);
        ReadPair(p);
        p.SetX(100);
        Console.WriteLine($"after readonly ref={p.Describe()}");
    }
}
```
{{#endtab }}
{{#endtabs }}

Readonly refs are the lightest lending tier — they provide a read-only guarantee to the callee without restricting the owner. Like frozen and borrowed, readonly refs are non-storable.

## Lending Rules Summary

An owned binding can lend its value to any of the three lending tiers. Each lending tier places different restrictions on the owner while the borrow is active:

| Lending Tier | Owner State | Owner Can Read | Owner Can Write | Owner Can Lend Again |
|---|---|---|---|---|
| `readonly ref` | LentReadonly | Yes | Yes | Yes (readonly ref, frozen) |
| `frozen` | LentFrozen | Yes | No | Yes (readonly ref, frozen) |
| `borrowed` | LentRef | No | No | No |

After the lending call returns, the owner transitions back to the Live state with full access restored.

## Value Types Satisfy All Tiers

Value types (structs) satisfy any ownership tier because they are copied on assignment and parameter passing. There is no aliasing concern — each binding holds its own independent copy.

{{#tabs }}
{{#tab name="Mist" }}
```mist
using System;

namespace OwnershipSystem;

public struct Vec2
{
    public int X;
    public int Y;
    public Vec2(int x, int y) { this.X = x; this.Y = y; }
    public string Describe() { return $"Vec2({X}, {Y})"; }
}

public class ValueTypeDemo
{
    public static void AcceptReadonly(readonly ref Vec2 v)
    {
        Console.WriteLine($"readonly ref vec={v.Describe()}");
    }

    public static void AcceptFrozen(frozen Vec2 v)
    {
        Console.WriteLine($"frozen vec={v.Describe()}");
    }

    public static void AcceptBorrowed(borrowed Vec2 v)
    {
        Console.WriteLine($"borrowed vec={v.Describe()}");
    }

    public static void AcceptOwned(owned Vec2 v)
    {
        Console.WriteLine($"owned vec={v.Describe()}");
    }

    public static void Run()
    {
        var v = new Vec2(3, 4);
        AcceptReadonly(v);
        AcceptFrozen(v);
        AcceptBorrowed(v);
        AcceptOwned(v);
        // Original is unaffected — each call received a copy
        Console.WriteLine($"original after all passes={v.Describe()}");
    }
}
```
{{#endtab }}
{{#tab name="Emitted C#" }}
```csharp
using System;

namespace OwnershipSystem;

public struct Vec2
{
    public int X;
    public int Y;
    public Vec2(int x, int y) { this.X = x; this.Y = y; }
    public string Describe() { return $"Vec2({X}, {Y})"; }
}

public class ValueTypeDemo
{
    public static void AcceptReadonly(Vec2 v)
    {
        Console.WriteLine($"readonly ref vec={v.Describe()}");
    }

    public static void AcceptFrozen(Vec2 v)
    {
        Console.WriteLine($"frozen vec={v.Describe()}");
    }

    public static void AcceptBorrowed(Vec2 v)
    {
        Console.WriteLine($"borrowed vec={v.Describe()}");
    }

    public static void AcceptOwned(Vec2 v)
    {
        Console.WriteLine($"owned vec={v.Describe()}");
    }

    public static void Run()
    {
        var v = new Vec2(3, 4);
        AcceptReadonly(v);
        AcceptFrozen(v);
        AcceptBorrowed(v);
        AcceptOwned(v);
        Console.WriteLine($"original after all passes={v.Describe()}");
    }
}
```
{{#endtab }}
{{#endtabs }}

Because structs are passed by value, the ownership annotations are semantically satisfied by the copy — the original is never aliased. This makes value types universally compatible with all ownership tiers without any special handling.
