using System;

namespace Snippet;

public class Program
{
    private static int[] GetDefaults()
    {
        return new[] { 10, 20, 30 };
    }

    public static void Main(string[] args)
    {
        System.Console.OutputEncoding = System.Text.Encoding.UTF8;
        int[] values = GetDefaults();
        Console.WriteLine($"Count: {values.Length}");
    }

    public static Program __CreateDefault()
    {
        var inst = new Program();
        return inst;
    }
}
