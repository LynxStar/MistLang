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
        Snippet.Counter c = new Snippet.Counter(10);
        Console.WriteLine($"readonly view={c.Describe()}");
    }

    public NonOwnedReadonlyDemo()
    {
    }

    public static NonOwnedReadonlyDemo __CreateDefault()
    {
        return new NonOwnedReadonlyDemo();
    }
}
