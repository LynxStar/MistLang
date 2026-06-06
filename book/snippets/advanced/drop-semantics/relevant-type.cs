using System;

namespace Snippets;

public class Logger
{
    string Name;

    bool Flushed;

    public Logger(string name)
    {
        this.Name = name;
        this.Flushed = false;
    }

    public void Drop()
    {
        if (Flushed)
        {
            return;
        }
        this.Flushed = true;
        System.Console.WriteLine($"Logger({Name}) flushed");
    }

    public void Log(string msg)
    {
        System.Console.WriteLine($"[{Name}] {msg}");
    }

    protected Logger()
    {
        this.Name = "";
        this.Flushed = false;
    }

    public static Logger __CreateDefault()
    {
        var inst = new Logger();
        return inst;
    }
}
public class Program
{
    public static void Main()
    {
        var logger = new Logger("app");
        try
        {
            var alias = logger;
            try
            {
                alias.Log("hello");
                logger.Log("world");
            }
            finally
            {
                alias.Drop();
            }
        }
        finally
        {
            logger.Drop();
        }
    }

    public static Program __CreateDefault()
    {
        var inst = new Program();
        return inst;
    }
}
