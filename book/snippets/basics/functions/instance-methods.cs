using System;

namespace Snippet;

public class Point
{
    private int x;

    private int y;

    public Point(int x, int y)
    {
        this.x = x;
        this.y = y;
    }

    public string Describe()
    {
        return $"Point({x}, {y})";
    }

    public Point Translate(int dx, int dy)
    {
        return new Point(this.x + dx, this.y + dy);
    }

    public static void Main(string[] args)
    {
        var p = new Point(3, 4);
        Console.WriteLine(p.Describe());
        var moved = p.Translate(10, 20);
        Console.WriteLine(moved.Describe());
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
