using System;
using System.Threading.Tasks;

async Task<int> SlowCompute()
{
    await Task.Yield();  // explicit yield point
    return 42;
}

async Task Main()
{
    // Each async call is a yield point — the scheduler may
    // run other tasks before resuming here
    var task = SlowCompute();

    // Explicit yield: give other tasks a chance to run
    await Task.Yield();

    var result = await task;
    Console.WriteLine(result);
}
