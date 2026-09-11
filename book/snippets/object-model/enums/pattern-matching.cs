using System;

namespace Snippet;

public class Program
{
    public static void Main(string[] args)
    {
        Direction d = Direction.North;
        bool isNorth = (d == Direction.North);
        bool isSouth = (d == Direction.South);
        Console.WriteLine($"d is Direction.North = {isNorth}");
        Console.WriteLine($"d is Direction.South = {isSouth}");
        Color c = Color.Green;
        bool isGreen = (c == Color.Green);
        bool isRed = (c == Color.Red);
        Console.WriteLine($"c is Color.Green = {isGreen}");
        Console.WriteLine($"c is Color.Red = {isRed}");
    }

    public Program()
    {
    }

    public static Program __CreateDefault()
    {
        return new Program();
    }
}
