using System;

namespace Snippet;

public class Program
{
    public static async System.Threading.Tasks.Task<string> FetchData()
    {
        return "data";
    }

    public static async System.Threading.Tasks.Task SendNotification()
    {
        Console.WriteLine("Notification sent");
    }

    public static async System.Threading.Tasks.Task ProcessAll()
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

    public static async System.Threading.Tasks.Task Main(string[] args)
    {
        await ProcessAll();
    }

    public Program()
    {
    }

    public static Program __CreateDefault()
    {
        return new Program();
    }
}
