using Standard;
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
            System.Console.WriteLine($"It's an int: {n}");
        }
        if (value.IsString)
        {
            string s = value.AsString;
            System.Console.WriteLine($"It's a string: {s}");
        }
    }

    public static Program __CreateDefault()
    {
        var inst = new Program();
        return inst;
    }
}
