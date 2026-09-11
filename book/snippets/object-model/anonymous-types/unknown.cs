using System;

namespace Snippet;

class Program
{
    public static void Main(string[] args)
    {
        object box = new { X = 10, Y = 20 };
        Console.WriteLine($"box is object: {box is object}");
        {
            if ((box is not null and var __e7 && ((dynamic)__e7).X is var x && ((dynamic)__e7).Y is var y))
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
