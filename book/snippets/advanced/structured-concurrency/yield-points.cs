using System;

namespace Snippet;

public class Program
{
    public static async System.Threading.Tasks.Task<int> SlowCompute()
    {
        var __e3 = System.Threading.Tasks.Task.Yield();
        try
        {
            return 42;
        }
        finally
        {
            await __e3;
        }
    }

    public static async System.Threading.Tasks.Task Main(string[] args)
    {
        System.Threading.Tasks.Task<int> task = SlowCompute();
        try
        {
            var __e4 = System.Threading.Tasks.Task.Yield();
            try
            {
                int result = await task;
                Console.WriteLine(result);
            }
            finally
            {
                await __e4;
            }
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
