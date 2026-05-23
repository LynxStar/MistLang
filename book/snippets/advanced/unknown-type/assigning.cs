using System;

namespace Snippet;

public class Program
{
    public static void Main(string[] args)
    {
        object u = 42;
        Console.WriteLine(u);
        u = "hello";
        Console.WriteLine(u);
        u = true;
        Console.WriteLine(u);
    }

    public static Program __CreateDefault()
    {
        var inst = new Program();
        return inst;
    }
}
