using System;

namespace Snippet;

public class Program
{
    public static void Main(string[] args)
    {
        (int, int) point = (5, 7);
        if (point is (> 0, > 0))
        {
            Console.WriteLine($"({point.Item1}, {point.Item2}) is in quadrant I");
        }
    }

    public Program()
    {
    }

    public static Program __CreateDefault()
    {
        return new Program();
    }
}
