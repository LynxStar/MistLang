using Standard;

namespace Snippet;

public class Program
{
    public static void Main(string[] args)
    {
        byte level = 250;
        byte step = 10;
        level += step;
        System.Console.WriteLine(System.FormattableString.Invariant($"level is {level}"));
    }

    public Program()
    {
    }

    public static Program __CreateDefault()
    {
        return new Program();
    }
}
