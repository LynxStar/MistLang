using System;

namespace UnionDemo;

public class Program
{
    public static void Main(string[] args)
    {
        Union_Int32_String x = 42;
        Union_Int32_String y = "hello";

        Console.WriteLine($"x holds an int");
        Console.WriteLine($"y holds a string");
    }
}
