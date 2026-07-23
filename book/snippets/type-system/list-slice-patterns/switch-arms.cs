using System;
using System.Text.RegularExpressions;

namespace Snippet;

public class Program
{
    public static void Main(string[] args)
    {
        var numbers = new[] { 10, 20, 30, 40, 50 };
        var label = numbers switch
        {
            [] => "empty",
            [var _] => "single",
            [var _, var _] => "pair",
            var _ => "many",
        };
        Console.WriteLine($"label={label}");
    }

    public static Program __CreateDefault()
    {
        var inst = new Program();
        return inst;
    }
}
