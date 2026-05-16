using System;

namespace ScopingDemo;

public class ShadowingDemo
{
    public static void Main(string[] args)
    {
        var x = 10;
        Console.WriteLine($"outer x = {x}");

        {
            var x = 99;
            Console.WriteLine($"inner x = {x}");
        }

        Console.WriteLine($"outer x after block = {x}");
    }
}
