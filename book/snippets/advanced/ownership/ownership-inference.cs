using System;

namespace Snippet;

public class Counter
{
    public int Value;

    public Counter(int value)
    {
        this.Value = value;
    }

    public void Increment()
    {
        this.Value = this.Value + 1;
    }

    public string Describe()
    {
        return $"Counter({Value})";
    }
}
public class OwnershipInferenceDemo
{
    public static void Run()
    {
        var counter = new Counter(0);
        counter.Increment();
        System.Console.WriteLine($"inferred owned={counter.Describe()}");
        var label = "hello";
        var alias = label;
        System.Console.WriteLine($"label={label}");
        System.Console.WriteLine($"alias={alias}");
    }

    public static OwnershipInferenceDemo __CreateDefault()
    {
        var inst = new OwnershipInferenceDemo();
        return inst;
    }
}
