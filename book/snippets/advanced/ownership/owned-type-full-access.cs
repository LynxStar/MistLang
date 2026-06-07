using System;
using System.Text.RegularExpressions;

namespace Snippet;

public class Accumulator
{
    int Total;

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

    protected Accumulator()
    {
        this.Total = 0;
    }

    public static Accumulator __CreateDefault()
    {
        var inst = new Accumulator();
        return inst;
    }
}
public class OwnedTypeFullAccessDemo
{
    public static void Run()
    {
        Accumulator a = new Accumulator(100);
        a.Add(5);
        a.Add(20);
        System.Console.WriteLine($"owned type={a.Describe()}");
    }

    public static OwnedTypeFullAccessDemo __CreateDefault()
    {
        var inst = new OwnedTypeFullAccessDemo();
        return inst;
    }
}
