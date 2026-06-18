using System;

namespace Snippet;

public class Point
{
    public int X;

    public int Y;

    public Point()
    {
        this.X = 0;
        this.Y = 0;
    }

    public static Point __CreateDefault()
    {
        var inst = new Point();
        return inst;
    }
}
public class Program
{
    public static void Main(string[] args)
    {
        var p1 = new Point();
        Point p2 = Point.__CreateDefault();
        Console.WriteLine($"new: X={p1.X} Y={p1.Y}");
        Console.WriteLine($"default: X={p2.X} Y={p2.Y}");
    }

    public static Program __CreateDefault()
    {
        var inst = new Program();
        return inst;
    }
}
