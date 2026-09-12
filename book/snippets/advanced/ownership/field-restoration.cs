using Standard;

namespace Snippet;

public class Pair
{
    public string Left;
    public string Right;

    public Pair(string left, string right)
    {
        this.Left = left;
        this.Right = right;
    }
}

public class FieldRestorationDemo
{
    public static void Run()
    {
        Snippet.Pair p = new Snippet.Pair("alpha", "beta");
        string extracted = p.Left;
        System.Console.WriteLine($"extracted={extracted}");
        p.Left = "restored";
        System.Console.WriteLine($"left after restore={p.Left}");
        System.Console.WriteLine($"right={p.Right}");
    }

    public FieldRestorationDemo()
    {
    }

    public static FieldRestorationDemo __CreateDefault()
    {
        return new FieldRestorationDemo();
    }
}
