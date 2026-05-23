using Standard;
using System;

namespace PatternDemo;

public class Program
{
    private static int ComputeScore()
    {
        return 85;
    }

    public static void Main(string[] args)
    {
        if (Program.ComputeScore() is var score and > 0)
        {
            System.Console.WriteLine($"Positive score: {score}");
        }
    }

    public static Program __CreateDefault()
    {
        var inst = new Program();
        return inst;
    }
}
