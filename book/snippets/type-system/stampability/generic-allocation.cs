using Standard;

namespace StampableConstraint;

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
    private static int AllocationLength<T>()
        where T : unmanaged
    {
        T[] items = new T[2];
        System.Span<T> buffer = stackalloc T[3];
        return items.Length + buffer.Length;
    }

    private static int Forward<T>()
        where T : unmanaged
    {
        return AllocationLength<T>();
    }

    public static void Main(string[] args)
    {
        System.Console.WriteLine($"int length={Forward<int>()}");
        System.Console.WriteLine($"point length={Forward<StampableConstraint.Point>()}");
    }

    public Program()
    {
    }

    public static Program __CreateDefault()
    {
        return new Program();
    }
}
