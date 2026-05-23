using Standard;
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
        System.Console.WriteLine($"num={num} parity={parity}");
    }

    public static Program __CreateDefault()
    {
        var inst = new Program();
        return inst;
    }
}
