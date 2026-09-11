using System;

namespace Snippet;

public class Program
{
    public static void Main(string[] args)
    {
        int[] numbers = new int[] { 10, 20, 30, 40, 50 };
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

    public Program()
    {
    }

    public static Program __CreateDefault()
    {
        return new Program();
    }
}
