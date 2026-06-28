using System;

namespace Snippet;

public record class Direction(string Value)
{
    public static readonly Direction North = new Direction("N");
    public static readonly Direction South = new Direction("S");
    public static readonly Direction East = new Direction("E");
    public static readonly Direction West = new Direction("W");

    public static implicit operator string(Direction e) => e.Value;

    public static explicit operator Direction?(string value)
    {
        if (Equals(value, North.Value)) return North;
        else if (Equals(value, South.Value)) return South;
        else if (Equals(value, East.Value)) return East;
        else if (Equals(value, West.Value)) return West;
        return null;
    }

    public override string ToString()
    {
        if (ReferenceEquals(this, North)) return "North";
        else if (ReferenceEquals(this, South)) return "South";
        else if (ReferenceEquals(this, East)) return "East";
        else if (ReferenceEquals(this, West)) return "West";
        return Value.ToString();
    }
}
