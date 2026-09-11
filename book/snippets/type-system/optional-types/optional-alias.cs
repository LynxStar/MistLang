using System;

namespace Snippet;

public class Program
{
    public static void Main(string[] args)
    {
        int? opt = 42;
        int? shorthand = opt;
        Console.WriteLine($"opt={(object?)opt ?? "Void"}");
        Console.WriteLine($"shorthand={(object?)shorthand ?? "Void"}");
    }

    public Program()
    {
    }

    public static Program __CreateDefault()
    {
        return new Program();
    }
}
