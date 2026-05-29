using System;

namespace Snippet;

public class DataService
{
    async System.Threading.Tasks.Task<int> FetchCount()
    {
        return 42;
    }

    async System.Threading.Tasks.Task LogMessage(string message)
    {
        System.Console.WriteLine(message);
    }

    public static async System.Threading.Tasks.Task<string> GetName()
    {
        return "Mist";
    }

    public static DataService __CreateDefault()
    {
        var inst = new DataService();
        return inst;
    }
}
