using System;

namespace Snippet;

public class Program
{
    public static void Main(string[] args)
    {
        Union_Int32_String value = 1;
        Console.WriteLine($"before: assigned int");
        value = "switched";
        if (value.IsString)
        {
            string s = value.AsString;
            Console.WriteLine($"after: {s}");
        }
    }

    public static Program __CreateDefault()
    {
        var inst = new Program();
        return inst;
    }
}
