using System;

namespace Snippet;

public class Program
{
    public static void Main(string[] args)
    {
        System.Func<int, int> doubler = x => x * 2;
        var square = (int x) => x * x;
        System.Console.WriteLine($"doubler(5)={doubler(5)}");
        System.Console.WriteLine($"square(4)={square(4)}");
    }

    public static Program __CreateDefault()
    {
        var inst = new Program();
        return inst;
    }
}
