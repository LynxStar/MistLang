using Standard;

int ComputeSync()
{
    return 2 + 2;
}
async System.Threading.Tasks.Task<int> ComputeAsync()
{
    return 3 + 3;
}
async System.Threading.Tasks.Task Main()
{
    System.Threading.Tasks.Task<int> task1 = System.Threading.Tasks.Task.Run(() => ComputeSync());
    try
    {
        System.Threading.Tasks.Task<int> task2 = ComputeAsync();
        try
        {
            int value = await ComputeAsync();
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
