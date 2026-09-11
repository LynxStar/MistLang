using System;

namespace Snippet;

public class Program
{
    public static void Main(string[] args)
    {
        Union_Int32_String value = 1;
        Console.WriteLine($"before: assigned int");
        value = "switched";
        {
            if (value.AsStringOrNull is string s)
            {
                Console.WriteLine($"after: {s}");
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
