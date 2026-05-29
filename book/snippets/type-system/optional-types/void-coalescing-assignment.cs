using System;

namespace Snippet;

public class Program
{
    public static void Main(string[] args)
    {
        int? x = null;
        x ??= 100;
        int narrowed = x;
        Console.WriteLine($"x={x}");
        Console.WriteLine($"narrowed={narrowed}");
    }

    public static Program __CreateDefault()
    {
        var inst = new Program();
        return inst;
    }
}
