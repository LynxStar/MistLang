using System;

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
        var p = new Pair("alpha", "beta");
        var extracted = p.Left;
        System.Console.WriteLine($"extracted={extracted}");
        p.Left = "restored";
        System.Console.WriteLine($"left after restore={p.Left}");
        System.Console.WriteLine($"right={p.Right}");
    }

    public static FieldRestorationDemo __CreateDefault()
    {
        var inst = new FieldRestorationDemo();
        return inst;
    }
}
