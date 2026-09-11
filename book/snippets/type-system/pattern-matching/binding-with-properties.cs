using System;

namespace Snippet;

public class Shape
{
    public Shape(string kind)
    {
        this.Kind = kind;
    }

    public string Kind { get; }
}

public class Circle : Snippet.Shape
{
    public Circle(int radius) : base("circle")
    {
        this.Radius = radius;
    }

    public int Radius { get; }
}

public class Program
{
    public static void Main(string[] args)
    {
        Snippet.Shape shape = new Snippet.Circle(8);
        {
            if (shape is Snippet.Circle c)
            {
                Console.WriteLine($"Narrowed to Circle, radius = {c.Radius}");
                if (c is { Radius: > 5 })
                {
                    Console.WriteLine("It's a big circle");
                }
            }
        }
    }

    public Program()
    {
    }

    public static Program __CreateDefault()
    {
        return new Program();
    }
}
