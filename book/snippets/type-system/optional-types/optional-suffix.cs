using System;

namespace Snippet;

public class Program
{
    public static void Main(string[] args)
    {
        int? maybeInt = 42;
        string maybeStr = "hello";
        Console.WriteLine($"maybeInt={(object?)maybeInt ?? "Void"}");
        Console.WriteLine($"maybeStr={(object?)maybeStr ?? "Void"}");
    }

    public Program()
    {
    }

    public static Program __CreateDefault()
    {
        return new Program();
    }
}
