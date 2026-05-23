using System;

namespace Snippet;

public class Program
{
    public static void Main(string[] args)
    {
        object maybeInt = 17;
        if (maybeInt is int narrowed)
        {
            int doubled = narrowed + narrowed;
            Console.WriteLine($"narrowed int doubled: {doubled}");
        }
    }

    public static Program __CreateDefault()
    {
        var inst = new Program();
        return inst;
    }
}
