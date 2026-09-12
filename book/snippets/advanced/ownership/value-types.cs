using Standard;

namespace Snippet;

[System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Sequential)]
public struct Vec2
{
    public int X;
    public int Y;

    public Vec2(int x, int y)
    {
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

    public readonly string Describe()
    {
        return $"Vec2({X}, {Y})";
    }
}

public class ValueTypeDemo
{
    public static void AcceptReadonly(ref readonly Snippet.Vec2 v)
    {
        System.Console.WriteLine($"readonly ref vec={v.Describe()}");
    }

    public static void AcceptFrozen(Snippet.Vec2 v)
    {
        System.Console.WriteLine($"frozen vec={v.Describe()}");
    }

    public static void AcceptBorrowed(Snippet.Vec2 v)
    {
        System.Console.WriteLine($"borrowed vec={v.Describe()}");
    }

    public static void AcceptOwned(Snippet.Vec2 v)
    {
        System.Console.WriteLine($"owned vec={v.Describe()}");
    }

    public static void Run()
    {
        Snippet.Vec2 v = new Snippet.Vec2(3, 4);
        AcceptReadonly(in v);
        AcceptFrozen(v);
        AcceptBorrowed(v);
        AcceptOwned(v);
        System.Console.WriteLine($"original after all passes={v.Describe()}");
    }

    public ValueTypeDemo()
    {
    }

    public static ValueTypeDemo __CreateDefault()
    {
        return new ValueTypeDemo();
    }
}
