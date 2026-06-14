using System;

namespace Snippet;

public class Counter
{
    public int Value;

    public Counter(int value)
    {
        this.Value = value;
    }

    public string Describe()
    {
        return $"Counter({Value})";
    }

    public void Increment()
    {
        this.Value = Value + 1;
    }
}
public class NonOwnedReadonlyDemo
{
    public static void Run()
    {
        Counter c = new Counter(10);
        System.Console.WriteLine($"readonly view={c.Describe()}");
    }

    public static NonOwnedReadonlyDemo __CreateDefault()
    {
        var inst = new NonOwnedReadonlyDemo();
        return inst;
    }
}
