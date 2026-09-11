using System;

namespace Snippet;

public class Program
{
    public static async System.Threading.Tasks.Task<int> ComputeValue()
    {
        return 42;
    }

    public static async System.Threading.Tasks.Task Main(string[] args)
    {
        System.Threading.Tasks.Task<int> task = ComputeValue();
        try
        {
            int result = await task;
            Console.WriteLine(result);
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
