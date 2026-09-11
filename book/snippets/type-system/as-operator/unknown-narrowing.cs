using System;

namespace Snippet;

public class Program
{
    public static void Main(string[] args)
    {
        object box = 99;
        int? maybeInt = (box is int __as159 ? __as159 : default(int?));
        {
            if (maybeInt is int n)
            {
                Console.WriteLine($"Got int: {n}");
            }
            else
            {
                Console.WriteLine("Not an int");
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
