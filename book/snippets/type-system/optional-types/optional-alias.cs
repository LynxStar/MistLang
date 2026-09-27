using Standard;

namespace Snippet;

public class Program
{
    public static void Main(string[] args)
    {
        int? opt = 42;
        int? shorthand = opt;
        System.Console.WriteLine(System.FormattableString.Invariant($"opt={(object?)opt ?? "Void"}"));
        System.Console.WriteLine(System.FormattableString.Invariant($"shorthand={(object?)shorthand ?? "Void"}"));
    }

    public Program()
    {
    }

    public static Program __CreateDefault()
    {
        return new Program();
    }
}
