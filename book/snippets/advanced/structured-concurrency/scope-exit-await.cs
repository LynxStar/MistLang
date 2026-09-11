using System;

async System.Threading.Tasks.Task<string> FetchData()
{
    return "data";
}
async System.Threading.Tasks.Task SendNotification()
{
    Console.WriteLine("Notification sent");
}
async System.Threading.Tasks.Task ProcessAll()
{
    var __e1 = FetchData();
    try
    {
        var __e2 = SendNotification();
        try
        {
            Console.WriteLine("Processing...");
        }
        finally
        {
            await __e2;
        }
    }
    finally
    {
        await __e1;
    }
}
