using System;

namespace Snippet;

public class Program
{
    public static async System.Threading.Tasks.Task<int> ExpensiveComputation()
    {
        Console.WriteLine("Computing...");
        return 42;
    }

    public static async System.Threading.Tasks.Task Main(string[] args)
    {
        System.Threading.Tasks.Task<int> task = ExpensiveComputation();
        try
        {
            int first = await task;
            int second = await task;
            Console.WriteLine(first == second);
        }
        finally
        {
            await task;
        }
    }

    public Program()
    {
    }

    public static Program __CreateDefault()
    {
        return new Program();
    }
}
