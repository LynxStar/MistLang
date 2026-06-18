using System;

namespace Snippet;

public class Program
{
    public static void Main(string[] args)
    {
        System.Func<int, bool> isEven = x => x % 2 == 0;
        System.Func<int, string> toStr = x => $"val={x}";
        System.Console.WriteLine($"isEven(4)={isEven(4)}");
        System.Console.WriteLine($"isEven(7)={isEven(7)}");
        System.Console.WriteLine($"toStr(99)={toStr(99)}");
    }

    public static Program __CreateDefault()
    {
        var inst = new Program();
        return inst;
    }
}
