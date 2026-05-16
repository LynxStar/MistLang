using System;

namespace SwitchDemo;

public class Program
{
    public static void Main(string[] args)
    {
        int num = 2;
        var parity = num switch
        {
            1 => "odd",
            2 => "even",
            3 => "odd",
            var _ => "unknown",
        };
        Console.WriteLine($"num={num} parity={parity}");
    }
}
