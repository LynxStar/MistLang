using System;

namespace Snippet;

public class Program
{
    public static void Main(string[] args)
    {
        Snippet.Greeting greeting = new Snippet.Greeting("Hello beautiful world");
        int wc = greeting.WordCount();
        string upper = greeting.Uppercase();
        Snippet.Greeting repeated = greeting.Repeat(2);
        Console.WriteLine(wc);
        Console.WriteLine(upper);
    }

    public Program()
    {
    }

    public static Program __CreateDefault()
    {
        return new Program();
    }
}
