using Standard;
using System;

namespace RefParameters;

public struct Point
{
    public int X;

    public int Y;

    public Point(int x, int y)
    {
        this.X = x;
        this.Y = y;
    }

    public readonly string Describe()
    {
        return $"Point({X}, {Y})";
    }

    public static Point __CreateDefault()
    {
        var inst = new Point();
        inst.X = 0;
        inst.Y = 0;
        return inst;
    }
}
public class Program
{
    private static void MutatePoint(ref Point p)
    {
        p.X = 100;
        p.Y = 200;
    }

    public static void Main()
    {
        Point pt = new Point(1, 2);
        System.Console.WriteLine($"before: {pt.Describe()}");
        MutatePoint(ref pt);
        System.Console.WriteLine($"after: {pt.Describe()}");
    }

    public static Program __CreateDefault()
    {
        var inst = new Program();
        return inst;
    }
}
