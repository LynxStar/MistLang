using Standard;
using System;

namespace Demo;

public class Program
{
    public static void Main(string[] args)
    {
        int score = 85;
        if (score >= 90)
        {
            System.Console.WriteLine("Grade: A");
        }
        else if (score >= 80)
        {
            System.Console.WriteLine("Grade: B");
        }
        else
        {
            System.Console.WriteLine("Grade: C or below");
        }
    }

    public static Program __CreateDefault()
    {
        var inst = new Program();
        return inst;
    }
}
