using System;

namespace Snippet;

public class Program
{
    public static void Main(string[] args)
    {
        Union_Int32_String value = 42;
        Union_Boolean_Int32_String widened = value;
        if (widened is int n)
        {
            Console.WriteLine($"widened int: {n}");
        }
    }

    public static Program __CreateDefault()
    {
        var inst = new Program();
        return inst;
    }
}
