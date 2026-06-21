using System;

namespace Snippet;

class Point
{
    public int X;

    public int Y;

    public Point(int x, int y)
    {
        X = x;
        Y = y;
    }

    public override string ToString()
    {
        return $"Point({X}, {Y})";
    }
}
class Container
{
    public Point Origin = new Point(0, 0);
}
class Program
{
    public static Point MakePoint(int x, int y)
    {
        return new Point(x, y);
    }

    public static void PrintPoint(Point p)
    {
        System.Console.WriteLine(p);
    }

    public static void Main(string[] args)
    {
        Point p1 = new Point(1, 2);
        Point p2 = new Point(0, 0);
        p2 = new Point(3, 4);
        var p3 = MakePoint(5, 6);
        PrintPoint(new(7, 8));
    }

    public static Program __CreateDefault()
    {
        var inst = new Program();
        return inst;
    }
}
