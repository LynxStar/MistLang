using Standard;
using System;

namespace SwitchDemo;

public class Program
{
    public static void Main(string[] args)
    {
        int temp = 75;
        var comfort = temp switch
        {
            < 32 => "Freezing",
            < 60 => "Cold",
            <= 80 => "Comfortable",
            var _ => "Hot",
        };
        System.Console.WriteLine($"Temperature {temp} => {comfort}");
        int score = 85;
        var grade = score switch
        {
            >= 90 and <= 100 => "A",
            >= 80 and < 90 => "B",
            >= 70 and < 80 => "C",
            >= 60 and < 70 => "D",
            var _ => "F",
        };
        System.Console.WriteLine($"Score {score} => Grade {grade}");
    }

    public static Program __CreateDefault()
    {
        var inst = new Program();
        return inst;
    }
}
