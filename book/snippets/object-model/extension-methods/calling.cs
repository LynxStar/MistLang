using Standard;
using System;

namespace ExtensionMethods;

public class Program
{
    public static void Main(string[] args)
    {
        var greeting = new Greeting("Hello beautiful world");
        var wc = greeting.WordCount();
        var upper = greeting.Uppercase();
        var repeated = greeting.Repeat(2);
        System.Console.WriteLine(wc);
        System.Console.WriteLine(upper);
    }

    public static Program __CreateDefault()
    {
        var inst = new Program();
        return inst;
    }
}
