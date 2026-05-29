using System;

namespace Snippet;

public class Program
{
    public static void Main(string[] args)
    {
        int maybeInt = 42;
        string maybeStr = "hello";
        Console.WriteLine($"maybeInt={maybeInt}");
        Console.WriteLine($"maybeStr={maybeStr}");
    }

    public static Program __CreateDefault()
    {
        var inst = new Program();
        return inst;
    }
}
