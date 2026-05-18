using System;

namespace CastOperators;

public class Program
{
    private static void PrintTemperature(Fahrenheit f)
    {
        Console.WriteLine($"Temperature: {f.Describe()}");
    }

    public static void Main(string[] args)
    {
        var freezing = new Celsius(0.0);
        PrintTemperature(freezing);
    }

    public static Program __CreateDefault()
    {
        var inst = new Program();
        return inst;
    }
}
