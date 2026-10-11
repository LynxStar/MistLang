using Standard;

namespace Snippet;

[System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Sequential)]
public struct Coordinate
{
    public int x;
    public int y;

    public Coordinate(int x, int y)
    {
        this.x = default(int);
        this.y = default(int);
        this.x = x;
        this.y = y;
    }

    private Coordinate(int p0, int p1, Mist.Emission.CSharp.__StructInit marker)
    {
        this.x = p0;
        this.y = p1;
    }

    internal static Coordinate __FromParts(int p0, int p1, Mist.Emission.CSharp.__StructInit marker)
    {
        return new Coordinate(p0, p1, default(Mist.Emission.CSharp.__StructInit));
    }
}
