using System;

namespace Snippet;

public class Program
{
    public static void Main(string[] args)
    {
        System.Console.OutputEncoding = System.Text.Encoding.UTF8;
        var point = (5, 7);
        if (point is (> 0, > 0))
        {
            Console.WriteLine($"({point.Item1}, {point.Item2}) is in quadrant I");
        }
    }

    public static Program __CreateDefault()
    {
        var inst = new Program();
        return inst;
    }
}
