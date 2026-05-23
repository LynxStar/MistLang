using System;

namespace Snippet;

public readonly struct Vector2
{
    public readonly int X;

    public readonly int Y;

    public Vector2(int x, int y)
    {
        this.X = x;
        this.Y = y;
    }

    public readonly int MagnitudeSquared()
    {
        return X * X + Y * Y;
    }

    public readonly string Describe()
    {
        return $"Vector2(X={X}, Y={Y})";
    }

    private Vector2(bool __defaultSentinel)
    {
        this.X = 0;
        this.Y = 0;
    }

    public static Vector2 __CreateDefault()
    {
        var inst = new Vector2(true);
        return inst;
    }
}
