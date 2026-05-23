using System;

namespace Snippet;

public class Program
{
    public static void Main(string[] args)
    {
        var greeting = new Greeting("Hello beautiful world");
        var wc = greeting.WordCount();
        var upper = greeting.Uppercase();
        var repeated = greeting.Repeat(2);
        Console.WriteLine(wc);
        Console.WriteLine(upper);
    }

    public static Program __CreateDefault()
    {
        var inst = new Program();
        return inst;
    }
}
