using System;

namespace Snippet;

public class Program
{
    public static async System.Threading.Tasks.Task<int> FetchData()
    {
        return 100;
    }

    public static async System.Threading.Tasks.Task<string> FormatData(int value)
    {
        return "Value: " + value.ToString();
    }

    public static async System.Threading.Tasks.Task Main(string[] args)
    {
        int result = await FetchData();
        string formatted = await FormatData(result);
        Console.WriteLine(formatted);
    }

    public Program()
    {
    }

    public static Program __CreateDefault()
    {
        return new Program();
    }
}
