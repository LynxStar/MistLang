using System;

namespace Snippet;

public class Program
{
    public static void Main(string[] args)
    {
        System.Func<int, bool> isEven = (int x) => x % 2 == 0;
        System.Func<int, string> toStr = (int x) => $"val={x}";
        Console.WriteLine($"isEven(4)={isEven(4)}");
        Console.WriteLine($"isEven(7)={isEven(7)}");
        Console.WriteLine($"toStr(99)={toStr(99)}");
    }

    public Program()
    {
    }

    public static Program __CreateDefault()
    {
        return new Program();
    }
}
