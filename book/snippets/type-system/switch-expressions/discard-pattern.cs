using System;

namespace Snippet;

public class Program
{
    public static void Main(string[] args)
    {
        System.Console.OutputEncoding = System.Text.Encoding.UTF8;
        var code = 99;
        var meaning = code switch
        {
            0 => "Zero",
            1 => "One",
            var _ => "Other",
        };
        Console.WriteLine($"Code {code} => {meaning}");
        var code2 = 50;
        var meaning2 = code2 switch
        {
            0 => "Zero",
            1 => "One",
            var _ => "Other",
        };
        Console.WriteLine($"Code {code2} => {meaning2}");
    }

    public static Program __CreateDefault()
    {
        var inst = new Program();
        return inst;
    }
}
