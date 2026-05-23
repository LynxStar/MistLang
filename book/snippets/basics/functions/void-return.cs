using Standard;
using System;

namespace Demo;

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

    public static Logger __CreateDefault()
    {
        var inst = new Logger();
        return inst;
    }
}
