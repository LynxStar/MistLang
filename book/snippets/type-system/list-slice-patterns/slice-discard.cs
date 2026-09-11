using System;

namespace Snippet;

public class Program
{
    public static void Main(string[] args)
    {
        int[] numbers = new int[] { 10, 20, 30, 40, 50 };
        if (numbers is [10, ..])
        {
            Console.WriteLine("starts with 10");
        }
        if (numbers is [.., 50])
        {
            Console.WriteLine("ends with 50");
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
