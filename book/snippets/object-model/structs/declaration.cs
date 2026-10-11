using Standard;

namespace Snippet;

[System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Sequential)]
public struct Color
{
    public int r;
    public int g;
    public int b;

    public Color(int r, int g, int b)
    {
        this.r = default(int);
        this.g = default(int);
        this.b = default(int);
        this.r = r;
        this.g = g;
        this.b = b;
    }

    private Color(int p0, int p1, int p2, Mist.Emission.CSharp.__StructInit marker)
    {
        this.r = p0;
        this.g = p1;
        this.b = p2;
    }

    internal static Color __FromParts(int p0, int p1, int p2, Mist.Emission.CSharp.__StructInit marker)
    {
        return new Color(p0, p1, p2, default(Mist.Emission.CSharp.__StructInit));
    }

    public readonly int Brightness()
    {
        return (this.r + this.g + this.b) / 3;
    }
}
