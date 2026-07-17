using System;
using System.Text.RegularExpressions;

namespace Snippet;

internal class Program
{
    public static void Main(string[] args)
    {
        object box = new { X = 10, Y = 20 };
        System.Console.WriteLine($"box is object: {box is object}");
        if (box is not null)
        {
            dynamic __pat_0 = box;
            var x = __pat_0.X;
            var y = __pat_0.Y;
            System.Console.WriteLine($"X={x} Y={y}");
        }
    }

    public static Program __CreateDefault()
    {
        var inst = new Program();
        return inst;
    }
}
