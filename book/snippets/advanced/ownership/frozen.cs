using Standard;
using System;

namespace OwnershipSystem;

public class FrozenDemo
{
    public static void FreezeAndRead(Pair p)
    {
        var sum = p.X + p.Y;
        System.Console.WriteLine($"frozen sum={sum}");
    }

    public static void Run()
    {
        Pair p = new Pair(5, 10);
        FreezeAndRead(p);
        p.SetX(50);
        System.Console.WriteLine($"after thaw={p.Describe()}");
    }

    public static FrozenDemo __CreateDefault()
    {
        var inst = new FrozenDemo();
        return inst;
    }
}
