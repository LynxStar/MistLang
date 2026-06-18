using System;

namespace Snippet;

public class Program
{
    public static void Main(string[] args)
    {
        System.Console.OutputEncoding = System.Text.Encoding.UTF8;
        int counter = 0;
        System.Func<int> increment = () => { counter = counter + 1; return counter; };
        System.Console.WriteLine($"increment()={increment()}");
        System.Console.WriteLine($"increment()={increment()}");
        System.Console.WriteLine($"counter={counter}");
    }

    public static Program __CreateDefault()
    {
        var inst = new Program();
        return inst;
    }
}
