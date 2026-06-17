using System;

namespace Snippet;

public class Program
{
    public static void Main(string[] args)
    {
        System.Console.OutputEncoding = System.Text.Encoding.UTF8;
        int statusValue = HttpStatus.Ok;
        string dirValue = Direction.North;
        byte colorValue = Color.Red;
        Vec2 upVec = CardinalDir.Up;
        Console.WriteLine($"HttpStatus.Ok as int = {statusValue}");
        Console.WriteLine($"Direction.North as string = {dirValue}");
        Console.WriteLine($"Color.Red as byte = {colorValue}");
        Console.WriteLine($"CardinalDir.Up as Vec2 = {upVec.Describe()}");
    }

    public static Program __CreateDefault()
    {
        var inst = new Program();
        return inst;
    }
}
