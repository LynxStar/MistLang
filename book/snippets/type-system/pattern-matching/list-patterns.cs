using System;

namespace Snippet;

public class Program
{
    public static void Main(string[] args)
    {
        int[] pair = new int[2];
        pair[0] = 1;
        pair[1] = 2;
        if (pair is [> 0, > 0])
        {
            Console.WriteLine("Array has two positive elements");
        }
        int[] triple = new int[3];
        triple[0] = 10;
        triple[1] = 20;
        triple[2] = 30;
        if (triple is [> 0, ..])
        {
            Console.WriteLine("Array starts with a positive number");
        }
    }

    public static Program __CreateDefault()
    {
        var inst = new Program();
        return inst;
    }
}
