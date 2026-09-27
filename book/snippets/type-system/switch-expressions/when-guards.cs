using Standard;

namespace Snippet;

public class Program
{
    public static void Main(string[] args)
    {
        int number = -5;
        string sign = number switch { int n when n > 0 => "positive", int n when n < 0 => "negative", var _ => "zero" };
        System.Console.WriteLine(System.FormattableString.Invariant($"Number {number} => {sign}"));
        int amount = 150;
        string tier = amount switch { int n when n >= 100 => System.FormattableString.Invariant($"premium (value={n})"), int n when n >= 50 => System.FormattableString.Invariant($"standard (value={n})"), var _ => "basic" };
        System.Console.WriteLine(System.FormattableString.Invariant($"Amount {amount} => {tier}"));
    }

    public Program()
    {
    }

    public static Program __CreateDefault()
    {
        return new Program();
    }
}
