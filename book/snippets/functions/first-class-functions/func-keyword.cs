using System;

namespace Snippet;

public class Program
{
    public static void Main(string[] args)
    {
        System.Func<int, int> doubler = x => x * 2;
        System.Func<int, int, int> adder = (x, y) => x + y;
        System.Func<string> greeter = () => "Hello, Mist!";
        System.Console.WriteLine($"doubler(5)={doubler(5)}");
        System.Console.WriteLine($"adder(3,4)={adder(3, 4)}");
        System.Console.WriteLine($"greeter()={greeter()}");
    }

    public static Program __CreateDefault()
    {
        var inst = new Program();
        return inst;
    }
}
