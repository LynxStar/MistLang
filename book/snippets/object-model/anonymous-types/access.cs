using System;

namespace Snippet;

public class Program
{
    public static void Main(string[] args)
    {
        var point = new { X = 10, Y = 20 };
        var sum = point.X + point.Y;
        System.Console.WriteLine($"X={point.X} Y={point.Y} sum={sum}");
        var first = new { X = 1, Y = 2 };
        var second = new { X = 10, Y = 20 };
        second = first;
        System.Console.WriteLine($"X={second.X} Y={second.Y}");
    }

    public static Program __CreateDefault()
    {
        var inst = new Program();
        return inst;
    }
}
