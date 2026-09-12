using Standard;

namespace Snippet;

[System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Sequential)]
public struct Vector2
{
    public int X;
    public int Y;

    private Vector2(int p0, int p1, Mist.Emission.CSharp.__StructInit marker)
    {
        this.X = p0;
        this.Y = p1;
    }

    internal static Vector2 __FromParts(int p0, int p1, Mist.Emission.CSharp.__StructInit marker)
    {
        return new Vector2(p0, p1, default(Mist.Emission.CSharp.__StructInit));
    }

    public Vector2()
    {
        this.X = default(int);
        this.Y = default(int);
    }

    public static Vector2 __CreateDefault()
    {
        return new Vector2();
    }
}

public class Program
{
    public static void Main(string[] args)
    {
        int[] counts = new int[4];
        System.Console.WriteLine($"counts[0]={counts[0]}");
        Snippet.Vector2[] positions = new Snippet.Vector2[3];
        System.Console.WriteLine($"positions[0].X={positions[0].X}");
        System.Console.WriteLine($"positions[1].Y={positions[1].Y}");
    }

    public Program()
    {
    }

    public static Program __CreateDefault()
    {
        return new Program();
    }
}
