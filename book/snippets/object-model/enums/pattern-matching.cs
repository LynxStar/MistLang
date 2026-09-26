using Standard;

namespace Snippet;

public class Program
{
    public static void Main(string[] args)
    {
        Snippet.Direction d = Snippet.Direction.North;
        bool isNorth = (d == Snippet.Direction.North);
        bool isSouth = (d == Snippet.Direction.South);
        System.Console.WriteLine($"d is Direction.North = {isNorth}");
        System.Console.WriteLine($"d is Direction.South = {isSouth}");
        Snippet.Color c = Snippet.Color.Green;
        bool isGreen = (c == Snippet.Color.Green);
        bool isRed = (c == Snippet.Color.Red);
        System.Console.WriteLine($"c is Color.Green = {isGreen}");
        System.Console.WriteLine($"c is Color.Red = {isRed}");
    }

    public Program()
    {
    }

    public static Program __CreateDefault()
    {
        return new Program();
    }
}
