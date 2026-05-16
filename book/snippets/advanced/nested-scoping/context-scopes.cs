using System;

namespace ScopingDemo;

public class Logger
{
    public static void Log(string msg)
    {
        var prefix = (string)MistContext.Peek<string>();
        Console.WriteLine($"[{prefix}] {msg}");
    }
}

public class ContextScopeDemo
{
    public static void Main(string[] args)
    {
        MistContext.Push<string>("OUTER");
        var prefix = "OUTER";
        Logger.Log("message 1");

        {
            MistContext.Push<string>("INNER");
            var prefix = "INNER";
            Logger.Log("message 2");
            MistContext.Pop<string>();
        }

        Logger.Log("message 3");
        MistContext.Pop<string>();
    }
}
