using System;

namespace Snippet;

public class Counter
{
    private static int totalCreated;

    private string name;

    private int value;

    public Counter(string name)
    {
        this.name = name;
        this.value = 0;
        totalCreated = totalCreated + 1;
    }

    public void Increment()
    {
        this.value = this.value + 1;
    }

    public string Describe()
    {
        return $"Counter '{name}' = {value}";
    }

    public static void Main(string[] args)
    {
        var c = new Counter("Alpha");
        c.Increment();
        c.Increment();
        Console.WriteLine(c.Describe());
    }

    protected Counter()
    {
        this.name = "";
        this.value = 0;
    }

    public static Counter __CreateDefault()
    {
        var inst = new Counter();
        return inst;
    }
}
