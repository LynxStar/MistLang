using System;

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
        return $"Pair({X}, {Y})";
    }
}

public class BorrowedDemo
{
    public static void MutatePair(Snippet.Pair p)
    {
        p.SetX(999);
        Console.WriteLine($"borrowed mutated={p.Describe()}");
    }

    public static void Run()
    {
        Snippet.Pair p = new Snippet.Pair(1, 2);
        MutatePair(p);
        Console.WriteLine($"owner after return={p.Describe()}");
    }

    public BorrowedDemo()
    {
    }

    public static BorrowedDemo __CreateDefault()
    {
        return new BorrowedDemo();
    }
}
