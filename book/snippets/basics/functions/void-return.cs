using System;

namespace Snippet;

public class Logger
{
    public static void Log(string message)
    {
        Console.WriteLine($"[LOG] {message}");
    }

    public static void Main(string[] args)
    {
        System.Console.OutputEncoding = System.Text.Encoding.UTF8;
        Log("Application started");
        Log("Processing complete");
    }

    public static Logger __CreateDefault()
    {
        var inst = new Logger();
        return inst;
    }
}
