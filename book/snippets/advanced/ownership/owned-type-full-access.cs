using System;

namespace Snippet;

public class Accumulator
{
    private int Total;

    public Accumulator(int seed)
    {
        this.Total = seed;
    }

    public void Add(int amount)
    {
        this.Total = Total + amount;
    }

    public string Describe()
    {
        return $"Accumulator(Total={Total})";
    }
}

public class OwnedTypeFullAccessDemo
{
    public static void Run()
    {
        Snippet.Accumulator a = new Snippet.Accumulator(100);
        a.Add(5);
        a.Add(20);
        Console.WriteLine($"owned type={a.Describe()}");
    }

    public OwnedTypeFullAccessDemo()
    {
    }

    public static OwnedTypeFullAccessDemo __CreateDefault()
    {
        return new OwnedTypeFullAccessDemo();
    }
}
