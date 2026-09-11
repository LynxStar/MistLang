using System;

namespace Snippet;

public class Program
{
    public static void Main(string[] args)
    {
        for (int i = 0; i < 5; i = i + 1)
        {
            Console.WriteLine($"i = {i}");
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
