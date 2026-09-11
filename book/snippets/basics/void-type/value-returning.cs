using System;

namespace Snippet;

public class Program
{
    public static void DoNothing()
    {
        return;
    }

    public static void Main(string[] args)
    {
        DoNothing();
        Standard.Void result = Standard.Void.Instance;
        Console.WriteLine($"DoNothing() returned: {result}");
    }

    public Program()
    {
    }

    public static Program __CreateDefault()
    {
        return new Program();
    }
}
