using System;

namespace Snippet;

internal class Point
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

internal class Program
{
    public static void Main(string[] args)
    {
        Snippet.Point p = new Snippet.Point(3, 4);
        Console.WriteLine(p.Describe());
    }

    public Program()
    {
    }

    public static Program __CreateDefault()
    {
        return new Program();
    }
}
