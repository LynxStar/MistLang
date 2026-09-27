using Standard;

namespace Snippet;

public class Program
{
    public static void Main(string[] args)
    {
        (int, int) coord = (0, 5);
        string quadrant = coord switch { (0, 0) => "origin", (> 0, > 0) => "quadrant I", (< 0, > 0) => "quadrant II", (< 0, < 0) => "quadrant III", (> 0, < 0) => "quadrant IV", (0, var _) => "on y-axis", (var _, var _) => "unknown" };
        System.Console.WriteLine(System.FormattableString.Invariant($"({coord.Item1}, {coord.Item2}) => {quadrant}"));
    }

    public Program()
    {
    }

    public static Program __CreateDefault()
    {
        return new Program();
    }
}
