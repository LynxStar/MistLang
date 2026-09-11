using System;

namespace Snippet;

public class Program
{
    public static void Main(string[] args)
    {
        string input = "hello";
        string category = input switch { var __e8 when System.Text.RegularExpressions.Regex.IsMatch(__e8, "^(?:" + "^\\d+$" + ")$") => "number", var __e9 when System.Text.RegularExpressions.Regex.IsMatch(__e9, "^(?:" + "^[a-z]+$" + ")$") => "lowercase", var __e10 when System.Text.RegularExpressions.Regex.IsMatch(__e10, "^(?:" + "^[A-Z]+$" + ")$") => "uppercase", var _ => "other" };
        Console.WriteLine($"'{input}' is {category}");
    }

    public Program()
    {
    }

    public static Program __CreateDefault()
    {
        return new Program();
    }
}
