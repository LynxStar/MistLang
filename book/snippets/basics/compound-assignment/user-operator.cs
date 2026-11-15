using Standard;

namespace Snippet;

[System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Sequential)]
public struct Vec2
{
    public int X;
    public int Y;

    public Vec2(int x, int y)
    {
        this.X = default(int);
        this.Y = default(int);
        this.X = x;
        this.Y = y;
    }

    private Vec2(int p0, int p1, Mist.Emission.CSharp.__StructInit marker)
    {
        this.X = p0;
        this.Y = p1;
    }

    internal static Vec2 __FromParts(int p0, int p1, Mist.Emission.CSharp.__StructInit marker)
    {
        return new Vec2(p0, p1, default(Mist.Emission.CSharp.__StructInit));
    }

    public static Snippet.Vec2 operator +(Snippet.Vec2 a, Snippet.Vec2 b)
    {
        return new Snippet.Vec2(a.X + b.X, a.Y + b.Y);
    }
}

public class Program
{
    public static void Main(string[] args)
    {
        Snippet.Vec2 position = new Snippet.Vec2(1, 2);
        position += new Snippet.Vec2(10, 20);
        System.Console.WriteLine(System.FormattableString.Invariant($"position is {position.X}, {position.Y}"));
    }

    public Program()
    {
    }

    public static Program __CreateDefault()
    {
        return new Program();
    }
}
