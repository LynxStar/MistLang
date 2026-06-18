using System;

namespace Snippet;

public class Program
{
    private static void Describe(object value)
    {
        if (value is int i)
        {
            Console.WriteLine($"int: {i}");
        }
        if (value is string s)
        {
            Console.WriteLine($"string: {s}");
        }
    }

    public static void Main(string[] args)
    {
        Describe(42);
        Describe("mist");
    }

    public static Program __CreateDefault()
    {
        var inst = new Program();
        return inst;
    }
}
