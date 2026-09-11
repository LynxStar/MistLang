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

public class PartialMoveDemo
{
    public static void Run()
    {
        Snippet.Pair p = new Snippet.Pair("alpha", "beta");
        string extracted = p.Left;
        Console.WriteLine($"extracted={extracted}");
        Console.WriteLine($"right still live={p.Right}");
    }

    public PartialMoveDemo()
    {
    }

    public static PartialMoveDemo __CreateDefault()
    {
        return new PartialMoveDemo();
    }
}
