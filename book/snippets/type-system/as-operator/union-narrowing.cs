using System;

namespace Snippet;

public class Program
{
    public static void Main(string[] args)
    {
        Union_Int32_String value = 42;
        int? maybeInt = (value is int __as_tmp ? __as_tmp : default(int?));
        if (maybeInt.IsInt32)
        {
            int n = maybeInt.AsInt32;
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
