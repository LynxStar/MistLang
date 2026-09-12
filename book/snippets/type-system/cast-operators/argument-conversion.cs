using Standard;

namespace Snippet;

public class Program
{
    private static void PrintTemperature(Snippet.Fahrenheit f)
    {
        System.Console.WriteLine($"Temperature: {f.Describe()}");
    }

    public static void Main(string[] args)
    {
        Snippet.Celsius freezing = new Snippet.Celsius(0.0);
        PrintTemperature(freezing);
    }

    public Program()
    {
    }

    public static Program __CreateDefault()
    {
        return new Program();
    }
}
