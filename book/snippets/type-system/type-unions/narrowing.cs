using System;

namespace UnionDemo;

public class Program
{
    public static void Main(string[] args)
    {
        Union_Int32_String value = 42;

        if (value.IsInt32)
        {
            int n = value.AsInt32;
            Console.WriteLine($"It's an int: {n}");
        }
        if (value.IsString)
        {
            string s = value.AsString;
            Console.WriteLine($"It's a string: {s}");
        }
    }
}
