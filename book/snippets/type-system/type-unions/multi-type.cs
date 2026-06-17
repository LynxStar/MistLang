using System;

namespace Snippet;

public class Program
{
    private static void DescribeTriple(Union_Boolean_Int32_String value)
    {
        if (value is int n)
        {
            Console.WriteLine($"  int: {n}");
        }
        if (value is string s)
        {
            Console.WriteLine($"  string: {s}");
        }
        if (value is bool b)
        {
            Console.WriteLine($"  bool: {b}");
        }
    }

    public static void Main(string[] args)
    {
        System.Console.OutputEncoding = System.Text.Encoding.UTF8;
        Union_Boolean_Int32_String tri1 = 99;
        Union_Boolean_Int32_String tri2 = "triple";
        Union_Boolean_Int32_String tri3 = true;
        Program.DescribeTriple(tri1);
        Program.DescribeTriple(tri2);
        Program.DescribeTriple(tri3);
    }

    public static Program __CreateDefault()
    {
        var inst = new Program();
        return inst;
    }
}
