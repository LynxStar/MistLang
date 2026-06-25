using System;

namespace Snippet;

interface INamed
{
    string Name();
}
public class Logger : INamed
{
    public string GetLabel()
    {
        return "SystemLogger";
    }

    string INamed.Name() => this.GetLabel();

    public static Logger __CreateDefault()
    {
        var inst = new Logger();
        return inst;
    }
}
public class Program
{
    public static void Main(string[] args)
    {
        var logger = new Logger();
        INamed named = logger;
        Console.WriteLine(named.Name());
    }

    public static Program __CreateDefault()
    {
        var inst = new Program();
        return inst;
    }
}
