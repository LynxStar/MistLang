using System;

namespace Snippet;

public class ShadowingDemo
{
    public static void Main(string[] args)
    {
        System.Console.OutputEncoding = System.Text.Encoding.UTF8;
        var x = 10;
        Console.WriteLine($"outer x = {x}");
        {
            var x = 99;
            Console.WriteLine($"inner x = {x}");
        }
        Console.WriteLine($"outer x after block = {x}");
    }

    public static ShadowingDemo __CreateDefault()
    {
        var inst = new ShadowingDemo();
        return inst;
    }
}
