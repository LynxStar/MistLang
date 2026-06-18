using System;

namespace Snippet;

public class Program
{
    public static void Main(string[] args)
    {
        var numbers = new int[3];
        numbers[0] = 10;
        numbers[1] = 20;
        numbers[2] = 30;
        foreach (int n in numbers)
        {
            Console.WriteLine($"Element: {n}");
        }
    }

    public static Program __CreateDefault()
    {
        var inst = new Program();
        return inst;
    }
}
