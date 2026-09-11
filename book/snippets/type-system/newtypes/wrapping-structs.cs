using System;

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

    public string Describe()
    {
        return $"({this.X}, {this.Y})";
    }
}

[System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Sequential)]
internal struct Position
{
    public int X;
    public int Y;

    public Position(int x, int y)
    {
        this.X = x;
        this.Y = y;
    }

    public string Describe()
    {
        return $"({this.X}, {this.Y})";
    }
}
