using System;

namespace Snippet;

public class Shape
{
    public string Kind { get; }

    public Shape(string kind)
    {
        this.Kind = kind;
    }

    protected Shape()
    {
        this.Kind = "";
    }

    public static Shape __CreateDefault()
    {
        var inst = new Shape();
        return inst;
    }
}
public class Circle : Shape
{
    public int Radius { get; }

    public Circle(int radius) : base("circle")
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
        Shape shape = new Circle(8);
        if (shape is Circle c)
        {
            Console.WriteLine($"Narrowed to Circle, radius = {c.Radius}");
            if (c is { Radius: > 5 })
            {
                Console.WriteLine("It's a big circle");
            }
        }
    }

    public static Program __CreateDefault()
    {
        var inst = new Program();
        return inst;
    }
}
