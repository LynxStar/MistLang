using System;

namespace Snippet;

public class Program
{
    public static void Main(string[] args)
    {
        System.Func<int, string> classify = (int n) => 
        {
            if (n > 0)
            {
                return "positive";
            }
            if (n < 0)
            {
                return "negative";
            }
            return "zero";
        }
;
        Console.WriteLine($"classify(5)={classify(5)}");
        Console.WriteLine($"classify(-1)={classify(-1)}");
        Console.WriteLine($"classify(0)={classify(0)}");
    }

    public Program()
    {
    }

    public static Program __CreateDefault()
    {
        return new Program();
    }
}
