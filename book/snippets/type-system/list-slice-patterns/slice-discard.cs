using System;
using System.Text.RegularExpressions;

namespace Snippet;

public class Program
{
    public static void Main(string[] args)
    {
        var numbers = new[] { 10, 20, 30, 40, 50 };
        if (numbers is [10, ..])
        {
            Console.WriteLine("starts with 10");
        }
        if (numbers is [.., 50])
        {
            Console.WriteLine("ends with 50");
        }
    }

    public static Program __CreateDefault()
    {
        var inst = new Program();
        return inst;
    }
}
