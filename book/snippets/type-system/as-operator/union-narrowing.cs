using System;

namespace Snippet;

public class Program
{
    public static void Main(string[] args)
    {
        Union_Int32_String value = 42;
        int? maybeInt = (value.IsInt32 ? (int?)value.AsInt32 : default(int?));
        if (maybeInt is int n)
        {
            System.Console.WriteLine($"Got int: {n}");
        }
        else
        {
            System.Console.WriteLine("Not an int");
        }
    }

    public static Program __CreateDefault()
    {
        var inst = new Program();
        return inst;
    }
}
