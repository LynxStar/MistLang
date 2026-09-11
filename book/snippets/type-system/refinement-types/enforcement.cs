using System;

namespace Snippet;

public class Program
{
    public static void Main(string[] args)
    {
        int known = 20;
        int userInput = 25;
        {
            if (userInput is int and >= 18 and var validAge)
            {
                Console.WriteLine($"Valid adult age: {validAge}");
            }
        }
        int childInput = 10;
        {
            if (childInput is int and >= 18 and var childAge)
            {
                Console.WriteLine("This won't print");
            }
            else
            {
                Console.WriteLine("Not an adult age");
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
