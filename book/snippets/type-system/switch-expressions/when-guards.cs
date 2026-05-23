using Standard;
using System;

namespace SwitchDemo;

public class Program
{
    public static void Main(string[] args)
    {
        int number = -5;
        var sign = number switch
        {
            int n when n > 0 => "positive",
            int n when n < 0 => "negative",
            var _ => "zero",
        };
        System.Console.WriteLine($"Number {number} => {sign}");
        int amount = 150;
        var tier = amount switch
        {
            int n when n >= 100 => $"premium (value={n})",
            int n when n >= 50 => $"standard (value={n})",
            var _ => "basic",
        };
        System.Console.WriteLine($"Amount {amount} => {tier}");
    }

    public static Program __CreateDefault()
    {
        var inst = new Program();
        return inst;
    }
}
