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

    public int Sum()
    {
        return X + Y;
    }

    public string Describe()
    {
        return $"Pair({X}, {Y})";
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
        Console.WriteLine($"frozen sum={sum}");
    }

    public static void Main(string[] args)
    {
        Snippet.Pair p = new Snippet.Pair(5, 10);
        FreezeAndRead(p);
        p.SetX(50);
        Console.WriteLine($"after thaw={p.Describe()}");
    }

    public FrozenDemo()
    {
    }

    public static FrozenDemo __CreateDefault()
    {
        return new FrozenDemo();
    }
}
