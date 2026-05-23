using Standard;
using System;

namespace UnknownDemo;

public class Program
{
    private static void Describe(object value)
    {
        if (value is int i)
        {
            System.Console.WriteLine($"int: {i}");
        }
        if (value is string s)
        {
            System.Console.WriteLine($"string: {s}");
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
