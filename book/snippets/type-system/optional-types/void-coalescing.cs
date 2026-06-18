using System;

namespace Snippet;

public class Program
{
    public static void Main(string[] args)
    {
        int? hasValue = 10;
        int? noValue = null;
        int result1 = hasValue ?? 0;
        int result2 = noValue ?? 42;
        Console.WriteLine($"hasValue ?? 0={result1}");
        Console.WriteLine($"noValue ?? 42={result2}");
    }

    public static Program __CreateDefault()
    {
        var inst = new Program();
        return inst;
    }
}
