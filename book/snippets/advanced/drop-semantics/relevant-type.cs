using System;

namespace Snippets;

public class Logger
{
    private string Name;
    private bool Flushed;

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
        Console.WriteLine($"Logger({Name}) flushed");
    }

    public void Log(string msg)
    {
        Console.WriteLine($"[{Name}] {msg}");
    }
}

public class Program
{
    public static void Main()
    {
        Snippets.Logger logger = new Snippets.Logger("app");
        try
        {
            Snippets.Logger alias = logger;
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

    public Program()
    {
    }

    public static Program __CreateDefault()
    {
        return new Program();
    }
}
