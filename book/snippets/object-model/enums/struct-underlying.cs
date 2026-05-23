using System;

namespace Snippet;

public readonly struct Vec2
{
    public readonly int x;

    public readonly int y;

    public Vec2(int x, int y)
    {
        this.x = x;
        this.y = y;
    }

    public readonly string Describe()
    {
        return $"({this.x}, {this.y})";
    }

    private Vec2(bool __defaultSentinel)
    {
        this.x = 0;
        this.y = 0;
    }

    public static Vec2 __CreateDefault()
    {
        var inst = new Vec2(true);
        return inst;
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
