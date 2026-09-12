using Standard;

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
        Snippet.Shape shape = new Snippet.Circle(5);
        {
            if (shape is Snippet.Circle c)
            {
                System.Console.WriteLine($"Circle with radius {c.Radius}");
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
