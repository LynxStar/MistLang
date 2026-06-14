using System;

namespace Snippet;

public record class Direction(string Value)
{
    public static readonly Direction North = new Direction("N");
    public static readonly Direction South = new Direction("S");
    public static readonly Direction East = new Direction("E");
    public static readonly Direction West = new Direction("W");

    public static implicit operator string(Direction e) => e.Value;

    public override string ToString()
    {
        if (ReferenceEquals(this, North)) return "North";
        else if (ReferenceEquals(this, South)) return "South";
        else if (ReferenceEquals(this, East)) return "East";
        else if (ReferenceEquals(this, West)) return "West";
        return Value.ToString();
    }

    public static Direction __CreateDefault() => North;
}
public class Program
{
    public static void Main(string[] args)
    {
        Direction dir = Direction.__CreateDefault();
        Console.WriteLine($"direction={dir}");
    }

    public static Program __CreateDefault()
    {
        var inst = new Program();
        return inst;
    }
}
