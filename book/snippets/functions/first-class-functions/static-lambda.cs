using System;

namespace Snippet;

public class Program
{
    public static void Main(string[] args)
    {
        System.Func<int, int, int> add = static (a, b) => a + b;
        System.Console.WriteLine($"add(10,20)={add(10, 20)}");
    }

    public static Program __CreateDefault()
    {
        var inst = new Program();
        return inst;
    }
}
