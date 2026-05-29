using System;

namespace Snippet;

public class Program
{
    public static void Main(string[] args)
    {
        int? opt = 42;
        int? shorthand = opt;
        System.Console.WriteLine($"opt={opt}");
        System.Console.WriteLine($"shorthand={shorthand}");
    }

    public static Program __CreateDefault()
    {
        var inst = new Program();
        return inst;
    }
}
