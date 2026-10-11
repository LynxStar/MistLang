using Standard;

namespace StampableGuarantee;

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

[System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Sequential)]
public struct Box<T>
    where T : unmanaged
{
    public T Item;

    public Box(T item)
    {
        this.Item = default(Box<T>).Item;
        Item = item;
    }

    private Box(T p0, Mist.Emission.CSharp.__StructInit marker)
    {
        this.Item = p0;
    }

    internal static Box<T> __FromParts(T p0, Mist.Emission.CSharp.__StructInit marker)
    {
        return new Box<T>(p0, default(Mist.Emission.CSharp.__StructInit));
    }
}

public class Program
{
    public static void Main(string[] args)
    {
        StampableGuarantee.Point[] points = new StampableGuarantee.Point[2];
        StampableGuarantee.Box<StampableGuarantee.Point>[] boxes = new StampableGuarantee.Box<StampableGuarantee.Point>[2];
        System.Console.WriteLine(System.FormattableString.Invariant($"points[0].X={points[0].X}"));
        System.Console.WriteLine(System.FormattableString.Invariant($"boxes[0].Item.Y={boxes[0].Item.Y}"));
    }

    public Program()
    {
    }

    public static Program __CreateDefault()
    {
        return new Program();
    }
}
