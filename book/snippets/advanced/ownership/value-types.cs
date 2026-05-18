using System;

namespace OwnershipSystem;

public struct Vec2
{
    public int X;

    public int Y;

    public Vec2(int x, int y)
    {
        this.X = x;
        this.Y = y;
    }

    public readonly string Describe()
    {
        return $"Vec2({X}, {Y})";
    }

    public static Vec2 __CreateDefault()
    {
        var inst = new Vec2();
        inst.X = 0;
        inst.Y = 0;
        return inst;
    }
}
public class ValueTypeDemo
{
    public static void AcceptReadonly(ref readonly Vec2 v)
    {
        Console.WriteLine($"readonly ref vec={v.Describe()}");
    }

    public static void AcceptFrozen(Vec2 v)
    {
        Console.WriteLine($"frozen vec={v.Describe()}");
    }

    public static void AcceptBorrowed(Vec2 v)
    {
        Console.WriteLine($"borrowed vec={v.Describe()}");
    }

    public static void AcceptOwned(Vec2 v)
    {
        Console.WriteLine($"owned vec={v.Describe()}");
    }

    public static void Run()
    {
        var v = new Vec2(3, 4);
        AcceptReadonly(v);
        AcceptFrozen(v);
        AcceptBorrowed(v);
        AcceptOwned(v);
        Console.WriteLine($"original after all passes={v.Describe()}");
    }

    public static ValueTypeDemo __CreateDefault()
    {
        var inst = new ValueTypeDemo();
        return inst;
    }
}
