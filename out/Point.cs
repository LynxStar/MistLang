using System;

namespace HelloMist;

public class Point
{
    private int x;

    private int y;

    public Point(int x)
    {
        this.x = x;
        this.y = y;
    }

    public Point() : this(0)
    {
    }

    public int GetX()
    {
        return x;
    }

    public int GetY()
    {
        return y;
    }

    public string Describe()
    {
        return $"Point({x}, {y})";
    }
}
