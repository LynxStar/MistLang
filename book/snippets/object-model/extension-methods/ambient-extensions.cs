using System;
using System.Linq;

namespace Snippet;

public class Program
{
    public static void Main(string[] args)
    {
        int[] numbers = new int[] { 1, 2, 3, 4, 5 };
        int count = numbers.Count();
        int first = numbers.First();
        System.Console.WriteLine($"Count: {count}");
        System.Console.WriteLine($"First: {first}");
    }

    public static Program __CreateDefault()
    {
        var inst = new Program();
        return inst;
    }
}
