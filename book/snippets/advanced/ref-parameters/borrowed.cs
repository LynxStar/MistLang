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
public class Program
{
    public static void MutateBorrowed(Pair p)
    {
        p.SetX(999);
        Console.WriteLine($"mutated={p.Describe()}");
    }

    public static void Main()
    {
        Pair p = new Pair(1, 2);
        MutateBorrowed(p);
        Console.WriteLine($"owner after={p.Describe()}");
    }

    public static Program __CreateDefault()
    {
        var inst = new Program();
        return inst;
    }
}
