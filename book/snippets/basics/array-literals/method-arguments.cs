using System;

namespace Demo;

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
        PrintAll(new[] { 10, 20, 30 });
    }
}
