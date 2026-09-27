using Standard;

namespace Snippet;

public interface IShape
{
    int Area();
}

[System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Sequential)]
public struct Circle : Snippet.IShape
{
    private Circle(Mist.Emission.CSharp.__StructInit marker)
    {
    }

    internal static Circle __FromParts(Mist.Emission.CSharp.__StructInit marker)
    {
        return new Circle(default(Mist.Emission.CSharp.__StructInit));
    }

    public readonly int Area()
    {
        return 3;
    }

    public Circle()
    {
    }

    public static Circle __CreateDefault()
    {
        return new Circle();
    }
}

[System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Sequential)]
public struct Square : Snippet.IShape
{
    private Square(Mist.Emission.CSharp.__StructInit marker)
    {
    }

    internal static Square __FromParts(Mist.Emission.CSharp.__StructInit marker)
    {
        return new Square(default(Mist.Emission.CSharp.__StructInit));
    }

    public readonly int Area()
    {
        return 4;
    }

    public Square()
    {
    }

    public static Square __CreateDefault()
    {
        return new Square();
    }
}

public class Program
{
    public static void Main(string[] cmdArgs)
    {
        ShapePair_2<Snippet.Circle, Snippet.Square> pair = new ShapePair_2<Snippet.Circle, Snippet.Square>((new Snippet.Circle(), new Snippet.Square()));
        System.Console.WriteLine(System.FormattableString.Invariant($"area={pair.TotalArea()}"));
    }

    public Program()
    {
    }

    public static Program __CreateDefault()
    {
        return new Program();
    }
}
