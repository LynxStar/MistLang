using Standard;
using System;

namespace OwnershipSystem;

public class BorrowedDemo
{
    public static void MutatePair(Pair p)
    {
        p.SetX(999);
        System.Console.WriteLine($"borrowed mutated={p.Describe()}");
    }

    public static void Run()
    {
        Pair p = new Pair(1, 2);
        MutatePair(p);
        System.Console.WriteLine($"owner after return={p.Describe()}");
    }

    public static BorrowedDemo __CreateDefault()
    {
        var inst = new BorrowedDemo();
        return inst;
    }
}
