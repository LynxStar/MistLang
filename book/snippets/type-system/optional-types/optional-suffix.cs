using System;

namespace Snippet;

public class Program
{
    public static void Main(string[] args)
    {
        System.Console.OutputEncoding = System.Text.Encoding.UTF8;
        int? maybeInt = 42;
        string? maybeStr = "hello";
        Console.WriteLine($"maybeInt={(object?)maybeInt ?? "Void"}");
        Console.WriteLine($"maybeStr={(object?)maybeStr ?? "Void"}");
    }

    public static Program __CreateDefault()
    {
        var inst = new Program();
        return inst;
    }
}
