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
        System.Console.OutputEncoding = System.Text.Encoding.UTF8;
        Describe(42);
        Describe("mist");
    }

    public static Program __CreateDefault()
    {
        var inst = new Program();
        return inst;
    }
}
