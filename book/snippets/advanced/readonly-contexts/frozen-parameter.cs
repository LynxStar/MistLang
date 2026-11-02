using Standard;

namespace Snippet;

public class Pair
{
    public int X;
    public int Y;

    public Pair(int x, int y)
    {
        this.X = default(int);
        this.Y = default(int);
        this.X = x;
        this.Y = y;
    }

    public int Sum()
    {
        return this.X + this.Y;
    }

    public string Describe()
    {
        return System.FormattableString.Invariant($"Pair({this.X}, {this.Y})");
    }

    public void SetX(int x)
    {
        this.X = x;
    }
}

public class FrozenDemo
{
    public static void FreezeAndRead(Snippet.Pair p)
    {
        int sum = p.Sum();
        System.Console.WriteLine(System.FormattableString.Invariant($"frozen sum={sum}"));
    }

    public static void Main(string[] args)
    {
        Snippet.Pair p = new Snippet.Pair(5, 10);
        FreezeAndRead(p);
        p.SetX(50);
        System.Console.WriteLine($"after thaw={p.Describe()}");
    }

    public FrozenDemo()
    {
    }

    public static FrozenDemo __CreateDefault()
    {
        return new FrozenDemo();
    }
}
