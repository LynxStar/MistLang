using Standard;
using System;

namespace Demo;

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
            System.Console.WriteLine($"i = {i}");
        }
    }

    public static Program __CreateDefault()
    {
        var inst = new Program();
        return inst;
    }
}
