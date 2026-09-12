using Standard;

async System.Threading.Tasks.Task<int> ComputeValue()
{
    return 42;
}
async System.Threading.Tasks.Task Main()
{
    System.Threading.Tasks.Task<int> task = ComputeValue();
    int result = await task;
    System.Console.WriteLine(result);
}
