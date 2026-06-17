using System;

namespace Snippet;

public class Program
{
    private static int ComputeScore()
    {
        return 85;
    }

    public static void Main(string[] args)
    {
        System.Console.OutputEncoding = System.Text.Encoding.UTF8;
        if (Program.ComputeScore() is var score and > 0)
        {
            Console.WriteLine($"Positive score: {score}");
        }
    }

    public static Program __CreateDefault()
    {
        var inst = new Program();
        return inst;
    }
}
