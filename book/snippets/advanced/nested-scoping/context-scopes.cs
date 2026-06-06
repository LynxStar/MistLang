using System;

namespace Snippet;

public class Logger
{
    public static void Log(string msg)
    {
        var prefix = (string)MistContext.Peek<string>();
        Console.WriteLine($"[{prefix}] {msg}");
    }

    public static Logger __CreateDefault()
    {
        var inst = new Logger();
        return inst;
    }
}
public class ContextScopeDemo
{
    public static void Main(string[] args)
    {
        var prefix = "OUTER";
        MistContext.Push<string>(prefix);
        try
        {
            Logger.Log("message 1");
            {
                var prefix = "INNER";
                MistContext.Push<string>(prefix);
                try
                {
                    Logger.Log("message 2");
                }
                finally
                {
                    MistContext.Pop<string>();
                }
            }
            Logger.Log("message 3");
        }
        finally
        {
            MistContext.Pop<string>();
        }
    }

    public static ContextScopeDemo __CreateDefault()
    {
        var inst = new ContextScopeDemo();
        return inst;
    }
}
