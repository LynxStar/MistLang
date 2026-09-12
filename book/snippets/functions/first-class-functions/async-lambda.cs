using Standard;

namespace Snippet;

public class Program
{
    public static async System.Threading.Tasks.Task<string> FetchData(int id)
    {
        return $"data-{id}";
    }

    public static async System.Threading.Tasks.Task Main(string[] args)
    {
        System.Func<int, System.Threading.Tasks.Task<string>> fetcher = async (int id) => await FetchData(id);
        System.Console.WriteLine($"fetcher(1)={await fetcher(1)}");
        System.Func<int, string> syncFetcher = (int id) => FetchData(id).GetAwaiter().GetResult();
        System.Console.WriteLine($"syncFetcher(2)={syncFetcher(2)}");
    }

    public Program()
    {
    }

    public static Program __CreateDefault()
    {
        return new Program();
    }
}
