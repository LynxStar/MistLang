using System;

namespace Snippet;

public class Program
{
    public static void Main(string[] args)
    {
        int code = 99;
        string meaning = code switch { 0 => "Zero", 1 => "One", var _ => "Other" };
        Console.WriteLine($"Code {code} => {meaning}");
        int code2 = 50;
        string meaning2 = code2 switch { 0 => "Zero", 1 => "One", var _ => "Other" };
        Console.WriteLine($"Code {code2} => {meaning2}");
    }

    public Program()
    {
    }

    public static Program __CreateDefault()
    {
        return new Program();
    }
}
