using System;

namespace Snippet;

public class Program
{
    public static void Main(string[] args)
    {
        Union_Int32_String value = 42;
        {
            if (value.AsInt32OrNull is int n)
            {
                Console.WriteLine($"It's an int: {n}");
            }
        }
        {
            if (value.AsStringOrNull is string s)
            {
                Console.WriteLine($"It's a string: {s}");
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
