using Standard;

namespace Snippet;

[System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Sequential)]
public struct Point
{
    public int X;
    public int Y;

    public Point(int x, int y)
    {
        this.X = x;
        this.Y = y;
    }

    private Point(int p0, int p1, Mist.Emission.CSharp.__StructInit marker)
    {
        this.X = p0;
        this.Y = p1;
    }

    internal static Point __FromParts(int p0, int p1, Mist.Emission.CSharp.__StructInit marker)
    {
        return new Point(p0, p1, default(Mist.Emission.CSharp.__StructInit));
    }

    public readonly string Describe()
    {
        return $"Point({X}, {Y})";
    }
}

public class Program
{
    private static void MutatePoint(ref Snippet.Point p)
    {
        p.X = 100;
        p.Y = 200;
    }

    public static void Main()
    {
        Snippet.Point pt = new Snippet.Point(1, 2);
        System.Console.WriteLine($"before: {pt.Describe()}");
        MutatePoint(ref pt);
        System.Console.WriteLine($"after: {pt.Describe()}");
    }

    public Program()
    {
    }

    public static Program __CreateDefault()
    {
        return new Program();
    }
}
