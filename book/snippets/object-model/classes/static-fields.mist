using System;

namespace Demo;

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
}
