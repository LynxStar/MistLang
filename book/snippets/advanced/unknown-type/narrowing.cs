using System;

namespace Snippet;

public class Program
{
    public static void Main(string[] args)
    {
        System.Console.OutputEncoding = System.Text.Encoding.UTF8;
        object maybeInt = 17;
        if (maybeInt is int narrowed)
        {
            var doubled = narrowed + narrowed;
            Console.WriteLine($"narrowed int doubled: {doubled}");
        }
    }

    public static Program __CreateDefault()
    {
        var inst = new Program();
        return inst;
    }
}
