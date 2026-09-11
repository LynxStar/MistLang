using System;

namespace Snippet;

public class Program
{
    public static void Main(string[] args)
    {
        int counter = 0;
        System.Func<int> increment = () => 
        {
            counter = counter + 1;
            return counter;
        }
;
        Console.WriteLine($"increment()={increment()}");
        Console.WriteLine($"increment()={increment()}");
        Console.WriteLine($"counter={counter}");
    }

    public Program()
    {
    }

    public static Program __CreateDefault()
    {
        return new Program();
    }
}
