using System;
using System.Text.RegularExpressions;

namespace Snippet;

public class Program
{
    public static void Main(string[] args)
    {
        var numbers = new[] { 10, 20, 30, 40, 50 };
        {
            if (numbers is [var first, .. var middle, var last])
            {
                Console.WriteLine($"first={first}");
                Console.WriteLine($"last={last}");
                Console.WriteLine($"middleLength={middle.Length}");
                Console.WriteLine($"middleFirst={middle[0]}");
            }
        }
    }

    public static Program __CreateDefault()
    {
        var inst = new Program();
        return inst;
    }
}
