using Standard;

async System.Threading.Tasks.Task<int> SlowCompute()
{
    var __e1309 = System.Threading.Tasks.Task.Yield();
    try
    {
        return 42;
    }
    finally
    {
        await __e1309;
    }
}
async System.Threading.Tasks.Task Main()
{
    System.Threading.Tasks.Task<int> task = SlowCompute();
    try
    {
        var __e1310 = System.Threading.Tasks.Task.Yield();
        try
        {
            int result = await task;
            System.Console.WriteLine(result.ToString(System.Globalization.CultureInfo.InvariantCulture));
        }
        finally
        {
            await __e1310;
        }
    }
    finally
    {
        await task;
    }
}
