using System;

namespace Snippet;

public class Program
{
    public static void Main(string[] args)
    {
        System.Console.OutputEncoding = System.Text.Encoding.UTF8;
        var coord = (0, 5);
        var quadrant = coord switch
        {
            (0, 0) => "origin",
            (> 0, > 0) => "quadrant I",
            (< 0, > 0) => "quadrant II",
            (< 0, < 0) => "quadrant III",
            (> 0, < 0) => "quadrant IV",
            (0, var _) => "on y-axis",
            (var _, 0) => "on x-axis",
            (var _, var _) => "unknown",
        };
        Console.WriteLine($"({coord.Item1}, {coord.Item2}) => {quadrant}");
    }

    public static Program __CreateDefault()
    {
        var inst = new Program();
        return inst;
    }
}
