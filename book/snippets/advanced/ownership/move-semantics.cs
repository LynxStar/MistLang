using Standard;
using System;

namespace OwnershipSystem;

public class MoveDemo
{
    public static void ConsumeHandle(UniqueHandle h)
    {
        h.Touch();
        System.Console.WriteLine($"consumed={h.Describe()}");
    }

    public static void Run()
    {
        UniqueHandle h1 = new UniqueHandle(10);
        System.Console.WriteLine($"before move={h1.Describe()}");
        UniqueHandle h2 = h1;
        h2.Touch();
        System.Console.WriteLine($"after move to h2={h2.Describe()}");
        ConsumeHandle(h2);
        System.Console.WriteLine($"transfer complete");
    }

    public static MoveDemo __CreateDefault()
    {
        var inst = new MoveDemo();
        return inst;
    }
}
