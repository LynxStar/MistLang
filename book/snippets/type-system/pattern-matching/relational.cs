using System;

namespace Snippet;

public class Program
{
    public static void Main(string[] args)
    {
        int score = 85;
        if (score is > 0 and < 100)
        {
            Console.WriteLine($"Score {score} is between 1 and 99");
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
