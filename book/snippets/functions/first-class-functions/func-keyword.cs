using System;

namespace Snippet;

public class Program
{
    public static void Main(string[] args)
    {
        System.Func<int, int> doubler = (int x) => x * 2;
        System.Func<int, int, int> adder = (int x, int y) => x + y;
        System.Func<string> greeter = () => "Hello, Mist!";
        Console.WriteLine($"doubler(5)={doubler(5)}");
        Console.WriteLine($"adder(3,4)={adder(3, 4)}");
        Console.WriteLine($"greeter()={greeter()}");
    }

    public Program()
    {
    }

    public static Program __CreateDefault()
    {
        return new Program();
    }
}
