using Standard;

namespace Snippet;

public class Logger
{
    public static void Log(string message)
    {
        System.Console.WriteLine($"[LOG] {message}");
    }

    public static void Main(string[] args)
    {
        Log("Application started");
        Log("Processing complete");
    }

    public Logger()
    {
    }

    public static Logger __CreateDefault()
    {
        return new Logger();
    }
}
