using System;

namespace Snippet;

public class Program
{
    public static void Main(string[] args)
    {
        int a = 20;
        int sum = a + 5;
        int diff = a - 2;
        int computed = a + 10;
        {
            if (computed is int and >= 18 and var verified)
            {
                Console.WriteLine($"Still adult: {verified}");
            }
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
