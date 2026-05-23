using Standard;
using System;

namespace SwitchDemo;

public class Program
{
    public static void Main(string[] args)
    {
        int code = 99;
        var meaning = code switch
        {
            0 => "Zero",
            1 => "One",
            var _ => "Other",
        };
        System.Console.WriteLine($"Code {code} => {meaning}");
        int code2 = 50;
        var meaning2 = code2 switch
        {
            0 => "Zero",
            1 => "One",
            var _ => "Other",
        };
        System.Console.WriteLine($"Code {code2} => {meaning2}");
    }

    public static Program __CreateDefault()
    {
        var inst = new Program();
        return inst;
    }
}
