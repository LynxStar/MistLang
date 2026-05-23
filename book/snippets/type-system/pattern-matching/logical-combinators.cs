using System;

namespace Snippet;

public class Program
{
    public static void Main(string[] args)
    {
        int temperature = 72;
        if (temperature is >= 60 and <= 80)
        {
            Console.WriteLine($"Temperature {temperature} is comfortable");
        }
        int value = 15;
        if (value is < 0 or > 10)
        {
            Console.WriteLine($"Value {value} is outside 0-10 range");
        }
    }

    public static Program __CreateDefault()
    {
        var inst = new Program();
        return inst;
    }
}
