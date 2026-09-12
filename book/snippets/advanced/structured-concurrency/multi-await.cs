using Standard;

async System.Threading.Tasks.Task<int> ExpensiveComputation()
{
    System.Console.WriteLine("Computing...");
    return 42;
}
async System.Threading.Tasks.Task Main()
{
    System.Threading.Tasks.Task<int> task = ExpensiveComputation();
    int first = await task;
    int second = await task;
    System.Console.WriteLine(first == second);
}
