using System;

namespace Snippet;

public class Program
{
    private static void Describe(Union_Int32_String value)
    {
        {
            if (value.AsInt32OrNull is int n)
            {
                Console.WriteLine($"  int: {n}");
            }
        }
        {
            if (value.AsStringOrNull is string s)
            {
                Console.WriteLine($"  string: {s}");
            }
        }
    }

    private static Union_Int32_String GetValue(bool useInt)
    {
        if (useInt)
        {
            return 100;
        }
        return "hello";
    }

    public static void Main(string[] args)
    {
        Snippet.Program.Describe(7);
        Snippet.Program.Describe("Mist");
        Union_Int32_String result = Snippet.Program.GetValue(true);
        Console.WriteLine($"GetValue(true) returned a value");
    }

    public Program()
    {
    }

    public static Program __CreateDefault()
    {
        return new Program();
    }
}
