using Standard;

async System.Threading.Tasks.Task<int> SlowCompute()
{
    var __e1 = System.Threading.Tasks.Task.Yield();
    try
    {
        return 42;
    }
    finally
    {
        await __e1;
    }
}
async System.Threading.Tasks.Task Main()
{
    System.Threading.Tasks.Task<int> task = SlowCompute();
    var __e2 = System.Threading.Tasks.Task.Yield();
    try
    {
        int result = await task;
        System.Console.WriteLine(result);
    }
    finally
    {
        await __e2;
    }
}
