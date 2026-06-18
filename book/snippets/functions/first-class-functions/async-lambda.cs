using System;

namespace Snippet;

public class Program
{
    public static async System.Threading.Tasks.Task<string> FetchData(int id)
    {
        return $"data-{id}";
    }

    public static async System.Threading.Tasks.Task Main(string[] args)
    {
        System.Console.OutputEncoding = System.Text.Encoding.UTF8;
        System.Func<int, System.Threading.Tasks.Task<string>> fetcher = async id => await FetchData(id);
        System.Console.WriteLine($"fetcher(1)={await fetcher(1)}");
        System.Func<int, string> syncFetcher = id => FetchData(id).GetAwaiter().GetResult();
        System.Console.WriteLine($"syncFetcher(2)={syncFetcher(2)}");
    }

    public static Program __CreateDefault()
    {
        var inst = new Program();
        return inst;
    }
}
