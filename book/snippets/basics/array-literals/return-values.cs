using System;

namespace Demo;

public class Program
{
    private static int[] GetDefaults()
    {
        return new[] { 10, 20, 30 };
    }

    public static void Main(string[] args)
    {
        int[] values = GetDefaults();
        Console.WriteLine($"Count: {values.Length}");
    }
}
