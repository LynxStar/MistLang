using Standard;

namespace Snippet;

public class Circle
{
    public Circle(int radius)
    {
        this.Radius = default(int);
        this.Radius = radius;
    }

    public int Radius { get; }
}

public class Program
{
    public static void Main(string[] args)
    {
        Snippet.Circle bigCircle = new Snippet.Circle(10);
        if (bigCircle is { Radius: > 5 })
        {
            System.Console.WriteLine("Found a circle with radius > 5");
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
