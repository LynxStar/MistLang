using System;

namespace Snippet;

public sealed record class CardinalDir(Snippet.Vec2 Value)
{
    public static readonly CardinalDir Up = new CardinalDir(new Snippet.Vec2(0, 1));
    public static readonly CardinalDir Down = new CardinalDir(new Snippet.Vec2(0, -1));
    public static readonly CardinalDir Left = new CardinalDir(new Snippet.Vec2(-1, 0));
    public static readonly CardinalDir Right = new CardinalDir(new Snippet.Vec2(1, 0));

    public static implicit operator Snippet.Vec2(CardinalDir e) => e.Value;

    public static explicit operator CardinalDir?(Snippet.Vec2 value)
    {
        if(Equals(value, CardinalDir.Up.Value)) return CardinalDir.Up;
        if(Equals(value, CardinalDir.Down.Value)) return CardinalDir.Down;
        if(Equals(value, CardinalDir.Left.Value)) return CardinalDir.Left;
        if(Equals(value, CardinalDir.Right.Value)) return CardinalDir.Right;
        return null;
    }

    public override string ToString()
    {
        if(ReferenceEquals(this, CardinalDir.Up)) return "Up";
        if(ReferenceEquals(this, CardinalDir.Down)) return "Down";
        if(ReferenceEquals(this, CardinalDir.Left)) return "Left";
        if(ReferenceEquals(this, CardinalDir.Right)) return "Right";
        return Value.ToString();
    }
}

[System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Sequential)]
public readonly struct Vec2
{
    public readonly int x;
    public readonly int y;

    public Vec2(int x, int y)
    {
        this.x = x;
        this.y = y;
    }

    private Vec2(int p0, int p1, Mist.Emission.CSharp.__StructInit marker)
    {
        this.x = p0;
        this.y = p1;
    }

    internal static Vec2 __FromParts(int p0, int p1, Mist.Emission.CSharp.__StructInit marker)
    {
        return new Vec2(p0, p1, default(Mist.Emission.CSharp.__StructInit));
    }

    public string Describe()
    {
        return $"({this.x}, {this.y})";
    }
}

public sealed record class HttpStatus(int Value)
{
    public static readonly HttpStatus Ok = new HttpStatus(200);
    public static readonly HttpStatus Created = new HttpStatus(201);
    public static readonly HttpStatus BadRequest = new HttpStatus(400);
    public static readonly HttpStatus NotFound = new HttpStatus(401);
    public static readonly HttpStatus InternalError = new HttpStatus(500);

    public static implicit operator int(HttpStatus e) => e.Value;

    public static explicit operator HttpStatus?(int value)
    {
        if(Equals(value, HttpStatus.Ok.Value)) return HttpStatus.Ok;
        if(Equals(value, HttpStatus.Created.Value)) return HttpStatus.Created;
        if(Equals(value, HttpStatus.BadRequest.Value)) return HttpStatus.BadRequest;
        if(Equals(value, HttpStatus.NotFound.Value)) return HttpStatus.NotFound;
        if(Equals(value, HttpStatus.InternalError.Value)) return HttpStatus.InternalError;
        return null;
    }

    public override string ToString()
    {
        if(ReferenceEquals(this, HttpStatus.Ok)) return "Ok";
        if(ReferenceEquals(this, HttpStatus.Created)) return "Created";
        if(ReferenceEquals(this, HttpStatus.BadRequest)) return "BadRequest";
        if(ReferenceEquals(this, HttpStatus.NotFound)) return "NotFound";
        if(ReferenceEquals(this, HttpStatus.InternalError)) return "InternalError";
        return Value.ToString();
    }
}

public sealed record class Direction(string Value)
{
    public static readonly Direction North = new Direction("N");
    public static readonly Direction South = new Direction("S");
    public static readonly Direction East = new Direction("E");
    public static readonly Direction West = new Direction("W");

    public static implicit operator string(Direction e) => e.Value;

    public static explicit operator Direction?(string value)
    {
        if(Equals(value, Direction.North.Value)) return Direction.North;
        if(Equals(value, Direction.South.Value)) return Direction.South;
        if(Equals(value, Direction.East.Value)) return Direction.East;
        if(Equals(value, Direction.West.Value)) return Direction.West;
        return null;
    }

    public override string ToString()
    {
        if(ReferenceEquals(this, Direction.North)) return "North";
        if(ReferenceEquals(this, Direction.South)) return "South";
        if(ReferenceEquals(this, Direction.East)) return "East";
        if(ReferenceEquals(this, Direction.West)) return "West";
        return Value.ToString();
    }
}

public sealed record class Color(byte Value)
{
    public static readonly Color Red = new Color(0);
    public static readonly Color Green = new Color(1);
    public static readonly Color Blue = new Color(2);

    public static implicit operator byte(Color e) => e.Value;

    public static explicit operator Color?(byte value)
    {
        if(Equals(value, Color.Red.Value)) return Color.Red;
        if(Equals(value, Color.Green.Value)) return Color.Green;
        if(Equals(value, Color.Blue.Value)) return Color.Blue;
        return null;
    }

    public override string ToString()
    {
        if(ReferenceEquals(this, Color.Red)) return "Red";
        if(ReferenceEquals(this, Color.Green)) return "Green";
        if(ReferenceEquals(this, Color.Blue)) return "Blue";
        return Value.ToString();
    }
}

public class Program
{
    public static void Main(string[] args)
    {
        int statusValue = HttpStatus.Ok;
        string dirValue = Direction.North;
        byte colorValue = Color.Red;
        Snippet.Vec2 upVec = CardinalDir.Up;
        Console.WriteLine($"HttpStatus.Ok as int = {statusValue}");
        Console.WriteLine($"Direction.North as string = {dirValue}");
        Console.WriteLine($"Color.Red as byte = {colorValue}");
        Console.WriteLine($"CardinalDir.Up as Vec2 = {upVec.Describe()}");
    }

    public Program()
    {
    }

    public static Program __CreateDefault()
    {
        return new Program();
    }
}
