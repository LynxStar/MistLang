using System;

namespace Snippet;

public class Counter
{
    private static int totalCreated;

    private string name;

    public Counter(string name)
    {
        this.name = name;
        totalCreated = totalCreated + 1;
    }

    public static int GetTotalCreated()
    {
        return totalCreated;
    }

    public static void Main(string[] args)
    {
        var a = new Counter("Alpha");
        var b = new Counter("Beta");
        Console.WriteLine($"Total created: {Counter.GetTotalCreated()}");
    }

    protected Counter()
    {
        this.name = "";
    }

    public static Counter __CreateDefault()
    {
        var inst = new Counter();
        return inst;
    }
}
