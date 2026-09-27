using Standard;

namespace Snippet;

public class Pair
{
    public int X;
    public int Y;

    public Pair(int x, int y)
    {
        this.X = x;
        this.Y = y;
    }

    public void SetX(int x)
    {
        this.X = x;
    }

    public string Describe()
    {
        return System.FormattableString.Invariant($"Pair({X}, {Y})");
    }
}

public class OwnedDemo
{
    public static void Run()
    {
        Snippet.Pair p = new Snippet.Pair(3, 7);
        p.SetX(100);
        System.Console.WriteLine($"owned pair={p.Describe()}");
    }

    public OwnedDemo()
    {
    }

    public static OwnedDemo __CreateDefault()
    {
        return new OwnedDemo();
    }
}
