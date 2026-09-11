using System;

namespace Snippet;

public class Program
{
    public static void Main(string[] args)
    {
        System.Func<int, int, int> add = static (int a, int b) => a + b;
        Console.WriteLine($"add(10,20)={add(10, 20)}");
    }

    public Program()
    {
    }

    public static Program __CreateDefault()
    {
        return new Program();
    }
}
