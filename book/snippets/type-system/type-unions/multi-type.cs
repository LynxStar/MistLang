using System;

namespace Snippet;

public class Program
{
    private static void DescribeTriple(Union_Boolean_Int32_String value)
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
        {
            if (value.AsBooleanOrNull is bool b)
            {
                Console.WriteLine($"  bool: {b}");
            }
        }
    }

    public static void Main(string[] args)
    {
        Union_Boolean_Int32_String tri1 = 99;
        Union_Boolean_Int32_String tri2 = "triple";
        Union_Boolean_Int32_String tri3 = true;
        Snippet.Program.DescribeTriple(tri1);
        Snippet.Program.DescribeTriple(tri2);
        Snippet.Program.DescribeTriple(tri3);
    }

    public Program()
    {
    }

    public static Program __CreateDefault()
    {
        return new Program();
    }
}
