using Standard;

async System.Threading.Tasks.Task<string> FetchData()
{
    return "data";
}
async System.Threading.Tasks.Task SendNotification()
{
    System.Console.WriteLine("Notification sent");
}
async System.Threading.Tasks.Task ProcessAll()
{
    var __e1 = FetchData();
    try
    {
        var __e2 = SendNotification();
        try
        {
            System.Console.WriteLine("Processing...");
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
