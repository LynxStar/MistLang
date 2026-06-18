using System;

namespace Snippet;

public class Program
{
    private static void PrintTemperature(Fahrenheit f)
    {
        Console.WriteLine($"Temperature: {f.Describe()}");
    }

    public static void Main(string[] args)
    {
        System.Console.OutputEncoding = System.Text.Encoding.UTF8;
        var freezing = new Celsius(0.0);
        PrintTemperature(freezing);
    }

    public static Program __CreateDefault()
    {
        var inst = new Program();
        return inst;
    }
}
