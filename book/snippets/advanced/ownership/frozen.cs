using Standard;

namespace Snippet;

public class FrozenDemo
{
    public static void FreezeAndRead(Snippet.Pair p)
    {
        int sum = p.X + p.Y;
        System.Console.WriteLine(System.FormattableString.Invariant($"frozen sum={sum}"));
    }

    public static void Run()
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
