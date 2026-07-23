using System;

namespace Snippet;

public class Program
{
    public static void Main(string[] args)
    {
        var triple = new[] { 1, 2, 3 };
        if (triple is [1, 2, 3])
        {
            Console.WriteLine("matched exactly [1, 2, 3]");
        }
        var pair = new[] { 1, 2 };
        if (pair is [1, 2, 3])
        {
            Console.WriteLine("unreachable");
        }
        else
        {
            Console.WriteLine("length mismatch: no match");
        }
    }

    public static Program __CreateDefault()
    {
        var inst = new Program();
        return inst;
    }
}
