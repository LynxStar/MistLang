using System;

namespace Snippet;

public class Program
{
    public static void Main(string[] args)
    {
        int num = 2;
        string parity = num switch { 1 => "odd", 2 => "even", 3 => "odd", var _ => "unknown" };
        Console.WriteLine($"num={num} parity={parity}");
    }

    public Program()
    {
    }

    public static Program __CreateDefault()
    {
        return new Program();
    }
}
