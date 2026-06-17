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
    public static void FreezeAndRead(Pair p)
    {
        var sum = p.Sum();
        Console.WriteLine($"frozen sum={sum}");
    }

    public static void Main(string[] args)
    {
        System.Console.OutputEncoding = System.Text.Encoding.UTF8;
        Pair p = new Pair(5, 10);
        FreezeAndRead(p);
        p.SetX(50);
        Console.WriteLine($"after thaw={p.Describe()}");
    }

    public static FrozenDemo __CreateDefault()
    {
        var inst = new FrozenDemo();
        return inst;
    }
}
