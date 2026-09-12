using System;

async System.Threading.Tasks.Task<int> ExpensiveComputation()
{
    Console.WriteLine("Computing...");
    return 42;
}
async System.Threading.Tasks.Task Main()
{
    System.Threading.Tasks.Task<int> task = ExpensiveComputation();
    int first = await task;
    int second = await task;
    Console.WriteLine(first == second);
}
