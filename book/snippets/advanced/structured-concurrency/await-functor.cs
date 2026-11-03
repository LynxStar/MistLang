using Standard;

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
        System.Console.WriteLine(result.ToString(System.Globalization.CultureInfo.InvariantCulture));
    }
    finally
    {
        await task;
    }
}
