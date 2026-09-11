using System;

namespace Snippet;

public class Program
{
    public static void Main(string[] args)
    {
        int x = 25;
        if (x >= 18)
        {
            int narrowed = x;
            Console.WriteLine($"Narrowed to AdultAge: {narrowed}");
        }
        int userInput = 21;
        {
            if (userInput is int and >= 21 and var validAge)
            {
                Console.WriteLine($"Valid drinking age: {validAge}");
            }
        }
        int age = 30;
        int baseValue = age;
        Console.WriteLine($"Widened to int: {baseValue}");
    }

    public Program()
    {
    }

    public static Program __CreateDefault()
    {
        return new Program();
    }
}
