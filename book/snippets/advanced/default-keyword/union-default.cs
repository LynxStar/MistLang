using System;

namespace Snippet;

public class Program
{
    public static void Main(string[] args)
    {
        System.Console.OutputEncoding = System.Text.Encoding.UTF8;
        int? maybeInt = default;
        Console.WriteLine($"maybeInt={(object?)maybeInt ?? "Void"}");
    }

    public static Program __CreateDefault()
    {
        var inst = new Program();
        return inst;
    }
}
