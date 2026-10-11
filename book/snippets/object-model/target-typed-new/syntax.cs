using Standard;

namespace Snippet;

internal class Point
{
    public int X;
    public int Y;

    public Point(int x, int y)
    {
        this.X = default(int);
        this.Y = default(int);
        X = x;
        Y = y;
    }

    public string Describe()
    {
        return System.FormattableString.Invariant($"Point({X}, {Y})");
    }
}

internal class Program
{
    public static void Main(string[] args)
    {
        Snippet.Point p = new Snippet.Point(3, 4);
        System.Console.WriteLine(p.Describe());
    }

    public Program()
    {
    }

    public static Program __CreateDefault()
    {
        return new Program();
    }
}
