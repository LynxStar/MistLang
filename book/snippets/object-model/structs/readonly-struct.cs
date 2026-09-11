using System;

namespace Snippet;

[System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Sequential)]
public readonly struct Vector2
{
    public readonly int X;
    public readonly int Y;

    public Vector2(int x, int y)
    {
        this.X = x;
        this.Y = y;
    }

    private Vector2(int p0, int p1, Mist.Emission.CSharp.__StructInit marker)
    {
        this.X = p0;
        this.Y = p1;
    }

    internal static Vector2 __FromParts(int p0, int p1, Mist.Emission.CSharp.__StructInit marker)
    {
        return new Vector2(p0, p1, default(Mist.Emission.CSharp.__StructInit));
    }

    public int MagnitudeSquared()
    {
        return X * X + Y * Y;
    }

    public string Describe()
    {
        return $"Vector2(X={X}, Y={Y})";
    }
}
