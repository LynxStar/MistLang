using System;

namespace Snippet;

public class Program
{
    public static void Main(string[] args)
    {
        int score = 85;
        if (score >= 90)
        {
            Console.WriteLine("Grade: A");
        }
        else         if (score >= 80)
        {
            Console.WriteLine("Grade: B");
        }
        else
        {
            Console.WriteLine("Grade: C or below");
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
