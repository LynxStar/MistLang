using Standard;
using System;

namespace RefParameters;

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

    protected Pair()
    {
        this.X = 0;
        this.Y = 0;
    }

    public static Pair __CreateDefault()
    {
        var inst = new Pair();
        return inst;
    }
}
public class Program
{
    public static void MutateBorrowed(Pair p)
    {
        p.SetX(999);
        System.Console.WriteLine($"mutated={p.Describe()}");
    }

    public static void Main()
    {
        Pair p = new Pair(1, 2);
        MutateBorrowed(p);
        System.Console.WriteLine($"owner after={p.Describe()}");
    }

    public static Program __CreateDefault()
    {
        var inst = new Program();
        return inst;
    }
}
