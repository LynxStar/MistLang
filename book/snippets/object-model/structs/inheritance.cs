using Standard;

namespace Snippet;

[System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Sequential)]
public struct Point
{
    public int X;
    public int Y;

    public Point(int x, int y)
    {
        this.X = default(int);
        this.Y = default(int);
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
}

[System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Sequential)]
public struct Point3D
{
    public int X;
    public int Y;
    public int Z;

    public Point3D(int x, int y, int z)
    {
        this.X = default(int);
        this.Y = default(int);
        this.Z = default(int);
        __Point_ctor(x, y);
        this.Z = z;
    }

    private void __Point_ctor(int x, int y)
    {
        this.X = x;
        this.Y = y;
    }

    private Point3D(int p0, int p1, int p2, Mist.Emission.CSharp.__StructInit marker)
    {
        this.X = p0;
        this.Y = p1;
        this.Z = p2;
    }

    internal static Point3D __FromParts(int p0, int p1, int p2, Mist.Emission.CSharp.__StructInit marker)
    {
        return new Point3D(p0, p1, p2, default(Mist.Emission.CSharp.__StructInit));
    }

    public static implicit operator Snippet.Point(Point3D value)
    {
        return Snippet.Point.__FromParts(value.X, value.Y, default);
    }
}
