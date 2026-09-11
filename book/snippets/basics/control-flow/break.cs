using System;

namespace Snippet;

public class Program
{
    public static void Main(string[] args)
    {
        for (int i = 0; i < 10; i = i + 1)
        {
            if (i == 3)
            {
                break;
            }
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
