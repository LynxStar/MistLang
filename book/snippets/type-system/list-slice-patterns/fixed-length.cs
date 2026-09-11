using System;

namespace Snippet;

public class Program
{
    public static void Main(string[] args)
    {
        int[] triple = new int[] { 1, 2, 3 };
        if (triple is [1, 2, 3])
        {
            Console.WriteLine("matched exactly [1, 2, 3]");
        }
        int[] pair = new int[] { 1, 2 };
        if (pair is [1, 2, 3])
        {
            Console.WriteLine("unreachable");
        }
        else
        {
            Console.WriteLine("length mismatch: no match");
        }
    }

    public Program()
    {
    }

    public static Program __CreateDefault()
    {
        return new Program();
    }
}
