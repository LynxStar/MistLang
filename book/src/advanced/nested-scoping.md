# Nested Scoping

Every pair of braces (`{ }`) in Mist creates a new lexical scope. Variables declared inside a block are visible only within that block and any blocks nested inside it. When execution leaves the block, those variables cease to exist — they cannot be referenced from the enclosing scope or from sibling blocks.

This rule applies uniformly to all brace-delimited constructs: method bodies, control flow branches, `readonly` blocks, `with` context blocks, and standalone blocks written purely for scoping purposes.

## Lexical Scoping with Braces

A standalone block introduces a new scope without any control flow semantics. Variables declared inside are confined to that block:

{{#tabs }}
{{#tab name="Mist" }}
```mist
using System;

namespace ScopingDemo;

public class Program
{
    public static void Main(string[] args)
    {
        var outer = "visible everywhere";

        {
            var inner = "only visible in this block";
            Console.WriteLine(inner);
            Console.WriteLine(outer);  // outer is accessible here
        }

        // inner is not accessible here — it went out of scope
        Console.WriteLine(outer);
    }
}
```
{{#endtab }}
{{#tab name="Emitted C#" }}
```csharp
using System;

namespace ScopingDemo;

public class Program
{
    public static void Main(string[] args)
    {
        var outer = "visible everywhere";

        {
            var inner = "only visible in this block";
            Console.WriteLine(inner);
            Console.WriteLine(outer);
        }

        Console.WriteLine(outer);
    }
}
```
{{#endtab }}
{{#endtabs }}

The emitted C# preserves the block structure exactly. The .NET runtime uses the same lexical scoping rules, so the block boundaries carry through unchanged.

## Variable Shadowing

An inner scope may declare a variable with the same name as a variable in an enclosing scope. The inner declaration *shadows* the outer one — for the duration of the inner scope, the name refers to the inner variable. When the inner scope exits, the outer variable is restored.

{{#tabs }}
{{#tab name="Mist" }}
```mist
using System;

namespace ScopingDemo;

public class ShadowingDemo
{
    public static void Main(string[] args)
    {
        var x = 10;
        Console.WriteLine($"outer x = {x}");

        {
            var x = 99;  // shadows the outer x
            Console.WriteLine($"inner x = {x}");
        }

        // outer x is restored — still 10
        Console.WriteLine($"outer x after block = {x}");
    }
}
```
{{#endtab }}
{{#tab name="Emitted C#" }}
```csharp
using System;

namespace ScopingDemo;

public class ShadowingDemo
{
    public static void Main(string[] args)
    {
        var x = 10;
        Console.WriteLine($"outer x = {x}");

        {
            var x = 99;
            Console.WriteLine($"inner x = {x}");
        }

        Console.WriteLine($"outer x after block = {x}");
    }
}
```
{{#endtab }}
{{#endtabs }}

Shadowing is not reassignment. The outer `x` is never modified — a completely new variable happens to share the same name within the inner scope. This distinction matters for ownership: shadowing an owned variable does not move or consume the original.

Shadowing works at any nesting depth. Each block can shadow variables from any enclosing scope:

{{#tabs }}
{{#tab name="Mist" }}
```mist
using System;

namespace ScopingDemo;

public class DeepShadowing
{
    public static void Main(string[] args)
    {
        var name = "first";

        {
            var name = "second";
            {
                var name = "third";
                Console.WriteLine(name);  // "third"
            }
            Console.WriteLine(name);  // "second"
        }

        Console.WriteLine(name);  // "first"
    }
}
```
{{#endtab }}
{{#tab name="Emitted C#" }}
```csharp
using System;

namespace ScopingDemo;

public class DeepShadowing
{
    public static void Main(string[] args)
    {
        var name = "first";

        {
            var name = "second";
            {
                var name = "third";
                Console.WriteLine(name);
            }
            Console.WriteLine(name);
        }

        Console.WriteLine(name);
    }
}
```
{{#endtab }}
{{#endtabs }}

## Ownership and Nested Scopes

Ownership annotations interact with scoping in a predictable way: when a scope exits, any owned variables declared in that scope are dropped. Borrows created within a scope are released when the scope ends, returning the original owner to its prior state.

This means you can use nested blocks to limit the lifetime of a borrow:

{{#tabs }}
{{#tab name="Mist" }}
```mist
using System;

namespace ScopingDemo;

public class Resource
{
    public int Id;
    public Resource(int id) { this.Id = id; }
    public string Describe() { return $"Resource({Id})"; }
    public void Update(int id) { this.Id = id; }
}

public class OwnershipScopeDemo
{
    public static void ReadOnly(frozen Resource r)
    {
        Console.WriteLine($"frozen view: {r.Describe()}");
    }

    public static void Main(string[] args)
    {
        owned Resource res = new Resource(1);

        {
            // Borrow is confined to this block
            ReadOnly(res);
        }

        // After the block, the owner is fully restored
        res.Update(2);
        Console.WriteLine($"after update: {res.Describe()}");
    }
}
```
{{#endtab }}
{{#tab name="Emitted C#" }}
```csharp
using System;

namespace ScopingDemo;

public class Resource
{
    public int Id;
    public Resource(int id) { this.Id = id; }
    public string Describe() { return $"Resource({Id})"; }
    public void Update(int id) { this.Id = id; }
}

public class OwnershipScopeDemo
{
    public static void ReadOnly(Resource r)
    {
        Console.WriteLine($"frozen view: {r.Describe()}");
    }

    public static void Main(string[] args)
    {
        Resource res = new Resource(1);

        {
            ReadOnly(res);
        }

        res.Update(2);
        Console.WriteLine($"after update: {res.Describe()}");
    }
}
```
{{#endtab }}
{{#endtabs }}

Ownership keywords like `owned` and `frozen` are erased in the emitted C# — the block structure remains, but the enforcement is purely compile-time.

## Context and Nested Scopes

Structured context values declared with `with` are scoped to their enclosing block. When the block exits, the context value is popped from the context stack. This gives you precise control over how far a context value propagates:

{{#tabs }}
{{#tab name="Mist" }}
```mist
using System;

namespace ScopingDemo;

public class Logger
{
    public static void Log(string msg) with string prefix
    {
        Console.WriteLine($"[{prefix}] {msg}");
    }
}

public class ContextScopeDemo
{
    public static void Main(string[] args)
    {
        with var prefix = "OUTER";
        Logger.Log("message 1");

        {
            with var prefix = "INNER";
            Logger.Log("message 2");  // uses "INNER"
        }

        Logger.Log("message 3");  // uses "OUTER" again
    }
}
```
{{#endtab }}
{{#tab name="Emitted C#" }}
```csharp
using System;

namespace ScopingDemo;

public class Logger
{
    public static void Log(string msg)
    {
        var prefix = (string)MistContext.Peek<string>();
        Console.WriteLine($"[{prefix}] {msg}");
    }
}

public class ContextScopeDemo
{
    public static void Main(string[] args)
    {
        MistContext.Push<string>("OUTER");
        var prefix = "OUTER";
        Logger.Log("message 1");

        {
            MistContext.Push<string>("INNER");
            var prefix = "INNER";
            Logger.Log("message 2");
            MistContext.Pop<string>();
        }

        Logger.Log("message 3");
        MistContext.Pop<string>();
    }
}
```
{{#endtab }}
{{#endtabs }}

The emitted C# shows the mechanism clearly: each `with` declaration pushes onto the context stack, and the compiler inserts a matching `Pop` at the end of the declaring scope. Nested blocks naturally create a stack discipline — inner context values shadow outer ones for the duration of the block, then the outer value is restored.

## Nested Type Declarations

Braces also create scoping boundaries for type declarations. A class, struct, interface, trait, or enum declared inside another type is scoped to its parent — accessible from outside only via the qualified name:

{{#tabs }}
{{#tab name="Mist" }}
```mist
using System;

namespace ScopingDemo;

public class Outer
{
    public class Inner
    {
        public static string Hello() { return "Outer.Inner"; }
    }

    public enum Status : string
    {
        Active: "active",
        Inactive: "inactive"
    }

    public static void Demo()
    {
        // Within Outer, nested types are accessible by simple name
        Console.WriteLine(Inner.Hello());
        Console.WriteLine($"Status.Active = {Status.Active}");
    }
}

public class Program
{
    public static void Main(string[] args)
    {
        // From outside, use the qualified name
        Console.WriteLine(Outer.Inner.Hello());
        Outer.Demo();
    }
}
```
{{#endtab }}
{{#tab name="Emitted C#" }}
```csharp
using System;

namespace ScopingDemo;

public class Outer
{
    public class Inner
    {
        public static string Hello() { return "Outer.Inner"; }
    }

    public readonly struct Status
    {
        public static readonly Status Active = new Status("active");
        public static readonly Status Inactive = new Status("inactive");

        public string Value { get; }
        private Status(string value) { Value = value; }

        public static implicit operator string(Status s) => s.Value;
        public override string ToString() => Value;
    }

    public static void Demo()
    {
        Console.WriteLine(Inner.Hello());
        Console.WriteLine($"Status.Active = {Status.Active}");
    }
}

public class Program
{
    public static void Main(string[] args)
    {
        Console.WriteLine(Outer.Inner.Hello());
        Outer.Demo();
    }
}
```
{{#endtab }}
{{#endtabs }}

Nested types can be nested to arbitrary depth. The compiler resolves names by walking outward through enclosing scopes — an inner type can reference sibling types and parent-scope types by simple name, while code outside the parent must use the fully qualified path.
