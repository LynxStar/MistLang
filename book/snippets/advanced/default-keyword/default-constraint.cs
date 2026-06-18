using System;

namespace Snippet;

public class Pair
{
    public int X;

    public int Y;

    public Pair()
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
public class Factory
{
    public static T Create<T>(Func<T> __defaultT)
        // where T : default (Mist constraint — enforced at compile time)
    {
        return __defaultT();
    }

    public static Factory __CreateDefault()
    {
        var inst = new Factory();
        return inst;
    }
}
public class Program
{
    public static void Main(string[] args)
    {
        System.Console.OutputEncoding = System.Text.Encoding.UTF8;
        var p = Factory.Create<Pair>(() => Pair.__CreateDefault());
        Console.WriteLine($"X={p.X} Y={p.Y}");
    }

    public static Program __CreateDefault()
    {
        var inst = new Program();
        return inst;
    }
}
