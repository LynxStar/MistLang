using System;

namespace Snippet;

public class Program
{
    public static void Main(string[] args)
    {
        int statusValue = HttpStatus.Ok;
        string dirValue = Direction.North;
        byte colorValue = Color.Red;
        Snippet.Vec2 upVec = CardinalDir.Up;
        Console.WriteLine($"HttpStatus.Ok as int = {statusValue}");
        Console.WriteLine($"Direction.North as string = {dirValue}");
        Console.WriteLine($"Color.Red as byte = {colorValue}");
        Console.WriteLine($"CardinalDir.Up as Vec2 = {upVec.Describe()}");
    }

    public Program()
    {
    }

    public static Program __CreateDefault()
    {
        return new Program();
    }
}
