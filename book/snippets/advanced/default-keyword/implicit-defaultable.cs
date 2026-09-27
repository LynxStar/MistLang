using Standard;

namespace Snippet;

public class Point
{
    public int X;
    public int Y;

    public Point()
    {
        this.X = default(int);
        this.Y = default(int);
    }

    public static Point __CreateDefault()
    {
        return new Point();
    }
}

public class Program
{
    public static void Main(string[] args)
    {
        Snippet.Point p1 = new Snippet.Point();
        Snippet.Point p2 = Snippet.Point.__CreateDefault();
        System.Console.WriteLine(System.FormattableString.Invariant($"new: X={p1.X} Y={p1.Y}"));
        System.Console.WriteLine(System.FormattableString.Invariant($"default: X={p2.X} Y={p2.Y}"));
    }

    public Program()
    {
    }

    public static Program __CreateDefault()
    {
        return new Program();
    }
}
