using System;

namespace Snippet;

public class Program
{
    public static void Main(string[] args)
    {
        int? x = null;
        x ??= 100;
        int narrowed = x;
        Console.WriteLine($"x={(object?)x ?? "Void"}");
        Console.WriteLine($"narrowed={narrowed}");
    }

    public static Program __CreateDefault()
    {
        var inst = new Program();
        return inst;
    }
}
