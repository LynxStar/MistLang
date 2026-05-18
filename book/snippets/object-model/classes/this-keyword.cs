using System;

namespace Demo;

public class Point
{
    private int x;

    private int y;

    public Point(int x, int y)
    {
        this.x = x;
        this.y = y;
    }

    public Point Translate(int dx, int dy)
    {
        return new Point(this.x + dx, this.y + dy);
    }

    public string Describe()
    {
        return $"Point({this.x}, {this.y})";
    }

    protected Point()
    {
        this.x = 0;
        this.y = 0;
    }

    public static Point __CreateDefault()
    {
        var inst = new Point();
        return inst;
    }
}
