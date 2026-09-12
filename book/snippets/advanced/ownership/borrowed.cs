using Standard;

namespace Snippet;

public class BorrowedDemo
{
    public static void MutatePair(Snippet.Pair p)
    {
        p.SetX(999);
        System.Console.WriteLine($"borrowed mutated={p.Describe()}");
    }

    public static void Run()
    {
        Snippet.Pair p = new Snippet.Pair(1, 2);
        MutatePair(p);
        System.Console.WriteLine($"owner after return={p.Describe()}");
    }

    public BorrowedDemo()
    {
    }

    public static BorrowedDemo __CreateDefault()
    {
        return new BorrowedDemo();
    }
}
