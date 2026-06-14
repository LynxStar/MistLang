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

    protected Pair()
    {
        this.Left = "";
        this.Right = "";
    }

    public static Pair __CreateDefault()
    {
        var inst = new Pair();
        return inst;
    }
}
public class PartialMoveDemo
{
    public static void Run()
    {
        var p = new Pair("alpha", "beta");
        var extracted = p.Left;
        System.Console.WriteLine($"extracted={extracted}");
        System.Console.WriteLine($"right still live={p.Right}");
    }

    public static PartialMoveDemo __CreateDefault()
    {
        var inst = new PartialMoveDemo();
        return inst;
    }
}
