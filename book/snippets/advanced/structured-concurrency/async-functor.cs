using System;
using System.Threading.Tasks;

int ComputeSync()
{
    return 2 + 2;
}

async Task<int> ComputeAsync()
{
    return 3 + 3;
}

async Task Main()
{
    // Sync method lifted into a task via Task.Run
    var task1 = Task.Run(() => ComputeSync());

    // Async method captured as task (no auto-await)
    var task2 = ComputeAsync();

    // Auto-unwrap: compiler inserts await
    var value = await ComputeAsync();

    await Task.WhenAll(task1, task2);
}
