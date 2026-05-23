using Standard;
using System;

namespace PatternDemo;

public class Program
{
    public static void Main(string[] args)
    {
        int score = 85;
        if (score is > 0 and < 100)
        {
            System.Console.WriteLine($"Score {score} is between 1 and 99");
        }
    }

    public static Program __CreateDefault()
    {
        var inst = new Program();
        return inst;
    }
}
