using System;
using System.Linq;

namespace Snippet;

public class Program
{
    public static void Main(string[] args)
    {
        System.Console.OutputEncoding = System.Text.Encoding.UTF8;
        int[] numbers = new int[] { 1, 2, 3, 4, 5 };
        int count = System.Linq.Enumerable.Count(numbers);
        int first = System.Linq.Enumerable.First(numbers);
        System.Console.WriteLine($"Count: {count}");
        System.Console.WriteLine($"First: {first}");
    }

    public static Program __CreateDefault()
    {
        var inst = new Program();
        return inst;
    }
}
