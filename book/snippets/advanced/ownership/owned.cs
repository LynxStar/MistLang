using Standard;
using System;

namespace OwnershipSystem;

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
public class OwnedDemo
{
    public static void Run()
    {
        Pair p = new Pair(3, 7);
        p.SetX(100);
        System.Console.WriteLine($"owned pair={p.Describe()}");
    }

    public static OwnedDemo __CreateDefault()
    {
        var inst = new OwnedDemo();
        return inst;
    }
}
