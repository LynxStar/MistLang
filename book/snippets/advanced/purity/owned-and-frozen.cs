using Standard;

namespace PurityInputs;

[System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Sequential)]
public struct Point
{
    public int X;
    public int Y;

    private Point(int p0, int p1, Mist.Emission.CSharp.__StructInit marker)
    {
        this.X = p0;
        this.Y = p1;
    }

    internal static Point __FromParts(int p0, int p1, Mist.Emission.CSharp.__StructInit marker)
    {
        return new Point(p0, p1, default(Mist.Emission.CSharp.__StructInit));
    }

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
    public static int Seeded()
    {
        System.Collections.Generic.List<int> items = new System.Collections.Generic.List<int>();
        items.Add(1);
        items.Add(2);
        items.Add(3);
        return items.Count;
    }

    public static int Size(System.Collections.Generic.List<int> xs)
    {
        return xs.Count;
    }

    public static int Shifted(PurityInputs.Point p)
    {
        p.X = p.X + 10;
        return p.X + p.Y;
    }

    public static void Main(string[] args)
    {
        System.Console.WriteLine(System.FormattableString.Invariant($"Seeded()={Seeded()}"));
        System.Collections.Generic.List<int> xs = new System.Collections.Generic.List<int>();
        xs.Add(4);
        xs.Add(5);
        System.Console.WriteLine(System.FormattableString.Invariant($"Size(xs)={Size(xs)}"));
        xs.Add(6);
        System.Console.WriteLine(System.FormattableString.Invariant($"Size(xs) after Add={Size(xs)}"));
        PurityInputs.Point p = new PurityInputs.Point();
        p.X = 1;
        p.Y = 2;
        System.Console.WriteLine(System.FormattableString.Invariant($"Shifted(p)={Shifted(p)} p.X={p.X}"));
    }

    public Program()
    {
    }

    public static Program __CreateDefault()
    {
        return new Program();
    }
}
