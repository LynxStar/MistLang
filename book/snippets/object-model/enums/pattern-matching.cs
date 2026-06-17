using System;

namespace Snippet;

public class Program
{
    public static void Main(string[] args)
    {
        System.Console.OutputEncoding = System.Text.Encoding.UTF8;
        var d = Direction.North;
        var isNorth = d is Direction.North;
        var isSouth = d is Direction.South;
        Console.WriteLine($"d is Direction.North = {isNorth}");
        Console.WriteLine($"d is Direction.South = {isSouth}");
        var c = Color.Green;
        var isGreen = c is Color.Green;
        var isRed = c is Color.Red;
        Console.WriteLine($"c is Color.Green = {isGreen}");
        Console.WriteLine($"c is Color.Red = {isRed}");
    }

    public static Program __CreateDefault()
    {
        var inst = new Program();
        return inst;
    }
}
