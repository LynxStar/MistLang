using System;

namespace Snippet;

public class Program
{
    public static void Main(string[] args)
    {
        Direction d = Direction.North;
        bool isNorth = d is Direction.North;
        bool isSouth = d is Direction.South;
        Console.WriteLine($"d is Direction.North = {isNorth}");
        Console.WriteLine($"d is Direction.South = {isSouth}");
        Color c = Color.Green;
        bool isGreen = c is Color.Green;
        bool isRed = c is Color.Red;
        Console.WriteLine($"c is Color.Green = {isGreen}");
        Console.WriteLine($"c is Color.Red = {isRed}");
    }

    public static Program __CreateDefault()
    {
        var inst = new Program();
        return inst;
    }
}
