using System;

namespace Snippet;

public class ShadowingDemo
{
    public static void Main(string[] args)
    {
        int x = 10;
        Console.WriteLine($"outer x = {x}");
        {
            int x_1 = 99;
            Console.WriteLine($"inner x = {x_1}");
        }
        Console.WriteLine($"outer x after block = {x}");
    }

    public ShadowingDemo()
    {
    }

    public static ShadowingDemo __CreateDefault()
    {
        return new ShadowingDemo();
    }
}
