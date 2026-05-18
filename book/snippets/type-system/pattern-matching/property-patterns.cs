using System;

namespace PatternDemo;

public class Circle
{
    public int Radius { get; }

    public Circle(int radius)
    {
        this.Radius = radius;
    }

    protected Circle()
    {
        this.Radius = 0;
    }

    public static Circle __CreateDefault()
    {
        var inst = new Circle();
        return inst;
    }
}
public class Program
{
    public static void Main(string[] args)
    {
        var bigCircle = new Circle(10);
        if (bigCircle is { Radius: > 5 })
        {
            Console.WriteLine("Found a circle with radius > 5");
        }
    }

    public static Program __CreateDefault()
    {
        var inst = new Program();
        return inst;
    }
}
