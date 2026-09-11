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
        PrintAll(new int[] { 10, 20, 30 });
    }

    public Program()
    {
    }

    public static Program __CreateDefault()
    {
        return new Program();
    }
}
