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

    public string Describe()
    {
        return $"Point({X}, {Y})";
    }
}
class Program
{
    public static void Main(string[] args)
    {
        Point p = new Point(3, 4);
        Console.WriteLine(p.Describe());
    }

    public static Program __CreateDefault()
    {
        var inst = new Program();
        return inst;
    }
}
