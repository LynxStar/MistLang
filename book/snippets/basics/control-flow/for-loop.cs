using Standard;
using System;

namespace Demo;

public class Program
{
    public static void Main(string[] args)
    {
        for (int i = 0; i < 5; i = i + 1)
        {
            System.Console.WriteLine($"i = {i}");
        }
    }

    public static Program __CreateDefault()
    {
        var inst = new Program();
        return inst;
    }
}
