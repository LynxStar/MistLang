# Enums

Enums in Mist define named constant sets with an underlying type. Unlike C# enums which are limited to integral types, Mist enums can have any readonly type as their underlying type — including strings, structs, classes, and even type unions. The compiler emits each enum as a `record class` with `public static readonly` fields for each member and an implicit conversion operator to the underlying type.

## Typed Enums

An enum with an explicit underlying type annotation declares the type after a colon. Each member specifies its value after a colon separator.

{{#tabs }}
{{#tab name="Mist" }}
```mist
using System;

namespace EnumDemo;

public enum Direction : string {
    North: "N",
    South: "S",
    East: "E",
    West: "W"
}
```
{{#endtab }}
{{#tab name="Emitted C#" }}
```csharp
using System;

namespace EnumDemo;

public record class Direction(string Value)
{
    public static readonly Direction North = new Direction("N");
    public static readonly Direction South = new Direction("S");
    public static readonly Direction East = new Direction("E");
    public static readonly Direction West = new Direction("W");

    public static implicit operator string(Direction e) => e.Value;
}
```
{{#endtab }}
{{#endtabs }}

The enum emits as a `record class` with a single `Value` property of the underlying type. Each member becomes a `public static readonly` field initialized with the specified value. The implicit operator allows assigning an enum value directly to a variable of the underlying type.

## Name-Only Enums

When no underlying type is specified and members have no explicit values, the enum uses `byte` as its underlying type and auto-assigns sequential values starting from 0.

{{#tabs }}
{{#tab name="Mist" }}
```mist
using System;

namespace EnumDemo;

public enum Color {
    Red,
    Green,
    Blue
}
```
{{#endtab }}
{{#tab name="Emitted C#" }}
```csharp
using System;

namespace EnumDemo;

public record class Color(byte Value)
{
    public static readonly Color Red = new Color((byte)0);
    public static readonly Color Green = new Color((byte)1);
    public static readonly Color Blue = new Color((byte)2);

    public static implicit operator byte(Color e) => e.Value;
}
```
{{#endtab }}
{{#endtabs }}

Name-only enums are the simplest form — you list the member names and the compiler handles the rest. The auto-assigned values are sequential bytes starting at zero.

## Mixed Explicit and Implicit Values

An enum can mix members with explicit values and members without. Members without a value increment from the last explicit value.

{{#tabs }}
{{#tab name="Mist" }}
```mist
using System;

namespace EnumDemo;

public enum HttpStatus : int {
    Ok: 200,
    Created: 201,
    BadRequest: 400,
    NotFound,
    InternalError: 500
}
```
{{#endtab }}
{{#tab name="Emitted C#" }}
```csharp
using System;

namespace EnumDemo;

public record class HttpStatus(int Value)
{
    public static readonly HttpStatus Ok = new HttpStatus(200);
    public static readonly HttpStatus Created = new HttpStatus(201);
    public static readonly HttpStatus BadRequest = new HttpStatus(400);
    public static readonly HttpStatus NotFound = new HttpStatus(401);
    public static readonly HttpStatus InternalError = new HttpStatus(500);

    public static implicit operator int(HttpStatus e) => e.Value;
}
```
{{#endtab }}
{{#endtabs }}

`NotFound` has no explicit value, so it receives 401 — one more than the preceding `BadRequest` (400). This mirrors the auto-increment behavior familiar from C-style enums.

## Union Underlying Types

The underlying type can be a type union, allowing members to hold values of different types within the same enum.

{{#tabs }}
{{#tab name="Mist" }}
```mist
using System;

namespace EnumDemo;

public enum MixedResult : int | string {
    Success: 0,
    ErrorMsg: "error"
}
```
{{#endtab }}
{{#tab name="Emitted C#" }}
```csharp
using System;

namespace EnumDemo;

public record class MixedResult(Union_Int32_String Value)
{
    public static readonly MixedResult Success = new MixedResult(0);
    public static readonly MixedResult ErrorMsg = new MixedResult("error");

    public static implicit operator Union_Int32_String(MixedResult e) => e.Value;
}
```
{{#endtab }}
{{#endtabs }}

When the underlying type is a union like `int | string`, the emitted `Value` property uses the compiler's discriminated union struct (`Union_Int32_String`). Each member's value is implicitly converted into the union type. We'll cover type unions in detail in the [Type Unions](../type-system/type-unions.md) chapter.

## User-Defined Underlying Types

Enums can use user-defined structs or classes as their underlying type, provided the type is `readonly`. Each member's value is an instance of that type.

### Struct Underlying Type

{{#tabs }}
{{#tab name="Mist" }}
```mist
using System;

namespace EnumDemo;

public readonly struct Vec2 {
    public int x;
    public int y;

    public Vec2(int x, int y)
    {
        this.x = x;
        this.y = y;
    }

    public string Describe()
    {
        return $"({this.x}, {this.y})";
    }
}

public enum CardinalDir : Vec2 {
    Up: new Vec2(0, 1),
    Down: new Vec2(0, -1),
    Left: new Vec2(-1, 0),
    Right: new Vec2(1, 0)
}
```
{{#endtab }}
{{#tab name="Emitted C#" }}
```csharp
using System;

namespace EnumDemo;

public readonly record struct Vec2
{
    public int x;

    public int y;

    public Vec2(int x, int y)
    {
        this.x = x;
        this.y = y;
    }

    public string Describe()
    {
        return $"({this.x}, {this.y})";
    }
}

public record class CardinalDir(Vec2 Value)
{
    public static readonly CardinalDir Up = new CardinalDir(new Vec2(0, 1));
    public static readonly CardinalDir Down = new CardinalDir(new Vec2(0, -1));
    public static readonly CardinalDir Left = new CardinalDir(new Vec2(-1, 0));
    public static readonly CardinalDir Right = new CardinalDir(new Vec2(1, 0));

    public static implicit operator Vec2(CardinalDir e) => e.Value;
}
```
{{#endtab }}
{{#endtabs }}

### Class Underlying Type

{{#tabs }}
{{#tab name="Mist" }}
```mist
using System;

namespace EnumDemo;

public readonly class Label {
    public string text;
    public int code;

    public Label(string text, int code)
    {
        this.text = text;
        this.code = code;
    }

    public string Describe()
    {
        return $"Label({this.text}, {this.code})";
    }
}

public enum Severity : Label {
    Critical: new Label("CRITICAL", 1),
    Warning: new Label("WARNING", 2),
    Info: new Label("INFO", 3)
}
```
{{#endtab }}
{{#tab name="Emitted C#" }}
```csharp
using System;

namespace EnumDemo;

public class Label
{
    public string text;

    public int code;

    public Label(string text, int code)
    {
        this.text = text;
        this.code = code;
    }

    public string Describe()
    {
        return $"Label({this.text}, {this.code})";
    }
}

public record class Severity(Label Value)
{
    public static readonly Severity Critical = new Severity(new Label("CRITICAL", 1));
    public static readonly Severity Warning = new Severity(new Label("WARNING", 2));
    public static readonly Severity Info = new Severity(new Label("INFO", 3));

    public static implicit operator Label(Severity e) => e.Value;
}
```
{{#endtab }}
{{#endtabs }}

The underlying type must be `readonly` — this ensures enum values are immutable after construction. Primitive types (`int`, `string`, `byte`, etc.) are inherently readonly and always accepted. User-defined types must be explicitly marked `readonly`.

## Implicit Conversion

Enum values implicitly convert to their underlying type. You can assign an enum member directly to a variable of the underlying type without a cast.

{{#tabs }}
{{#tab name="Mist" }}
```mist
using System;

namespace EnumDemo;

public class Program
{
    public static void Main(string[] args)
    {
        // Implicit conversion to underlying type
        int statusValue = HttpStatus.Ok;
        string dirValue = Direction.North;
        byte colorValue = Color.Red;
        Vec2 upVec = CardinalDir.Up;

        Console.WriteLine($"HttpStatus.Ok as int = {statusValue}");
        Console.WriteLine($"Direction.North as string = {dirValue}");
        Console.WriteLine($"Color.Red as byte = {colorValue}");
        Console.WriteLine($"CardinalDir.Up as Vec2 = {upVec.Describe()}");
    }
}
```
{{#endtab }}
{{#tab name="Emitted C#" }}
```csharp
using System;

namespace EnumDemo;

public class Program
{
    public static void Main(string[] args)
    {
        // Implicit conversion to underlying type
        int statusValue = HttpStatus.Ok;
        string dirValue = Direction.North;
        byte colorValue = Color.Red;
        Vec2 upVec = CardinalDir.Up;

        Console.WriteLine($"HttpStatus.Ok as int = {statusValue}");
        Console.WriteLine($"Direction.North as string = {dirValue}");
        Console.WriteLine($"Color.Red as byte = {colorValue}");
        Console.WriteLine($"CardinalDir.Up as Vec2 = {upVec.Describe()}");
    }
}
```
{{#endtab }}
{{#endtabs }}

The implicit conversion operator emitted on the record class (`public static implicit operator T(EnumName e) => e.Value;`) makes this seamless. The enum value's `Value` property is extracted automatically wherever the underlying type is expected.

## Pattern Matching

Enum values support pattern matching with the `is` keyword. The compiler emits an equality comparison against the enum member.

{{#tabs }}
{{#tab name="Mist" }}
```mist
using System;

namespace EnumDemo;

public class Program
{
    public static void Main(string[] args)
    {
        Direction d = Direction.North;
        bool isNorth = d is Direction.North;
        bool isSouth = d is Direction.South;

        Console.WriteLine($"d is Direction.North = {isNorth}");
        Console.WriteLine($"d is Direction.South = {isSouth}");

        Color c = Color.Green;
        bool isGreen = c is Color.Green;
        bool isRed = c is Color.Red;

        Console.WriteLine($"c is Color.Green = {isGreen}");
        Console.WriteLine($"c is Color.Red = {isRed}");
    }
}
```
{{#endtab }}
{{#tab name="Emitted C#" }}
```csharp
using System;

namespace EnumDemo;

public class Program
{
    public static void Main(string[] args)
    {
        Direction d = Direction.North;
        bool isNorth = d == Direction.North;
        bool isSouth = d == Direction.South;

        Console.WriteLine($"d is Direction.North = {isNorth}");
        Console.WriteLine($"d is Direction.South = {isSouth}");

        Color c = Color.Green;
        bool isGreen = c == Color.Green;
        bool isRed = c == Color.Red;

        Console.WriteLine($"c is Color.Green = {isGreen}");
        Console.WriteLine($"c is Color.Red = {isRed}");
    }
}
```
{{#endtab }}
{{#endtabs }}

In Mist, you write `d is Direction.North` to test if an enum variable matches a specific member. The compiler emits this as a value equality check (`d == Direction.North`) rather than C#'s `is` pattern, since enums are record classes with built-in equality semantics.
