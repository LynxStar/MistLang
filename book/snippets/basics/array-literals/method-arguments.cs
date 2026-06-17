using System;

namespace Snippet;

public class Program
{
    private static void PrintAll(int[] items)
    {
        foreach (int item in items)
        {
            Console.WriteLine(item);
        }
    }

    public static void Main(string[] args)
    {
        System.Console.OutputEncoding = System.Text.Encoding.UTF8;
        PrintAll([10, 20, 30]);
    }

    public static Program __CreateDefault()
    {
        var inst = new Program();
        return inst;
    }
}
