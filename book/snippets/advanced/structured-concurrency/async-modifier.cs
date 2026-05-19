using System;
using System.Threading.Tasks;

public class DataService
{
    // int becomes Task<int> in C#
    async Task<int> FetchCount()
    {
        return 42;
    }

    // void becomes Task in C#
    async Task LogMessage(string message)
    {
        Console.WriteLine(message);
    }

    public static async Task<string> GetName()
    {
        return "Mist";
    }
}
