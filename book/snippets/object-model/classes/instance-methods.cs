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

    public int DistanceFromOrigin()
    {
        return Math.Abs(this.x) + Math.Abs(this.y);
    }

    public static void Main(string[] args)
    {
        Snippet.Point p = new Snippet.Point(3, 4);
        Console.WriteLine($"Distance: {p.DistanceFromOrigin()}");
    }
}
