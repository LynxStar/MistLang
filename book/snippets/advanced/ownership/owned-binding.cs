using Standard;

namespace Snippet;

public class Point
{
    public int X;
    public int Y;

    public Point(int x, int y)
    {
        this.X = x;
        this.Y = y;
    }

    public string Describe()
    {
        return System.FormattableString.Invariant($"Point({X}, {Y})");
    }
}

public class OwnedBindingDemo
{
    public static void Run()
    {
        Snippet.Point p = new Snippet.Point(3, 7);
        System.Console.WriteLine($"owned binding={p.Describe()}");
    }

    public OwnedBindingDemo()
    {
    }

    public static OwnedBindingDemo __CreateDefault()
    {
        return new OwnedBindingDemo();
    }
}
