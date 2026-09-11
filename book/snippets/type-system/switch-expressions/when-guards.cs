using System;

namespace Snippet;

public class Program
{
    public static void Main(string[] args)
    {
        int number = -5;
        string sign = number switch { int n when n > 0 => "positive", int n when n < 0 => "negative", var _ => "zero" };
        Console.WriteLine($"Number {number} => {sign}");
        int amount = 150;
        string tier = amount switch { int n when n >= 100 => $"premium (value={n})", int n when n >= 50 => $"standard (value={n})", var _ => "basic" };
        Console.WriteLine($"Amount {amount} => {tier}");
    }

    public Program()
    {
    }

    public static Program __CreateDefault()
    {
        return new Program();
    }
}
