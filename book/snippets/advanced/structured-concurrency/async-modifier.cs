using Standard;

namespace Snippet;

public class DataService
{
    private async System.Threading.Tasks.Task<int> FetchCount()
    {
        return 42;
    }

    private async System.Threading.Tasks.Task LogMessage(string message)
    {
        System.Console.WriteLine(message);
    }

    public static async System.Threading.Tasks.Task<string> GetName()
    {
        return "Mist";
    }

    public DataService()
    {
    }

    public static DataService __CreateDefault()
    {
        return new DataService();
    }
}
