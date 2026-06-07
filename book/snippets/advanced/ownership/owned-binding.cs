using System;

namespace Snippet;

public class Point
{
    public int X;

    public int Y;

    public Point(int x, int y)
    {
        this.X = x;
        this.Y = y;
    }

    public string Describe()
    {
        return $"Point({X}, {Y})";
    }

    protected Point()
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
public class OwnedBindingDemo
{
    public static void Run()
    {
        Point p = new Point(3, 7);
        System.Console.WriteLine($"owned binding={p.Describe()}");
    }

    public static OwnedBindingDemo __CreateDefault()
    {
        var inst = new OwnedBindingDemo();
        return inst;
    }
}
