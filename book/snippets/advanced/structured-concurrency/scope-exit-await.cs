using System;
using System.Threading.Tasks;

async Task<string> FetchData()
{
    return "data";
}

async Task SendNotification()
{
    Console.WriteLine("Notification sent");
}

// Discarded tasks are awaited at scope exit
async Task ProcessAll()
{
    var __task_0 = FetchData();        // discarded — not awaited
    var __task_1 = SendNotification(); // discarded — not awaited

    Console.WriteLine("Processing...");

    // Compiler inserts this at scope exit:
    await Task.WhenAll(__task_0, __task_1);
}
