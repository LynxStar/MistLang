using System;

namespace Snippet;

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
