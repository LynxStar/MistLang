using System;
using System.Text.RegularExpressions;

namespace Snippet;

public class Program
{
    public static void Main(string[] args)
    {
        var numbers = new[] { 10, 20, 30, 40, 50 };
        var empty = numbers[0..0];
        if (empty is [])
        {
            Console.WriteLine("empty matched");
        }
        if (numbers is [])
        {
            Console.WriteLine("unreachable");
        }
        else
        {
            Console.WriteLine("numbers is not empty");
        }
    }

    public static Program __CreateDefault()
    {
        var inst = new Program();
        return inst;
    }
}
