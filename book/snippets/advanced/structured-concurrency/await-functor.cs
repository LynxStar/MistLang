using System;

async System.Threading.Tasks.Task<int> ComputeValue()
{
    return 42;
}
async System.Threading.Tasks.Task Main()
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
