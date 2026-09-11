using System;

namespace Snippet;

public class Program
{
    public static int ComputeSync()
    {
        return 2 + 2;
    }

    public static async System.Threading.Tasks.Task<int> ComputeAsync()
    {
        return 3 + 3;
    }

    public static async System.Threading.Tasks.Task Main(string[] args)
    {
        System.Threading.Tasks.Task<int> task1 = System.Threading.Tasks.Task.Run(() => ComputeSync());
        try
        {
            System.Threading.Tasks.Task<int> task2 = ComputeAsync();
            try
            {
                int value = await ComputeAsync();
                Console.WriteLine(value);
            }
            finally
            {
                await task2;
            }
        }
        finally
        {
            await task1;
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
