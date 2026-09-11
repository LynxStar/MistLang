using System;

namespace Snippet;

internal class Program
{
    public static void Main(string[] args)
    {
        object box = new { X = 10, Y = 20 };
        Console.WriteLine($"box is object: {box is object}");
        {
            if ((box is not null and var __e1 && ((dynamic)__e1).X is var x && ((dynamic)__e1).Y is var y))
            {
                Console.WriteLine($"X={x} Y={y}");
            }
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
