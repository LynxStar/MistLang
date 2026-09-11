using System;

namespace Snippet;

class Program
{
    public static void Main(string[] args)
    {
        var point = new { X = 10, Y = 20 };
        int sum = point.X + point.Y;
        Console.WriteLine($"X={point.X} Y={point.Y} sum={sum}");
        var first = new { X = 1, Y = 2 };
        var second = new { X = 10, Y = 20 };
        second = first;
        Console.WriteLine($"X={second.X} Y={second.Y}");
    }

    public Program()
    {
    }

    public static Program __CreateDefault()
    {
        return new Program();
    }
}
