using System;

namespace Snippet;

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

public class Program
{
    public static void Main(string[] args)
    {
        Direction d = Direction.North;
        bool isNorth = (d == Direction.North);
        bool isSouth = (d == Direction.South);
        Console.WriteLine($"d is Direction.North = {isNorth}");
        Console.WriteLine($"d is Direction.South = {isSouth}");
        Color c = Color.Green;
        bool isGreen = (c == Color.Green);
        bool isRed = (c == Color.Red);
        Console.WriteLine($"c is Color.Green = {isGreen}");
        Console.WriteLine($"c is Color.Red = {isRed}");
    }

    public Program()
    {
    }

    public static Program __CreateDefault()
    {
        return new Program();
    }
}
