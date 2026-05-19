using System;
using System.Threading.Tasks;

async Task<int> ComputeValue()
{
    return 42;
}

async Task Main()
{
    var task = ComputeValue();

    // Suspend until the task completes, unwrap to int
    var result = await task;

    Console.WriteLine(result);  // 42
}
