using System;

namespace ScopingDemo;

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
