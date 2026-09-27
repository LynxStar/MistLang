using Standard;

namespace Snippet;

public class Program
{
    public static void Main(string[] args)
    {
        int statusValue = Snippet.HttpStatus.Ok;
        string dirValue = Snippet.Direction.North;
        byte colorValue = Snippet.Color.Red;
        Snippet.Vec2 upVec = Snippet.CardinalDir.Up;
        System.Console.WriteLine(System.FormattableString.Invariant($"HttpStatus.Ok as int = {statusValue}"));
        System.Console.WriteLine($"Direction.North as string = {dirValue}");
        System.Console.WriteLine(System.FormattableString.Invariant($"Color.Red as byte = {colorValue}"));
        System.Console.WriteLine($"CardinalDir.Up as Vec2 = {upVec.Describe()}");
    }

    public Program()
    {
    }

    public static Program __CreateDefault()
    {
        return new Program();
    }
}
