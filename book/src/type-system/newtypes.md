# Newtypes

A newtype creates a distinct type that wraps an existing type. Unlike type aliases, which are transparent and interchangeable with their source type, a newtype is a separate type at compile time — you cannot accidentally pass a `UserId` where a plain `int` is expected, or vice versa. This gives you type safety for domain concepts without runtime overhead.

## Newtype Declaration

A newtype is declared with the `type` keyword, a name, and `= new SourceType;`. The source type can be any primitive, struct, or class.

{{#tabs }}
{{#tab name="Mist" }}
```mist
namespace Newtypes;

type UserId = new int;
type Label = new string;
type Flag = new bool;
```
{{#endtab }}
{{#tab name="Emitted C#" }}
```csharp
namespace Newtypes;

public readonly struct UserId
{
    public readonly int Value;

    public UserId(int value)
    {
        Value = value;
    }

    public static explicit operator UserId(int value) => new UserId(value);
    public static explicit operator int(UserId id) => id.Value;
}

public readonly struct Label
{
    public readonly string Value;

    public Label(string value)
    {
        Value = value;
    }

    public static explicit operator Label(string value) => new Label(value);
    public static explicit operator string(Label id) => id.Value;
}

public readonly struct Flag
{
    public readonly bool Value;

    public Flag(bool value)
    {
        Value = value;
    }

    public static explicit operator Flag(bool value) => new Flag(value);
    public static explicit operator bool(Flag id) => id.Value;
}
```
{{#endtab }}
{{#endtabs }}

Each newtype wrapping a primitive emits as a `readonly struct` with a `Value` field holding the wrapped value. The compiler generates explicit cast operators in both directions — from the source type to the newtype and back — so conversions are always intentional.

## Wrapping Structs

When a newtype wraps a struct, the emitted type reproduces the struct's constructor and public members directly. This means you construct the newtype the same way you'd construct the underlying struct.

{{#tabs }}
{{#tab name="Mist" }}
```mist
namespace Newtypes;

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
        return $"({this.X}, {this.Y})";
    }
}

type Position = new Point;
```
{{#endtab }}
{{#tab name="Emitted C#" }}
```csharp
namespace Newtypes;

public struct Point
{
    public int X;

    public int Y;

    public Point(int x, int y)
    {
        this.X = x;
        this.Y = y;
    }

    public readonly string Describe()
    {
        return $"({this.X}, {this.Y})";
    }
}

public struct Position
{
    public int X;

    public int Y;

    public Position(int x, int y)
    {
        this.X = x;
        this.Y = y;
    }

    public readonly string Describe()
    {
        return $"({this.X}, {this.Y})";
    }
}
```
{{#endtab }}
{{#endtabs }}

The `Position` newtype gets its own struct with the same fields and methods as `Point`. Even though they have identical shapes, `Position` and `Point` are distinct types — you cannot assign one to the other without an explicit cast.

## Wrapping Classes

Newtypes can also wrap classes. The emitted type is a class that mirrors the wrapped class's constructor and public members.

{{#tabs }}
{{#tab name="Mist" }}
```mist
namespace Newtypes;

public class Entity
{
    private string name;
    private int id;

    public Entity(string name, int id)
    {
        this.name = name;
        this.id = id;
    }

    public string GetName()
    {
        return this.name;
    }

    public int GetId()
    {
        return this.id;
    }

    public string Describe()
    {
        return $"Entity({this.name}, {this.id})";
    }
}

type Actor = new Entity;
```
{{#endtab }}
{{#tab name="Emitted C#" }}
```csharp
namespace Newtypes;

public class Entity
{
    private string name;

    private int id;

    public Entity(string name, int id)
    {
        this.name = name;
        this.id = id;
    }

    public string GetName()
    {
        return this.name;
    }

    public int GetId()
    {
        return this.id;
    }

    public string Describe()
    {
        return $"Entity({this.name}, {this.id})";
    }
}

public class Actor
{
    private string name;

    private int id;

    public Actor(string name, int id)
    {
        this.name = name;
        this.id = id;
    }

    public string GetName()
    {
        return this.name;
    }

    public int GetId()
    {
        return this.id;
    }

    public string Describe()
    {
        return $"Entity({this.name}, {this.id})";
    }
}
```
{{#endtab }}
{{#endtabs }}

The `Actor` newtype is a completely separate class from `Entity`. It has the same constructor signature and public methods, but the type system treats them as unrelated types.

## Newtypes vs Type Aliases

The key difference between newtypes and type aliases is **interchangeability**:

| | Type Alias | Newtype |
|---|---|---|
| **Declaration** | `type Name = SourceType;` | `type Name = new SourceType;` |
| **Type identity** | Same type as the source — fully interchangeable | Distinct type — not interchangeable |
| **Assignment** | Can assign source type values directly | Requires explicit cast |
| **Purpose** | Readability and convenience | Type safety and domain modeling |

A type alias is transparent: `type Seconds = int;` means `Seconds` and `int` are the same type everywhere. A newtype is opaque: `type Seconds = new int;` means `Seconds` is its own type, and you must cast explicitly to convert between `Seconds` and `int`.

Use type aliases when you want a shorter or more descriptive name. Use newtypes when you want the compiler to prevent accidental mixing of values that happen to share the same underlying representation (e.g., `UserId` vs `OrderId`, both wrapping `int`).

## Member Access

Newtype instances expose the wrapped type's public members directly. For primitive newtypes, the wrapped value is accessible via the `.Value` property. For struct and class newtypes, the source type's fields and methods are available directly on the newtype instance.

{{#tabs }}
{{#tab name="Mist" }}
```mist
using System;

namespace Newtypes;

public class Program
{
    public static void Main(string[] args)
    {
        // Primitive newtype: access via .Value
        var uid = (UserId)42;
        Console.WriteLine(uid.Value);

        // Struct newtype: access fields and methods directly
        var pos = new Position(10, 20);
        int x = pos.X;
        int y = pos.Y;
        Console.WriteLine(pos.Describe());

        // Class newtype: access methods directly
        var actor = new Actor("Hero", 1);
        Console.WriteLine(actor.GetName());
        Console.WriteLine(actor.GetId());
    }
}
```
{{#endtab }}
{{#tab name="Emitted C#" }}
```csharp
using System;

namespace Newtypes;

public class Program
{
    public static void Main(string[] args)
    {
        // Primitive newtype: access via .Value
        var uid = ((UserId)42);
        Console.WriteLine(uid.Value);

        // Struct newtype: access fields and methods directly
        var pos = new Position(10, 20);
        int x = pos.X;
        int y = pos.Y;
        Console.WriteLine(pos.Describe());

        // Class newtype: access methods directly
        var actor = new Actor("Hero", 1);
        Console.WriteLine(actor.GetName());
        Console.WriteLine(actor.GetId());
    }
}
```
{{#endtab }}
{{#endtabs }}

For primitive newtypes (`UserId`, `Label`, `Flag`), the underlying value lives in the `Value` field on the emitted struct. For struct and class newtypes (`Position`, `Actor`), the members are reproduced directly on the newtype, so you access them the same way you would on the original type.

## Explicit Casting

Converting between a newtype and its source type requires an explicit cast. This is intentional — the whole point of a newtype is to prevent accidental mixing of types that share the same representation.

{{#tabs }}
{{#tab name="Mist" }}
```mist
using System;

namespace Newtypes;

public class Program
{
    public static void Main(string[] args)
    {
        // Cast from source type to newtype
        int raw = 99;
        UserId uid = (UserId)raw;

        // Cast from newtype back to source type
        int back = (int)uid;

        Console.WriteLine($"raw={raw}");
        Console.WriteLine($"uid={uid.Value}");
        Console.WriteLine($"back={back}");
    }
}
```
{{#endtab }}
{{#tab name="Emitted C#" }}
```csharp
using System;

namespace Newtypes;

public class Program
{
    public static void Main(string[] args)
    {
        // Cast from source type to newtype
        int raw = 99;
        UserId uid = ((UserId)raw);

        // Cast from newtype back to source type
        int back = ((int)uid);

        Console.WriteLine($"raw={raw}");
        Console.WriteLine($"uid={uid.Value}");
        Console.WriteLine($"back={back}");
    }
}
```
{{#endtab }}
{{#endtabs }}

The cast syntax is `(NewType)value` to wrap and `(SourceType)newtypeValue` to unwrap. In the emitted C#, these use the explicit cast operators that the compiler generates on the newtype struct. Without the cast, the compiler reports a type error — there is no implicit conversion between a newtype and its source type.
