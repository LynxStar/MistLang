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

    public int DistanceFromOrigin()
    {
        return Math.Abs(this.x) + Math.Abs(this.y);
    }

    public static void Main(string[] args)
    {
        var p = new Point(3, 4);
        Console.WriteLine($"Distance: {p.DistanceFromOrigin()}");
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
