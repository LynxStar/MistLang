using System;

namespace OwnershipSystem;

public class ReadonlyRefDemo
{
    public static void ReadPair(Pair p)
    {
        var sum = p.X + p.Y;
        Console.WriteLine($"readonly ref sum={sum}");
    }

    public static void Run()
    {
        Pair p = new Pair(3, 7);
        ReadPair(p);
        p.SetX(100);
        Console.WriteLine($"after readonly ref={p.Describe()}");
    }
}
