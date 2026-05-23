using Standard;
using System;

namespace EnumDemo;

public class Program
{
    public static void Main(string[] args)
    {
        int statusValue = HttpStatus.Ok;
        string dirValue = Direction.North;
        byte colorValue = Color.Red;
        Vec2 upVec = CardinalDir.Up;
        System.Console.WriteLine($"HttpStatus.Ok as int = {statusValue}");
        System.Console.WriteLine($"Direction.North as string = {dirValue}");
        System.Console.WriteLine($"Color.Red as byte = {colorValue}");
        System.Console.WriteLine($"CardinalDir.Up as Vec2 = {upVec.Describe()}");
    }

    public static Program __CreateDefault()
    {
        var inst = new Program();
        return inst;
    }
}
