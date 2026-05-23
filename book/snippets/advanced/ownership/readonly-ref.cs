using Standard;
using System;

namespace OwnershipSystem;

public class ReadonlyRefDemo
{
    public static void ReadPair(ref readonly Pair p)
    {
        var sum = p.X + p.Y;
        System.Console.WriteLine($"readonly ref sum={sum}");
    }

    public static void Run()
    {
        Pair p = new Pair(3, 7);
        ReadPair(p);
        p.SetX(100);
        System.Console.WriteLine($"after readonly ref={p.Describe()}");
    }

    public static ReadonlyRefDemo __CreateDefault()
    {
        var inst = new ReadonlyRefDemo();
        return inst;
    }
}
