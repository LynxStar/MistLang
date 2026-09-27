using Standard;

namespace Snippet;

internal class Program
{
    public static void Main(string[] args)
    {
        var point = new { X = 10, Y = 20 };
        int sum = point.X + point.Y;
        System.Console.WriteLine(System.FormattableString.Invariant($"X={point.X} Y={point.Y} sum={sum}"));
        var first = new { X = 1, Y = 2 };
        var second = new { X = 10, Y = 20 };
        second = first;
        System.Console.WriteLine(System.FormattableString.Invariant($"X={second.X} Y={second.Y}"));
    }

    public Program()
    {
    }

    public static Program __CreateDefault()
    {
        return new Program();
    }
}
