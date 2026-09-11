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

class Container
{
    public Snippet.Point Origin = new Snippet.Point(0, 0);

    public Container()
    {
        this.Origin = new Snippet.Point(0, 0);
    }

    public static Container __CreateDefault()
    {
        return new Container();
    }
}

class Program
{
    public static Snippet.Point MakePoint(int x, int y)
    {
        return new Snippet.Point(x, y);
    }

    public static void PrintPoint(Snippet.Point p)
    {
        Console.WriteLine(p.Describe());
    }

    public static void Main(string[] args)
    {
        Snippet.Point p1 = new Snippet.Point(1, 2);
        Snippet.Point p2 = new Snippet.Point(0, 0);
        p2 = new Snippet.Point(3, 4);
        Snippet.Point p3 = MakePoint(5, 6);
        PrintPoint(new Snippet.Point(7, 8));
    }

    public Program()
    {
    }

    public static Program __CreateDefault()
    {
        return new Program();
    }
}
