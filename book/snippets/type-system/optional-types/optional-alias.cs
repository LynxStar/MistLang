using System;

namespace Snippet;

public class Program
{
    public static void Main(string[] args)
    {
        int? opt = 42;
        int? shorthand = opt;
        System.Console.WriteLine($"opt={(object?)opt ?? "Void"}");
        System.Console.WriteLine($"shorthand={(object?)shorthand ?? "Void"}");
    }

    public static Program __CreateDefault()
    {
        var inst = new Program();
        return inst;
    }
}
