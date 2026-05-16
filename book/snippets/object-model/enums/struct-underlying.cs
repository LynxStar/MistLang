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
