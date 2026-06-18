using System;

namespace Snippet;

public class Counter
{
    private static int totalCreated;

    public Counter()
    {
        totalCreated = totalCreated + 1;
    }

    public static int GetTotalCreated()
    {
        return totalCreated;
    }

    public static void Main(string[] args)
    {
        System.Console.OutputEncoding = System.Text.Encoding.UTF8;
        var c1 = new Counter();
        var c2 = new Counter();
        Console.WriteLine($"Total: {Counter.GetTotalCreated()}");
    }
}
