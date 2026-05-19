using System;
using System.Threading.Tasks;

async Task<int> ExpensiveComputation()
{
    Console.WriteLine("Computing...");
    return 42;
}

async Task Main()
{
    var task = ExpensiveComputation();

    // Await the same task multiple times
    var first = await task;   // suspends until complete, returns 42
    var second = await task;  // already complete, returns 42 immediately

    Console.WriteLine(first == second);  // true
}
