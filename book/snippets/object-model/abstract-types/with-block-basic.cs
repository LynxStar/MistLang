using System;

namespace Snippet;

public interface INamed
{
    string Name();
}

public class Logger : Snippet.INamed
{
    public string GetLabel()
    {
        return "SystemLogger";
    }

    public Logger()
    {
    }

    public static Logger __CreateDefault()
    {
        return new Logger();
    }

    string Snippet.INamed.Name() => this.GetLabel();
}

public class Program
{
    public static void Main(string[] args)
    {
        Snippet.Logger logger = new Snippet.Logger();
        Snippet.INamed named = logger;
        Console.WriteLine(named.Name());
    }

    public Program()
    {
    }

    public static Program __CreateDefault()
    {
        return new Program();
    }
}
