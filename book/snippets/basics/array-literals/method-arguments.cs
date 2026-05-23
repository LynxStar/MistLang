using Standard;
using System;

namespace Demo;

public class Program
{
    private static void PrintAll(int[] items)
    {
        foreach (int item in items)
        {
            System.Console.WriteLine(item);
        }
    }

    public static void Main(string[] args)
    {
        PrintAll([10, 20, 30]);
    }

    public static Program __CreateDefault()
    {
        var inst = new Program();
        return inst;
    }
}
